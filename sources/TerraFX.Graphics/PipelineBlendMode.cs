// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Graphics;

/// <summary>Identifies the fixed color-blend behavior of a graphics pipeline.</summary>
public enum PipelineBlendMode
{
    /// <summary>Replace each render-target value with the pixel-shader output.</summary>
    Opaque = 0,

    /// <summary>Blend premultiplied-alpha source color over the destination.</summary>
    PremultipliedAlpha = 1,
}
