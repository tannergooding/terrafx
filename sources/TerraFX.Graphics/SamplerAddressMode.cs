// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Graphics;

/// <summary>Defines how sampler coordinates outside the texture extent are handled.</summary>
public enum SamplerAddressMode
{
    /// <summary>Clamp coordinates to the edge texel.</summary>
    Clamp = 0,

    /// <summary>Repeat coordinates across the texture extent.</summary>
    Repeat = 1,
}
