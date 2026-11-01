// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Graphics;

/// <summary>Identifies the dimensionality of one texture allocation.</summary>
public enum TextureDimension
{
    /// <summary>No selected texture dimension.</summary>
    None = 0,

    /// <summary>A 2D texture or 2D array.</summary>
    Dimension2D = 1,

    /// <summary>A 3D texture; array layers are fixed to one.</summary>
    Dimension3D = 2,
}
