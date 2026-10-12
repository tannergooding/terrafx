// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Numerics;

namespace TerraFX.Physics;

/// <summary>Copies the half-extents of an axis-aligned local 2D box.</summary>
public sealed class BoxShapeDefinition2D : ShapeDefinition2D
{
    /// <summary>Initializes a box definition without normalizing its finite input bits.</summary>
    /// <param name="halfExtents">The copied local half-extents.</param>
    /// <exception cref="ArgumentException">A component is not finite.</exception>
    public BoxShapeDefinition2D(Vector2 halfExtents)
    {
        if (!halfExtents.IsFinite().All())
        {
            throw new ArgumentException("Half extents must be finite.", nameof(halfExtents));
        }

        HalfExtents = halfExtents;
    }

    /// <summary>Gets the copied local half-extents.</summary>
    public Vector2 HalfExtents { get; }
}
