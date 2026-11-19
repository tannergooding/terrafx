// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using TerraFX;

namespace TerraFX.Entities;

internal sealed partial class WorldGuidRegistry
{
    private const int TerminalAnchorStateEscrowed = 3;
    private const int TerminalAnchorStateInvoking = 2;
    private const int TerminalAnchorStateNone = 0;
    private const int TerminalAnchorStatePrepared = 1;
    private const int TerminalAnchorStatePublisherReturned = 4;
    private const int TerminalAnchorStateRetained = 5;

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Any failed terminal close must retain its exact process-rooted owner before the original exception is rethrown.")]
    internal void InvokeTerminalWorldClose(World world, WorldLifetimeControl lifetime)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(lifetime);

        var domain = lifetime.AdmissionDomain;
        var grant = lifetime.WorldGuidGrant;

        if (!domain.TryBeginTerminalInvocation(this, grant, lifetime))
        {
            throw new InvalidOperationException("The exact WorldGuid terminal anchor is not prepared for this close invocation.");
        }

        try
        {
            world.DisposeWithinTerminalInvocation(this);
        }
        catch (Exception failure)
        {
            _ = domain.TryRetainTerminalFailure(failure);
            throw;
        }

        if (!domain.TryAcceptTerminalCallerReturn(lifetime)
            || !TryCompleteTerminalAnchor(lifetime.RetirementPermit, grant, domain))
        {
            var failure = new InvalidOperationException("The terminal World close did not complete its exact caller-return handoff.");
            _ = domain.TryRetainTerminalFailure(failure);
            throw failure;
        }
    }

    internal bool TryAttachTerminalAnchor(
        in WorldGuidGrant grant,
        WorldAdmissionDomain domain,
        WorldLifetimeControl? lifetime)
    {
        ArgumentNullException.ThrowIfNull(domain);

        if (!grant.IsOwnedBy(this))
        {
            return false;
        }

        using var scope = ReentrantGate.EnterScope(ref _admissionGate);

        if (!IsKeyCellValid(grant.KeyCellIndex, grant.KeyGeneration)
            || !IsBindingCellValid(grant.BindingCellIndex, grant.BindingGeneration)
            || !IsUseCellValid(grant.OwnerUseCellIndex, grant.OwnerUseGeneration))
        {
            return false;
        }

        ref var keyCell = ref GetKeyCell(grant.KeyCellIndex);
        ref var bindingCell = ref GetBindingCell(grant.BindingCellIndex);
        ref var ownerUseCell = ref GetUseCell(grant.OwnerUseCellIndex);
        var isReserved = bindingCell._state == CellStateReserved;
        var isActive = (bindingCell._state == CellStateActive) && bindingCell._worldOwnerBound;

        if ((keyCell._worldGuid != grant.WorldGuid)
            || (!isReserved && !isActive)
            || (bindingCell._keyCellIndex != grant.KeyCellIndex)
            || (bindingCell._keyGeneration != grant.KeyGeneration)
            || (bindingCell._worldOwnerBound != isActive)
            || (bindingCell._independentUseCount != 0)
            || (bindingCell._terminalDomain is not null)
            || (bindingCell._terminalState != TerminalAnchorStateNone)
            || (bindingCell._terminalFailure is not null)
            || (ownerUseCell._state != CellStateReserved)
            || (ownerUseCell._role != UseRoleOwner)
            || (ownerUseCell._bindingCellIndex != grant.BindingCellIndex)
            || (ownerUseCell._bindingGeneration != grant.BindingGeneration)
            || (lifetime is not null
                && (!isActive
                    || !ReferenceEquals(lifetime.Registry, this)
                    || !lifetime.WorldGuidGrant.Equals(grant))))
        {
            return false;
        }

        if (isReserved)
        {
            if (!IsPreparationCellValid(grant.PreparationCellIndex, grant.PreparationGeneration)
                || !IsUseCellValid(grant.CoordinatorUseCellIndex, grant.CoordinatorUseGeneration))
            {
                return false;
            }

            ref var preparationCell = ref GetPreparationCell(grant.PreparationCellIndex);
            ref var coordinatorUseCell = ref GetUseCell(grant.CoordinatorUseCellIndex);

            if ((preparationCell._state != CellStateReserved)
                || (coordinatorUseCell._state != CellStateReserved)
                || (coordinatorUseCell._role != UseRoleCoordinator)
                || (coordinatorUseCell._bindingCellIndex != grant.BindingCellIndex)
                || (coordinatorUseCell._bindingGeneration != grant.BindingGeneration))
            {
                return false;
            }
        }

        bindingCell._terminalDomain = domain;
        bindingCell._terminalState = TerminalAnchorStatePrepared;
        return true;
    }

    internal bool TryAttachTerminalAnchor(in WorldGuidGrant grant, WorldAdmissionDomain domain)
        => TryAttachTerminalAnchor(grant, domain, lifetime: null);

    internal bool TryBeginTerminalInvocation(in WorldGuidGrant grant, WorldAdmissionDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);

        if (!grant.IsOwnedBy(this))
        {
            return false;
        }

        using var scope = ReentrantGate.EnterScope(ref _admissionGate);

        if (!IsKeyCellValid(grant.KeyCellIndex, grant.KeyGeneration)
            || !IsBindingCellValid(grant.BindingCellIndex, grant.BindingGeneration))
        {
            return false;
        }

        ref var keyCell = ref GetKeyCell(grant.KeyCellIndex);
        ref var bindingCell = ref GetBindingCell(grant.BindingCellIndex);

        if ((keyCell._worldGuid != grant.WorldGuid)
            || (bindingCell._state != CellStateActive)
            || !bindingCell._worldOwnerBound
            || (bindingCell._keyCellIndex != grant.KeyCellIndex)
            || (bindingCell._keyGeneration != grant.KeyGeneration)
            || (bindingCell._independentUseCount != 0)
            || !ReferenceEquals(bindingCell._terminalDomain, domain)
            || (bindingCell._terminalState != TerminalAnchorStatePrepared)
            || (bindingCell._terminalFailure is not null))
        {
            return false;
        }

        bindingCell._terminalState = TerminalAnchorStateInvoking;
        return true;
    }

    internal bool TryCompleteTerminalAnchor(
        WorldGuidRetirementPermit permit,
        in WorldGuidGrant grant,
        WorldAdmissionDomain domain)
    {
        ArgumentNullException.ThrowIfNull(permit);
        ArgumentNullException.ThrowIfNull(domain);

        if (!grant.IsOwnedBy(this) || !permit.IsTerminalEscrowedFor(this, grant))
        {
            return false;
        }

        using var scope = ReentrantGate.EnterScope(ref _admissionGate);

        if (!IsKeyCellValid(grant.KeyCellIndex, grant.KeyGeneration)
            || !IsBindingCellReleaseValid(grant.BindingCellIndex, grant.BindingGeneration)
            || !IsUseCellValid(grant.OwnerUseCellIndex, grant.OwnerUseGeneration))
        {
            return false;
        }

        ref var keyCell = ref GetKeyCell(grant.KeyCellIndex);
        ref var bindingCell = ref GetBindingCell(grant.BindingCellIndex);
        ref var ownerUseCell = ref GetUseCell(grant.OwnerUseCellIndex);

        if ((keyCell._worldGuid != grant.WorldGuid)
            || (bindingCell._state != CellStateRevoked)
            || (bindingCell._keyCellIndex != grant.KeyCellIndex)
            || (bindingCell._keyGeneration != grant.KeyGeneration)
            || (bindingCell._independentUseCount != 0)
            || !ReferenceEquals(bindingCell._terminalDomain, domain)
            || (bindingCell._terminalState != TerminalAnchorStatePublisherReturned)
            || (bindingCell._terminalFailure is not null)
            || (ownerUseCell._state != CellStateReserved)
            || (ownerUseCell._role != UseRoleOwner)
            || (ownerUseCell._bindingCellIndex != grant.BindingCellIndex)
            || (ownerUseCell._bindingGeneration != grant.BindingGeneration))
        {
            return false;
        }

        var bucketIndex = FindIndexBucket(grant.WorldGuid);

        if (bucketIndex < 0)
        {
            return false;
        }

        var index = _index ?? throw new InvalidOperationException("The WorldGuid index is unavailable.");
        ref var bucket = ref index[bucketIndex];

        if ((bucket._keyCellIndex != grant.KeyCellIndex)
            || (bucket._keyGeneration != grant.KeyGeneration)
            || !domain.TryCommitTerminalAnchorCompletion(this, grant))
        {
            return false;
        }

        ReleaseBindingCell(grant.BindingCellIndex, ref bindingCell);
        ReleaseUseCell(grant.OwnerUseCellIndex, ref ownerUseCell);
        keyCell._roleCount -= 2;
        ReleaseKeyIfUnowned(grant.KeyCellIndex, ref keyCell);
        return true;
    }

    internal bool TryDetachTerminalAnchor(in WorldGuidGrant grant, WorldAdmissionDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);

        if (!grant.IsOwnedBy(this))
        {
            return false;
        }

        using var scope = ReentrantGate.EnterScope(ref _admissionGate);

        if (!IsKeyCellValid(grant.KeyCellIndex, grant.KeyGeneration)
            || !IsBindingCellValid(grant.BindingCellIndex, grant.BindingGeneration))
        {
            return false;
        }

        ref var keyCell = ref GetKeyCell(grant.KeyCellIndex);
        ref var bindingCell = ref GetBindingCell(grant.BindingCellIndex);

        if ((keyCell._worldGuid != grant.WorldGuid)
            || (bindingCell._state != CellStateReserved)
            || (bindingCell._keyCellIndex != grant.KeyCellIndex)
            || (bindingCell._keyGeneration != grant.KeyGeneration)
            || !ReferenceEquals(bindingCell._terminalDomain, domain)
            || (bindingCell._terminalState != TerminalAnchorStatePrepared)
            || (bindingCell._terminalFailure is not null))
        {
            return false;
        }

        bindingCell._terminalDomain = null;
        bindingCell._terminalState = TerminalAnchorStateNone;
        return true;
    }

    internal bool TryEscrowTerminalRetirement(
        WorldGuidRetirementPermit permit,
        WorldAdmissionDomain domain,
        WorldLifetimeControl lifetime,
        in WorldGuidGrant grant)
    {
        ArgumentNullException.ThrowIfNull(permit);
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(lifetime);

        if (!grant.IsOwnedBy(this)
            || !permit.TryGetRetirementGrant(this, out var permitGrant)
            || !permitGrant.Equals(grant))
        {
            return false;
        }

        using var scope = ReentrantGate.EnterScope(ref _admissionGate);

        if (!IsKeyCellValid(grant.KeyCellIndex, grant.KeyGeneration)
            || !IsBindingCellReleaseValid(grant.BindingCellIndex, grant.BindingGeneration)
            || !IsUseCellValid(grant.OwnerUseCellIndex, grant.OwnerUseGeneration))
        {
            return false;
        }

        ref var keyCell = ref GetKeyCell(grant.KeyCellIndex);
        ref var bindingCell = ref GetBindingCell(grant.BindingCellIndex);
        ref var ownerUseCell = ref GetUseCell(grant.OwnerUseCellIndex);

        if ((keyCell._worldGuid != grant.WorldGuid)
            || (bindingCell._state != CellStateRevoked)
            || (bindingCell._keyCellIndex != grant.KeyCellIndex)
            || (bindingCell._keyGeneration != grant.KeyGeneration)
            || (bindingCell._independentUseCount != 0)
            || !ReferenceEquals(bindingCell._terminalDomain, domain)
            || (bindingCell._terminalState != TerminalAnchorStateInvoking)
            || (bindingCell._terminalFailure is not null)
            || (ownerUseCell._state != CellStateReserved)
            || (ownerUseCell._role != UseRoleOwner)
            || (ownerUseCell._bindingCellIndex != grant.BindingCellIndex)
            || (ownerUseCell._bindingGeneration != grant.BindingGeneration)
            || !permit.TryConsumeForTerminalEscrow(this, grant))
        {
            return false;
        }

        bindingCell._terminalState = TerminalAnchorStateEscrowed;
        return true;
    }

    internal bool TryMarkTerminalPublisherReturned(
        WorldGuidRetirementPermit permit,
        in WorldGuidGrant grant,
        WorldAdmissionDomain domain)
    {
        ArgumentNullException.ThrowIfNull(permit);
        ArgumentNullException.ThrowIfNull(domain);

        if (!grant.IsOwnedBy(this) || !permit.IsTerminalEscrowedFor(this, grant))
        {
            return false;
        }

        using var scope = ReentrantGate.EnterScope(ref _admissionGate);

        if (!IsKeyCellValid(grant.KeyCellIndex, grant.KeyGeneration)
            || !IsBindingCellReleaseValid(grant.BindingCellIndex, grant.BindingGeneration))
        {
            return false;
        }

        ref var keyCell = ref GetKeyCell(grant.KeyCellIndex);
        ref var bindingCell = ref GetBindingCell(grant.BindingCellIndex);

        if ((keyCell._worldGuid != grant.WorldGuid)
            || (bindingCell._state != CellStateRevoked)
            || (bindingCell._keyCellIndex != grant.KeyCellIndex)
            || (bindingCell._keyGeneration != grant.KeyGeneration)
            || !ReferenceEquals(bindingCell._terminalDomain, domain)
            || (bindingCell._terminalState != TerminalAnchorStateEscrowed)
            || (bindingCell._terminalFailure is not null))
        {
            return false;
        }

        bindingCell._terminalState = TerminalAnchorStatePublisherReturned;
        return true;
    }

    internal bool TryRetainTerminalFailure(
        in WorldGuidGrant grant,
        WorldAdmissionDomain domain,
        Exception failure)
    {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(failure);

        if (!grant.IsOwnedBy(this))
        {
            return false;
        }

        using var scope = ReentrantGate.EnterScope(ref _admissionGate);

        if (!IsKeyCellValid(grant.KeyCellIndex, grant.KeyGeneration)
            || !IsBindingCellValid(grant.BindingCellIndex, grant.BindingGeneration))
        {
            return false;
        }

        ref var keyCell = ref GetKeyCell(grant.KeyCellIndex);
        ref var bindingCell = ref GetBindingCell(grant.BindingCellIndex);

        if ((keyCell._worldGuid != grant.WorldGuid)
            || (bindingCell._state is not (CellStateReserved or CellStateActive or CellStateRevoked))
            || (bindingCell._keyCellIndex != grant.KeyCellIndex)
            || (bindingCell._keyGeneration != grant.KeyGeneration)
            || !ReferenceEquals(bindingCell._terminalDomain, domain)
            || (bindingCell._terminalState is TerminalAnchorStateNone or TerminalAnchorStateRetained)
            || (bindingCell._terminalFailure is not null))
        {
            return false;
        }

        bindingCell._terminalFailure = failure;
        bindingCell._terminalState = TerminalAnchorStateRetained;
        return true;
    }
}
