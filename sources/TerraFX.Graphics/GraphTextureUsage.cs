// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Graphics;

/// <summary>Defines one graph access mode for a texture range.</summary>
public enum GraphTextureUsage
{
    /// <summary>Read the range through shader sampling.</summary>
    SampledRead = 0,

    /// <summary>Read the range as storage data.</summary>
    StorageRead = 1,

    /// <summary>Write the range as storage data.</summary>
    StorageWrite = 2,

    /// <summary>Read the range as a copy source.</summary>
    CopySource = 3,

    /// <summary>Write the range as a copy destination.</summary>
    CopyDestination = 4,
}
