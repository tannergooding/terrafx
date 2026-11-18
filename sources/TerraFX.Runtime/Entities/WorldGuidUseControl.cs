// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class WorldGuidUseControl : IWorldGuidRetentionOwner
{
    internal const long ControlByteCharge = 256;

    private const int StateActive = 1;
    private const int StateQuarantined = 3;
    private const int StateReleased = 4;
    private const int StateReleasing = 2;
    private const int StateUnissued = 0;

    private WorldDomainRetentionAnchor _anchor;
    private readonly uint _carrierGeneration;
    private int _carrierState;
    private readonly WorldAdmissionDomain _domain;
    private readonly WorldGuidGrant _grant;
    private readonly WorldLifetimeControl _lifetime;
    private readonly WorldGuidRegistry _registry;
    private readonly WorldDomainReservation _reservation;
    private uint _useCellGeneration;
    private int _useCellIndex = -1;

    internal WorldGuidUseControl(
        WorldGuidRegistry registry,
        WorldAdmissionDomain domain,
        WorldDomainReservation reservation,
        WorldLifetimeControl lifetime,
        in WorldGuidGrant grant,
        uint carrierGeneration,
        long workspaceLimit)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(reservation);
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentOutOfRangeException.ThrowIfNegative(workspaceLimit);

        if (!grant.IsOwnedBy(registry)
            || !reservation.IsOwnedBy(domain)
            || (carrierGeneration == 0))
        {
            throw new ArgumentException("The independent WorldGuid use control requires its exact registry grant and generation.", nameof(grant));
        }

        _registry = registry;
        _domain = domain;
        _reservation = reservation;
        _lifetime = lifetime;
        _grant = grant;
        _carrierGeneration = carrierGeneration;
        _anchor.Generation = carrierGeneration;
        _anchor.InitialWorkspaceBytes = workspaceLimit;
        _anchor.WorkspaceLimit = workspaceLimit;
        _anchor.OperationBytes = checked(ControlByteCharge + workspaceLimit);
        _anchor.HistoryRootIndex = -1;
        _anchor.HistoryInterestIndex = -1;
        _anchor.OperationInterestIndex = -1;
    }

    internal WorldDomainRetentionAnchor Anchor => _anchor;

    internal bool CanReleaseWorkspace
        => Volatile.Read(ref _carrierState) is StateActive or StateQuarantined;

    internal bool CanReserveWorkspace
        => Volatile.Read(ref _carrierState) == StateActive;

    internal uint CarrierGeneration => _carrierGeneration;

    internal WorldAdmissionDomain Domain => _domain;

    internal WorldDomainReservation DomainReservation => _reservation;

    internal WorldGuidGrant Grant => _grant;

    internal bool IsActive => Volatile.Read(ref _carrierState) == StateActive;

    internal bool IsQuarantined => Volatile.Read(ref _carrierState) == StateQuarantined;

    internal bool IsReleased => Volatile.Read(ref _carrierState) == StateReleased;

    internal bool IsReleasing => Volatile.Read(ref _carrierState) == StateReleasing;

    internal WorldLifetimeControl Lifetime => _lifetime;

    internal WorldGuidUseControl? Next => _anchor.Next;

    internal WorldGuidRegistry Registry => _registry;

    internal uint UseCellGeneration => _useCellGeneration;

    internal int UseCellIndex => _useCellIndex;

    internal long WorkspaceLimit => _anchor.WorkspaceLimit;

    internal void ApplyActiveWorkspaceTransfer(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);

        if ((bytes > _anchor.ActiveWorkspaceBytes)
            || (bytes > (_anchor.OperationBytes - ControlByteCharge)))
        {
            throw new InvalidOperationException("The independent use does not own the active workspace being transferred.");
        }

        _anchor.ActiveWorkspaceBytes -= bytes;
        _anchor.OperationBytes -= bytes;
        _anchor.UninstalledActiveWorldWorkspaceBytes = checked(_anchor.UninstalledActiveWorldWorkspaceBytes + bytes);
    }

    internal void ApplyWorkspaceRelease(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);

        if (bytes > _anchor.ActiveWorkspaceBytes)
        {
            throw new InvalidOperationException("The independent use does not own the released workspace bytes.");
        }

        _anchor.ActiveWorkspaceBytes -= bytes;
        _anchor.WorkspaceLimit = checked(_anchor.WorkspaceLimit + bytes);
    }

    internal void ApplyWorkspaceReservation(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);

        if (bytes > _anchor.WorkspaceLimit)
        {
            throw new InvalidOperationException("The independent use does not own the reserved workspace bytes.");
        }

        _anchor.WorkspaceLimit -= bytes;
        _anchor.ActiveWorkspaceBytes = checked(_anchor.ActiveWorkspaceBytes + bytes);
    }

    internal void ApplyWorkspaceReturn(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);

        if (bytes > (_anchor.InitialWorkspaceBytes - _anchor.WorkspaceLimit - _anchor.ActiveWorkspaceBytes))
        {
            throw new InvalidOperationException("The independent use does not own the returned workspace bytes.");
        }

        _anchor.WorkspaceLimit += bytes;
        _anchor.OperationBytes = checked(_anchor.OperationBytes + bytes);
        _anchor.UninstalledWorldWorkspaceBytes -= bytes;
    }

    internal void ApplyWorkspaceTransfer(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);

        if ((bytes > _anchor.WorkspaceLimit)
            || (bytes > (_anchor.OperationBytes - ControlByteCharge)))
        {
            throw new InvalidOperationException("The independent use does not own the transferred workspace bytes.");
        }

        _anchor.WorkspaceLimit -= bytes;
        _anchor.OperationBytes -= bytes;
        _anchor.UninstalledWorldWorkspaceBytes = checked(_anchor.UninstalledWorldWorkspaceBytes + bytes);
    }

    internal void ClearDomainInterests()
    {
        _anchor.HistoryRootIndex = -1;
        _anchor.HistoryRootGeneration = 0;
        _anchor.HistoryInterestIndex = -1;
        _anchor.HistoryInterestGeneration = 0;
        _anchor.OperationInterestIndex = -1;
        _anchor.OperationInterestGeneration = 0;
        _anchor.OperationBytes = 0;
        _anchor.InitialWorkspaceBytes = 0;
        _anchor.WorkspaceLimit = 0;
        _anchor.ActiveWorkspaceBytes = 0;
        _anchor.UninstalledWorldWorkspaceBytes = 0;
        _anchor.UninstalledActiveWorldWorkspaceBytes = 0;
    }

    internal void ClearRetainedOwner(object currentOwner)
    {
        ArgumentNullException.ThrowIfNull(currentOwner);

        if ((Volatile.Read(ref _carrierState) != StateActive)
            || !ReferenceEquals(_anchor.RetainedOwner, currentOwner))
        {
            throw new InvalidOperationException("The independent use does not retain this owner.");
        }

        _anchor.RetainedOwner = null;
    }

    internal void CommitSceneWorkspaceTransfer(long activeWorkspaceBytes, long operationBytes)
    {
        _anchor.ActiveWorkspaceBytes = activeWorkspaceBytes;
        _anchor.OperationBytes = operationBytes;
    }

    internal void CompleteDomainRelease()
    {
        ClearDomainInterests();
        _anchor.RetainedOwner = null;
        _anchor.Next = null;
        Volatile.Write(ref _carrierState, StateReleased);
    }

    internal WorldGuidUse CreateCarrier()
    {
        if (Volatile.Read(ref _carrierState) != StateActive)
        {
            throw new InvalidOperationException("The independent use has not been fully admitted.");
        }

        return new WorldGuidUse(this, _carrierGeneration);
    }

    internal bool IsActiveUseFor(WorldGuidRegistry registry, in WorldGuidGrant grant)
        => ReferenceEquals(_registry, registry)
            && _grant.Equals(grant)
            && (Volatile.Read(ref _carrierState) is StateUnissued or StateActive);

    internal void IssueDomainInterests(
        int historyRootIndex,
        uint historyRootGeneration,
        int historyInterestIndex,
        uint historyInterestGeneration,
        int operationInterestIndex,
        uint operationInterestGeneration,
        long operationBytes)
    {
        if ((historyRootIndex < 0)
            || (historyRootGeneration == 0)
            || (historyInterestIndex < 0)
            || (historyInterestGeneration == 0)
            || (operationInterestIndex < 0)
            || (operationInterestGeneration == 0)
            || (operationBytes < ControlByteCharge)
            || (Volatile.Read(ref _carrierState) != StateUnissued))
        {
            throw new InvalidOperationException("The independent use cannot receive these domain interests.");
        }

        _anchor.HistoryRootIndex = historyRootIndex;
        _anchor.HistoryRootGeneration = historyRootGeneration;
        _anchor.HistoryInterestIndex = historyInterestIndex;
        _anchor.HistoryInterestGeneration = historyInterestGeneration;
        _anchor.OperationInterestIndex = operationInterestIndex;
        _anchor.OperationInterestGeneration = operationInterestGeneration;
        _anchor.OperationBytes = operationBytes;
    }

    internal void IssueUseCell(int useCellIndex, uint useCellGeneration)
    {
        if ((useCellIndex < 0)
            || (useCellGeneration == 0)
            || (Volatile.Read(ref _carrierState) != StateUnissued)
            || (_anchor.HistoryInterestIndex < 0)
            || (_anchor.OperationInterestIndex < 0))
        {
            throw new InvalidOperationException("The independent use cannot receive this process cell.");
        }

        _useCellIndex = useCellIndex;
        _useCellGeneration = useCellGeneration;
        Volatile.Write(ref _carrierState, StateActive);
    }

    internal void Quarantine(object retainedOwner, int failureStage, int failureCategory)
    {
        ArgumentNullException.ThrowIfNull(retainedOwner);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(failureStage);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(failureCategory);
        var state = Volatile.Read(ref _carrierState);

        if (state is StateReleased or StateQuarantined)
        {
            return;
        }

        _anchor.RetainedOwner = retainedOwner;
        _anchor.FailureStage = failureStage;
        _anchor.FailureCategory = failureCategory;
        Volatile.Write(ref _carrierState, StateQuarantined);
    }

    internal void Release(uint carrierGeneration)
        => Release(carrierGeneration, default);

    internal void Release(uint carrierGeneration, in WorldCosmeticCreditOwner.AcquisitionJournal journal)
    {
        journal.ValidateControl(this);

        if (carrierGeneration != _carrierGeneration)
        {
            throw new InvalidOperationException("The WorldGuid use carrier generation is stale.");
        }

        var state = Volatile.Read(ref _carrierState);

        if (state is StateReleased or StateQuarantined)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _carrierState, StateReleasing, StateActive) != StateActive)
        {
            if (Volatile.Read(ref _carrierState) is StateReleased or StateQuarantined)
            {
                return;
            }

            throw new InvalidOperationException("The WorldGuid use is already being released.");
        }

        var domain = _domain;
        var registry = _registry;
        WorldDomainReservation? returnParent = null;

        try
        {
            try
            {
                if (!domain.TryRetainWorldGuidUseReturnPart(this, out returnParent))
                {
                    throw new InvalidOperationException("The independent WorldGuid use cannot retain its exact parent's return part.");
                }

                if ((_anchor.ActiveWorkspaceBytes != 0)
                    || (_anchor.UninstalledWorldWorkspaceBytes != 0)
                    || (_anchor.UninstalledActiveWorldWorkspaceBytes != 0))
                {
                    throw new InvalidOperationException("The independent WorldGuid use still owns active workspace reservations.");
                }

                if (!registry.TryReleaseIndependentUse(this, journal))
                {
                    throw new InvalidOperationException("The independent WorldGuid U could not be released by its exact generation.");
                }
            }
            catch
            {
                Volatile.Write(ref _carrierState, StateQuarantined);
                throw;
            }

            if (!domain.TryReleaseIndependentUse(this, journal))
            {
                Volatile.Write(ref _carrierState, StateQuarantined);
                throw new InvalidOperationException("The independent WorldGuid domain interests could not be released by their exact generations.");
            }
        }
        finally
        {
            if (returnParent is not null)
            {
                domain.CompleteWorldReturnPart(returnParent);
            }
        }
    }

    internal void SetNext(WorldGuidUseControl? next)
        => _anchor.Next = next;

    internal void SetRetainedOwner(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var state = Volatile.Read(ref _carrierState);

        if (state is StateQuarantined or StateReleased)
        {
            throw new InvalidOperationException("The independent use no longer accepts a retained owner.");
        }

        _anchor.RetainedOwner = owner;
    }

    internal void TransferRetainedOwner(object currentOwner, object nextOwner)
    {
        ArgumentNullException.ThrowIfNull(currentOwner);
        ArgumentNullException.ThrowIfNull(nextOwner);

        if ((Volatile.Read(ref _carrierState) != StateActive)
            || !ReferenceEquals(_anchor.RetainedOwner, currentOwner))
        {
            throw new InvalidOperationException("The independent use is not owned by the transferring Scene operation.");
        }

        _anchor.RetainedOwner = nextOwner;
    }

    internal bool TryReleaseWorkspace(long bytes)
        => _domain.TryReleaseWorldGuidUseWorkspace(this, bytes);

    internal bool TryReserveWorkspace(long bytes)
        => _domain.TryReserveWorldGuidUseWorkspace(this, bytes);

    internal bool TryReturnWorkspaceFromWorldReservation(
        WorldDomainReservation reservation,
        long bytes)
        => _domain.TryReturnWorldGuidUseWorkspace(this, reservation, bytes);

    internal bool TryTransferActiveWorkspaceToWorldReservation(
        WorldDomainReservation reservation,
        long bytes)
        => _domain.TryTransferActiveWorldGuidUseWorkspace(this, reservation, bytes);

    internal bool TryTransferWorkspaceToWorldReservation(
        WorldDomainReservation reservation,
        long bytes)
        => _domain.TryTransferWorldGuidUseWorkspace(this, reservation, bytes);

    [StructLayout(LayoutKind.Sequential, Pack = 8, Size = 128)]
    internal struct WorldDomainRetentionAnchor
    {
        internal long ActiveWorkspaceBytes;

        internal int FailureCategory;

        internal int FailureClaim;

        internal int FailureCode;

        internal int FailureLimit;

        internal int FailureStage;

        internal int Flags;

        internal uint Generation;

        internal uint HistoryInterestGeneration;

        internal int HistoryInterestIndex;

        internal uint HistoryRootGeneration;

        internal int HistoryRootIndex;

        internal WorldGuidUseControl? Next;

        internal long OperationBytes;

        internal uint OperationInterestGeneration;

        internal int OperationInterestIndex;

        internal long InitialWorkspaceBytes;

        internal long UninstalledWorldWorkspaceBytes;

        internal long UninstalledActiveWorldWorkspaceBytes;

        internal object? RetainedOwner;

        internal int ReservedFailure;

        internal int State;

        internal int WorkCount;

        internal long WorkspaceLimit;
    }
}
