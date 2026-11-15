// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Threading;
using TerraFX;
using TerraFX.Numerics;

namespace TerraFX.Entities;

public sealed partial class World
{
    private static readonly Guid s_branchControlSchemaId = new ContentSchemaIdentityAssignment("state/control-branch").SchemaId;
    private static readonly Guid s_controlRecordSchemaId = new ContentSchemaIdentityAssignment("state/control").SchemaId;
    private static readonly Guid s_frame2ControlSchemaId = new ContentSchemaIdentityAssignment("state/control-frame2").SchemaId;
    private static readonly Guid s_frame3ControlSchemaId = new ContentSchemaIdentityAssignment("state/control-frame3").SchemaId;
    private static readonly Guid s_sceneControlSchemaId = new ContentSchemaIdentityAssignment("state/control-scene").SchemaId;
    private static readonly Guid s_stepControlSchemaId = new ContentSchemaIdentityAssignment("state/control-step").SchemaId;
    private static readonly Guid s_timeScaleControlSchemaId = new ContentSchemaIdentityAssignment("state/control-timescale").SchemaId;

    private static EbinFieldReadView GetRequiredControlField(EbinRecordReadView record, uint tag)
    {
        if (!record.TryGetField(tag, out var field))
        {
            throw new ArgumentException("A candidate control record omits a required field.");
        }

        return field;
    }

    private static bool IsWithin(Vector2 minimum, Vector2 maximum, Vector2 allowedMinimum, Vector2 allowedMaximum)
        => (minimum.X >= allowedMinimum.X)
        && (minimum.Y >= allowedMinimum.Y)
        && (maximum.X <= allowedMaximum.X)
        && (maximum.Y <= allowedMaximum.Y);

    private static bool IsWithin(Vector3 minimum, Vector3 maximum, Vector3 allowedMinimum, Vector3 allowedMaximum)
        => (minimum.X >= allowedMinimum.X)
        && (minimum.Y >= allowedMinimum.Y)
        && (minimum.Z >= allowedMinimum.Z)
        && (maximum.X <= allowedMaximum.X)
        && (maximum.Y <= allowedMaximum.Y)
        && (maximum.Z <= allowedMaximum.Z);

    private static float ReadControlFloat(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredControlField(record, tag);

        if ((field.Kind != 10)
            || !field.IsRequired
            || (field.Count != 1)
            || (field.Payload.Length != sizeof(float)))
        {
            throw new ArgumentException("A candidate control binary32 field has an invalid wire shape.");
        }

        var value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(field.Payload));

        if (!float.IsFinite(value) || (value < 0.0f) || (value > 4.0f))
        {
            throw new ArgumentException("A candidate control time scale is outside its supported range.");
        }

