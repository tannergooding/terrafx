// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX;

/// <summary>Identifies supported content authoring annotations.</summary>
[Flags]
public enum ContentAuthoring
{
    /// <summary>No authoring behavior is assigned.</summary>
    None = 0,

    /// <summary>The field may be overridden by a prefab.</summary>
    PrefabEditable = 1,

    /// <summary>The field may be animated.</summary>
    Animatable = 2,
}
