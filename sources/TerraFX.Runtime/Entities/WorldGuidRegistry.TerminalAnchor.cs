// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;

namespace TerraFX.Entities;

internal sealed partial class WorldGuidRegistry
{
    private const int TerminalAnchorStateNone = 0;
    private const int TerminalAnchorStatePrepared = 1;
    private const int TerminalAnchorStateRetained = 2;

    internal bool TryAttachTerminalAnchor(in WorldGuidGrant grant, WorldAdmissionDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);

        if (!grant.IsOwnedBy(this))
        {
            return false;
        }

        using var scope = ReentrantGate.EnterScope(ref _admissionGate);

        if (!IsKeyCellValid(grant.KeyCellIndex, grant.KeyGeneration)
            || !IsBindingCellValid(grant.BindingCellIndex, grant.BindingGeneration)
            || !IsUseCellValid(grant.OwnerUseCellIndex, grant.OwnerUseGeneration)
            || !IsPreparationCellValid(grant.PreparationCellIndex, grant.PreparationGeneration)
            || !IsUseCellValid(grant.CoordinatorUseCellIndex, grant.CoordinatorUseGeneration))
        {
            return false;
        }

        ref var keyCell = ref GetKeyCell(grant.KeyCellIndex);
        ref var bindingCell = ref GetBindingCell(grant.BindingCellIndex);
        ref var preparationCell = ref GetPreparationCell(grant.PreparationCellIndex);
        ref var ownerUseCell = ref GetUseCell(grant.OwnerUseCellIndex);
        ref var coordinatorUseCell = ref GetUseCell(grant.CoordinatorUseCellIndex);

        if ((keyCell._worldGuid != grant.WorldGuid)
            || (bindingCell._state != CellStateReserved)
            || (bindingCell._keyCellIndex != grant.KeyCellIndex)
            || (bindingCell._keyGeneration != grant.KeyGeneration)
            || bindingCell._worldOwnerBound
            || (bindingCell._independentUseCount != 0)
            || (bindingCell._terminalDomain is not null)
            || (bindingCell._terminalState != TerminalAnchorStateNone)
            || (bindingCell._terminalFailure is not null)
            || (preparationCell._state != CellStateReserved)
            || (ownerUseCell._state != CellStateReserved)
            || (ownerUseCell._role != UseRoleOwner)
            || (ownerUseCell._bindingCellIndex != grant.BindingCellIndex)
            || (ownerUseCell._bindingGeneration != grant.BindingGeneration)
            || (coordinatorUseCell._state != CellStateReserved)
            || (coordinatorUseCell._role != UseRoleCoordinator)
            || (coordinatorUseCell._bindingCellIndex != grant.BindingCellIndex)
            || (coordinatorUseCell._bindingGeneration != grant.BindingGeneration))
        {
            return false;
        }

        bindingCell._terminalDomain = domain;
        bindingCell._terminalState = TerminalAnchorStatePrepared;
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
            || (bindingCell._state != CellStateReserved)
            || (bindingCell._keyCellIndex != grant.KeyCellIndex)
            || (bindingCell._keyGeneration != grant.KeyGeneration)
            || !ReferenceEquals(bindingCell._terminalDomain, domain)
            || (bindingCell._terminalState != TerminalAnchorStatePrepared)
            || (bindingCell._terminalFailure is not null))
        {
            return false;
        }

        bindingCell._terminalFailure = failure;
        bindingCell._terminalState = TerminalAnchorStateRetained;
        return true;
    }
}
