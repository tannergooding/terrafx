// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Physics;

/// <summary>Copies the radius of a sphere centered at local zero.</summary>
public sealed class SphereShapeDefinition3D : ShapeDefinition3D
{
    /// <summary>Initializes a sphere definition without clamping its finite input bits.</summary>
    /// <param name="radius">The copied local radius.</param>
    /// <exception cref="ArgumentException">The radius is not finite.</exception>
    public SphereShapeDefinition3D(float radius)
    {
        if (!float.IsFinite(radius))
        {
            throw new ArgumentException("The radius must be finite.", nameof(radius));
        }

        Radius = radius;
    }

    /// <summary>Gets the copied local radius.</summary>
    public float Radius { get; }
}
