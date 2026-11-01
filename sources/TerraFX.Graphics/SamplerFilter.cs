// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Graphics;

/// <summary>Defines one sampler filtering operation.</summary>
public enum SamplerFilter
{
    /// <summary>No mip-level interpolation; valid only for the mip filter.</summary>
    None = 0,

    /// <summary>Nearest-texel filtering.</summary>
    Nearest = 1,

    /// <summary>Linear filtering.</summary>
    Linear = 2,
}
