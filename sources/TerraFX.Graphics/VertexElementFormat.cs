// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Graphics;

/// <summary>Identifies the storage format of one vertex input element.</summary>
public enum VertexElementFormat
{
    /// <summary>Two 32-bit floating-point components.</summary>
    Float2 = 0,

    /// <summary>Three 32-bit floating-point components.</summary>
    Float3 = 1,

    /// <summary>Four 32-bit floating-point components.</summary>
    Float4 = 2,

    /// <summary>Two 16-bit floating-point components expanded to shader floating-point values.</summary>
    Half2 = 3,

    /// <summary>Four 16-bit floating-point components expanded to shader floating-point values.</summary>
    Half4 = 4,

    /// <summary>Four normalized 8-bit unsigned components expanded to shader floating-point values.</summary>
    UNorm8x4 = 5,
}
