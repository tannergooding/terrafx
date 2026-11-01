// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Graphics;

/// <summary>Identifies one supported shader pipeline stage.</summary>
public enum ShaderStage
{
    /// <summary>No shader stage.</summary>
    None = 0,

    /// <summary>Vertex processing stage.</summary>
    Vertex = 1,

    /// <summary>Pixel processing stage.</summary>
    Pixel = 2,

    /// <summary>Compute processing stage.</summary>
    Compute = 3,
}
