// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using TerraFX;
using TerraFX.Assets;

namespace TerraFX.Entities;

internal static class WorldPersistenceWorldRecordCaptureCoordinator
{
    private static readonly Guid s_schemaId = new ContentSchemaIdentityAssignment("state/world").SchemaId;

    internal static EbinWriteStatus Capture(
        World world,
        WorldPersistenceCaptureLease lease,
        CompiledContentSchemaRegistry schemas,
        scoped ReadOnlySpan<byte> ecsRecord,
        scoped ReadOnlySpan<byte> scenesRecord,
        out byte[]? worldRecord,
        out int assetIdCount,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(schemas);
        lease.ValidateCurrent();

        if (!lease.IsBoundTo(world))
        {
            throw new ArgumentException("The capture lease must belong to this exact World.", nameof(lease));
        }

        worldRecord = null;
        assetIdCount = 0;
        var status = WorldPersistenceRegistrationCaptureCoordinator.Capture(
            world,
            lease,
            out var registrationRecordBytes,
            out var registrationReservation,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        using var registrationReservationScope = registrationReservation
            ?? throw new InvalidOperationException("The registration capture reservation was not created.");
        var registrationRecord = registrationRecordBytes
            ?? throw new InvalidOperationException("The registration capture did not produce its record.");
        var limits = WorldPersistenceEbinLimits.Create(world.Options);
        status = lease.MeasureConfigurationRecord(out var configurationBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var configurationCandidateStorage = new byte[configurationBytes];
        var configurationRecord = new byte[configurationBytes];
        status = WorldPersistenceConfigurationWriter.Write(
            world.Options,
            limits,
            configurationCandidateStorage,
            configurationRecord,
            out var configurationBytesWritten,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (configurationBytesWritten != configurationRecord.Length)
        {
            throw new InvalidOperationException("The configuration record extent changed after measurement.");
        }

        status = lease.MeasureClockRecord(out var clockWorkspace, out diagnostic, cancellationToken);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var clockBuffers = CreateClockBuffers(clockWorkspace);
        status = WorldPersistenceClockWriter.Write(
            lease,
            clockWorkspace,
            limits,
            ref clockBuffers,
            out var clockBytesWritten,
            out diagnostic,
            cancellationToken);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (clockBytesWritten != clockWorkspace.RecordBytes)
        {
            throw new InvalidOperationException("The clock record extent changed after measurement.");
        }

        var frame2Record = ReadOnlySpan<byte>.Empty;
        var frame2Buffers = default(WorldPersistenceSpatialFrameBuffers);
        var frame2Workspace = default(WorldPersistenceSpatialFrameWorkspaceRequirements);

        if (lease.Frame2 is SpatialFrame2 frame2)
        {
            status = WorldPersistenceSpatialFrameWriter.MeasureFrame2(
                lease.WorldGuid,
                lease.OwnerId,
                frame2,
                limits,
                out frame2Workspace,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            frame2Buffers = CreateSpatialFrameBuffers(frame2Workspace);
            status = WorldPersistenceSpatialFrameWriter.WriteFrame2(
                lease.WorldGuid,
                lease.OwnerId,
                frame2,
                frame2Workspace,
                limits,
                ref frame2Buffers,
                out var frame2BytesWritten,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            if (frame2BytesWritten != frame2Workspace.RecordBytes)
            {
                throw new InvalidOperationException("The Frame2 record extent changed after measurement.");
            }

            frame2Record = frame2Buffers.Destination;
        }

        status = WorldPersistenceSpatialFrameWriter.MeasureFrame3(
            lease.WorldGuid,
            lease.OwnerId,
            lease.Frame3,
            limits,
            out var frame3Workspace,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var frame3Buffers = CreateSpatialFrameBuffers(frame3Workspace);
        status = WorldPersistenceSpatialFrameWriter.WriteFrame3(
            lease.WorldGuid,
            lease.OwnerId,
            lease.Frame3,
            frame3Workspace,
            limits,
            ref frame3Buffers,
            out var frame3BytesWritten,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (frame3BytesWritten != frame3Workspace.RecordBytes)
        {
            throw new InvalidOperationException("The Frame3 record extent changed after measurement.");
        }

        var controlSlices = new EbinPayloadSlice[lease.ControlRecordCount];
        status = WorldPersistenceControlRecordArrayWriter.Measure(
            lease,
            limits,
            controlSlices,
            out var controlWorkspace,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var controlsArrayPayload = new byte[controlWorkspace.ArrayPayloadBytes];
        var controlRecordStorage = new byte[controlWorkspace.RecordStorageBytes];
        var controlCandidateStorage = new byte[controlWorkspace.MaximumCandidateRecordBytes];
        var controlRootPayloadStorage = new byte[controlWorkspace.MaximumRootPayloadBytes];
        status = WorldPersistenceControlRecordArrayWriter.Write(
            lease,
            limits,
            controlWorkspace,
            controlSlices,
            controlRecordStorage,
            controlCandidateStorage,
            controlRootPayloadStorage,
            controlsArrayPayload,
            out var controlsBytesWritten,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (controlsBytesWritten != controlsArrayPayload.Length)
        {
            throw new InvalidOperationException("The control array extent changed after measurement.");
        }

        status = WorldPersistenceCursorWriter.Measure(lease, limits, out var cursorsBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var cursorsCandidateStorage = new byte[cursorsBytes];
        var cursorsRecord = new byte[cursorsBytes];
        status = WorldPersistenceCursorWriter.Write(
            lease,
            limits,
            cursorsCandidateStorage,
            cursorsRecord,
            out var cursorsBytesWritten,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (cursorsBytesWritten != cursorsRecord.Length)
        {
            throw new InvalidOperationException("The cursor record extent changed after measurement.");
        }

        var referenceCount = WorldPersistenceAssetIdSetWriter.MeasureReferenceCount(
            schemas,
            ecsRecord,
            scenesRecord,
            limits);
        var assetIdWorkspace = new uint[referenceCount];
        assetIdCount = WorldPersistenceAssetIdSetWriter.CopySortedUniqueAssetIds(
            schemas,
            ecsRecord,
            scenesRecord,
            limits,
            assetIdWorkspace);

        var emptyArrayPayload = new byte[16];
        status = EbinPayloadWriter.WriteRecordArray(
            [],
            [],
            limits,
            emptyArrayPayload,
            out var emptyArrayBytesWritten,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (emptyArrayBytesWritten != emptyArrayPayload.Length)
        {
            throw new InvalidOperationException("The empty random-stream array extent changed after measurement.");
        }

        var sources = new WorldPersistenceWorldRecordSources(
            lease,
            assetIdWorkspace.AsSpan(0, assetIdCount),
            clockBuffers.Destination,
            configurationRecord,
            lease.ControlRecordCount,
            controlsArrayPayload,
            cursorsRecord,
            ecsRecord,
            frame2Record,
            frame3Buffers.Destination,
            emptyArrayPayload,
            randomStreamCount: 0,
            registrationRecord,
            scenesRecord,
            strictLayoutRecord: []);
        status = WorldPersistenceWorldRecordWriter.MeasureRootPayloadStorage(
            sources,
            limits,
            out var rootPayloadStorageBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var rootPayloadStorage = new byte[rootPayloadStorageBytes];
        status = WorldPersistenceWorldRecordWriter.Measure(
            sources,
            limits,
            rootPayloadStorage,
            out var worldWorkspace,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var childProducerWorkspaceBytes = checked(
            (2L * configurationBytes)
            + clockWorkspace.WorkspaceBytes
            + frame2Workspace.WorkspaceBytes
            + frame3Workspace.WorkspaceBytes
            + controlWorkspace.WorkspaceBytes
            + (2L * cursorsBytes)
            + emptyArrayPayload.Length);
        var captureWorkspace = new WorldPersistenceWorldRecordCaptureWorkspaceRequirements(
            referenceCount,
            assetIdCount,
            worldWorkspace,
            childProducerWorkspaceBytes);

        if (!lease.TryReserveAdmissionBytes(captureWorkspace.WorkspaceBytes, out var reservation))
        {
            throw new InvalidOperationException("The World-record child and root workspaces were refused by admission.");
        }

        using var reservationScope = reservation
            ?? throw new InvalidOperationException("The World-record capture reservation was not created.");

        status = lease.WriteConfigurationRecord(
            reservationScope,
            configurationCandidateStorage,
            configurationRecord,
            out configurationBytesWritten,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = reservationScope.WriteClockRecord(
            world,
            lease,
            clockWorkspace,
            ref clockBuffers,
            out clockBytesWritten,
            out diagnostic,
            cancellationToken);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (lease.Frame2 is SpatialFrame2 capturedFrame2)
        {
            using var frameUse = reservationScope.AcquireUse(world, frame2Workspace.WorkspaceBytes);
            status = WorldPersistenceSpatialFrameWriter.WriteFrame2(
                lease.WorldGuid,
                lease.OwnerId,
                capturedFrame2,
                frame2Workspace,
                limits,
                ref frame2Buffers,
                out _,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }
        }

        using (reservationScope.AcquireUse(world, frame3Workspace.WorkspaceBytes))
        {
            status = WorldPersistenceSpatialFrameWriter.WriteFrame3(
                lease.WorldGuid,
                lease.OwnerId,
                lease.Frame3,
                frame3Workspace,
                limits,
                ref frame3Buffers,
                out _,
                out diagnostic);
        }

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        using (reservationScope.AcquireUse(world, controlWorkspace.WorkspaceBytes))
        {
            status = WorldPersistenceControlRecordArrayWriter.Write(
                lease,
                limits,
                controlWorkspace,
                controlSlices,
                controlRecordStorage,
                controlCandidateStorage,
                controlRootPayloadStorage,
                controlsArrayPayload,
                out _,
                out diagnostic);
        }

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = lease.WriteCursorsRecord(
            reservationScope,
            cursorsCandidateStorage,
            cursorsRecord,
            out cursorsBytesWritten,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var worldDestination = new byte[worldWorkspace.RecordBytes];
        status = Write(
            world,
            lease,
            reservationScope,
            schemas,
            sources,
            captureWorkspace,
            assetIdWorkspace,
            rootPayloadStorage,
            new byte[worldWorkspace.RecordBytes],
            worldDestination,
            out assetIdCount,
            out var worldBytesWritten,
            out diagnostic);

        if (status is EbinWriteStatus.Complete)
        {
            if (worldBytesWritten != worldDestination.Length)
            {
                throw new InvalidOperationException("The World record extent changed after capture measurement.");
            }

            worldRecord = worldDestination;
        }

        return status;
    }

    internal static EbinWriteStatus Write(
        World world,
        WorldPersistenceCaptureLease lease,
        WorldPersistenceAdmissionReservation reservation,
        CompiledContentSchemaRegistry schemas,
        WorldPersistenceWorldRecordSources sources,
        WorldPersistenceWorldRecordCaptureWorkspaceRequirements workspace,
        scoped Span<uint> assetIdWorkspace,
        scoped Span<byte> rootPayloadStorage,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int assetIdCount,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(reservation);
        ArgumentNullException.ThrowIfNull(schemas);
        lease.ValidateCurrent();

        if (!lease.IsBoundTo(world)
            || (sources.WorldGuid != lease.WorldGuid)
            || (sources.LeaseSequence != lease.Sequence))
        {
            throw new ArgumentException("The capture lease and World-root sources must belong to this exact World capture.", nameof(lease));
        }

        if (assetIdWorkspace.Length != workspace.AssetIdReferenceCount)
        {
            throw new ArgumentException("The AssetId workspace must match the exact live reference count.", nameof(assetIdWorkspace));
        }

        var limits = WorldPersistenceEbinLimits.Create(world.Options);
        var measuredReferenceCount = WorldPersistenceAssetIdSetWriter.MeasureReferenceCount(
            schemas,
            sources.EcsRecord,
            sources.ScenesRecord,
            limits);

        if (measuredReferenceCount != workspace.AssetIdReferenceCount)
        {
            return InvalidWorkspace(
                "asset-id-reference-count-mismatch",
                checked((ulong)measuredReferenceCount),
                checked((ulong)workspace.AssetIdReferenceCount),
                out assetIdCount,
                out bytesWritten,
                out diagnostic);
        }

        using (reservation.AcquireUse(world, workspace.WorkspaceBytes))
        {
        }

        using (reservation.AcquireUse(world, workspace.AssetIdWorkspaceBytes))
        {
            assetIdCount = WorldPersistenceAssetIdSetWriter.CopySortedUniqueAssetIds(
                schemas,
                sources.EcsRecord,
                sources.ScenesRecord,
                limits,
                assetIdWorkspace);
        }

        if (assetIdCount != workspace.AssetIdCount)
        {
            return InvalidWorkspace(
                "asset-id-count-mismatch",
                checked((ulong)assetIdCount),
                checked((ulong)workspace.AssetIdCount),
                out assetIdCount,
                out bytesWritten,
                out diagnostic);
        }

        if (sources.AssetIds.Length != assetIdCount)
        {
            return InvalidWorkspace(
                "asset-id-set-count-mismatch",
                checked((ulong)assetIdCount),
                checked((ulong)sources.AssetIds.Length),
                out assetIdCount,
                out bytesWritten,
                out diagnostic);
        }

        for (var index = 0; index < assetIdCount; index++)
        {
            if (assetIdWorkspace[index] != sources.AssetIds[index])
            {
                var actual = assetIdWorkspace[index];
                var expected = sources.AssetIds[index];
                diagnostic = CreateDiagnostic("asset-id-set-mismatch", actual, expected);
                bytesWritten = 0;
                return EbinWriteStatus.InvalidRecord;
            }
        }

        var status = WorldPersistenceWorldRecordWriter.Measure(
            sources,
            limits,
            rootPayloadStorage,
            out var measuredWorkspace,
            out diagnostic);
        bytesWritten = 0;

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var measuredCaptureWorkspace = new WorldPersistenceWorldRecordCaptureWorkspaceRequirements(
            measuredReferenceCount,
            assetIdCount,
            measuredWorkspace,
            workspace.ChildProducerWorkspaceBytes);

        if (!workspace.Matches(measuredCaptureWorkspace))
        {
            diagnostic = CreateDiagnostic(
                "world-workspace-mismatch",
                checked((ulong)measuredCaptureWorkspace.WorkspaceBytes),
                checked((ulong)workspace.WorkspaceBytes));
            return EbinWriteStatus.InvalidRecord;
        }

        return lease.WriteWorldRecord(
            reservation,
            sources,
            workspace.WorldRecord,
            rootPayloadStorage,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }

    private static WorldPersistenceClockBuffers CreateClockBuffers(WorldPersistenceClockWorkspaceRequirements workspace)
        => new() {
            ClockCandidateStorage = new byte[workspace.RecordBytes],
            ClockPayloadStorage = new byte[workspace.ClockPayloadStorageBytes],
            Destination = new byte[workspace.RecordBytes],
            FrameCandidateStorage = new byte[workspace.FrameCandidateStorageBytes],
            FramePayloadStorage = new byte[workspace.FramePayloadStorageBytes],
            FrameProfileCandidateStorage = new byte[workspace.InitialFrame3.ProfileRecordBytes],
            FrameProfileStorage = new byte[workspace.InitialFrame3.ProfileRecordBytes],
            InitialFrame2Storage = new byte[workspace.InitialFrame2.RecordBytes],
            InitialFrame3Storage = new byte[workspace.InitialFrame3.RecordBytes],
            SegmentArrayPayloadStorage = new byte[workspace.SegmentArrayPayloadBytes],
            SegmentCandidateStorage = new byte[workspace.SegmentRecordBytes],
            SegmentSlices = new EbinPayloadSlice[workspace.SegmentCount],
            SegmentStorage = new byte[workspace.SegmentStorageBytes],
        };

    private static EbinStructuralDiagnostic CreateDiagnostic(string category, ulong actual, ulong expected)
        => new(
            s_schemaId,
            7,
            category,
            null,
            actual,
            expected,
            EbinStructuralDiagnosticSource.Construction);

    private static WorldPersistenceSpatialFrameBuffers CreateSpatialFrameBuffers(
        WorldPersistenceSpatialFrameWorkspaceRequirements workspace)
        => new() {
            Destination = new byte[workspace.RecordBytes],
            FrameCandidateStorage = new byte[workspace.RecordBytes],
            FramePayloadStorage = new byte[workspace.FramePayloadStorageBytes],
            ProfileCandidateStorage = new byte[workspace.ProfileRecordBytes],
            ProfileStorage = new byte[workspace.ProfileRecordBytes],
        };

    private static EbinWriteStatus InvalidWorkspace(
        string category,
        ulong actual,
        ulong expected,
        out int assetIdCount,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        assetIdCount = 0;
        bytesWritten = 0;
        diagnostic = CreateDiagnostic(category, actual, expected);
        return EbinWriteStatus.InvalidRecord;
    }
}
