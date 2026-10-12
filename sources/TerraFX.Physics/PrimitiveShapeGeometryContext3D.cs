// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Physics;

/// <summary>Provides immutable geometry-only admission limits for 3D primitives.</summary>
public sealed class PrimitiveShapeGeometryContext3D
{
    /// <summary>Initializes limits derived from the supplied spatial settings.</summary>
    /// <param name="spatialSettings">The valid spatial declaration.</param>
    /// <exception cref="ArgumentException">The spatial declaration is invalid.</exception>
    public PrimitiveShapeGeometryContext3D(SpatialSettings spatialSettings)
    {
        _ = spatialSettings.ChunkWidth;
        SpatialSettings = spatialSettings;
        MinimumPrimitiveExtent = SpatialMath.ConvertQuantity(1, 40, 0, spatialSettings.MetersPerUnitExponent, 1);
        MaximumLocalExtent = SpatialMath.ConvertQuantity(64, 1, 0, spatialSettings.MetersPerUnitExponent, 1);
    }

    /// <summary>Gets the shared context using default spatial settings.</summary>
    public static PrimitiveShapeGeometryContext3D Default { get; } = new PrimitiveShapeGeometryContext3D(SpatialSettings.Default);

    /// <summary>Gets the maximum admitted radius from local zero, in engine units.</summary>
    public float MaximumLocalExtent { get; }

    /// <summary>Gets the minimum admitted radius or box half-extent, in engine units.</summary>
    public float MinimumPrimitiveExtent { get; }

    /// <summary>Gets the frozen spatial declaration used by this context.</summary>
    public SpatialSettings SpatialSettings { get; }
}
