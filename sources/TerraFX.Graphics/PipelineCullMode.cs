// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Graphics;

/// <summary>Identifies which triangle winding the rasterizer discards.</summary>
public enum PipelineCullMode
{
    /// <summary>Do not discard triangles based on winding.</summary>
    None = 0,

    /// <summary>Discard front-facing triangles.</summary>
    Front = 1,

    /// <summary>Discard back-facing triangles.</summary>
    Back = 2,
}
