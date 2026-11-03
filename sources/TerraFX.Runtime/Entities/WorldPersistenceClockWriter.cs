// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using TerraFX;
using TerraFX.Numerics;

namespace TerraFX.Entities;

internal static class WorldPersistenceClockWriter
{
    private const int ClockFieldCapacity = 10;
    private const int TimeSegmentFieldCount = 3;
    private static readonly Guid s_clockSchemaId = new ContentSchemaIdentityAssignment("state/clock").SchemaId;
    private static readonly Guid s_timeSegmentSchemaId = new ContentSchemaIdentityAssignment("state/time-segment").SchemaId;

    internal static EbinWriteStatus MeasureWorkspace(
        WorldPersistenceCaptureLease lease,
        EbinReadLimits limits,
        out WorldPersistenceClockWorkspaceRequirements workspace,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(limits);
        workspace = default;
        diagnostic = null;
        lease.ValidateCurrent();

        var sourceStatus = ValidateClockSource(lease, cancellationToken, out var segmentCount, out _, out diagnostic);

        if (sourceStatus is not EbinWriteStatus.Complete)
        {
            return sourceStatus;
        }

        var segmentStatus = MeasureTimeSegmentRecord(
            sequence: 1,
            startNextTick: 0,
            timeScale: lease.InitialTimeScale,
            limits,
            out var segmentRecordBytes,
            out diagnostic);

        if (segmentStatus is not EbinWriteStatus.Complete)
        {
            return segmentStatus;
        }

        var segmentStorageBytes64 = checked((long)segmentCount * segmentRecordBytes);

        if (segmentStorageBytes64 > int.MaxValue)
        {
            diagnostic = CreateDiagnostic("record-byte-budget", checked((ulong)segmentStorageBytes64), checked((ulong)limits.MaximumRecordBytes));
            return EbinWriteStatus.InvalidRecord;
        }

        var segmentStorageBytes = checked((int)segmentStorageBytes64);
        var arrayStatus = WorldPersistenceRecordArrayLayout.MeasurePayloadBytes(
            segmentCount,
            segmentStorageBytes,
            limits,
            out var segmentArrayPayloadBytes,
            out diagnostic);

        if (arrayStatus is not EbinWriteStatus.Complete)
        {
            return arrayStatus;
        }

        var frame2 = lease.InitialFrame2;
        var currentFrame2 = lease.Frame2;

        if (frame2.HasValue != currentFrame2.HasValue)
        {
            diagnostic = CreateDiagnostic("initial-frame2-presence-mismatch", frame2.HasValue ? 1UL : 0UL, currentFrame2.HasValue ? 1UL : 0UL);
            return EbinWriteStatus.InvalidRecord;
        }

        var frame2Requirements = default(WorldPersistenceSpatialFrameWorkspaceRequirements);

        if (frame2 is { } initialFrame2)
        {
            var frame2Status = WorldPersistenceSpatialFrameWriter.MeasureFrame2(
                lease.WorldGuid,
                lease.OwnerId,
                initialFrame2,
                limits,
                out frame2Requirements,
                out diagnostic);

            if (frame2Status is not EbinWriteStatus.Complete)
            {
                return frame2Status;
            }
        }

        var frame3Status = WorldPersistenceSpatialFrameWriter.MeasureFrame3(
            lease.WorldGuid,
            lease.OwnerId,
            lease.InitialFrame3,
            limits,
            out var frame3Requirements,
            out diagnostic);

        if (frame3Status is not EbinWriteStatus.Complete)
        {
            return frame3Status;
        }

        if (frame2.HasValue && (frame2Requirements.ProfileRecordBytes != frame3Requirements.ProfileRecordBytes))
        {
            diagnostic = CreateDiagnostic(
                "frame-profile-record-size-mismatch",
                checked((ulong)frame2Requirements.ProfileRecordBytes),
                checked((ulong)frame3Requirements.ProfileRecordBytes));
            return EbinWriteStatus.InvalidRecord;
        }

        if ((lease.InitialFrame3.ProfileId != lease.Frame3.ProfileId)
            || (lease.InitialFrame3.Settings != lease.Frame3.Settings)
            || (frame2 is { } initialFrame2Profile
                && currentFrame2 is { } currentFrame2Profile
                && ((initialFrame2Profile.ProfileId != currentFrame2Profile.ProfileId)
                    || (initialFrame2Profile.Settings != currentFrame2Profile.Settings)))
            || (frame2 is { } initialFrame2Value
                && ((initialFrame2Value.ProfileId != lease.InitialFrame3.ProfileId)
                    || (initialFrame2Value.Settings != lease.InitialFrame3.Settings))))
        {
            diagnostic = CreateDiagnostic("initial-frame-profile-mismatch", 1, 0);
            return EbinWriteStatus.InvalidRecord;
        }

        var clockFieldCount = frame2.HasValue ? ClockFieldCapacity : ClockFieldCapacity - 1;

        if (limits.MaximumFieldsPerRecord < clockFieldCount)
        {
            diagnostic = CreateDiagnostic(
                "field-count-budget",
                checked((ulong)clockFieldCount),
                checked((ulong)limits.MaximumFieldsPerRecord));
            return EbinWriteStatus.InvalidRecord;
        }

        var framePayloadStorageBytes = frame2.HasValue
            ? Math.Max(frame2Requirements.FramePayloadStorageBytes, frame3Requirements.FramePayloadStorageBytes)
            : frame3Requirements.FramePayloadStorageBytes;
        var frameCandidateStorageBytes = frame2.HasValue
            ? Math.Max(frame2Requirements.RecordBytes, frame3Requirements.RecordBytes)
            : frame3Requirements.RecordBytes;
        var clockPayloadStorageBytes64 = GetClockPayloadBytes(
            segmentArrayPayloadBytes,
            frame2.HasValue ? frame2Requirements.RecordBytes : 0,
            frame3Requirements.RecordBytes);

        if (clockPayloadStorageBytes64 > int.MaxValue)
        {
            diagnostic = CreateDiagnostic("clock-payload-byte-budget", checked((ulong)clockPayloadStorageBytes64), int.MaxValue);
            return EbinWriteStatus.InvalidRecord;
        }

        var clockPayloadStorageBytes = checked((int)clockPayloadStorageBytes64);
        Span<int> fieldPayloadLengths = stackalloc int[ClockFieldCapacity];
        var fieldCount = BuildClockPayloadLengths(
            segmentArrayPayloadBytes,
            frame2.HasValue ? frame2Requirements.RecordBytes : 0,
            frame3Requirements.RecordBytes,
            fieldPayloadLengths);
        var clockRecordBytes64 = GetRecordBytes(fieldPayloadLengths[..fieldCount]);

        if (clockRecordBytes64 > limits.MaximumRecordBytes || clockRecordBytes64 > int.MaxValue)
        {
            diagnostic = CreateDiagnostic(
                "record-byte-budget",
                checked((ulong)clockRecordBytes64),
                checked((ulong)Math.Min(limits.MaximumRecordBytes, int.MaxValue)));
            return EbinWriteStatus.InvalidRecord;
        }

        var clockRecordBytes = checked((int)clockRecordBytes64);
        var frameProfileRecordBytes = frame3Requirements.ProfileRecordBytes;
        var workspaceBytes = GetWorkspaceBytes(
            segmentCount,
            segmentStorageBytes,
            segmentArrayPayloadBytes,
            segmentRecordBytes,
            frameProfileRecordBytes,
            framePayloadStorageBytes,
            frameCandidateStorageBytes,
            frame2.HasValue ? frame2Requirements.RecordBytes : 0,
            frame3Requirements.RecordBytes,
            clockPayloadStorageBytes,
            clockRecordBytes);

        workspace = new WorldPersistenceClockWorkspaceRequirements(
            lease.ControlRecordCount,
            segmentCount,
            segmentRecordBytes,
            segmentStorageBytes,
            segmentArrayPayloadBytes,
            frame2.HasValue,
            frame2Requirements,
            frame3Requirements,
            framePayloadStorageBytes,
            frameCandidateStorageBytes,
            clockPayloadStorageBytes,
            clockRecordBytes,
            workspaceBytes);

        diagnostic = null;
        return EbinWriteStatus.Complete;
    }

