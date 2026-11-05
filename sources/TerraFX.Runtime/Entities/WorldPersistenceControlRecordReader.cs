// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using TerraFX;

namespace TerraFX.Entities;

internal static class WorldPersistenceControlRecordReader
{
    private const ushort ArrayKind = 24;
    private const ushort ByteArrayKind = 13;
    private const ushort FloatKind = 10;
    private const ushort GuidKind = 11;
    private const ushort Int32Kind = 7;
    private const ushort Int64Kind = 8;
    private const ushort RecordKind = 22;
    private const int RootFieldCount = 3;
    private const int TimeScaleFieldCount = 2;
    private const ushort UInt16Kind = 2;
    private const ushort UInt32Kind = 3;
    private const ushort UInt64Kind = 4;
    private const ushort UInt8Kind = 1;

    private static readonly Guid s_branchSchemaId = new ContentSchemaIdentityAssignment("state/control-branch").SchemaId;
    private static readonly Guid s_controlSchemaId = new ContentSchemaIdentityAssignment("state/control").SchemaId;
    private static readonly Guid s_frame2ControlSchemaId = new ContentSchemaIdentityAssignment("state/control-frame2").SchemaId;
    private static readonly Guid s_frame2SchemaId = new ContentSchemaIdentityAssignment("foundation/frame2").SchemaId;
    private static readonly Guid s_frame3ControlSchemaId = new ContentSchemaIdentityAssignment("state/control-frame3").SchemaId;
    private static readonly Guid s_frame3SchemaId = new ContentSchemaIdentityAssignment("foundation/frame3").SchemaId;
    private static readonly Guid s_identityHistorySchemaId = new ContentSchemaIdentityAssignment("state/identity-history").SchemaId;
    private static readonly Guid s_profileSchemaId = new ContentSchemaIdentityAssignment("foundation/profile").SchemaId;
    private static readonly Guid s_sceneControlSchemaId = new ContentSchemaIdentityAssignment("state/control-scene").SchemaId;
    private static readonly Guid s_stepSchemaId = new ContentSchemaIdentityAssignment("state/control-step").SchemaId;
    private static readonly Guid s_timeScaleSchemaId = new ContentSchemaIdentityAssignment("state/control-timescale").SchemaId;

