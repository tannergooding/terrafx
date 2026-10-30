// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

/// <summary>Selects how destroying an entity treats its hierarchy.</summary>
public enum HierarchyDestroyMode
{
    /// <summary>Destroys only the selected entity and preserves its direct children.</summary>
    DetachChildrenKeepWorld = 0,

    /// <summary>Destroys the selected entity and its descendants.</summary>
    DestroySubtree = 1,
}
