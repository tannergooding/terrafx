// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Graphics;

/// <summary>Identifies the fixed reversed-Z depth behavior of a graphics pipeline.</summary>
public enum PipelineDepthMode
{
    /// <summary>Do not attach or test a depth target.</summary>
    Disabled = 0,

    /// <summary>Accept greater-or-equal depth without writing depth values.</summary>
    ReversedGreaterEqualReadOnly = 1,

    /// <summary>Accept greater-or-equal depth and write depth values.</summary>
    ReversedGreaterEqualWrite = 2,
}
