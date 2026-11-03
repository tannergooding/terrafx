// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class WorldPersistenceAdmissionReservation : IDisposable
{
    private int _activeUseCount;
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

            if (workspaceBytes > _bytes)
            {
                throw new ArgumentException("The persistence reservation does not cover the operation workspaces.", nameof(workspaceBytes));
            }

            _activeUseCount = checked(_activeUseCount + 1);
            return new UseScope(this);
        }
    }

    internal void ReleaseUse()
    {
        lock (_disposeGate)
        {
            if (_activeUseCount <= 0)
            {
                throw new InvalidOperationException("The persistence reservation has no active operation use.");
            }

            _activeUseCount--;

            if (_isDisposed && (_activeUseCount == 0))
            {
                ReleaseCharge();
            }
        }
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

        lock (_disposeGate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (!ReferenceEquals(_world, world) || !ReferenceEquals(world.Options, options))
            {
                throw new ArgumentException("The persistence reservation and options must belong to this exact World.", nameof(world));
            }

            if (checked((long)candidateStorage.Length + destination.Length) > _bytes)
            {
                throw new ArgumentException("The persistence reservation does not cover both configuration-record workspaces.", nameof(candidateStorage));
            }

            return WorldPersistenceConfigurationWriter.Write(
                options,
                WorldPersistenceEbinLimits.Create(options),
                candidateStorage,
                destination,
                out bytesWritten,
                out diagnostic);
        }
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

        lock (_disposeGate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (!ReferenceEquals(_world, world) || !branch.IsBoundTo(world))
            {
                throw new ArgumentException("The identity-branch capture and reservation must belong to this exact World.", nameof(world));
            }

            if (checked((long)candidateStorage.Length + destination.Length) > _bytes)
            {
                throw new ArgumentException("The persistence reservation does not cover both identity-branch workspaces.", nameof(candidateStorage));
            }

            return WorldPersistenceIdentityBranchWriter.Write(
                in branch,
                WorldPersistenceEbinLimits.Create(world.Options),
                candidateStorage,
                destination,
                out bytesWritten,
                out diagnostic);
        }
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

        lock (_disposeGate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (!ReferenceEquals(_world, world) || !history.IsBoundTo(world))
            {
                throw new ArgumentException("The identity-history capture and reservation must belong to this exact World.", nameof(world));
            }

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

            if (workspaceBytes > _bytes)
            {
                throw new ArgumentException("The persistence reservation does not cover every identity-history workspace.", nameof(branchSlices));
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

        lock (_disposeGate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (!ReferenceEquals(_world, world) || !history.IsBoundTo(world))
            {
                throw new ArgumentException("The identity-history capture and reservation must belong to this exact World.", nameof(world));
            }

            var workspaceBytes = checked(
                (long)MemoryMarshal.AsBytes(stableIds).Length
                + MemoryMarshal.AsBytes(metadata).Length
                + payloadStorage.Length
                + candidateStorage.Length
                + destination.Length);

            if (workspaceBytes > _bytes)
            {
                throw new ArgumentException("The persistence reservation does not cover every identity-page workspace.", nameof(stableIds));
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
        private WorldPersistenceAdmissionReservation? _reservation;

        internal UseScope(WorldPersistenceAdmissionReservation reservation)
        {
            _reservation = reservation;
        }

        public void Dispose()
        {
            var reservation = _reservation;
            _reservation = null;
            reservation?.ReleaseUse();
        }
    }
}
