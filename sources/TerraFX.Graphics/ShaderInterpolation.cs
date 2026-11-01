// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Graphics;

/// <summary>Identifies the interpolation mode for a reflected stage input or output.</summary>
public enum ShaderInterpolation
{
    /// <summary>No interpolation mode.</summary>
    None = 0,

    /// <summary>Perspective-correct interpolation.</summary>
    Smooth = 1,

    /// <summary>Non-perspective interpolation.</summary>
    NoPerspective = 2,

    /// <summary>Flat interpolation.</summary>
    Flat = 3,
}
