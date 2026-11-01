// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Declares the permitted GPU operations for one buffer.</summary>
[Flags]
public enum BufferUsage
{
    /// <summary>No permitted use.</summary>
    None = 0,

    /// <summary>Vertex input.</summary>
    Vertex = 1,

    /// <summary>Index input.</summary>
    Index = 2,

    /// <summary>Uniform-buffer reads.</summary>
    Uniform = 4,

    /// <summary>Shader storage access.</summary>
    Storage = 8,

    /// <summary>Copy source access.</summary>
    CopySource = 16,

    /// <summary>Copy destination access.</summary>
    CopyDestination = 32,
}
