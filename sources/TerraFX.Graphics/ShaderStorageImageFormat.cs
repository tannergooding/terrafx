// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Graphics;

/// <summary>Identifies a storage-image shader format reflected from the shader binary.</summary>
public enum ShaderStorageImageFormat
{
    /// <summary>No storage-image format; valid only for non-storage-image resources.</summary>
    None = 0,

    /// <summary>One 32-bit floating-point component.</summary>
    R32Float = 1,

    /// <summary>One 32-bit signed-integer component.</summary>
    R32SInt = 2,

    /// <summary>One 32-bit unsigned-integer component.</summary>
    R32UInt = 3,

    /// <summary>Four 16-bit floating-point components.</summary>
    Rgba16Float = 4,
}
