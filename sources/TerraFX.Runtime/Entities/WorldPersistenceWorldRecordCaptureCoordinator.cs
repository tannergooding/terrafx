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
        scoped ReadOnlySpan<byte> scenesRecord,
        WorldPersistenceAdmissionReservation scenesReservation,
        out WorldPersistenceAdmissionReservation? retainedReservation,
        out byte[]? worldRecord,
        out int assetIdCount,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(scenesReservation);
        lease.ValidateCurrent();
        retainedReservation = null;

        if (!lease.IsBoundTo(world))
        {
            throw new ArgumentException("The capture lease must belong to this exact World.", nameof(lease));
        }

        var status = WorldPersistenceEcsCaptureCoordinator.Capture(
            world,
            lease,
            out var ecsRecordBytes,
            out var ecsReservation,
            out diagnostic,
            cancellationToken);

        if (status is not EbinWriteStatus.Complete)
        {
            worldRecord = null;
            assetIdCount = 0;
            return status;
        }

        using var ecsReservationScope = ecsReservation
            ?? throw new InvalidOperationException("The ECS capture reservation was not created.");
        var ecsRecord = ecsRecordBytes
            ?? throw new InvalidOperationException("The ECS capture did not produce its record.");
        return CaptureFromRecords(
            world,
            lease,
            schemas,
            ecsRecord,
            scenesRecord,
            scenesReservation,
            out retainedReservation,
            out worldRecord,
            out assetIdCount,
            out diagnostic,
            cancellationToken);
    }

    internal static EbinWriteStatus Capture(
        World world,
        WorldPersistenceCaptureLease lease,
        CompiledContentSchemaRegistry schemas,
        scoped ReadOnlySpan<byte> scenesRecord,
        WorldPersistenceAdmissionReservation scenesReservation,
        out byte[]? worldRecord,
        out int assetIdCount,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        var status = Capture(
            world,
            lease,
            schemas,
            scenesRecord,
            scenesReservation,
            out var retainedReservation,
            out worldRecord,
            out assetIdCount,
            out diagnostic,
            cancellationToken);
        retainedReservation?.Dispose();
        return status;
    }

    internal static EbinWriteStatus CaptureFromRecords(
        World world,
        WorldPersistenceCaptureLease lease,
        CompiledContentSchemaRegistry schemas,
        scoped ReadOnlySpan<byte> ecsRecord,
        scoped ReadOnlySpan<byte> scenesRecord,
        WorldPersistenceAdmissionReservation scenesReservation,
        out WorldPersistenceAdmissionReservation? retainedReservation,
        out byte[]? worldRecord,
        out int assetIdCount,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(scenesReservation);
        lease.ValidateCurrent();
        retainedReservation = null;
        worldRecord = null;
        assetIdCount = 0;

        if (!lease.IsBoundTo(world))
        {
            throw new ArgumentException("The capture lease must belong to this exact World.", nameof(lease));
        }

        using var scenesRecordUse = scenesReservation.AcquireUse(world, scenesRecord.Length);
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

        status = lease.MeasureClockRecord(out var clockWorkspace, out diagnostic, cancellationToken);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var hasFrame2 = lease.Frame2.HasValue;
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

        status = WorldPersistenceControlRecordArrayWriter.Measure(
            lease,
            limits,
            out var controlWorkspace,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = WorldPersistenceCursorWriter.Measure(lease, limits, out var cursorsBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var hasStrictLayout = lease.Profile is SimulationProfile.StrictLocal;
        var strictLayoutWorkspace = default(WorldPersistenceStrictLayoutWorkspaceRequirements);

        if (hasStrictLayout)
        {
            status = WorldPersistenceStrictLayoutWriter.Measure(
                lease,
                world.Registration,
                limits,
                cancellationToken,
                out strictLayoutWorkspace,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }
        }

        var referenceCount = WorldPersistenceAssetIdSetWriter.MeasureReferenceCount(
            schemas,
            ecsRecord,
            scenesRecord,
            limits);
        var assetIdWorkspaceBytes = checked((long)referenceCount * sizeof(uint));
        WorldPersistenceAdmissionReservation? reservationScope = null;
        var retainReservation = false;

        try
        {
            uint[] assetIdWorkspace;

            if (referenceCount == 0)
            {
                assetIdWorkspace = [];
            }
            else
            {
                var maximumAdditionalBytes = Math.Max(
                    0L,
                    world.Options.MaximumPersistenceAdmissionBytes - assetIdWorkspaceBytes);

                if (!lease.TryReserveAdmissionBytes(assetIdWorkspaceBytes, maximumAdditionalBytes, out reservationScope))
                {
                    throw new InvalidOperationException("The World-record AssetId workspace was refused by admission.");
                }

                if (reservationScope is null)
                {
                    throw new InvalidOperationException("The World-record AssetId reservation was not created.");
                }

                assetIdWorkspace = new uint[referenceCount];
                assetIdCount = WorldPersistenceAssetIdSetWriter.CopySortedUniqueAssetIds(
                    schemas,
                    ecsRecord,
                    scenesRecord,
                    limits,
                    assetIdWorkspace);
            }

            Span<int> rootFieldPayloadLengths = stackalloc int[12];
            var rootFieldCount = 0;
            rootFieldPayloadLengths[rootFieldCount++] = configurationBytes;
            rootFieldPayloadLengths[rootFieldCount++] = clockWorkspace.RecordBytes;

            if (hasFrame2)
            {
                rootFieldPayloadLengths[rootFieldCount++] = frame2Workspace.RecordBytes;
            }

            rootFieldPayloadLengths[rootFieldCount++] = frame3Workspace.RecordBytes;
            rootFieldPayloadLengths[rootFieldCount++] = ecsRecord.Length;
            rootFieldPayloadLengths[rootFieldCount++] = scenesRecord.Length;
            rootFieldPayloadLengths[rootFieldCount++] = checked(32 + (assetIdCount * sizeof(uint)));
            rootFieldPayloadLengths[rootFieldCount++] = controlWorkspace.ArrayPayloadBytes;
            rootFieldPayloadLengths[rootFieldCount++] = cursorsBytes;
            rootFieldPayloadLengths[rootFieldCount++] = 16;

            if (hasStrictLayout)
            {
                rootFieldPayloadLengths[rootFieldCount++] = strictLayoutWorkspace.RecordBytes;
            }

            rootFieldPayloadLengths[rootFieldCount++] = registrationRecord.Length;
            status = WorldPersistenceWorldRecordWriter.MeasureRecordExtent(
                rootFieldPayloadLengths[..rootFieldCount],
                limits,
                out var rootPayloadStorageBytes,
                out var worldRecordBytes,
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
                + 16
                + strictLayoutWorkspace.WorkspaceBytes);
            var worldWorkspace = new WorldPersistenceWorldRecordWorkspaceRequirements(
                assetIdCount,
                lease.ControlRecordCount,
                hasFrame2,
                hasStrictLayout,
                randomStreamCount: 0,
                rootPayloadStorageBytes,
                worldRecordBytes);
            var captureWorkspace = new WorldPersistenceWorldRecordCaptureWorkspaceRequirements(
                referenceCount,
                assetIdCount,
                worldWorkspace,
                childProducerWorkspaceBytes);

            if (reservationScope is null)
            {
                if (!lease.TryReserveAdmissionBytes(captureWorkspace.WorkspaceBytes, out reservationScope))
                {
                    throw new InvalidOperationException("The World-record child and root workspaces were refused by admission.");
                }

                if (reservationScope is null)
                {
                    throw new InvalidOperationException("The World-record capture reservation was not created.");
                }
            }
            else
            {
                var additionalBytes = checked(captureWorkspace.WorkspaceBytes - assetIdWorkspaceBytes);

                if (additionalBytes < 0)
                {
                    throw new InvalidOperationException("The measured World-record workspace is smaller than its reserved AssetId scratch.");
                }

                if ((additionalBytes > 0) && !reservationScope.TryGrow(additionalBytes))
                {
                    throw new InvalidOperationException("The World-record child and root workspaces were refused by admission.");
                }
            }

            var reservation = reservationScope
                ?? throw new InvalidOperationException("The World-record capture reservation was not created.");
            var configurationCandidateStorage = new byte[configurationBytes];
            var configurationRecord = new byte[configurationBytes];
            var clockBuffers = CreateClockBuffers(clockWorkspace);
            var frame2Buffers = hasFrame2 ? CreateSpatialFrameBuffers(frame2Workspace) : default;
            var frame3Buffers = CreateSpatialFrameBuffers(frame3Workspace);
            var controlSlices = new EbinPayloadSlice[lease.ControlRecordCount];
            var controlsArrayPayload = new byte[controlWorkspace.ArrayPayloadBytes];
            var controlRecordStorage = new byte[controlWorkspace.RecordStorageBytes];
            var controlCandidateStorage = new byte[controlWorkspace.MaximumCandidateRecordBytes];
            var controlRootPayloadStorage = new byte[controlWorkspace.MaximumRootPayloadBytes];
            var cursorsCandidateStorage = new byte[cursorsBytes];
            var cursorsRecord = new byte[cursorsBytes];
            var strictLayoutBuffers = default(WorldPersistenceStrictLayoutBuffers);
            var strictLayoutRecord = ReadOnlySpan<byte>.Empty;

            if (hasStrictLayout)
            {
                strictLayoutBuffers = WorldPersistenceStrictLayoutBuffers.Create(strictLayoutWorkspace);

                if (strictLayoutBuffers.GetWorkspaceBytes() != strictLayoutWorkspace.WorkspaceBytes)
                {
                    throw new InvalidOperationException("The StrictLocal layout workspace extent changed after measurement.");
                }
            }

            status = lease.WriteConfigurationRecord(
                reservation,
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

            status = reservation.WriteClockRecord(
                world,
                lease,
                clockWorkspace,
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

            if (lease.Frame2 is SpatialFrame2 capturedFrame2)
            {
                using var frameUse = reservation.AcquireUse(world, frame2Workspace.WorkspaceBytes);
                status = WorldPersistenceSpatialFrameWriter.WriteFrame2(
                    lease.WorldGuid,
                    lease.OwnerId,
                    capturedFrame2,
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

            using (reservation.AcquireUse(world, frame3Workspace.WorkspaceBytes))
            {
                status = WorldPersistenceSpatialFrameWriter.WriteFrame3(
                    lease.WorldGuid,
                    lease.OwnerId,
                    lease.Frame3,
                    frame3Workspace,
                    limits,
                    ref frame3Buffers,
                    out var frame3BytesWritten,
                    out diagnostic);

                if (status is EbinWriteStatus.Complete)
                {
                    if (frame3BytesWritten != frame3Workspace.RecordBytes)
                    {
                        throw new InvalidOperationException("The Frame3 record extent changed after measurement.");
                    }
                }
            }

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            using (reservation.AcquireUse(world, controlWorkspace.WorkspaceBytes))
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
                    out var controlsBytesWritten,
                    out diagnostic);

                if (status is EbinWriteStatus.Complete)
                {
                    if (controlsBytesWritten != controlsArrayPayload.Length)
                    {
                        throw new InvalidOperationException("The control array extent changed after measurement.");
                    }
                }
            }

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            status = lease.WriteCursorsRecord(
                reservation,
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

            if (hasStrictLayout)
            {
                using var layoutUse = reservation.AcquireUse(world, strictLayoutWorkspace.WorkspaceBytes);
                status = WorldPersistenceStrictLayoutWriter.Write(
                    lease,
                    world.Registration,
                    strictLayoutWorkspace,
                    limits,
                    strictLayoutBuffers.ArchetypeSlices,
                    strictLayoutBuffers.SlotSlices,
                    strictLayoutBuffers.ComponentSlices,
                    strictLayoutBuffers.ChunkSlices,
                    strictLayoutBuffers.ArchetypeRecordStorage,
                    strictLayoutBuffers.SlotRecordStorage,
                    strictLayoutBuffers.ComponentRecordStorage,
                    strictLayoutBuffers.ChunkRecordStorage,
                    strictLayoutBuffers.ArchetypePayloadStorage,
                    strictLayoutBuffers.FreeSlotScratch,
                    strictLayoutBuffers.RowIdScratch,
                    strictLayoutBuffers.ArchetypeDigestStorage,
                    strictLayoutBuffers.ChildCandidateStorage,
                    strictLayoutBuffers.RootPayloadStorage,
                    strictLayoutBuffers.CandidateStorage,
                    strictLayoutBuffers.Destination,
                    cancellationToken,
                    out var strictLayoutBytesWritten,
                    out diagnostic);

                if (status is not EbinWriteStatus.Complete)
                {
                    return status;
                }

                if (strictLayoutBytesWritten != strictLayoutBuffers.Destination.Length)
                {
                    throw new InvalidOperationException("The StrictLocal layout extent changed after measurement.");
                }

                strictLayoutRecord = strictLayoutBuffers.Destination;
            }

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
                strictLayoutRecord);
            status = WorldPersistenceWorldRecordWriter.MeasureRootPayloadStorage(
                sources,
                limits,
                out var measuredRootPayloadStorageBytes,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            if (measuredRootPayloadStorageBytes != rootPayloadStorageBytes)
            {
                throw new InvalidOperationException("The World-record root-payload extent changed after admission measurement.");
            }

            var rootPayloadStorage = new byte[rootPayloadStorageBytes];
            status = WorldPersistenceWorldRecordWriter.Measure(
                sources,
                limits,
                rootPayloadStorage,
                out var measuredWorldWorkspace,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            if (!worldWorkspace.Matches(measuredWorldWorkspace))
            {
                throw new InvalidOperationException("The World-record extent changed after admission measurement.");
            }

            var worldDestination = new byte[worldWorkspace.RecordBytes];
            status = Write(
                world,
                lease,
                reservation,
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
                retainedReservation = reservation;
                retainReservation = true;
            }

            return status;
        }
        finally
        {
            if (!retainReservation)
            {
                reservationScope?.Dispose();
            }
        }
    }

    internal static EbinWriteStatus CaptureFromRecords(
        World world,
        WorldPersistenceCaptureLease lease,
        CompiledContentSchemaRegistry schemas,
        scoped ReadOnlySpan<byte> ecsRecord,
        scoped ReadOnlySpan<byte> scenesRecord,
        WorldPersistenceAdmissionReservation scenesReservation,
        out byte[]? worldRecord,
        out int assetIdCount,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        var status = CaptureFromRecords(
            world,
            lease,
            schemas,
            ecsRecord,
            scenesRecord,
            scenesReservation,
            out var retainedReservation,
            out worldRecord,
            out assetIdCount,
            out diagnostic,
            cancellationToken);
        retainedReservation?.Dispose();
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