    internal static void Validate(
        EbinRecordReadView worldRecord,
        uint fieldTag,
        int controlRecordCount,
        EbinRecordReadView clockRecord,
        WorldPersistenceClockContents clock,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfNegative(controlRecordCount);

        var initialFrame3 = new EbinRecordReadView(clock.InitialFrame3Record.Span);
        var worldGuid = ReadGuid(initialFrame3, 1, parameterName);
        var frame3Revision = ValidateFrameRecord(
            initialFrame3,
            s_frame3SchemaId,
            worldGuid,
            expectedProfileRecord: [],
            parameterName);
        var expectedProfileRecord = initialFrame3.GetRecord(2).EncodedBytes;
        var latestFrame3Record = initialFrame3.EncodedBytes;
        var frame2Revision = 0L;
        var latestFrame2Record = ReadOnlySpan<byte>.Empty;

        if (clock.HasInitialFrame2)
        {
            var initialFrame2 = new EbinRecordReadView(clock.InitialFrame2Record.Span);
            frame2Revision = ValidateFrameRecord(
                initialFrame2,
                s_frame2SchemaId,
                worldGuid,
                expectedProfileRecord,
                parameterName);
            latestFrame2Record = initialFrame2.EncodedBytes;
        }

        var previousAppliedAtNextTick = 0L;
        var previousPausedStepTick = 0L;
        var timeScaleControlCount = 0;
        var hasPausedStep = false;
        var (initialSegmentSequence, initialSegmentStart, currentTimeScale) = WorldPersistenceClockReader.ReadTimeSegment(
            clockRecord,
            0,
            clock.NextTick,
            parameterName);

        if ((initialSegmentSequence != 1) || (initialSegmentStart != 0))
        {
            throw new ArgumentException("The state/clock initial time-scale segment is invalid.", parameterName);
        }

        var identityHistory = worldRecord.GetRecord(5).GetRecord(5);
        ValidateRecordHeader(identityHistory, s_identityHistorySchemaId, fieldCount: 7, parameterName);
        var branchField = GetRequiredField(identityHistory, 6, ArrayKind, expectedCount: null, parameterName);

        if (branchField.Count is < 1 or > 1024)
        {
            throw new ArgumentException("The identity-branch directory count is outside its supported range.", parameterName);
        }

        var branchCount = checked((int)branchField.Count);
        Span<byte> branchControlCounts = stackalloc byte[1024];
        branchControlCounts.Clear();

        for (var index = 0; index < controlRecordCount; index++)
        {
            var control = worldRecord.GetRecordArrayElement(fieldTag, index);
            ValidateRecordHeader(control, s_controlSchemaId, RootFieldCount, parameterName);

            var sequence = ReadInt64(control, 1, parameterName);
            var appliedAtNextTick = ReadInt64(control, 2, parameterName);

            if ((sequence != checked((long)index + 1))
                || (appliedAtNextTick < 0)
                || (appliedAtNextTick >= clock.NextTick)
                || (appliedAtNextTick < previousAppliedAtNextTick))
            {
                throw new ArgumentException("The state/control sequence or applied-tick order is invalid.", parameterName);
            }

            previousAppliedAtNextTick = appliedAtNextTick;
            _ = GetRequiredField(control, 3, RecordKind, expectedCount: 1, parameterName);
            var payload = control.GetRecord(3);

            if (payload.Header.SchemaId == s_timeScaleSchemaId)
            {
                var (value, segmentSequence) = ReadTimeScalePayload(payload, parameterName);
                var expectedSequence = checked((long)timeScaleControlCount + 2);

                if ((segmentSequence != expectedSequence)
                    || (segmentSequence > clock.SegmentCount))
                {
                    throw new ArgumentException("A state/control-timescale payload has an invalid segment sequence.", parameterName);
                }

                var (segmentSequenceFromClock, segmentStartNextTick, segmentScale) = WorldPersistenceClockReader.ReadTimeSegment(
                    clockRecord,
                    checked((int)(segmentSequence - 1)),
                    clock.NextTick,
                    parameterName);

                if ((segmentSequenceFromClock != segmentSequence)
                    || (segmentStartNextTick != appliedAtNextTick)
                    || (BitConverter.SingleToUInt32Bits(segmentScale) != BitConverter.SingleToUInt32Bits(value)))
                {
                    throw new ArgumentException("A state/control-timescale payload does not match its clock segment.", parameterName);
                }

                currentTimeScale = value;
                timeScaleControlCount = checked(timeScaleControlCount + 1);
            }
            else if (payload.Header.SchemaId == s_stepSchemaId)
            {
                var executedTick = ReadPausedStepPayload(payload, parameterName);

                if ((executedTick != appliedAtNextTick)
                    || (executedTick >= clock.NextTick)
                    || (currentTimeScale != 0.0f)
                    || (hasPausedStep && (executedTick == previousPausedStepTick)))
                {
                    throw new ArgumentException("A state/control-step does not describe one explicit paused tick.", parameterName);
                }

                previousPausedStepTick = executedTick;
                hasPausedStep = true;
            }
            else if (payload.Header.SchemaId == s_frame2ControlSchemaId)
            {
                if (!clock.HasInitialFrame2)
                {
                    throw new ArgumentException("A Frame2 control is present without an initial Frame2.", parameterName);
                }

                ValidateFramePayload(payload, s_frame2ControlSchemaId, s_frame2SchemaId, parameterName);
                var frame = payload.GetRecord(1);
                var frameRevision = ValidateFrameRecord(
                    frame,
                    s_frame2SchemaId,
                    worldGuid,
                    expectedProfileRecord,
                    parameterName);

                if ((frame2Revision == long.MaxValue) || (frameRevision != (frame2Revision + 1)))
                {
                    throw new ArgumentException("A Frame2 control does not advance its frame revision by one.", parameterName);
                }

                frame2Revision = frameRevision;
                latestFrame2Record = frame.EncodedBytes;
            }
            else if (payload.Header.SchemaId == s_frame3ControlSchemaId)
            {
                ValidateFramePayload(payload, s_frame3ControlSchemaId, s_frame3SchemaId, parameterName);
                var frame = payload.GetRecord(1);
                var frameRevision = ValidateFrameRecord(
                    frame,
                    s_frame3SchemaId,
                    worldGuid,
                    expectedProfileRecord,
                    parameterName);

                if ((frame3Revision == long.MaxValue) || (frameRevision != (frame3Revision + 1)))
                {
                    throw new ArgumentException("A Frame3 control does not advance its frame revision by one.", parameterName);
                }

                frame3Revision = frameRevision;
                latestFrame3Record = frame.EncodedBytes;
            }
            else if (payload.Header.SchemaId == s_branchSchemaId)
            {
                var (branchId, lineageId, branchIndex) = ReadBranchPayload(payload, parameterName);
                ValidateBranchControlJoin(
                    identityHistory,
                    branchCount,
                    branchControlCounts,
                    branchIndex,
                    branchId,
                    lineageId,
                    appliedAtNextTick,
                    parameterName);
            }
            else if (payload.Header.SchemaId == s_sceneControlSchemaId)
            {
                if ((payload.Header.Major != 1)
                    || (payload.Header.Minor != 0)
                    || (payload.FieldCount is < 3 or > 6))
                {
                    throw new ArgumentException("A state/control-scene payload has the wrong schema version or field count.", parameterName);
                }
            }
            else
            {
                throw new ArgumentException("The state/control payload has an unsupported schema identity.", parameterName);
            }
        }

        for (var branchIndex = 1; branchIndex < branchCount; branchIndex++)
        {
            var branch = identityHistory.GetRecordArrayElement(6, branchIndex);
            var hasSourceCheckpointDigest = branch.TryGetField(7, out _);
            var hasForkSource = branch.TryGetField(8, out _);
            var requiresDirectControl = !hasSourceCheckpointDigest && !hasForkSource;

            if (requiresDirectControl != (branchControlCounts[branchIndex] == 1))
            {
                throw new ArgumentException("The identity-branch directory and state/control-branch records disagree.", parameterName);
            }
        }

        if (clock.SegmentCount != checked(timeScaleControlCount + 1))
        {
            throw new ArgumentException("The state/clock segment count does not match its time-scale controls.", parameterName);
        }

        var hasCurrentFrame2 = worldRecord.TryGetField(3, out var currentFrame2Field);

        if (hasCurrentFrame2 != clock.HasInitialFrame2)
        {
            throw new ArgumentException("The current World Frame2 presence disagrees with its initial clock frame.", parameterName);
        }

        if (hasCurrentFrame2)
        {
            var currentFrame2 = new EbinRecordReadView(currentFrame2Field.Payload);
            _ = ValidateFrameRecord(
                currentFrame2,
                s_frame2SchemaId,
                worldGuid,
                expectedProfileRecord,
                parameterName);

            if (!currentFrame2.EncodedBytes.SequenceEqual(latestFrame2Record))
            {
                throw new ArgumentException("The current World Frame2 does not match its initial frame and controls.", parameterName);
            }
        }

        var currentFrame3 = worldRecord.GetRecord(4);
        _ = ValidateFrameRecord(
            currentFrame3,
            s_frame3SchemaId,
            worldGuid,
            expectedProfileRecord,
            parameterName);

        if (!currentFrame3.EncodedBytes.SequenceEqual(latestFrame3Record))
        {
            throw new ArgumentException("The current World Frame3 does not match its initial frame and controls.", parameterName);
        }
    }

