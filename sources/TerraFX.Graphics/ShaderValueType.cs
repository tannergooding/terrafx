// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Graphics;

/// <summary>Identifies a reflected shader scalar, vector, or matrix type.</summary>
public enum ShaderValueType
{
    /// <summary>No shader value type.</summary>
    None = 0,

    /// <summary>32-bit floating-point scalar.</summary>
    Float1 = 1,

    /// <summary>Two-component 32-bit floating-point vector.</summary>
    Float2 = 2,

    /// <summary>Three-component 32-bit floating-point vector.</summary>
    Float3 = 3,

    /// <summary>Four-component 32-bit floating-point vector.</summary>
    Float4 = 4,

    /// <summary>Four-by-four 32-bit floating-point matrix.</summary>
    Float4x4 = 5,

    /// <summary>32-bit signed integer scalar.</summary>
    Int1 = 6,

    /// <summary>Two-component 32-bit signed integer vector.</summary>
    Int2 = 7,

    /// <summary>Three-component 32-bit signed integer vector.</summary>
    Int3 = 8,

    /// <summary>Four-component 32-bit signed integer vector.</summary>
    Int4 = 9,

    /// <summary>32-bit unsigned integer scalar.</summary>
    UInt1 = 10,

    /// <summary>Two-component 32-bit unsigned integer vector.</summary>
    UInt2 = 11,

    /// <summary>Three-component 32-bit unsigned integer vector.</summary>
    UInt3 = 12,

    /// <summary>Four-component 32-bit unsigned integer vector.</summary>
    UInt4 = 13,
}
