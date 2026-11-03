// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;
using TerraFX;
using TerraFX.Numerics;

namespace TerraFX.Entities;

internal static class WorldPersistenceControlRecordWriter
{
    private static readonly Guid s_branchSchemaId = new ContentSchemaIdentityAssignment("state/control-branch").SchemaId;
    private static readonly Guid s_frame2SchemaId = new ContentSchemaIdentityAssignment("state/control-frame2").SchemaId;
    private static readonly Guid s_frame3SchemaId = new ContentSchemaIdentityAssignment("state/control-frame3").SchemaId;
    private static readonly Guid s_schemaId = new ContentSchemaIdentityAssignment("state/control").SchemaId;
    private static readonly Guid s_timeScaleSchemaId = new ContentSchemaIdentityAssignment("state/control-timescale").SchemaId;

    internal static EbinWriteStatus Measure(
        WorldPersistenceCaptureLease lease,
        WorldPersistenceControlCaptureRow control,
        EbinReadLimits limits,
        out WorldPersistenceControlRecordWorkspaceRequirements workspace,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(limits);
        workspace = default;
        diagnostic = null;
        lease.ValidateCurrent();
        ValidateControl(control);
        var frameWorkspace = MeasureFrame(lease, control, limits, out var payloadCapacity, out diagnostic);

        if (diagnostic is not null)
        {
            return EbinWriteStatus.InvalidRecord;
        }

        Span<byte> payloadRecordStorage = stackalloc byte[payloadCapacity];
        var payloadStatus = WritePayloadRecord(
            lease,
            control,
            limits,
            payloadRecordStorage,
            out var payloadRecordBytes,
            out var payloadScratchBytes,
            out diagnostic);

        if (payloadStatus is not EbinWriteStatus.Complete)
        {
            return payloadStatus;
        }

        var rootPayloadBytes = checked(payloadRecordBytes + 16);
        Span<byte> rootPayloadStorage = stackalloc byte[rootPayloadBytes];
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[3];
        var buildStatus = BuildFields(
            control,
            payloadRecordStorage[..payloadRecordBytes],
            fields,
            rootPayloadStorage[..rootPayloadBytes],
            out var fieldCount,
            out var payloadBytes,
            out diagnostic);

        if (buildStatus is not EbinWriteStatus.Complete)
        {
            return buildStatus;
        }

        if (payloadBytes != rootPayloadBytes)
        {
            throw new InvalidOperationException("The state/control root payload extent changed after measurement.");
        }

        var source = new EbinRecordWriteView(
            s_schemaId,
            major: 1,
            minor: 0,
            fields[..fieldCount],
            rootPayloadStorage[..payloadBytes]);
        var status = EbinStructuralWriter.Measure(in source, limits, out var recordBytes, out diagnostic);

        if (status is EbinWriteStatus.Complete)
        {
            workspace = new WorldPersistenceControlRecordWorkspaceRequirements(
                control.Kind,
                payloadRecordBytes,
                rootPayloadBytes,
                recordBytes,
                checked(
                    payloadScratchBytes
                    + payloadCapacity
                    + (3L * Unsafe.SizeOf<EbinFieldSource>())
                    + sizeof(long)),
                frameWorkspace);
        }

        return status;
    }

