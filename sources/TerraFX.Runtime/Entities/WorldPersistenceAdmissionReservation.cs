// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class WorldPersistenceAdmissionReservation : IDisposable
{
    private int _activeUseCount;
    private long _activeWorkspaceBytes;
    private readonly long _bytes;
    private readonly Lock _disposeGate = new();
    private readonly WorldDomainReservation _domainReservation;
    private bool _isChargeReleased;
    private bool _isDisposed;
    private readonly World _world;

    internal WorldPersistenceAdmissionReservation(
        World world,
        WorldDomainReservation domainReservation,
        long bytes)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(domainReservation);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
        _bytes = bytes;
        _domainReservation = domainReservation;
        _world = world;
    }

    public void Dispose()
    {
        lock (_disposeGate)
        {
            _isDisposed = true;

            if (_activeUseCount == 0)
            {
                ReleaseCharge();
            }
        }
    }

    internal UseScope AcquireUse(World world, long workspaceBytes)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentOutOfRangeException.ThrowIfNegative(workspaceBytes);

        lock (_disposeGate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (!ReferenceEquals(_world, world))
            {
                throw new ArgumentException("The persistence reservation belongs to another World.", nameof(world));
            }

            if (workspaceBytes > (_bytes - _activeWorkspaceBytes))
            {
                throw new ArgumentException("The persistence reservation does not cover the simultaneous operation workspaces.", nameof(workspaceBytes));
            }

            _activeUseCount = checked(_activeUseCount + 1);
            _activeWorkspaceBytes = checked(_activeWorkspaceBytes + workspaceBytes);
            return new UseScope(this, workspaceBytes);
        }
    }

    internal void ReleaseUse(long workspaceBytes)
    {
        lock (_disposeGate)
        {
            if ((_activeUseCount <= 0)
                || (workspaceBytes < 0)
                || (workspaceBytes > _activeWorkspaceBytes))
            {
                throw new InvalidOperationException("The persistence reservation has no active operation use.");
            }

            _activeUseCount--;
            _activeWorkspaceBytes -= workspaceBytes;

            if (_isDisposed && (_activeUseCount == 0))
            {
                ReleaseCharge();
            }
        }
    }

    internal EbinWriteStatus WriteClockRecord(
        World world,
        WorldPersistenceCaptureLease lease,
        WorldPersistenceClockWorkspaceRequirements workspace,
        ref WorldPersistenceClockBuffers buffers,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(world);
        lease.ValidateCurrent();

        if (!ReferenceEquals(_world, world) || !lease.IsBoundTo(world))
        {
            throw new ArgumentException("The capture lease and reservation must belong to this exact World.", nameof(lease));
        }

        using var activeUse = AcquireUse(world, buffers.GetWorkspaceBytes());
        return WorldPersistenceClockWriter.Write(
            lease,
            workspace,
            WorldPersistenceEbinLimits.Create(world.Options),
            ref buffers,
            out bytesWritten,
            out diagnostic,
            cancellationToken);
    }

    internal EbinWriteStatus WriteConfigurationRecord(
        World world,
        WorldOptions options,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(options);
        var workspaceBytes = checked((long)candidateStorage.Length + destination.Length);
        using var activeUse = AcquireUse(world, workspaceBytes);

        if (!ReferenceEquals(world.Options, options))
        {
            throw new ArgumentException("The persistence reservation and options must belong to this exact World.", nameof(world));
        }

        return WorldPersistenceConfigurationWriter.Write(
            options,
            WorldPersistenceEbinLimits.Create(options),
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }

    internal EbinWriteStatus WriteCursorsRecord(
        World world,
        WorldPersistenceCaptureLease lease,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(world);
        lease.ValidateCurrent();

        if (!ReferenceEquals(_world, world) || !lease.IsBoundTo(world))
        {
            throw new ArgumentException("The capture lease and reservation must belong to this exact World.", nameof(lease));
        }

        var workspaceBytes = checked((long)candidateStorage.Length + destination.Length);
        using var activeUse = AcquireUse(world, workspaceBytes);
        return WorldPersistenceCursorWriter.Write(
            lease,
            WorldPersistenceEbinLimits.Create(world.Options),
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }

    internal EbinWriteStatus WriteEcsRecord(
        World world,
        WorldPersistenceCaptureLease lease,
        ref WorldPersistenceEcsBuffers buffers,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(world);
        lease.ValidateCurrent();

        if (!ReferenceEquals(_world, world) || !lease.IsBoundTo(world))
        {
            throw new ArgumentException("The capture lease and reservation must belong to this exact World.", nameof(lease));
        }

        using var activeUse = AcquireUse(world, buffers.GetWorkspaceBytes());

        return WorldPersistenceEcsWriter.Write(
            lease,
            ref buffers,
            WorldPersistenceEbinLimits.Create(world.Options),
            out bytesWritten,
            out diagnostic,
            cancellationToken);
    }

    internal EbinWriteStatus WriteEntityRecord(
        World world,
        WorldPersistenceCaptureLease lease,
        in World.EntityStateCaptureEnumerator entity,
        scoped Span<EbinPayloadSlice> componentSlices,
        scoped Span<byte> componentStorage,
        scoped Span<EbinPayloadSlice> tagSlices,
        scoped Span<byte> tagStorage,
        scoped Span<EbinPayloadSlice> transientSchemaSlices,
        scoped Span<byte> transientSchemaStorage,
        scoped Span<byte> childCandidateStorage,
        scoped Span<byte> payloadStorage,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(world);
        lease.ValidateCurrent();

        if (!ReferenceEquals(_world, world) || !entity.IsBoundTo(world) || !lease.IsBoundTo(world))
        {
            throw new ArgumentException("The Entity capture, lease, and reservation must belong to this exact World.", nameof(entity));
        }

        var workspaceBytes = checked(
            (long)MemoryMarshal.AsBytes(componentSlices).Length
            + componentStorage.Length
            + MemoryMarshal.AsBytes(tagSlices).Length
            + tagStorage.Length
            + MemoryMarshal.AsBytes(transientSchemaSlices).Length
            + transientSchemaStorage.Length
            + childCandidateStorage.Length
            + payloadStorage.Length
            + candidateStorage.Length
            + destination.Length);
        using var activeUse = AcquireUse(world, workspaceBytes);

        return WorldPersistenceEntityWriter.Write(
            lease,
            in entity,
            WorldPersistenceEbinLimits.Create(world.Options),
            componentSlices,
            componentStorage,
            tagSlices,
            tagStorage,
            transientSchemaSlices,
            transientSchemaStorage,
            childCandidateStorage,
            payloadStorage,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic,
            cancellationToken);
    }

    internal EbinWriteStatus WriteIdentityBranchRecord(
        World world,
        in World.IdentityBranchCaptureEnumerator branch,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(world);
        var workspaceBytes = checked((long)candidateStorage.Length + destination.Length);
        using var activeUse = AcquireUse(world, workspaceBytes);

        if (!branch.IsBoundTo(world))
        {
            throw new ArgumentException("The identity-branch capture and reservation must belong to this exact World.", nameof(world));
        }

        return WorldPersistenceIdentityBranchWriter.Write(
            in branch,
            WorldPersistenceEbinLimits.Create(world.Options),
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }

    internal EbinWriteStatus WriteIdentityHistoryRecord(
        World world,
        in World.IdentityHistoryCapture history,
        scoped Span<EbinPayloadSlice> branchSlices,
        scoped Span<byte> branchStorage,
        scoped Span<EbinPayloadSlice> pageSlices,
        scoped Span<byte> pageStorage,
        scoped Span<Guid> pageStableIds,
        scoped Span<uint> pageMetadata,
        scoped Span<byte> pagePayloadStorage,
        scoped Span<byte> childCandidateStorage,
        scoped Span<byte> rootPayloadStorage,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(world);
        var workspaceBytes = checked(
            (long)MemoryMarshal.AsBytes(branchSlices).Length
            + branchStorage.Length
            + MemoryMarshal.AsBytes(pageSlices).Length
            + pageStorage.Length
            + MemoryMarshal.AsBytes(pageStableIds).Length
            + MemoryMarshal.AsBytes(pageMetadata).Length
            + pagePayloadStorage.Length
            + childCandidateStorage.Length
            + rootPayloadStorage.Length
            + candidateStorage.Length
            + destination.Length);
        using var activeUse = AcquireUse(world, workspaceBytes);

        if (!history.IsBoundTo(world))
        {
            throw new ArgumentException("The identity-history capture and reservation must belong to this exact World.", nameof(world));
        }

        return WorldPersistenceIdentityHistoryWriter.Write(
            in history,
            WorldPersistenceEbinLimits.Create(world.Options),
            branchSlices,
            branchStorage,
            pageSlices,
            pageStorage,
            pageStableIds,
            pageMetadata,
            pagePayloadStorage,
            childCandidateStorage,
            rootPayloadStorage,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }

    internal EbinWriteStatus WriteIdentityPageRecord(
        World world,
        in World.IdentityHistoryCapture history,
        int pageIndex,
        scoped Span<Guid> stableIds,
        scoped Span<uint> metadata,
        scoped Span<byte> payloadStorage,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(world);
        var workspaceBytes = checked(
            (long)MemoryMarshal.AsBytes(stableIds).Length
            + MemoryMarshal.AsBytes(metadata).Length
            + payloadStorage.Length
            + candidateStorage.Length
            + destination.Length);
        using var activeUse = AcquireUse(world, workspaceBytes);

        if (!history.IsBoundTo(world))
        {
            throw new ArgumentException("The identity-history capture and reservation must belong to this exact World.", nameof(world));
        }

        return WorldPersistenceIdentityPageWriter.Write(
            in history,
            WorldPersistenceEbinLimits.Create(world.Options),
            pageIndex,
            stableIds,
            metadata,
            payloadStorage,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }

    internal EbinWriteStatus WriteRegistrationRecord(
        World world,
        WorldPersistenceCaptureLease lease,
        WorldPersistenceRegistrationWorkspaceRequirements workspace,
        scoped ReadOnlySpan<byte> componentsArrayPayload,
        scoped ReadOnlySpan<byte> tagsArrayPayload,
        scoped ReadOnlySpan<byte> systemsArrayPayload,
        scoped ReadOnlySpan<byte> resourcesArrayPayload,
        scoped Span<byte> rootPayloadStorage,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(world);
        lease.ValidateCurrent();

        if (!ReferenceEquals(_world, world) || !lease.IsBoundTo(world))
        {
            throw new ArgumentException("The capture lease and reservation must belong to this exact World.", nameof(lease));
        }

        var limits = WorldPersistenceEbinLimits.Create(world.Options);
        var status = WorldPersistenceRegistrationWriter.MeasureWorkspace(
            world.Registration,
            componentsArrayPayload,
            tagsArrayPayload,
            systemsArrayPayload,
            resourcesArrayPayload,
            limits,
            rootPayloadStorage,
            out var measuredWorkspace,
            out diagnostic);
        bytesWritten = 0;

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (!workspace.Matches(measuredWorkspace))
        {
            diagnostic = new EbinStructuralDiagnostic(
                new ContentSchemaIdentityAssignment("state/registration").SchemaId,
                0,
                "registration-workspace-mismatch",
                null,
                0,
                1,
                EbinStructuralDiagnosticSource.Construction);
            return EbinWriteStatus.InvalidRecord;
        }

        if ((candidateStorage.Length != workspace.RecordBytes)
            || (destination.Length != workspace.RecordBytes))
        {
            throw new ArgumentException("The registration root record workspaces must match the measured extent.");
        }

        using var activeUse = AcquireUse(world, workspace.WorkspaceBytes);
        return WorldPersistenceRegistrationWriter.Write(
            world.Registration,
            componentsArrayPayload,
            tagsArrayPayload,
            systemsArrayPayload,
            resourcesArrayPayload,
            limits,
            rootPayloadStorage,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }

    internal EbinWriteStatus WriteWorldRecord(
        World world,
        WorldPersistenceCaptureLease lease,
        WorldPersistenceWorldRecordSources sources,
        WorldPersistenceWorldRecordWorkspaceRequirements workspace,
        scoped Span<byte> rootPayloadStorage,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(world);
        lease.ValidateCurrent();

        if (!ReferenceEquals(_world, world)
            || !lease.IsBoundTo(world)
            || (sources.WorldGuid != lease.WorldGuid)
            || (sources.LeaseSequence != lease.Sequence))
        {
            throw new ArgumentException("The capture lease, World-root sources, and reservation must belong to this exact World capture.", nameof(lease));
        }

        using var activeUse = AcquireUse(world, workspace.WorkspaceBytes);
        var limits = WorldPersistenceEbinLimits.Create(world.Options);
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

        if (!workspace.Matches(measuredWorkspace))
        {
            diagnostic = new EbinStructuralDiagnostic(
                new ContentSchemaIdentityAssignment("state/world").SchemaId,
                0,
                "world-workspace-mismatch",
                null,
                0,
                1,
                EbinStructuralDiagnosticSource.Construction);
            return EbinWriteStatus.InvalidRecord;
        }

        if ((candidateStorage.Length != workspace.RecordBytes)
            || (destination.Length != workspace.RecordBytes))
        {
            throw new ArgumentException("The World root record workspaces must match the measured extent.");
        }

        return WorldPersistenceWorldRecordWriter.Write(
            sources,
            workspace,
            limits,
            rootPayloadStorage,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }

    private void ReleaseCharge()
    {
        if (_isChargeReleased)
        {
            return;
        }

        _domainReservation.Dispose();
        _world.ReleasePersistenceAdmissionBytes(_bytes);
        _isChargeReleased = true;
    }

    internal ref struct UseScope
    {
        private long _workspaceBytes;
        private WorldPersistenceAdmissionReservation? _reservation;

        internal UseScope(WorldPersistenceAdmissionReservation reservation, long workspaceBytes)
        {
            _workspaceBytes = workspaceBytes;
            _reservation = reservation;
        }

        public void Dispose()
        {
            var reservation = _reservation;
            _reservation = null;

            if (reservation is not null)
            {
                var workspaceBytes = _workspaceBytes;
                _workspaceBytes = 0;
                reservation.ReleaseUse(workspaceBytes);
            }
        }
    }
}
