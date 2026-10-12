// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Physics;

/// <summary>Returns the result and geometry provenance of 2D primitive validation.</summary>
public sealed class ShapeValidationResponse2D
{
    internal ShapeValidationResponse2D(PrimitiveShapeGeometryContext2D geometryContext, ShapeStatistics2D statistics)
    {
        GeometryContext = geometryContext;
        Statistics = statistics;
        Status = ShapeValidationStatus.Valid;
    }

    internal ShapeValidationResponse2D(PrimitiveShapeGeometryContext2D geometryContext, ShapeValidationFault fault)
    {
        GeometryContext = geometryContext;
        Fault = fault;
        Status = ShapeValidationStatus.Rejected;
    }

    /// <summary>Gets the first rejection fault, or <see langword="null"/> when valid.</summary>
    public ShapeValidationFault? Fault { get; }

    /// <summary>Gets the immutable context used for validation.</summary>
    public PrimitiveShapeGeometryContext2D GeometryContext { get; }

    /// <summary>Gets the statistics for valid geometry, or <see langword="null"/> when rejected.</summary>
    public ShapeStatistics2D? Statistics { get; }

    /// <summary>Gets whether the geometry passed admission.</summary>
    public ShapeValidationStatus Status { get; }
}