        return value;
    }

    private static Guid ReadControlGuid(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredControlField(record, tag);

        if ((field.Kind != 11)
            || !field.IsRequired
            || (field.Count != 1)
            || (field.Payload.Length != 16))
        {
            throw new ArgumentException("A candidate control GUID field has an invalid wire shape.");
        }

        var value = new Guid(field.Payload, bigEndian: true);

        if (value == Guid.Empty)
        {
            throw new ArgumentException("A candidate control identity cannot be empty.");
        }

        return value;
    }

    private static long ReadControlInt64(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredControlField(record, tag);

        if ((field.Kind != 8)
            || !field.IsRequired
            || (field.Count != 1)
            || (field.Payload.Length != sizeof(long)))
        {
            throw new ArgumentException("A candidate control Int64 field has an invalid wire shape.");
        }

        return BinaryPrimitives.ReadInt64LittleEndian(field.Payload);
    }

    private static ushort ReadControlUInt16(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredControlField(record, tag);

        if ((field.Kind != 2)
            || !field.IsRequired
            || (field.Count != 1)
            || (field.Payload.Length != sizeof(ushort)))
        {
            throw new ArgumentException("A candidate control UInt16 field has an invalid wire shape.");
        }

        return BinaryPrimitives.ReadUInt16LittleEndian(field.Payload);
    }

    private static SpatialFrame2 RestoreFrame2(scoped ReadOnlySpan<byte> record, FoundationPersistenceBinding binding)
    {
        var data = new FoundationFrame2Data(EbinCodec.Stage(record), binding);
        return data.Project(binding);
    }

    private static SpatialFrame3 RestoreFrame3(scoped ReadOnlySpan<byte> record, FoundationPersistenceBinding binding)
    {
        var data = new FoundationFrame3Data(EbinCodec.Stage(record), binding);
        return data.Project(binding);
    }

    internal void RestoreCheckpointClockAndControls(
        ReadOnlyMemory<byte> worldRecordMemory,
        WorldPersistenceClockContents clock,
        WorldPersistenceCursorContents cursors,
        int controlRecordCount,
        long sceneControlPayloadBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(cursors);
        ArgumentOutOfRangeException.ThrowIfNegative(controlRecordCount);
        ArgumentOutOfRangeException.ThrowIfNegative(sceneControlPayloadBytes);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ThrowIfFaulted();
        EnsureOwnerThread();

        if (!_isRestoreCandidate
            || !_isCandidateInitialized
            || !_isRestoreEcsRowsRestored
            || !_isRestoreHierarchyInstalled)
        {
            throw new InvalidOperationException("Checkpoint clock state can be restored only after candidate ECS and Scene state are installed.");
        }

        var worldRecord = new EbinRecordReadView(worldRecordMemory.Span);

        if ((worldRecord.Header.SchemaId != new ContentSchemaIdentityAssignment("state/world").SchemaId)
            || (worldRecord.Header.Major != 1)
            || (worldRecord.Header.Minor != 0)
            || !worldRecord.TryGetField(8, out var controlArray)
            || (controlArray.Kind != 24)
            || !controlArray.IsRequired
            || (controlArray.Count != (ulong)controlRecordCount))
        {
            throw new ArgumentException("The candidate state/world control array does not match its validated count.", nameof(worldRecordMemory));
        }

        if (_isRestoreClockStateRestored
            || (clock.NextTick != _nextTickIndex)
            || (clock.TickRate != _tickRate)
            || (BitConverter.SingleToUInt32Bits(clock.InitialTimeScale)
                != BitConverter.SingleToUInt32Bits(_initialTimeScale))
            || (clock.CurrentSegmentSequence != clock.SegmentCount)
            || (clock.NextPublicationSequence != checked(clock.NextTick + 1))
            || (cursors.NextControlSequence != checked((long)controlRecordCount + 1))
            || (_controlRecordCount != 0)
            || (_controlRecords.Length != controlRecordCount)
            || (_controlStorageByteReservation != ((controlRecordCount == 0)
                ? 0
                : ArrayBytes(controlRecordCount, Unsafe.SizeOf<WorldControlRecord>())))
            || (_sceneControlPayloadByteReservation != sceneControlPayloadBytes))
        {
            throw new ArgumentException("The validated clock or control state does not match the candidate restore reservation.");
        }

        var currentFrame2FieldPresent = worldRecord.TryGetField(3, out var currentFrame2Field);

        if (currentFrame2FieldPresent != clock.HasInitialFrame2
            || clock.HasInitialFrame2 != _initialFrame2.HasValue)
        {
            throw new ArgumentException("The candidate Frame2 state does not match its construction profile.", nameof(worldRecordMemory));
        }

        var frameBinding = CreateRestoreFrameBinding();
        var initialFrame3 = RestoreFrame3(clock.InitialFrame3Record.Span, frameBinding);
        var currentFrame3 = RestoreFrame3(worldRecord.GetRecord(4).EncodedBytes, frameBinding);
        SpatialFrame2? initialFrame2 = null;
        SpatialFrame2? currentFrame2 = null;

        if (clock.HasInitialFrame2)
        {
            initialFrame2 = RestoreFrame2(clock.InitialFrame2Record.Span, frameBinding);
            currentFrame2 = RestoreFrame2(currentFrame2Field.Payload, frameBinding);
        }

        var scenePayloadBytes = 0L;

        for (var controlIndex = 0; controlIndex < controlRecordCount; controlIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var control = worldRecord.GetRecordArrayElement(8, controlIndex);

            if ((control.Header.SchemaId != s_controlRecordSchemaId)
                || (control.Header.Major != 1)
                || (control.Header.Minor != 0)
                || (control.FieldCount != 3)
                || (ReadControlInt64(control, 1) != (controlIndex + 1)))
            {
                throw new ArgumentException("A candidate control record does not match its validated sequence.", nameof(worldRecordMemory));
            }

            var sequence = ReadControlInt64(control, 1);
            var appliedAtNextTick = ReadControlInt64(control, 2);
            var payload = control.GetRecord(3);
            WorldControlRecord restoredControl;

            if (payload.Header.SchemaId == s_timeScaleControlSchemaId)
            {
                restoredControl = new WorldControlRecord(
                    sequence,
                    appliedAtNextTick,
                    WorldControlKind.TimeScale,
                    timeScaleValue: ReadControlFloat(payload, 1),
                    segmentSequence: ReadControlInt64(payload, 2));
            }
            else if (payload.Header.SchemaId == s_stepControlSchemaId)
            {
                restoredControl = new WorldControlRecord(
                    sequence,
                    appliedAtNextTick,
                    WorldControlKind.Step);
            }
            else if (payload.Header.SchemaId == s_frame2ControlSchemaId)
            {
                restoredControl = new WorldControlRecord(
                    sequence,
                    appliedAtNextTick,
                    WorldControlKind.Frame2,
                    frame2: RestoreFrame2(payload.GetRecord(1).EncodedBytes, frameBinding));
            }
            else if (payload.Header.SchemaId == s_frame3ControlSchemaId)
            {
                restoredControl = new WorldControlRecord(
                    sequence,
                    appliedAtNextTick,
                    WorldControlKind.Frame3,
                    frame3: RestoreFrame3(payload.GetRecord(1).EncodedBytes, frameBinding));
            }
            else if (payload.Header.SchemaId == s_branchControlSchemaId)
            {
                restoredControl = new WorldControlRecord(
                    sequence,
                    appliedAtNextTick,
                    WorldControlKind.Branch,
                    branchId: ReadControlGuid(payload, 1),
                    lineageId: ReadControlGuid(payload, 2),
                    branchIndex: ReadControlUInt16(payload, 3));
            }
            else if (payload.Header.SchemaId == s_sceneControlSchemaId)
            {
                var scenePayload = payload.EncodedBytes.ToArray();
                scenePayloadBytes = checked(scenePayloadBytes + scenePayload.Length);
                restoredControl = new WorldControlRecord(
                    sequence,
                    appliedAtNextTick,
                    WorldControlKind.Scene,
                    scenePayloadRecord: scenePayload);
            }
            else
            {
                throw new ArgumentException("A candidate control record has an unsupported payload schema.", nameof(worldRecordMemory));
            }

            _controlRecords[controlIndex] = restoredControl;
        }

        if (scenePayloadBytes != sceneControlPayloadBytes)
        {
            throw new ArgumentException("The restored Scene control payload bytes do not match their retained reservation.", nameof(worldRecordMemory));
        }

        _initialFrame2 = initialFrame2;
        _initialFrame3 = initialFrame3;
        _currentFrameCohort = new WorldFrameCohort(currentFrame3, currentFrame2, cohortGeneration: 1);
        _timeScale = clock.TimeScale;
        _timeScaleSegmentSequence = clock.CurrentSegmentSequence;
        _nextControlSequence = cursors.NextControlSequence;
        _controlRecordCount = controlRecordCount;
        _publicationSequence = checked(clock.NextPublicationSequence - 1);

        if (cursors.NextOwnerRequestSequence is long nextOwnerRequestSequence)
        {
            _nextOwnerRequestSequence = nextOwnerRequestSequence;
            _acceptedOwnerRequests = cursors.AcceptedOwnerRequests
                ?? throw new ArgumentException("The candidate owner-request cursor omitted its accepted count.", nameof(cursors));
            _completedOwnerRequests = _acceptedOwnerRequests;
            _committedOwnerRequests = _acceptedOwnerRequests;
        }

        _isRestoreClockStateRestored = true;
    }

    private FoundationPersistenceBinding CreateRestoreFrameBinding()
    {
        var profile = new FoundationProfileBinding(_spatialProfileId, _spatialSettings);
        var frame2 = _initialFrame2;
        var frame3 = _initialFrame3;
        return new FoundationPersistenceBinding(
            _worldGuid,
            profile,
            _ownerId,
            bindingRevision: 0,
            extentPolicy2: frame2 is SpatialFrame2 initialFrame2
                ? (minimum, maximum) => IsWithin(minimum, maximum, initialFrame2.Minimum, initialFrame2.Maximum)
                : null,
            extentPolicy3: (minimum, maximum) => IsWithin(minimum, maximum, frame3.Minimum, frame3.Maximum));
    }
}
