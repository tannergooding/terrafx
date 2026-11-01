// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Rendering;

/// <summary>Identifies the spatial dimension captured by a render snapshot.</summary>
public enum RenderFrameDimension
{
    /// <summary>The snapshot uses a 2D spatial frame.</summary>
    TwoDimensional,

    /// <summary>The snapshot uses a 3D spatial frame.</summary>
    ThreeDimensional,
}
