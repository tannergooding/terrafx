// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Input;

/// <summary>Identifies the units of a pointer-scroll value.</summary>
public enum PointerScrollUnit
{
    /// <summary>No unit is assigned; this is not valid in a scroll event.</summary>
    None = 0,

    /// <summary>Scroll is measured in wheel detents.</summary>
    Detents = 1,

    /// <summary>Scroll is measured in platform surface-scale units.</summary>
    SurfaceScale = 2,
}
