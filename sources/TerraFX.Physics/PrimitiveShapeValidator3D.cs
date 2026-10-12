// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Numerics;

namespace TerraFX.Physics;

/// <summary>Performs geometry-only admission and statistics calculation for 3D primitives.</summary>
public static class PrimitiveShapeValidator3D
{
    /// <summary>Validates the box dimensions and exact enclosing radius.</summary>
    /// <param name="definition">The finite local box definition.</param>
    /// <param name="geometryContext">The immutable geometry admission context.</param>
    /// <returns>The deterministic admission result and, when valid, primitive statistics.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static ShapeValidationResponse3D Validate(BoxShapeDefinition3D definition, PrimitiveShapeGeometryContext3D geometryContext)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(geometryContext);

        var halfExtents = definition.HalfExtents;
        var minimum = geometryContext.MinimumPrimitiveExtent;
        var maximum = geometryContext.MaximumLocalExtent;

        if (halfExtents.X < minimum)
        {
            return new ShapeValidationResponse3D(
                geometryContext,
                new ShapeValidationFault(ShapeValidationFaultCode.PrimitiveExtentBelowMinimum, ShapeValidationField.HalfExtentX, halfExtents.X, minimum));
        }

        if (halfExtents.Y < minimum)
        {
            return new ShapeValidationResponse3D(
                geometryContext,
                new ShapeValidationFault(ShapeValidationFaultCode.PrimitiveExtentBelowMinimum, ShapeValidationField.HalfExtentY, halfExtents.Y, minimum));
        }

        if (halfExtents.Z < minimum)
        {
            return new ShapeValidationResponse3D(
                geometryContext,
                new ShapeValidationFault(ShapeValidationFaultCode.PrimitiveExtentBelowMinimum, ShapeValidationField.HalfExtentZ, halfExtents.Z, minimum));
        }

        if (halfExtents.X > maximum)
        {
            return new ShapeValidationResponse3D(
                geometryContext,
                new ShapeValidationFault(ShapeValidationFaultCode.LocalExtentExceeded, ShapeValidationField.HalfExtentX, halfExtents.X, maximum));
        }

        if (halfExtents.Y > maximum)
        {
            return new ShapeValidationResponse3D(
                geometryContext,
                new ShapeValidationFault(ShapeValidationFaultCode.LocalExtentExceeded, ShapeValidationField.HalfExtentY, halfExtents.Y, maximum));
        }

        if (halfExtents.Z > maximum)
        {
            return new ShapeValidationResponse3D(
                geometryContext,
                new ShapeValidationFault(ShapeValidationFaultCode.LocalExtentExceeded, ShapeValidationField.HalfExtentZ, halfExtents.Z, maximum));
        }

        if (PrimitiveShapeValidationKernel.IsBeyondLocalExtent(halfExtents, maximum, out var enclosingRadius))
        {
            return new ShapeValidationResponse3D(
                geometryContext,
                new ShapeValidationFault(ShapeValidationFaultCode.LocalExtentExceeded, ShapeValidationField.EnclosingRadius, enclosingRadius, maximum));
        }

        var statistics = new ShapeStatistics3D(
            geometryContext.SpatialSettings,
            8,
            12,
            6,
            PrimitiveShapeValidationKernel.CalculateBoxVolume(halfExtents.X, halfExtents.Y, halfExtents.Z),
            new Vector3(-halfExtents.X, -halfExtents.Y, -halfExtents.Z),
            halfExtents,
            enclosingRadius,
            1,
            null,
            null,
            null);

        return new ShapeValidationResponse3D(geometryContext, statistics);
    }

    /// <summary>Validates the sphere radius and calculates its analytic volume.</summary>
    /// <param name="definition">The finite local sphere definition.</param>
    /// <param name="geometryContext">The immutable geometry admission context.</param>
    /// <returns>The deterministic admission result and, when valid, primitive statistics.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static ShapeValidationResponse3D Validate(SphereShapeDefinition3D definition, PrimitiveShapeGeometryContext3D geometryContext)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(geometryContext);

        if (definition.Radius < geometryContext.MinimumPrimitiveExtent)
        {
            return new ShapeValidationResponse3D(
                geometryContext,
                new ShapeValidationFault(
                    ShapeValidationFaultCode.PrimitiveExtentBelowMinimum,
                    ShapeValidationField.Radius,
                    definition.Radius,
                    geometryContext.MinimumPrimitiveExtent));
        }

        if (definition.Radius > geometryContext.MaximumLocalExtent)
        {
            return new ShapeValidationResponse3D(
                geometryContext,
                new ShapeValidationFault(
                    ShapeValidationFaultCode.LocalExtentExceeded,
                    ShapeValidationField.Radius,
                    definition.Radius,
                    geometryContext.MaximumLocalExtent));
        }

        var statistics = new ShapeStatistics3D(
            geometryContext.SpatialSettings,
            null,
            null,
            null,
            PrimitiveShapeValidationKernel.CalculateSphereVolume(definition.Radius),
            new Vector3(-definition.Radius, -definition.Radius, -definition.Radius),
            new Vector3(definition.Radius, definition.Radius, definition.Radius),
            definition.Radius,
            1,
            null,
            null,
            null);

        return new ShapeValidationResponse3D(geometryContext, statistics);
    }
}
