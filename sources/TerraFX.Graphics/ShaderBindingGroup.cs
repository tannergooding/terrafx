// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Graphics;

/// <summary>Identifies a portable shader resource group.</summary>
public enum ShaderBindingGroup
{
    /// <summary>No shader resource group.</summary>
    None = 0,

    /// <summary>Frame-scoped resources.</summary>
    Frame = 1,

    /// <summary>Material-scoped resources.</summary>
    Material = 2,

    /// <summary>Object-scoped resources.</summary>
    ObjectData = 3,

    /// <summary>Pass-scoped resources.</summary>
    Pass = 4,
}
