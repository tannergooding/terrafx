// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Numerics;

namespace TerraFX.Physics;

/// <summary>Performs geometry-only admission and statistics calculation for 2D primitives.</summary>
public static class PrimitiveShapeValidator2D
{
    /// <summary>Validates the box dimensions and exact enclosing radius.</summary>
    /// <param name="definition">The finite local box definition.</param>
    /// <param name="geometryContext">The immutable geometry admission context.</param>
    /// <returns>The deterministic admission result and, when valid, primitive statistics.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static ShapeValidationResponse2D Validate(BoxShapeDefinition2D definition, PrimitiveShapeGeometryContext2D geometryContext)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(geometryContext);

        var halfExtents = definition.HalfExtents;
        var minimum = geometryContext.MinimumPrimitiveExtent;
        var maximum = geometryContext.MaximumLocalExtent;

        if (halfExtents.X < minimum)
        {
            return new ShapeValidationResponse2D(
                geometryContext,
                new ShapeValidationFault(ShapeValidationFaultCode.PrimitiveExtentBelowMinimum, ShapeValidationField.HalfExtentX, halfExtents.X, minimum));
        }

        if (halfExtents.Y < minimum)
        {
            return new ShapeValidationResponse2D(
                geometryContext,
                new ShapeValidationFault(ShapeValidationFaultCode.PrimitiveExtentBelowMinimum, ShapeValidationField.HalfExtentY, halfExtents.Y, minimum));
        }

        if (halfExtents.X > maximum)
        {
            return new ShapeValidationResponse2D(
                geometryContext,
                new ShapeValidationFault(ShapeValidationFaultCode.LocalExtentExceeded, ShapeValidationField.HalfExtentX, halfExtents.X, maximum));
        }

        if (halfExtents.Y > maximum)
        {
            return new ShapeValidationResponse2D(
                geometryContext,
                new ShapeValidationFault(ShapeValidationFaultCode.LocalExtentExceeded, ShapeValidationField.HalfExtentY, halfExtents.Y, maximum));
        }

        if (PrimitiveShapeValidationKernel.IsBeyondLocalExtent(halfExtents, maximum, out var enclosingRadius))
        {
            return new ShapeValidationResponse2D(
                geometryContext,
                new ShapeValidationFault(ShapeValidationFaultCode.LocalExtentExceeded, ShapeValidationField.EnclosingRadius, enclosingRadius, maximum));
        }

        var statistics = new ShapeStatistics2D(
            geometryContext.SpatialSettings,
            4,
            4,
            null,
            PrimitiveShapeValidationKernel.CalculateBoxArea(halfExtents.X, halfExtents.Y),
            new Vector2(-halfExtents.X, -halfExtents.Y),
            halfExtents,
            enclosingRadius,
            1,
            null,
            null,
            null);

        return new ShapeValidationResponse2D(geometryContext, statistics);
    }

    /// <summary>Validates the circle radius and calculates its analytic area.</summary>
    /// <param name="definition">The finite local circle definition.</param>
    /// <param name="geometryContext">The immutable geometry admission context.</param>
    /// <returns>The deterministic admission result and, when valid, primitive statistics.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static ShapeValidationResponse2D Validate(CircleShapeDefinition2D definition, PrimitiveShapeGeometryContext2D geometryContext)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(geometryContext);

        if (definition.Radius < geometryContext.MinimumPrimitiveExtent)
        {
            return new ShapeValidationResponse2D(
                geometryContext,
                new ShapeValidationFault(
                    ShapeValidationFaultCode.PrimitiveExtentBelowMinimum,
                    ShapeValidationField.Radius,
                    definition.Radius,
                    geometryContext.MinimumPrimitiveExtent));
        }

        if (definition.Radius > geometryContext.MaximumLocalExtent)
        {
            return new ShapeValidationResponse2D(
                geometryContext,
                new ShapeValidationFault(
                    ShapeValidationFaultCode.LocalExtentExceeded,
                    ShapeValidationField.Radius,
                    definition.Radius,
                    geometryContext.MaximumLocalExtent));
        }

        var statistics = new ShapeStatistics2D(
            geometryContext.SpatialSettings,
            null,
            null,
            null,
            PrimitiveShapeValidationKernel.CalculateCircleArea(definition.Radius),
            new Vector2(-definition.Radius, -definition.Radius),
            new Vector2(definition.Radius, definition.Radius),
            definition.Radius,
            1,
            null,
            null,
            null);

        return new ShapeValidationResponse2D(geometryContext, statistics);
    }
}
