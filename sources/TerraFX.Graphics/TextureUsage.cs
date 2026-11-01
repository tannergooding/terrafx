// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Declares the permitted uses of a texture.</summary>
[Flags]
public enum TextureUsage
{
    /// <summary>No permitted use.</summary>
    None = 0,

    /// <summary>Shader sampling.</summary>
    Sampled = 1,

    /// <summary>Shader storage access.</summary>
    Storage = 2,

    /// <summary>Color attachment access.</summary>
    ColorAttachment = 4,

    /// <summary>Depth attachment access.</summary>
    DepthAttachment = 8,

    /// <summary>Copy source access.</summary>
    CopySource = 16,

    /// <summary>Copy destination access.</summary>
    CopyDestination = 32,
}
