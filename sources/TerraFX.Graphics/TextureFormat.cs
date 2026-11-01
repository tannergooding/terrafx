// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Graphics;

/// <summary>Identifies a portable texture format.</summary>
public enum TextureFormat
{
    /// <summary>No texture format.</summary>
    None = 0,

    /// <summary>Single-channel 8-bit unsigned normalized.</summary>
    R8Unorm = 1,

    /// <summary>Four-channel 8-bit unsigned normalized.</summary>
    Rgba8Unorm = 2,

    /// <summary>Four-channel 8-bit sRGB.</summary>
    Rgba8Srgb = 3,

    /// <summary>Four-channel 8-bit unsigned normalized, BGRA order.</summary>
    Bgra8Unorm = 4,

    /// <summary>Four-channel 8-bit sRGB, BGRA order.</summary>
    Bgra8Srgb = 5,

    /// <summary>Four-channel 16-bit floating point.</summary>
    Rgba16Float = 6,

    /// <summary>Single-channel 32-bit floating point.</summary>
    R32Float = 7,

    /// <summary>Single-channel 32-bit unsigned integer.</summary>
    R32UInt = 8,

    /// <summary>32-bit floating-point depth.</summary>
    D32Float = 9,
}