    internal static EbinWriteStatus Write(
        WorldPersistenceCaptureLease lease,
        WorldPersistenceClockWorkspaceRequirements workspace,
        EbinReadLimits limits,
        ref WorldPersistenceClockBuffers buffers,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(limits);
        bytesWritten = 0;
        diagnostic = null;
        lease.ValidateCurrent();

        var measureStatus = MeasureWorkspace(
            lease,
            limits,
            out var currentWorkspace,
            out diagnostic,
            cancellationToken);

        if (measureStatus is not EbinWriteStatus.Complete)
        {
            return measureStatus;
        }

        if (!AreSameWorkspace(workspace, currentWorkspace))
        {
            diagnostic = CreateDiagnostic(
                "workspace-requirements-stale",
                checked((ulong)workspace.RecordBytes),
                checked((ulong)currentWorkspace.RecordBytes));
            return EbinWriteStatus.InvalidRecord;
        }

        var workspaceStatus = ValidateBuffers(workspace, buffers, out diagnostic);

        if (workspaceStatus is not EbinWriteStatus.Complete)
        {
            return workspaceStatus;
        }

        var segmentStatus = WriteTimeSegment(
            sequence: 1,
            startNextTick: 0,
            timeScale: lease.InitialTimeScale,
            limits,
            buffers.SegmentCandidateStorage,
            buffers.SegmentStorage[..workspace.SegmentRecordBytes],
            out var bytesWrittenForSegment,
            out diagnostic);

        if (segmentStatus is not EbinWriteStatus.Complete)
        {
            return segmentStatus;
        }

        if (bytesWrittenForSegment != workspace.SegmentRecordBytes)
        {
            diagnostic = CreateDiagnostic("time-segment-record-length-changed", bytesWrittenForSegment, workspace.SegmentRecordBytes);
            return EbinWriteStatus.InvalidRecord;
        }

        buffers.SegmentSlices[0] = new EbinPayloadSlice(0, bytesWrittenForSegment);
        var segmentStorageOffset = bytesWrittenForSegment;
        var segmentIndex = 1;
        var expectedSegmentSequence = 2L;

        for (var controlIndex = 0; controlIndex < workspace.ControlRecordCount; controlIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var control = lease.GetControlCapture(controlIndex);

            if (control.Kind is not WorldPersistenceControlKind.TimeScale)
            {
                continue;
            }

            if (control.SegmentSequence != expectedSegmentSequence)
            {
                diagnostic = CreateDiagnostic(
                    "time-segment-sequence-mismatch",
                    checked((ulong)control.SegmentSequence),
                    checked((ulong)expectedSegmentSequence));
                return EbinWriteStatus.InvalidRecord;
            }

            var segmentDestination = buffers.SegmentStorage.Slice(segmentStorageOffset, workspace.SegmentRecordBytes);
            segmentStatus = WriteTimeSegment(
                control.SegmentSequence,
                control.AppliedAtNextTick,
                control.TimeScaleValue,
                limits,
                buffers.SegmentCandidateStorage,
                segmentDestination,
                out bytesWrittenForSegment,
                out diagnostic);

            if (segmentStatus is not EbinWriteStatus.Complete)
            {
                return segmentStatus;
            }

            if (bytesWrittenForSegment != workspace.SegmentRecordBytes)
            {
                diagnostic = CreateDiagnostic("time-segment-record-length-changed", bytesWrittenForSegment, workspace.SegmentRecordBytes);
                return EbinWriteStatus.InvalidRecord;
            }

            buffers.SegmentSlices[segmentIndex++] = new EbinPayloadSlice(segmentStorageOffset, bytesWrittenForSegment);
            segmentStorageOffset = checked(segmentStorageOffset + bytesWrittenForSegment);
            expectedSegmentSequence = checked(expectedSegmentSequence + 1);
        }

        if ((segmentIndex != workspace.SegmentCount) || (segmentStorageOffset != workspace.SegmentStorageBytes))
        {
            diagnostic = CreateDiagnostic("time-segment-count-mismatch", segmentIndex, workspace.SegmentCount);
            return EbinWriteStatus.InvalidRecord;
        }

        var arrayStatus = EbinPayloadWriter.WriteRecordArray(
            buffers.SegmentSlices,
            buffers.SegmentStorage,
            limits,
            buffers.SegmentArrayPayloadStorage,
            out var segmentArrayPayloadBytes,
            out diagnostic);

        if (arrayStatus is not EbinWriteStatus.Complete)
        {
            return arrayStatus;
        }

        if (segmentArrayPayloadBytes != workspace.SegmentArrayPayloadBytes)
        {
            diagnostic = CreateDiagnostic("time-segment-array-length-changed", segmentArrayPayloadBytes, workspace.SegmentArrayPayloadBytes);
            return EbinWriteStatus.InvalidRecord;
        }

        var frameBuffers = new WorldPersistenceSpatialFrameBuffers {
            FrameCandidateStorage = buffers.FrameCandidateStorage,
            FramePayloadStorage = buffers.FramePayloadStorage,
            ProfileCandidateStorage = buffers.FrameProfileCandidateStorage,
            ProfileStorage = buffers.FrameProfileStorage,
        };

        if (workspace.HasInitialFrame2)
        {
            frameBuffers.Destination = buffers.InitialFrame2Storage;
            var frame2Status = WorldPersistenceSpatialFrameWriter.WriteFrame2(
                lease.WorldGuid,
                lease.OwnerId,
                lease.InitialFrame2
                    ?? throw new InvalidOperationException("The measured clock workspace requires an initial Frame2."),
                workspace.InitialFrame2,
                limits,
                ref frameBuffers,
                out var frame2BytesWritten,
                out diagnostic);

            if (frame2Status is not EbinWriteStatus.Complete)
            {
                return frame2Status;
            }

            if (frame2BytesWritten != workspace.InitialFrame2.RecordBytes)
            {
                diagnostic = CreateDiagnostic("initial-frame2-record-length-changed", frame2BytesWritten, workspace.InitialFrame2.RecordBytes);
                return EbinWriteStatus.InvalidRecord;
            }
        }

        frameBuffers.Destination = buffers.InitialFrame3Storage;
        var frame3Status = WorldPersistenceSpatialFrameWriter.WriteFrame3(
            lease.WorldGuid,
            lease.OwnerId,
            lease.InitialFrame3,
            workspace.InitialFrame3,
            limits,
            ref frameBuffers,
            out var frame3BytesWritten,
            out diagnostic);

        if (frame3Status is not EbinWriteStatus.Complete)
        {
            return frame3Status;
        }

        if (frame3BytesWritten != workspace.InitialFrame3.RecordBytes)
        {
            diagnostic = CreateDiagnostic("initial-frame3-record-length-changed", frame3BytesWritten, workspace.InitialFrame3.RecordBytes);
            return EbinWriteStatus.InvalidRecord;
        }

        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[ClockFieldCapacity];
        var status = BuildClockFields(
            lease,
            workspace,
            buffers.SegmentArrayPayloadStorage[..segmentArrayPayloadBytes],
            workspace.HasInitialFrame2 ? buffers.InitialFrame2Storage : default,
            buffers.InitialFrame3Storage[..frame3BytesWritten],
            fields,
            buffers.ClockPayloadStorage,
            out var fieldCount,
            out var payloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (payloadBytes != workspace.ClockPayloadStorageBytes)
        {
            diagnostic = CreateDiagnostic("clock-payload-length-changed", payloadBytes, workspace.ClockPayloadStorageBytes);
            return EbinWriteStatus.InvalidRecord;
        }

        var source = new EbinRecordWriteView(
            s_clockSchemaId,
            major: 1,
            minor: 0,
            fields[..fieldCount],
            buffers.ClockPayloadStorage[..payloadBytes]);
        var rootMeasureStatus = EbinStructuralWriter.Measure(in source, limits, out var measuredRecordBytes, out diagnostic);

        if (rootMeasureStatus is not EbinWriteStatus.Complete)
        {
            return rootMeasureStatus;
        }

        if (measuredRecordBytes != workspace.RecordBytes)
        {
            diagnostic = CreateDiagnostic("clock-record-length-changed", measuredRecordBytes, workspace.RecordBytes);
            return EbinWriteStatus.InvalidRecord;
        }

        return EbinStructuralWriter.Write(
            in source,
            limits,
            buffers.ClockCandidateStorage,
            buffers.Destination,
            out bytesWritten,
            out diagnostic);
    }

    private static EbinWriteStatus AddBooleanField(
        uint tag,
        bool value,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = EbinPayloadWriter.Write(value, payloadStorage[payloadBytes..], out var fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(tag, 9, 1, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus AddFloatField(
        uint tag,
        float value,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = EbinPayloadWriter.Write(value, payloadStorage[payloadBytes..], out var fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(tag, 10, 1, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus AddInt64Field(
        uint tag,
        long value,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = EbinPayloadWriter.Write(value, payloadStorage[payloadBytes..], out var fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(tag, 8, 1, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus AddRecordArrayField(
        uint tag,
        int count,
        scoped ReadOnlySpan<byte> recordArray,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        if (recordArray.Length > payloadStorage.Length - payloadBytes)
        {
            diagnostic = CreateDiagnostic("workspace-too-small", payloadStorage.Length, checked(payloadBytes + recordArray.Length));
            return EbinWriteStatus.WorkspaceTooSmall;
        }

        recordArray.CopyTo(payloadStorage[payloadBytes..]);
        fields[fieldCount++] = new EbinFieldSource(
            tag,
            24,
            1,
            checked((ulong)count),
            new EbinPayloadSlice(payloadBytes, recordArray.Length));
        payloadBytes = checked(payloadBytes + recordArray.Length);
        diagnostic = null;
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus AddRecordField(
        uint tag,
        scoped ReadOnlySpan<byte> record,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        if (record.Length > payloadStorage.Length - payloadBytes)
        {
            diagnostic = CreateDiagnostic("workspace-too-small", payloadStorage.Length, checked(payloadBytes + record.Length));
            return EbinWriteStatus.WorkspaceTooSmall;
        }

        record.CopyTo(payloadStorage[payloadBytes..]);
        fields[fieldCount++] = new EbinFieldSource(tag, 22, 1, 1, new EbinPayloadSlice(payloadBytes, record.Length));
        payloadBytes = checked(payloadBytes + record.Length);
        diagnostic = null;
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus AddUInt32Field(
        uint tag,
        uint value,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = EbinPayloadWriter.Write(value, payloadStorage[payloadBytes..], out var fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(tag, 3, 1, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static long Align16(long value)
        => checked((value + 15L) & ~15L);

    private static bool AreSameBits(float left, float right)
        => BitConverter.SingleToInt32Bits(left) == BitConverter.SingleToInt32Bits(right);

    private static bool AreSameFrame2Bits(SpatialFrame2 left, SpatialFrame2 right)
        => (left.OwnerId == right.OwnerId)
        && (left.ProfileId == right.ProfileId)
        && (left.Settings == right.Settings)
        && (left.Revision == right.Revision)
        && left.Origin.Chunk.Equals(right.Origin.Chunk)
        && AreSameBits(left.Origin.Offset.X, right.Origin.Offset.X)
        && AreSameBits(left.Origin.Offset.Y, right.Origin.Offset.Y)
        && AreSameBits(left.Minimum.X, right.Minimum.X)
        && AreSameBits(left.Minimum.Y, right.Minimum.Y)
        && AreSameBits(left.Maximum.X, right.Maximum.X)
        && AreSameBits(left.Maximum.Y, right.Maximum.Y);

    private static bool AreSameFrame3Bits(SpatialFrame3 left, SpatialFrame3 right)
        => (left.OwnerId == right.OwnerId)
        && (left.ProfileId == right.ProfileId)
        && (left.Settings == right.Settings)
        && (left.Revision == right.Revision)
        && left.Origin.Chunk.Equals(right.Origin.Chunk)
        && AreSameBits(left.Origin.Offset.X, right.Origin.Offset.X)
        && AreSameBits(left.Origin.Offset.Y, right.Origin.Offset.Y)
        && AreSameBits(left.Origin.Offset.Z, right.Origin.Offset.Z)
        && AreSameBits(left.Minimum.X, right.Minimum.X)
        && AreSameBits(left.Minimum.Y, right.Minimum.Y)
        && AreSameBits(left.Minimum.Z, right.Minimum.Z)
        && AreSameBits(left.Maximum.X, right.Maximum.X)
        && AreSameBits(left.Maximum.Y, right.Maximum.Y)
        && AreSameBits(left.Maximum.Z, right.Maximum.Z);

    private static bool AreSameWorkspace(
        WorldPersistenceClockWorkspaceRequirements left,
        WorldPersistenceClockWorkspaceRequirements right)
        => (left.ControlRecordCount == right.ControlRecordCount)
        && (left.SegmentCount == right.SegmentCount)
        && (left.SegmentRecordBytes == right.SegmentRecordBytes)
        && (left.SegmentStorageBytes == right.SegmentStorageBytes)
        && (left.SegmentArrayPayloadBytes == right.SegmentArrayPayloadBytes)
        && (left.HasInitialFrame2 == right.HasInitialFrame2)
        && (left.InitialFrame2.RecordBytes == right.InitialFrame2.RecordBytes)
        && (left.InitialFrame3.RecordBytes == right.InitialFrame3.RecordBytes)
        && (left.FramePayloadStorageBytes == right.FramePayloadStorageBytes)
        && (left.FrameCandidateStorageBytes == right.FrameCandidateStorageBytes)
        && (left.ClockPayloadStorageBytes == right.ClockPayloadStorageBytes)
        && (left.RecordBytes == right.RecordBytes)
        && (left.WorkspaceBytes == right.WorkspaceBytes);

    private static EbinWriteStatus BuildClockFields(
        WorldPersistenceCaptureLease lease,
        WorldPersistenceClockWorkspaceRequirements workspace,
        scoped ReadOnlySpan<byte> segmentArray,
        scoped ReadOnlySpan<byte> initialFrame2,
        scoped ReadOnlySpan<byte> initialFrame3,
        Span<EbinFieldSource> fields,
        Span<byte> payloadStorage,
        out int fieldCount,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        fields.Clear();
        payloadStorage.Clear();
        fieldCount = 0;
        payloadBytes = 0;
        var nextPublicationSequence = checked(lease.NextTickIndex + 1);
        var status = AddInt64Field(1, lease.NextTickIndex, fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddUInt32Field(2, checked((uint)lease.TickRate), fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddFloatField(3, lease.TimeScale, fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddInt64Field(4, nextPublicationSequence, fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddRecordArrayField(
            5,
            workspace.SegmentCount,
            segmentArray,
            fields,
            ref fieldCount,
            payloadStorage,
            ref payloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddBooleanField(6, value: false, fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddInt64Field(7, lease.TimeScaleSegmentSequence, fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (workspace.HasInitialFrame2)
        {
            status = AddRecordField(8, initialFrame2, fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }
        }

        status = AddRecordField(9, initialFrame3, fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        return AddInt64Field(10, value: 0, fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);
    }

    private static int BuildClockPayloadLengths(
        int segmentArrayPayloadBytes,
        int initialFrame2RecordBytes,
        int initialFrame3RecordBytes,
        Span<int> payloadLengths)
    {
        payloadLengths.Clear();
        var fieldCount = 0;
        payloadLengths[fieldCount++] = sizeof(long);
        payloadLengths[fieldCount++] = sizeof(uint);
        payloadLengths[fieldCount++] = sizeof(float);
        payloadLengths[fieldCount++] = sizeof(long);
        payloadLengths[fieldCount++] = segmentArrayPayloadBytes;
        payloadLengths[fieldCount++] = sizeof(byte);
        payloadLengths[fieldCount++] = sizeof(long);

        if (initialFrame2RecordBytes != 0)
        {
            payloadLengths[fieldCount++] = initialFrame2RecordBytes;
        }

        payloadLengths[fieldCount++] = initialFrame3RecordBytes;
        payloadLengths[fieldCount++] = sizeof(long);
        return fieldCount;
    }

    private static EbinWriteStatus BuildTimeSegmentFields(
        long sequence,
        long startNextTick,
        float timeScale,
        Span<EbinFieldSource> fields,
        Span<byte> payloadStorage,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        fields.Clear();
        payloadStorage.Clear();
        payloadBytes = 0;
        var fieldCount = 0;
        var status = AddInt64Field(1, sequence, fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddInt64Field(2, startNextTick, fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        return AddFloatField(3, timeScale, fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);
    }

    private static EbinStructuralDiagnostic CreateDiagnostic(string category, int claim, int limit)
        => CreateDiagnostic(category, checked((ulong)claim), checked((ulong)limit));

    private static EbinStructuralDiagnostic CreateDiagnostic(string category, ulong claim, ulong limit)
        => new(
            s_clockSchemaId,
            0,
            category,
            null,
            claim,
            limit,
            EbinStructuralDiagnosticSource.Construction);

    private static long GetClockPayloadBytes(int segmentArrayBytes, int frame2Bytes, int frame3Bytes)
        => checked(41L + segmentArrayBytes + frame2Bytes + frame3Bytes);

    private static long GetRecordBytes(scoped ReadOnlySpan<int> payloadLengths)
    {
        var recordBytes = Align16(checked(64L + (checked((long)payloadLengths.Length) * 32L)));

        for (var index = 0; index < payloadLengths.Length; index++)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(payloadLengths[index]);
            recordBytes = checked(Align16(recordBytes) + payloadLengths[index]);
        }

        return Align16(recordBytes);
    }

    private static long GetWorkspaceBytes(
        int segmentCount,
        int segmentStorageBytes,
        int segmentArrayPayloadBytes,
        int segmentRecordBytes,
        int frameProfileRecordBytes,
        int framePayloadStorageBytes,
        int frameCandidateStorageBytes,
        int initialFrame2Bytes,
        int initialFrame3Bytes,
        int clockPayloadStorageBytes,
        int clockRecordBytes)
    {
        long bytes = 0;
        bytes = checked(bytes + ((long)segmentCount * Unsafe.SizeOf<EbinPayloadSlice>()));
        bytes = checked(bytes + segmentStorageBytes);
        bytes = checked(bytes + segmentArrayPayloadBytes);
        bytes = checked(bytes + segmentRecordBytes);
        bytes = checked(bytes + (2L * frameProfileRecordBytes));
        bytes = checked(bytes + framePayloadStorageBytes);
        bytes = checked(bytes + frameCandidateStorageBytes);
        bytes = checked(bytes + initialFrame2Bytes);
        bytes = checked(bytes + initialFrame3Bytes);
        bytes = checked(bytes + clockPayloadStorageBytes);
        bytes = checked(bytes + (2L * clockRecordBytes));
        return bytes;
    }

    private static bool IsValidTimeScale(float value)
        => float.IsFinite(value) && (value >= 0.0f) && (value <= 4.0f);

    private static EbinWriteStatus MeasureTimeSegmentRecord(
        long sequence,
        long startNextTick,
        float timeScale,
        EbinReadLimits limits,
        out int recordBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[TimeSegmentFieldCount];
        Span<byte> payloadStorage = stackalloc byte[32];
        var status = BuildTimeSegmentFields(sequence, startNextTick, timeScale, fields, payloadStorage, out var payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            recordBytes = 0;
            return status;
        }

        var source = new EbinRecordWriteView(
            s_timeSegmentSchemaId,
            major: 1,
            minor: 0,
            fields,
            payloadStorage[..payloadBytes]);
        return EbinStructuralWriter.Measure(in source, limits, out recordBytes, out diagnostic);
    }

    private static EbinWriteStatus ValidateBuffers(
        WorldPersistenceClockWorkspaceRequirements workspace,
        WorldPersistenceClockBuffers buffers,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var spanStatus = ValidateLength(buffers.SegmentSlices.Length, workspace.SegmentCount, out diagnostic);

        if (spanStatus is not EbinWriteStatus.Complete)
        {
            return spanStatus;
        }

        spanStatus = ValidateLength(buffers.SegmentStorage.Length, workspace.SegmentStorageBytes, out diagnostic);

        if (spanStatus is not EbinWriteStatus.Complete)
        {
            return spanStatus;
        }

        spanStatus = ValidateLength(buffers.SegmentArrayPayloadStorage.Length, workspace.SegmentArrayPayloadBytes, out diagnostic);

        if (spanStatus is not EbinWriteStatus.Complete)
        {
            return spanStatus;
        }

        spanStatus = ValidateLength(buffers.SegmentCandidateStorage.Length, workspace.SegmentRecordBytes, out diagnostic);

        if (spanStatus is not EbinWriteStatus.Complete)
        {
            return spanStatus;
        }

        spanStatus = ValidateLength(buffers.FrameProfileCandidateStorage.Length, workspace.InitialFrame3.ProfileRecordBytes, out diagnostic);

        if (spanStatus is not EbinWriteStatus.Complete)
        {
            return spanStatus;
        }

        spanStatus = ValidateLength(buffers.FrameProfileStorage.Length, workspace.InitialFrame3.ProfileRecordBytes, out diagnostic);

        if (spanStatus is not EbinWriteStatus.Complete)
        {
            return spanStatus;
        }

        spanStatus = ValidateLength(buffers.FramePayloadStorage.Length, workspace.FramePayloadStorageBytes, out diagnostic);

        if (spanStatus is not EbinWriteStatus.Complete)
        {
            return spanStatus;
        }

        spanStatus = ValidateLength(buffers.FrameCandidateStorage.Length, workspace.FrameCandidateStorageBytes, out diagnostic);

        if (spanStatus is not EbinWriteStatus.Complete)
        {
            return spanStatus;
        }

        spanStatus = ValidateLength(buffers.InitialFrame2Storage.Length, workspace.HasInitialFrame2 ? workspace.InitialFrame2.RecordBytes : 0, out diagnostic);

        if (spanStatus is not EbinWriteStatus.Complete)
        {
            return spanStatus;
        }

        spanStatus = ValidateLength(buffers.InitialFrame3Storage.Length, workspace.InitialFrame3.RecordBytes, out diagnostic);

        if (spanStatus is not EbinWriteStatus.Complete)
        {
            return spanStatus;
        }

        spanStatus = ValidateLength(buffers.ClockPayloadStorage.Length, workspace.ClockPayloadStorageBytes, out diagnostic);

        if (spanStatus is not EbinWriteStatus.Complete)
        {
            return spanStatus;
        }

        spanStatus = ValidateLength(buffers.ClockCandidateStorage.Length, workspace.RecordBytes, out diagnostic);

        if (spanStatus is not EbinWriteStatus.Complete)
        {
            return spanStatus;
        }

        return ValidateLength(buffers.Destination.Length, workspace.RecordBytes, out diagnostic);
    }

    private static EbinWriteStatus ValidateClockSource(
        WorldPersistenceCaptureLease lease,
        CancellationToken cancellationToken,
        out int segmentCount,
        out float finalTimeScale,
        out EbinStructuralDiagnostic? diagnostic)
    {
        segmentCount = 0;
        finalTimeScale = 0;
        diagnostic = null;
        var nextTick = lease.NextTickIndex;

        if (nextTick < 1)
        {
            diagnostic = CreateDiagnostic("next-tick-range", checked((ulong)Math.Max(nextTick, 0)), 1);
            return EbinWriteStatus.InvalidRecord;
        }

        if (lease.TickRate is < 1 or > 1000)
        {
            diagnostic = CreateDiagnostic("tick-rate-range", checked((ulong)Math.Max(lease.TickRate, 0)), 1000);
            return EbinWriteStatus.InvalidRecord;
        }

        if (!IsValidTimeScale(lease.InitialTimeScale) || !IsValidTimeScale(lease.TimeScale))
        {
            diagnostic = CreateDiagnostic("time-scale-range", unchecked((uint)BitConverter.SingleToInt32Bits(lease.TimeScale)), 4);
            return EbinWriteStatus.InvalidRecord;
        }

        if (lease.NextControlSequence != checked((long)lease.ControlRecordCount + 1))
        {
            diagnostic = CreateDiagnostic(
                "next-control-sequence-mismatch",
                checked((ulong)lease.NextControlSequence),
                checked((ulong)lease.ControlRecordCount + 1UL));
            return EbinWriteStatus.InvalidRecord;
        }

        long previousAppliedAtNextTick = 0;
        var hasPreviousControl = false;
        var timeScaleControlCount = 0;
        finalTimeScale = lease.InitialTimeScale;
        segmentCount = 1;
        var currentFrame2 = lease.InitialFrame2;
        var currentFrame3 = lease.InitialFrame3;

        for (var index = 0; index < lease.ControlRecordCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var control = lease.GetControlCapture(index);

            if (control.Sequence != checked((long)index + 1))
            {
                diagnostic = CreateDiagnostic(
                    "control-sequence-mismatch",
                    checked((ulong)control.Sequence),
                    checked((ulong)index + 1));
                return EbinWriteStatus.InvalidRecord;
            }

            if ((control.AppliedAtNextTick < 0)
                || (control.AppliedAtNextTick >= nextTick)
                || (hasPreviousControl && (control.AppliedAtNextTick < previousAppliedAtNextTick)))
            {
                diagnostic = CreateDiagnostic(
                    "control-next-tick-order",
                    checked((ulong)Math.Max(control.AppliedAtNextTick, 0)),
                    checked((ulong)nextTick));
                return EbinWriteStatus.InvalidRecord;
            }

            previousAppliedAtNextTick = control.AppliedAtNextTick;
            hasPreviousControl = true;

            if (control.Kind is WorldPersistenceControlKind.TimeScale)
            {
                if (!IsValidTimeScale(control.TimeScaleValue))
                {
                    diagnostic = CreateDiagnostic("time-scale-range", unchecked((uint)BitConverter.SingleToInt32Bits(control.TimeScaleValue)), 4);
                    return EbinWriteStatus.InvalidRecord;
                }

                var expectedSequence = checked((long)timeScaleControlCount + 2);

                if (control.SegmentSequence != expectedSequence)
                {
                    diagnostic = CreateDiagnostic(
                        "time-segment-sequence-mismatch",
                        checked((ulong)control.SegmentSequence),
                        checked((ulong)expectedSequence));
                    return EbinWriteStatus.InvalidRecord;
                }

                timeScaleControlCount = checked(timeScaleControlCount + 1);
                segmentCount = checked(segmentCount + 1);
                finalTimeScale = control.TimeScaleValue;
            }
            else if (control.Kind is WorldPersistenceControlKind.Frame2)
            {
                if ((currentFrame2 is not { } previousFrame)
                    || (control.Frame2 is not { } candidateFrame))
                {
                    diagnostic = CreateDiagnostic("frame2-control-without-initial-frame", 1, 0);
                    return EbinWriteStatus.InvalidRecord;
                }

                var frameStatus = ValidateFrame2Transition(
                    previousFrame,
                    candidateFrame,
                    lease.OwnerId,
                    lease.InitialFrame3.ProfileId,
                    lease.InitialFrame3.Settings,
                    out diagnostic);

                if (frameStatus is not EbinWriteStatus.Complete)
                {
                    return frameStatus;
                }

                currentFrame2 = candidateFrame;
            }
            else if (control.Kind is WorldPersistenceControlKind.Frame3)
            {
                if (control.Frame3 is not { } candidateFrame)
                {
                    diagnostic = CreateDiagnostic("frame3-control-missing-frame", 0, 1);
                    return EbinWriteStatus.InvalidRecord;
                }

                var frameStatus = ValidateFrame3Transition(
                    currentFrame3,
                    candidateFrame,
                    lease.OwnerId,
                    lease.InitialFrame3.ProfileId,
                    lease.InitialFrame3.Settings,
                    out diagnostic);

                if (frameStatus is not EbinWriteStatus.Complete)
                {
                    return frameStatus;
                }

                currentFrame3 = candidateFrame;
            }
            else if (control.Kind is not WorldPersistenceControlKind.Branch)
            {
                diagnostic = CreateDiagnostic("unknown-control-kind", checked((ulong)control.Kind), 3);
                return EbinWriteStatus.InvalidRecord;
            }
        }

        if (!AreSameFrame3Bits(currentFrame3, lease.Frame3))
        {
            diagnostic = CreateDiagnostic(
                "current-frame3-control-mismatch",
                checked((ulong)currentFrame3.Revision),
                checked((ulong)lease.Frame3.Revision));
            return EbinWriteStatus.InvalidRecord;
        }

        if (currentFrame2.HasValue != lease.Frame2.HasValue)
        {
            diagnostic = CreateDiagnostic(
                "current-frame2-control-mismatch",
                currentFrame2.HasValue ? 1UL : 0UL,
                lease.Frame2.HasValue ? 1UL : 0UL);
            return EbinWriteStatus.InvalidRecord;
        }

        if ((currentFrame2 is { } finalFrame2)
            && (lease.Frame2 is { } capturedFrame2)
            && !AreSameFrame2Bits(finalFrame2, capturedFrame2))
        {
            diagnostic = CreateDiagnostic(
                "current-frame2-control-mismatch",
                checked((ulong)finalFrame2.Revision),
                checked((ulong)capturedFrame2.Revision));
            return EbinWriteStatus.InvalidRecord;
        }

        if (lease.TimeScaleSegmentSequence != segmentCount)
        {
            diagnostic = CreateDiagnostic(
                "current-segment-sequence-mismatch",
                checked((ulong)lease.TimeScaleSegmentSequence),
                checked((ulong)segmentCount));
            return EbinWriteStatus.InvalidRecord;
        }

        if (BitConverter.SingleToInt32Bits(finalTimeScale) != BitConverter.SingleToInt32Bits(lease.TimeScale))
        {
            diagnostic = CreateDiagnostic(
                "clock-time-scale-segment-mismatch",
                unchecked((uint)BitConverter.SingleToInt32Bits(lease.TimeScale)),
                unchecked((uint)BitConverter.SingleToInt32Bits(finalTimeScale)));
            return EbinWriteStatus.InvalidRecord;
        }

        if (nextTick == long.MaxValue)
        {
            diagnostic = CreateDiagnostic(
                "next-publication-sequence-range",
                checked((ulong)long.MaxValue),
                checked((ulong)(long.MaxValue - 1)));
            return EbinWriteStatus.InvalidRecord;
        }

        diagnostic = null;
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus ValidateFrame2Transition(
        SpatialFrame2 previous,
        SpatialFrame2 candidate,
        uint ownerId,
        SpatialProfileId profileId,
        SpatialSettings settings,
        out EbinStructuralDiagnostic? diagnostic)
    {
        diagnostic = null;

        if ((candidate.OwnerId != ownerId)
            || (candidate.ProfileId != profileId)
            || (candidate.Settings != settings))
        {
            diagnostic = CreateDiagnostic("frame2-control-profile-mismatch", candidate.OwnerId, ownerId);
            return EbinWriteStatus.InvalidRecord;
        }

        if (previous.Revision == long.MaxValue || (candidate.Revision != checked(previous.Revision + 1)))
        {
            diagnostic = CreateDiagnostic("frame2-control-revision", checked((ulong)candidate.Revision), checked((ulong)Math.Min(previous.Revision, long.MaxValue - 1) + 1));
            return EbinWriteStatus.InvalidRecord;
        }

        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus ValidateFrame3Transition(
        SpatialFrame3 previous,
        SpatialFrame3 candidate,
        uint ownerId,
        SpatialProfileId profileId,
        SpatialSettings settings,
        out EbinStructuralDiagnostic? diagnostic)
    {
        diagnostic = null;

        if ((candidate.OwnerId != ownerId)
            || (candidate.ProfileId != profileId)
            || (candidate.Settings != settings))
        {
            diagnostic = CreateDiagnostic("frame3-control-profile-mismatch", candidate.OwnerId, ownerId);
            return EbinWriteStatus.InvalidRecord;
        }

        if (previous.Revision == long.MaxValue || (candidate.Revision != checked(previous.Revision + 1)))
        {
            diagnostic = CreateDiagnostic("frame3-control-revision", checked((ulong)candidate.Revision), checked((ulong)Math.Min(previous.Revision, long.MaxValue - 1) + 1));
            return EbinWriteStatus.InvalidRecord;
        }

        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus ValidateLength(int actual, int expected, out EbinStructuralDiagnostic? diagnostic)
    {
        if (actual == expected)
        {
            diagnostic = null;
            return EbinWriteStatus.Complete;
        }

        diagnostic = CreateDiagnostic("workspace-length-mismatch", checked((ulong)actual), checked((ulong)expected));
        return (actual < expected) ? EbinWriteStatus.WorkspaceTooSmall : EbinWriteStatus.InvalidRecord;
    }

    private static EbinWriteStatus WriteTimeSegment(
        long sequence,
        long startNextTick,
        float timeScale,
        EbinReadLimits limits,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        bytesWritten = 0;

        if (sequence < 1)
        {
            diagnostic = CreateDiagnostic("time-segment-sequence-range", checked((ulong)Math.Max(sequence, 0)), long.MaxValue);
            return EbinWriteStatus.InvalidRecord;
        }

        if (startNextTick < 0)
        {
            diagnostic = CreateDiagnostic("time-segment-start-range", 0, long.MaxValue);
            return EbinWriteStatus.InvalidRecord;
        }

        if (!IsValidTimeScale(timeScale))
        {
            diagnostic = CreateDiagnostic("time-segment-scale-range", unchecked((uint)BitConverter.SingleToInt32Bits(timeScale)), 4);
            return EbinWriteStatus.InvalidRecord;
        }

        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[TimeSegmentFieldCount];
        Span<byte> payloadStorage = stackalloc byte[32];
        var status = BuildTimeSegmentFields(sequence, startNextTick, timeScale, fields, payloadStorage, out var payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var source = new EbinRecordWriteView(
            s_timeSegmentSchemaId,
            major: 1,
            minor: 0,
            fields,
            payloadStorage[..payloadBytes]);
        return EbinStructuralWriter.Write(
            in source,
            limits,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }
}
