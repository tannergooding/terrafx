// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Graphics;

/// <summary>Identifies a nonbaseline shader capability requirement.</summary>
public enum ShaderCapability
{
    /// <summary>No shader capability.</summary>
    None = 0,

    /// <summary>16-bit floating-point shader operations.</summary>
    Float16 = 1,

    /// <summary>64-bit floating-point shader operations.</summary>
    DoublePrecision = 2,

    /// <summary>Wave operations.</summary>
    WaveOperations = 3,

    /// <summary>Descriptor indexing.</summary>
    DescriptorIndexing = 4,

    /// <summary>Scalar block layout.</summary>
    ScalarBlockLayout = 5,

    /// <summary>Geometry shader stage.</summary>
    GeometryShader = 6,

    /// <summary>Tessellation shader stages.</summary>
    Tessellation = 7,
}
