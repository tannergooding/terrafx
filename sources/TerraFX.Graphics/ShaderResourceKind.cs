// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Graphics;

/// <summary>Identifies a reflected shader resource register class.</summary>
public enum ShaderResourceKind
{
    /// <summary>No shader resource kind.</summary>
    None = 0,

    /// <summary>Uniform buffer register class.</summary>
    UniformBuffer = 1,

    /// <summary>Sampled image register class.</summary>
    SampledImage = 2,

    /// <summary>Storage image register class.</summary>
    StorageImage = 3,

    /// <summary>Storage buffer register class.</summary>
    StorageBuffer = 4,

    /// <summary>Sampler register class.</summary>
    Sampler = 5,
}
