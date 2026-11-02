// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Graphics;

/// <summary>Defines the logical access state of one imported graph resource range.</summary>
public enum GraphResourceState
{
    /// <summary>The range has no established access state.</summary>
    Undefined = 0,

    /// <summary>The resource is read as a copy source.</summary>
    CopySource = 1,

    /// <summary>The resource is written as a copy destination.</summary>
    CopyDestination = 2,

    /// <summary>The buffer is read as vertex data.</summary>
    VertexRead = 3,

    /// <summary>The buffer is read as index data.</summary>
    IndexRead = 4,

    /// <summary>The resource is read as uniform data.</summary>
    UniformRead = 5,

    /// <summary>The texture is read by shader sampling.</summary>
    ShaderSampledRead = 6,

    /// <summary>The resource is read as storage data.</summary>
    StorageRead = 7,

    /// <summary>The resource is written as storage data.</summary>
    StorageWrite = 8,

    /// <summary>The texture is used as a color attachment.</summary>
    ColorAttachment = 9,

    /// <summary>The texture is read as a depth attachment.</summary>
    DepthRead = 10,

    /// <summary>The texture is written as a depth attachment.</summary>
    DepthWrite = 11,

    /// <summary>The resource is ready for presentation.</summary>
    Present = 12,
}