    internal static EbinWriteStatus Write(
        WorldPersistenceCaptureLease lease,
        WorldPersistenceControlCaptureRow control,
        WorldPersistenceControlRecordWorkspaceRequirements workspace,
        EbinReadLimits limits,
        scoped Span<byte> rootPayloadStorage,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(limits);
        bytesWritten = 0;
        lease.ValidateCurrent();
        var measureStatus = Measure(
            lease,
            control,
            limits,
            out var measuredWorkspace,
            out diagnostic);

        if (measureStatus is not EbinWriteStatus.Complete)
        {
            return measureStatus;
        }

        if (!workspace.Matches(measuredWorkspace))
        {
            diagnostic = CreateDiagnostic("control-workspace-mismatch");
            return EbinWriteStatus.InvalidRecord;
        }

        if (rootPayloadStorage.Length != workspace.RootPayloadBytes)
        {
            throw new ArgumentException("The state/control payload workspace must match its measured extent.", nameof(rootPayloadStorage));
        }

        if ((candidateStorage.Length != workspace.RecordBytes)
            || (destination.Length != workspace.RecordBytes))
        {
            throw new ArgumentException("The state/control record buffers must match the measured extent.");
        }

        _ = MeasureFrame(lease, control, limits, out var payloadCapacity, out diagnostic);

        if (diagnostic is not null)
        {
            return EbinWriteStatus.InvalidRecord;
        }

        Span<byte> payloadRecordStorage = stackalloc byte[payloadCapacity];
        var payloadStatus = WritePayloadRecord(
            lease,
            control,
            limits,
            payloadRecordStorage,
            out var payloadRecordBytes,
            out _,
            out diagnostic);

        if (payloadStatus is not EbinWriteStatus.Complete)
        {
            return payloadStatus;
        }

        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[3];
        var buildStatus = BuildFields(
            control,
            payloadRecordStorage[..payloadRecordBytes],
            fields,
            rootPayloadStorage,
            out var fieldCount,
            out var payloadBytes,
            out diagnostic);

        if (buildStatus is not EbinWriteStatus.Complete)
        {
            return buildStatus;
        }

        if ((payloadRecordBytes != workspace.PayloadRecordBytes)
            || (payloadBytes != workspace.RootPayloadBytes)
            || (fieldCount != fields.Length))
        {
            throw new InvalidOperationException("The state/control record changed after workspace measurement.");
        }

        var source = new EbinRecordWriteView(
            s_schemaId,
            major: 1,
            minor: 0,
            fields[..fieldCount],
            rootPayloadStorage[..payloadBytes]);
        return EbinStructuralWriter.Write(
            in source,
            limits,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }

    private static EbinWriteStatus AddGuidField(
        uint tag,
        Guid value,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadOffset,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = EbinPayloadWriter.Write(value, payloadStorage[payloadOffset..], out var fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(tag, 11, 1, 1, new EbinPayloadSlice(payloadOffset, fieldBytes));
        payloadOffset = checked(payloadOffset + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus AddInt64Field(
        uint tag,
        long value,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadOffset,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = EbinPayloadWriter.Write(value, payloadStorage[payloadOffset..], out var fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(tag, 8, 1, 1, new EbinPayloadSlice(payloadOffset, fieldBytes));
        payloadOffset = checked(payloadOffset + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus BuildFields(
        WorldPersistenceControlCaptureRow control,
        scoped ReadOnlySpan<byte> controlPayloadRecord,
        Span<EbinFieldSource> fields,
        Span<byte> rootPayloadStorage,
        out int fieldCount,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        fields.Clear();
        rootPayloadStorage.Clear();
        payloadBytes = 0;
        fieldCount = 0;
        var status = AddInt64Field(
            1,
            control.Sequence,
            fields,
            ref fieldCount,
            rootPayloadStorage,
            ref payloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddInt64Field(
            2,
            control.AppliedAtNextTick,
            fields,
            ref fieldCount,
            rootPayloadStorage,
            ref payloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (controlPayloadRecord.Length <= 0)
        {
            diagnostic = CreateDiagnostic("control-payload-record-size");
            return EbinWriteStatus.InvalidRecord;
        }

        controlPayloadRecord.CopyTo(rootPayloadStorage[payloadBytes..]);
        fields[fieldCount++] = new EbinFieldSource(
            3,
            22,
            1,
            1,
            new EbinPayloadSlice(payloadBytes, controlPayloadRecord.Length));
        payloadBytes = checked(payloadBytes + controlPayloadRecord.Length);
        diagnostic = null;
        return EbinWriteStatus.Complete;
    }

    private static EbinStructuralDiagnostic CreateDiagnostic(string category, ulong claim = 0, ulong limit = 1)
        => new(
            s_schemaId,
            1,
            category,
            null,
            claim,
            limit,
            EbinStructuralDiagnosticSource.Construction);

    private static WorldPersistenceSpatialFrameWorkspaceRequirements MeasureFrame(
        WorldPersistenceCaptureLease lease,
        WorldPersistenceControlCaptureRow control,
        EbinReadLimits limits,
        out int payloadCapacity,
        out EbinStructuralDiagnostic? diagnostic)
    {
        switch (control.Kind)
        {
            case WorldPersistenceControlKind.Frame2 when control.Frame2 is SpatialFrame2 frame2:
            {
                var status = WorldPersistenceSpatialFrameWriter.MeasureFrame2(
                    lease.WorldGuid,
                    lease.OwnerId,
                    frame2,
                    limits,
                    out var workspace,
                    out diagnostic);
                payloadCapacity = status is EbinWriteStatus.Complete ? checked(workspace.RecordBytes + 128) : 0;
                return workspace;
            }

            case WorldPersistenceControlKind.Frame3 when control.Frame3 is SpatialFrame3 frame3:
            {
                var status = WorldPersistenceSpatialFrameWriter.MeasureFrame3(
                    lease.WorldGuid,
                    lease.OwnerId,
                    frame3,
                    limits,
                    out var workspace,
                    out diagnostic);
                payloadCapacity = status is EbinWriteStatus.Complete ? checked(workspace.RecordBytes + 128) : 0;
                return workspace;
            }

            case WorldPersistenceControlKind.Branch:
            case WorldPersistenceControlKind.TimeScale:
            {
                payloadCapacity = 256;
                diagnostic = null;
                return default;
            }

            default:
            {
                payloadCapacity = 0;
                diagnostic = CreateDiagnostic("control-frame-missing");
                return default;
            }
        }
    }

    private static EbinWriteStatus MeasureFramePayload(
        WorldPersistenceCaptureLease lease,
        WorldPersistenceControlCaptureRow control,
        EbinReadLimits limits,
        scoped Span<byte> destination,
        bool is2D,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var frame2 = control.Frame2;
        var frame3 = control.Frame3;

        if (is2D)
        {
            if (frame2 is not SpatialFrame2 selectedFrame2)
            {
                bytesWritten = 0;
                diagnostic = CreateDiagnostic("control-frame2-missing");
                return EbinWriteStatus.InvalidRecord;
            }

            var measureStatus = WorldPersistenceSpatialFrameWriter.MeasureFrame2(
                lease.WorldGuid,
                lease.OwnerId,
                selectedFrame2,
                limits,
                out var frameWorkspace,
                out diagnostic);

            if (measureStatus is not EbinWriteStatus.Complete)
            {
                bytesWritten = 0;
                return measureStatus;
            }

            Span<byte> frameDestination = stackalloc byte[frameWorkspace.RecordBytes];
            Span<byte> frameCandidateStorage = stackalloc byte[frameWorkspace.RecordBytes];
            Span<byte> framePayloadStorage = stackalloc byte[frameWorkspace.FramePayloadStorageBytes];
            Span<byte> profileCandidateStorage = stackalloc byte[frameWorkspace.ProfileRecordBytes];
            Span<byte> profileStorage = stackalloc byte[frameWorkspace.ProfileRecordBytes];
            var frameBuffers = new WorldPersistenceSpatialFrameBuffers {
                Destination = frameDestination,
                FrameCandidateStorage = frameCandidateStorage,
                FramePayloadStorage = framePayloadStorage,
                ProfileCandidateStorage = profileCandidateStorage,
                ProfileStorage = profileStorage,
            };
            var frameStatus = WorldPersistenceSpatialFrameWriter.WriteFrame2(
                lease.WorldGuid,
                lease.OwnerId,
                selectedFrame2,
                frameWorkspace,
                limits,
                ref frameBuffers,
                out var frameBytes,
                out diagnostic);

            if (frameStatus is not EbinWriteStatus.Complete)
            {
                bytesWritten = 0;
                return frameStatus;
            }

            return WriteFrameWrapper(
                s_frame2SchemaId,
                frameDestination[..frameBytes],
                limits,
                destination,
                out bytesWritten,
                out diagnostic);
        }

        if (frame3 is not SpatialFrame3 selectedFrame3)
        {
            bytesWritten = 0;
            diagnostic = CreateDiagnostic("control-frame3-missing");
            return EbinWriteStatus.InvalidRecord;
        }

        var frame3Status = WorldPersistenceSpatialFrameWriter.MeasureFrame3(
            lease.WorldGuid,
            lease.OwnerId,
            selectedFrame3,
            limits,
            out var frame3Workspace,
            out diagnostic);

        if (frame3Status is not EbinWriteStatus.Complete)
        {
            bytesWritten = 0;
            return frame3Status;
        }

        Span<byte> frame3Destination = stackalloc byte[frame3Workspace.RecordBytes];
        Span<byte> frame3CandidateStorage = stackalloc byte[frame3Workspace.RecordBytes];
        Span<byte> frame3PayloadStorage = stackalloc byte[frame3Workspace.FramePayloadStorageBytes];
        Span<byte> profile3CandidateStorage = stackalloc byte[frame3Workspace.ProfileRecordBytes];
        Span<byte> profile3Storage = stackalloc byte[frame3Workspace.ProfileRecordBytes];
        var frame3Buffers = new WorldPersistenceSpatialFrameBuffers {
            Destination = frame3Destination,
            FrameCandidateStorage = frame3CandidateStorage,
            FramePayloadStorage = frame3PayloadStorage,
            ProfileCandidateStorage = profile3CandidateStorage,
            ProfileStorage = profile3Storage,
        };
        frame3Status = WorldPersistenceSpatialFrameWriter.WriteFrame3(
            lease.WorldGuid,
            lease.OwnerId,
            selectedFrame3,
            frame3Workspace,
            limits,
            ref frame3Buffers,
            out var frame3Bytes,
            out diagnostic);

        if (frame3Status is not EbinWriteStatus.Complete)
        {
            bytesWritten = 0;
            return frame3Status;
        }

        return WriteFrameWrapper(
            s_frame3SchemaId,
            frame3Destination[..frame3Bytes],
            limits,
            destination,
            out bytesWritten,
            out diagnostic);
    }

    private static void ValidateControl(WorldPersistenceControlCaptureRow control)
    {
        if ((control.Sequence <= 0) || (control.AppliedAtNextTick < 0))
        {
            throw new InvalidOperationException("A captured World control has invalid sequence or tick identity.");
        }

        switch (control.Kind)
        {
            case WorldPersistenceControlKind.Branch:
            {
                if ((control.BranchId == Guid.Empty)
                    || (control.LineageId == Guid.Empty)
                    || (control.BranchIndex == 0))
                {
                    throw new InvalidOperationException("A branch control has incomplete identity metadata.");
                }

                break;
            }

            case WorldPersistenceControlKind.Frame2:
            {
                if (control.Frame2 is null || control.Frame3 is not null)
                {
                    throw new InvalidOperationException("A 2D-frame control has inconsistent dimensional data.");
                }

                break;
            }

            case WorldPersistenceControlKind.Frame3:
            {
                if (control.Frame3 is null || control.Frame2 is not null)
                {
                    throw new InvalidOperationException("A 3D-frame control has inconsistent dimensional data.");
                }

                break;
            }

            case WorldPersistenceControlKind.TimeScale:
            {
                if (!float.IsFinite(control.TimeScaleValue)
                    || (control.TimeScaleValue < 0)
                    || (control.TimeScaleValue > 4)
                    || (control.SegmentSequence <= 0))
                {
                    throw new InvalidOperationException("A time-scale control has invalid value or segment metadata.");
                }

                break;
            }

            default:
            {
                throw new InvalidOperationException("The World contains an unknown persistence control kind.");
            }
        }
    }

    private static EbinWriteStatus WriteBranchPayload(
        WorldPersistenceControlCaptureRow control,
        EbinReadLimits limits,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[3];
        Span<byte> payloadStorage = stackalloc byte[34];
        fields.Clear();
        var fieldCount = 0;
        var payloadOffset = 0;
        var status = AddGuidField(1, control.BranchId, fields, ref fieldCount, payloadStorage, ref payloadOffset, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            bytesWritten = 0;
            return status;
        }

        status = AddGuidField(2, control.LineageId, fields, ref fieldCount, payloadStorage, ref payloadOffset, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            bytesWritten = 0;
            return status;
        }

        status = EbinPayloadWriter.Write(control.BranchIndex, payloadStorage[payloadOffset..], out var branchIndexBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            bytesWritten = 0;
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(
            3,
            2,
            1,
            1,
            new EbinPayloadSlice(payloadOffset, branchIndexBytes));
        payloadOffset = checked(payloadOffset + branchIndexBytes);
        var source = new EbinRecordWriteView(
            s_branchSchemaId,
            1,
            0,
            fields[..fieldCount],
            payloadStorage[..payloadOffset]);
        return WritePayloadSource(in source, limits, destination, out bytesWritten, out diagnostic);
    }

    private static EbinWriteStatus WriteFrameWrapper(
        Guid schemaId,
        scoped ReadOnlySpan<byte> frameRecord,
        EbinReadLimits limits,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        Span<EbinFieldSource> fields = [new EbinFieldSource(1, 22, 1, 1, new EbinPayloadSlice(0, frameRecord.Length))];
        var source = new EbinRecordWriteView(schemaId, 1, 0, fields, frameRecord);
        var status = EbinStructuralWriter.Measure(in source, limits, out var recordBytes, out diagnostic);
        bytesWritten = 0;

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (destination.Length < recordBytes)
        {
            throw new ArgumentException("The spatial-frame control destination is too small.", nameof(destination));
        }

        Span<byte> candidateStorage = stackalloc byte[recordBytes];
        return EbinStructuralWriter.Write(
            in source,
            limits,
            candidateStorage,
            destination[..recordBytes],
            out bytesWritten,
            out diagnostic);
    }

    private static EbinWriteStatus WritePayloadRecord(
        WorldPersistenceCaptureLease lease,
        WorldPersistenceControlCaptureRow control,
        EbinReadLimits limits,
        scoped Span<byte> destination,
        out int bytesWritten,
        out long payloadScratchBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        switch (control.Kind)
        {
            case WorldPersistenceControlKind.Branch:
            {
                var branchStatus = WriteBranchPayload(control, limits, destination, out bytesWritten, out diagnostic);
                payloadScratchBytes = checked(34 + (3L * Unsafe.SizeOf<EbinFieldSource>()) + bytesWritten);
                return branchStatus;
            }

            case WorldPersistenceControlKind.Frame2:
            {
                var frame2Workspace = MeasureFrame(lease, control, limits, out _, out diagnostic);

                if (diagnostic is not null)
                {
                    bytesWritten = 0;
                    payloadScratchBytes = 0;
                    return EbinWriteStatus.InvalidRecord;
                }

                var frame2Status = MeasureFramePayload(lease, control, limits, destination, is2D: true, out bytesWritten, out diagnostic);
                payloadScratchBytes = checked(frame2Workspace.WorkspaceBytes + bytesWritten + Unsafe.SizeOf<EbinFieldSource>());
                return frame2Status;
            }

            case WorldPersistenceControlKind.Frame3:
            {
                var frame3Workspace = MeasureFrame(lease, control, limits, out _, out diagnostic);

                if (diagnostic is not null)
                {
                    bytesWritten = 0;
                    payloadScratchBytes = 0;
                    return EbinWriteStatus.InvalidRecord;
                }

                var frame3Status = MeasureFramePayload(lease, control, limits, destination, is2D: false, out bytesWritten, out diagnostic);
                payloadScratchBytes = checked(frame3Workspace.WorkspaceBytes + bytesWritten + Unsafe.SizeOf<EbinFieldSource>());
                return frame3Status;
            }

            case WorldPersistenceControlKind.TimeScale:
            {
                var timeScaleStatus = WriteTimeScalePayload(control, limits, destination, out bytesWritten, out diagnostic);
                payloadScratchBytes = checked(12 + (2L * Unsafe.SizeOf<EbinFieldSource>()) + bytesWritten);
                return timeScaleStatus;
            }

            default:
            {
                bytesWritten = 0;
                diagnostic = CreateDiagnostic("control-kind");
                payloadScratchBytes = 0;
                return EbinWriteStatus.InvalidRecord;
            }
        }
    }

    private static EbinWriteStatus WritePayloadSource(
        in EbinRecordWriteView source,
        EbinReadLimits limits,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = EbinStructuralWriter.Measure(in source, limits, out var recordBytes, out diagnostic);
        bytesWritten = 0;

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (destination.Length < recordBytes)
        {
            throw new ArgumentException("The control-payload destination is too small.", nameof(destination));
        }

        Span<byte> candidateStorage = stackalloc byte[recordBytes];
        return EbinStructuralWriter.Write(
            in source,
            limits,
            candidateStorage,
            destination[..recordBytes],
            out bytesWritten,
            out diagnostic);
    }

    private static EbinWriteStatus WriteTimeScalePayload(
        WorldPersistenceControlCaptureRow control,
        EbinReadLimits limits,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[2];
        Span<byte> payloadStorage = stackalloc byte[12];
        var status = EbinPayloadWriter.Write(control.TimeScaleValue, payloadStorage, out var timeScaleBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            bytesWritten = 0;
            return status;
        }

        status = EbinPayloadWriter.Write(
            control.SegmentSequence,
            payloadStorage[timeScaleBytes..],
            out var segmentBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            bytesWritten = 0;
            return status;
        }

        fields[0] = new EbinFieldSource(1, 10, 1, 1, new EbinPayloadSlice(0, timeScaleBytes));
        fields[1] = new EbinFieldSource(2, 8, 1, 1, new EbinPayloadSlice(timeScaleBytes, segmentBytes));
        var source = new EbinRecordWriteView(
            s_timeScaleSchemaId,
            1,
            0,
            fields,
            payloadStorage[..checked(timeScaleBytes + segmentBytes)]);
        return WritePayloadSource(in source, limits, destination, out bytesWritten, out diagnostic);
    }
}