    internal static void ValidateWorldGuidFrames(
        ReadOnlyMemory<byte> frame2Record,
        ReadOnlyMemory<byte> frame3Record,
        Guid worldGuid,
        string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(worldGuid, Guid.Empty);

        if (frame3Record.IsEmpty
            || (ReadGuid(new EbinRecordReadView(frame3Record.Span), 1, parameterName) != worldGuid))
        {
            throw new ArgumentException("The current World Frame3 identity does not match state/identity-history.", parameterName);
        }

        if (!frame2Record.IsEmpty
            && (ReadGuid(new EbinRecordReadView(frame2Record.Span), 1, parameterName) != worldGuid))
        {
            throw new ArgumentException("The current World Frame2 identity does not match state/identity-history.", parameterName);
        }
    }

    private static EbinFieldReadView GetRequiredField(
        EbinRecordReadView record,
        uint tag,
        ushort kind,
        ulong? expectedCount,
        string parameterName)
    {
        if (!record.TryGetField(tag, out var field)
            || (field.Kind != kind)
            || !field.IsRequired
            || (expectedCount.HasValue && (field.Count != expectedCount.Value)))
        {
            throw new ArgumentException("A State2 control field has an invalid wire shape.", parameterName);
        }

        return field;
    }

    private static (Guid BranchId, Guid LineageId, ushort BranchIndex) ReadBranchPayload(
        EbinRecordReadView payload,
        string parameterName)
    {
        ValidateRecordHeader(payload, s_branchSchemaId, fieldCount: 3, parameterName);
        var branchId = ReadGuid(payload, 1, parameterName);
        var lineageId = ReadGuid(payload, 2, parameterName);
        var branchIndex = ReadUInt16(payload, 3, parameterName);

        if ((branchId == Guid.Empty)
            || (lineageId == Guid.Empty)
            || (branchIndex is < 1 or > 1023))
        {
            throw new ArgumentException("A state/control-branch payload has invalid identity metadata.", parameterName);
        }

        return (branchId, lineageId, branchIndex);
    }

