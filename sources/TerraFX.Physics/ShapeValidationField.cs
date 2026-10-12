// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Physics;

/// <summary>Identifies the geometry field associated with a validation fault.</summary>
public enum ShapeValidationField
{
    /// <summary>No specific geometry field.</summary>
    None = 0,

    /// <summary>The circle or sphere radius.</summary>
    Radius = 1,

    /// <summary>The X half-extent of a box.</summary>
    HalfExtentX = 2,

    /// <summary>The Y half-extent of a box.</summary>
    HalfExtentY = 3,

    /// <summary>The Z half-extent of a box.</summary>
    HalfExtentZ = 4,

    /// <summary>The box's exact enclosing-radius predicate.</summary>
    EnclosingRadius = 5
}
