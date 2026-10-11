// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX;

/// <summary>Declares exact decimal distance units and a power-of-two chunk width.</summary>
public readonly struct SpatialSettings : IEquatable<SpatialSettings>
{
    /// <summary>Initializes a spatial declaration.</summary>
    /// <param name="metersPerUnitExponent">The decimal meters-per-unit exponent, in [-30, 30].</param>
    /// <param name="chunkSizeExponent">The binary chunk-width exponent, in [8, 16].</param>
    /// <exception cref="ArgumentOutOfRangeException">An exponent is outside its admitted range.</exception>
    public SpatialSettings(int metersPerUnitExponent, int chunkSizeExponent)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(metersPerUnitExponent, -30);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(metersPerUnitExponent, 30);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSizeExponent, 8);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(chunkSizeExponent, 16);
        MetersPerUnitExponent = metersPerUnitExponent;
        ChunkSizeExponent = chunkSizeExponent;
    }

    /// <summary>Gets the meter declaration with a 4096-unit chunk width.</summary>
    public static SpatialSettings Default => new SpatialSettings(0, 12);

    /// <summary>Gets the exponent defining exactly 2^e units per chunk.</summary>
    public int ChunkSizeExponent { get; }

    /// <summary>Gets the exact binary32 chunk width.</summary>
    /// <exception cref="ArgumentException">This declaration is invalid, including zero initialization.</exception>
    public float ChunkWidth
    {
        get
        {
            SpatialKernel.ValidateSettings(this, nameof(SpatialSettings));
            return float.ScaleB(1.0f, ChunkSizeExponent);
        }
    }

    /// <summary>Gets the exponent defining exactly 10^e meters per unit.</summary>
    public int MetersPerUnitExponent { get; }

    /// <summary>Negates stored-exponent equality.</summary>
    public static bool operator !=(SpatialSettings left, SpatialSettings right) => !(left == right);

    /// <summary>Compares stored exponents.</summary>
    public static bool operator ==(SpatialSettings left, SpatialSettings right) => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(SpatialSettings other) => MetersPerUnitExponent == other.MetersPerUnitExponent && ChunkSizeExponent == other.ChunkSizeExponent;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SpatialSettings other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(MetersPerUnitExponent, ChunkSizeExponent);
}
