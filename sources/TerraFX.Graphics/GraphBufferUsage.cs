// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Graphics;

/// <summary>Defines one graph access mode for a buffer range.</summary>
public enum GraphBufferUsage
{
    /// <summary>Read the range as vertex input.</summary>
    VertexRead = 0,

    /// <summary>Read the range as index input.</summary>
    IndexRead = 1,

    /// <summary>Read the range as uniform data.</summary>
    UniformRead = 2,

    /// <summary>Read the range as storage data.</summary>
    StorageRead = 3,

    /// <summary>Write the range as storage data.</summary>
    StorageWrite = 4,

    /// <summary>Read the range as a copy source.</summary>
    CopySource = 5,

    /// <summary>Write the range as a copy destination.</summary>
    CopyDestination = 6,
}