    private static float ReadFiniteSingle(scoped ReadOnlySpan<byte> payload, int offset, string parameterName)
    {
        var value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(payload[offset..]));

        if (!float.IsFinite(value))
        {
            throw new ArgumentException("A State2 frame contains a nonfinite binary32 value.", parameterName);
        }

        return value;
    }

    private static float ReadFloat(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, FloatKind, expectedCount: 1, parameterName);

        if (field.Payload.Length != sizeof(float))
        {
            throw new ArgumentException("A State2 control binary32 field has an invalid payload length.", parameterName);
        }

        var value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(field.Payload));

        if (!float.IsFinite(value) || (value < 0.0f) || (value > 4.0f))
        {
            throw new ArgumentException("A State2 control time scale is outside its supported range.", parameterName);
        }

        return value;
    }

    private static Guid ReadGuid(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, GuidKind, expectedCount: 1, parameterName);

        if (field.Payload.Length != 16)
        {
            throw new ArgumentException("A State2 control GUID field has an invalid payload length.", parameterName);
        }

        return new Guid(field.Payload, bigEndian: true);
    }

    private static int ReadInt32(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, Int32Kind, expectedCount: 1, parameterName);

        if (field.Payload.Length != sizeof(int))
        {
            throw new ArgumentException("A State2 frame Int32 field has an invalid payload length.", parameterName);
        }

        return BinaryPrimitives.ReadInt32LittleEndian(field.Payload);
    }

    private static long ReadInt64(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, Int64Kind, expectedCount: 1, parameterName);

        if (field.Payload.Length != sizeof(long))
        {
            throw new ArgumentException("A State2 control Int64 field has an invalid payload length.", parameterName);
        }

        return BinaryPrimitives.ReadInt64LittleEndian(field.Payload);
    }

    private static long ReadPausedStepPayload(EbinRecordReadView payload, string parameterName)
    {
        ValidateRecordHeader(payload, s_stepSchemaId, fieldCount: 1, parameterName);
        return ReadInt64(payload, 1, parameterName);
    }

    private static (float Value, long SegmentSequence) ReadTimeScalePayload(
        EbinRecordReadView payload,
        string parameterName)
    {
        ValidateRecordHeader(payload, s_timeScaleSchemaId, TimeScaleFieldCount, parameterName);
        var value = ReadFloat(payload, 1, parameterName);
        var segmentSequence = ReadInt64(payload, 2, parameterName);

        if (segmentSequence < 2)
        {
            throw new ArgumentException("A state/control-timescale segment sequence must follow the initial segment.", parameterName);
        }

        return (value, segmentSequence);
    }

    private static ushort ReadUInt16(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, UInt16Kind, expectedCount: 1, parameterName);

        if (field.Payload.Length != sizeof(ushort))
        {
            throw new ArgumentException("A State2 control UInt16 field has an invalid payload length.", parameterName);
        }

        return BinaryPrimitives.ReadUInt16LittleEndian(field.Payload);
    }

    private static uint ReadUInt32(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, UInt32Kind, expectedCount: 1, parameterName);

        if (field.Payload.Length != sizeof(uint))
        {
            throw new ArgumentException("A State2 frame UInt32 field has an invalid payload length.", parameterName);
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(field.Payload);
    }

    private static ulong ReadUInt64(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, UInt64Kind, expectedCount: 1, parameterName);

        if (field.Payload.Length != sizeof(ulong))
        {
            throw new ArgumentException("A State2 frame UInt64 field has an invalid payload length.", parameterName);
        }

        return BinaryPrimitives.ReadUInt64LittleEndian(field.Payload);
    }

    private static byte ReadUInt8(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, UInt8Kind, expectedCount: 1, parameterName);

        if (field.Payload.Length != sizeof(byte))
        {
            throw new ArgumentException("A State2 frame UInt8 field has an invalid payload length.", parameterName);
        }

        return field.Payload[0];
    }

    private static void ValidateBranchControlJoin(
        EbinRecordReadView identityHistory,
        int branchCount,
        Span<byte> branchControlCounts,
        ushort branchIndex,
        Guid branchId,
        Guid lineageId,
        long appliedAtNextTick,
        string parameterName)
    {
        if ((branchIndex >= branchCount) || (branchControlCounts[branchIndex] != 0))
        {
            throw new ArgumentException("A state/control-branch names an absent or duplicate identity directory row.", parameterName);
        }

        var branch = identityHistory.GetRecordArrayElement(6, branchIndex);
        var directoryIndex = ReadUInt16(branch, 1, parameterName);
        var directoryBranchId = ReadGuid(branch, 2, parameterName);
        var creationLineageId = ReadGuid(branch, 3, parameterName);
        var createdAtNextTick = ReadInt64(branch, 6, parameterName);

        if ((directoryIndex != branchIndex)
            || (directoryBranchId != branchId)
            || (creationLineageId != lineageId)
            || (createdAtNextTick != appliedAtNextTick)
            || branch.TryGetField(7, out _)
            || branch.TryGetField(8, out _))
        {
            throw new ArgumentException("A state/control-branch does not match its direct identity-history row.", parameterName);
        }

        branchControlCounts[branchIndex] = 1;
    }

    private static void ValidateFramePayload(
        EbinRecordReadView payload,
        Guid payloadSchemaId,
        Guid frameSchemaId,
        string parameterName)
    {
        ValidateRecordHeader(payload, payloadSchemaId, fieldCount: 1, parameterName);
        var frameField = GetRequiredField(payload, 1, RecordKind, expectedCount: 1, parameterName);
        var frame = new EbinRecordReadView(frameField.Payload);

        if ((frame.Header.SchemaId != frameSchemaId)
            || (frame.Header.Major != 1)
            || (frame.Header.Minor != 0))
        {
            throw new ArgumentException("A state/control frame payload has the wrong schema identity or version.", parameterName);
        }
    }

    private static void ValidateFramePosition(
        EbinFieldReadView positionField,
        ushort expectedKind,
        int dimensions,
        string parameterName)
    {
        var expectedBytes = checked(dimensions * sizeof(float) * 2);

        if ((positionField.Kind != expectedKind)
            || (positionField.Count != 1)
            || (positionField.Payload.Length != expectedBytes))
        {
            throw new ArgumentException("A State2 frame origin has an invalid wire shape.", parameterName);
        }

        var offsetStart = checked(dimensions * sizeof(int));

        for (var index = 0; index < dimensions; index++)
        {
            _ = ReadFiniteSingle(positionField.Payload, checked(offsetStart + (index * sizeof(float))), parameterName);
        }
    }

    private static long ValidateFrameRecord(
        EbinRecordReadView frame,
        Guid schemaId,
        Guid worldGuid,
        scoped ReadOnlySpan<byte> expectedProfileRecord,
        string parameterName)
    {
        ValidateRecordHeader(frame, schemaId, fieldCount: 6, parameterName);

        if (ReadGuid(frame, 1, parameterName) != worldGuid)
        {
            throw new ArgumentException("A State2 frame owner does not match the initial frame identity.", parameterName);
        }

        var profile = new EbinRecordReadView(
            GetRequiredField(frame, 2, RecordKind, expectedCount: 1, parameterName).Payload);
        ValidateProfileRecord(profile, expectedProfileRecord, parameterName);
        var isFrame2 = schemaId == s_frame2SchemaId;
        var positionKind = isFrame2 ? (ushort)20 : (ushort)21;
        var dimensions = isFrame2 ? 2 : 3;
        var position = GetRequiredField(frame, 3, positionKind, expectedCount: 1, parameterName);
        ValidateFramePosition(position, positionKind, dimensions, parameterName);
        var revision = ReadUInt64(frame, 4, parameterName);

        if (revision is 0 or > long.MaxValue)
        {
            throw new ArgumentException("A State2 frame revision is outside the positive signed range.", parameterName);
        }

        var vectorKind = isFrame2 ? (ushort)14 : (ushort)15;
        var minimum = GetRequiredField(frame, 5, vectorKind, expectedCount: 1, parameterName);
        var maximum = GetRequiredField(frame, 6, vectorKind, expectedCount: 1, parameterName);
        ValidateFrameVectorBounds(minimum.Payload, maximum.Payload, dimensions, parameterName);
        return checked((long)revision);
    }

    private static void ValidateFrameVectorBounds(
        scoped ReadOnlySpan<byte> minimum,
        scoped ReadOnlySpan<byte> maximum,
        int dimensions,
        string parameterName)
    {
        var expectedBytes = checked(dimensions * sizeof(float));

        if ((minimum.Length != expectedBytes) || (maximum.Length != expectedBytes))
        {
            throw new ArgumentException("A State2 frame extent has an invalid payload length.", parameterName);
        }

        for (var index = 0; index < dimensions; index++)
        {
            var minimumValue = ReadFiniteSingle(minimum, checked(index * sizeof(float)), parameterName);
            var maximumValue = ReadFiniteSingle(maximum, checked(index * sizeof(float)), parameterName);

            if (minimumValue > maximumValue)
            {
                throw new ArgumentException("A State2 frame minimum exceeds its maximum.", parameterName);
            }
        }
    }

    private static void ValidateProfileRecord(
        EbinRecordReadView profile,
        scoped ReadOnlySpan<byte> expectedProfileRecord,
        string parameterName)
    {
        if ((profile.Header.SchemaId != s_profileSchemaId)
            || (profile.Header.Major != 2)
            || (profile.Header.Minor != 0)
            || (profile.FieldCount < 9)
            || (ReadUInt32(profile, 1, parameterName) != 1)
            || (ReadUInt32(profile, 2, parameterName) != 2)
            || (ReadUInt32(profile, 3, parameterName) != 2))
        {
            throw new ArgumentException("A State2 frame profile has the wrong schema or fixed identifiers.", parameterName);
        }

        var chunkSizeExponent = ReadUInt8(profile, 5, parameterName);
        var profileId = ReadGuid(profile, 9, parameterName);
        var metersPerUnitExponent = ReadInt32(profile, 10, parameterName);

        if ((profileId == Guid.Empty)
            || (chunkSizeExponent is < 8 or > 16)
            || (metersPerUnitExponent is < -30 or > 30))
        {
            throw new ArgumentException("A State2 frame profile has invalid spatial settings or identity.", parameterName);
        }

        var settings = new SpatialSettings(metersPerUnitExponent, chunkSizeExponent);
        var settingsDigest = GetRequiredField(profile, 6, ByteArrayKind, expectedCount: 32, parameterName);
        var registryDigest = GetRequiredField(profile, 7, ByteArrayKind, expectedCount: 32, parameterName);
        var kernelDigest = GetRequiredField(profile, 8, ByteArrayKind, expectedCount: 32, parameterName);
        Span<byte> expectedSettingsDigest = stackalloc byte[32];
        FoundationProfileBinding.ComputeSettingsDigest(settings, expectedSettingsDigest);

        if (!settingsDigest.Payload.SequenceEqual(expectedSettingsDigest)
            || !registryDigest.Payload.SequenceEqual(FoundationCatalogue.RegistryDigest)
            || !kernelDigest.Payload.SequenceEqual(FoundationCatalogue.KernelDigest)
            || (!expectedProfileRecord.IsEmpty && !profile.EncodedBytes.SequenceEqual(expectedProfileRecord)))
        {
            throw new ArgumentException("A State2 frame profile digest or immutable profile selection is inconsistent.", parameterName);
        }
    }

    private static void ValidateRecordHeader(
        EbinRecordReadView record,
        Guid schemaId,
        int fieldCount,
        string parameterName)
    {
        if ((record.Header.SchemaId != schemaId)
            || (record.Header.Major != 1)
            || (record.Header.Minor != 0)
            || (record.FieldCount != fieldCount))
        {
            throw new ArgumentException("A State2 control record has the wrong schema identity, version, or field count.", parameterName);
        }
    }
}
