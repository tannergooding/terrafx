// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using TerraFX;

namespace TerraFX.Entities;

internal static class WorldPersistenceControlRecordReader
{
    private const ushort FloatKind = 10;
    private const ushort GuidKind = 11;
    private const ushort Int64Kind = 8;
    private const ushort RecordKind = 22;
    private const int RootFieldCount = 3;
    private const int TimeScaleFieldCount = 2;
    private const ushort UInt16Kind = 2;

    private static readonly Guid s_branchSchemaId = new ContentSchemaIdentityAssignment("state/control-branch").SchemaId;
    private static readonly Guid s_controlSchemaId = new ContentSchemaIdentityAssignment("state/control").SchemaId;
    private static readonly Guid s_frame2ControlSchemaId = new ContentSchemaIdentityAssignment("state/control-frame2").SchemaId;
    private static readonly Guid s_frame2SchemaId = new ContentSchemaIdentityAssignment("foundation/frame2").SchemaId;
    private static readonly Guid s_frame3ControlSchemaId = new ContentSchemaIdentityAssignment("state/control-frame3").SchemaId;
    private static readonly Guid s_frame3SchemaId = new ContentSchemaIdentityAssignment("foundation/frame3").SchemaId;
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

        var previousAppliedAtNextTick = 0L;
        var timeScaleControlCount = 0;

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

                timeScaleControlCount = checked(timeScaleControlCount + 1);
            }
            else if (payload.Header.SchemaId == s_frame2ControlSchemaId)
            {
                if (!clock.HasInitialFrame2)
                {
                    throw new ArgumentException("A Frame2 control is present without an initial Frame2.", parameterName);
                }

                ValidateFramePayload(payload, s_frame2ControlSchemaId, s_frame2SchemaId, parameterName);
            }
            else if (payload.Header.SchemaId == s_frame3ControlSchemaId)
            {
                ValidateFramePayload(payload, s_frame3ControlSchemaId, s_frame3SchemaId, parameterName);
            }
            else if (payload.Header.SchemaId == s_branchSchemaId)
            {
                ValidateBranchPayload(payload, parameterName);
            }
            else
            {
                throw new ArgumentException("The state/control payload has an unsupported schema identity.", parameterName);
            }
        }

        if (clock.SegmentCount != checked(timeScaleControlCount + 1))
        {
            throw new ArgumentException("The state/clock segment count does not match its time-scale controls.", parameterName);
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

    private static long ReadInt64(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, Int64Kind, expectedCount: 1, parameterName);

        if (field.Payload.Length != sizeof(long))
        {
            throw new ArgumentException("A State2 control Int64 field has an invalid payload length.", parameterName);
        }

        return BinaryPrimitives.ReadInt64LittleEndian(field.Payload);
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

    private static void ValidateBranchPayload(EbinRecordReadView payload, string parameterName)
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
