// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Graphics;

/// <summary>Identifies a supported native shader binary format.</summary>
public enum ShaderBinaryFormat
{
    /// <summary>No shader binary format.</summary>
    None = 0,

    /// <summary>DirectX Intermediate Language.</summary>
    Dxil = 1,

    /// <summary>Standard Portable Intermediate Representation for Vulkan.</summary>
    SpirV = 2,
}
