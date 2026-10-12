// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal readonly struct WorldGuidGrant
{
    private readonly int _keyCellIndex;
    private readonly int _bindingCellIndex;
    private readonly uint _bindingGeneration;
    private readonly int _coordinatorUseCellIndex;
    private readonly uint _coordinatorUseGeneration;
    private readonly uint _keyGeneration;
    private readonly int _ownerUseCellIndex;
    private readonly uint _ownerUseGeneration;
    private readonly int _preparationCellIndex;
    private readonly uint _preparationGeneration;
    private readonly WorldGuidRegistry? _registry;
    private readonly Guid _worldGuid;

    internal WorldGuidGrant(
        WorldGuidRegistry registry,
        Guid worldGuid,
        int keyCellIndex,
        uint keyGeneration,
        int bindingCellIndex,
        uint bindingGeneration,
        int preparationCellIndex,
        uint preparationGeneration,
        int ownerUseCellIndex,
        uint ownerUseGeneration,
        int coordinatorUseCellIndex,
        uint coordinatorUseGeneration)
    {
        _registry = registry;
        _worldGuid = worldGuid;
        _keyCellIndex = keyCellIndex;
        _keyGeneration = keyGeneration;
        _bindingCellIndex = bindingCellIndex;
        _bindingGeneration = bindingGeneration;
        _preparationCellIndex = preparationCellIndex;
        _preparationGeneration = preparationGeneration;
        _ownerUseCellIndex = ownerUseCellIndex;
        _ownerUseGeneration = ownerUseGeneration;
        _coordinatorUseCellIndex = coordinatorUseCellIndex;
        _coordinatorUseGeneration = coordinatorUseGeneration;
    }

    internal int BindingCellIndex => _bindingCellIndex;

    internal uint BindingGeneration => _bindingGeneration;

    internal int CoordinatorUseCellIndex => _coordinatorUseCellIndex;

    internal uint CoordinatorUseGeneration => _coordinatorUseGeneration;

    internal bool IsValid
        => (_registry is not null)
        && (_worldGuid != Guid.Empty)
        && (_keyGeneration != 0)
        && (_bindingGeneration != 0)
        && (_preparationGeneration != 0)
        && (_ownerUseGeneration != 0)
        && (_coordinatorUseGeneration != 0);

    internal int KeyCellIndex => _keyCellIndex;

    internal uint KeyGeneration => _keyGeneration;

    internal int OwnerUseCellIndex => _ownerUseCellIndex;

    internal uint OwnerUseGeneration => _ownerUseGeneration;

    internal int PreparationCellIndex => _preparationCellIndex;

    internal uint PreparationGeneration => _preparationGeneration;

    internal Guid WorldGuid => _worldGuid;

    internal bool IsOwnedBy(WorldGuidRegistry registry)
        => IsValid && ReferenceEquals(_registry, registry);
}
