// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Numerics;

namespace TerraFX;

/// <summary>Stores a raw spatial origin, admitted unit direction, and finite distance without a negative sign.</summary>
public readonly struct SpatialRay2 : IEquatable<SpatialRay2>
{
    /// <summary>Initializes a ray, normalizing a direction whose norm is within 1e-5 of one.</summary>
    /// <param name="origin">The raw finite origin, retained without normalization.</param>
    /// <param name="unitDirection">The finite near-unit direction.</param>
    /// <param name="maximumDistance">The finite distance in the origin's declared units; negative zero is rejected.</param>
    /// <exception cref="ArgumentException">The origin or direction is invalid, or distance is nonfinite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The distance is negative, including negative zero.</exception>
    public SpatialRay2(SpatialPosition2 origin, Vector2 unitDirection, float maximumDistance)
    {
        ThrowHelper.ThrowIfNotFinite(origin.Offset, nameof(origin));
        ThrowHelper.ThrowIfNotFinite(unitDirection);
        SpatialKernel.ValidateRayDirection(unitDirection.X, unitDirection.Y, 0.0f, nameof(unitDirection));
        SpatialKernel.ValidateDistance(maximumDistance);
        Origin = origin;
        Direction = Vector2.Normalize(unitDirection);
        MaxDistance = maximumDistance;
    }

    /// <summary>Gets the normalized direction.</summary>
    public Vector2 Direction { get; }

    /// <summary>Gets the maximum distance.</summary>
    public float MaxDistance { get; }

    /// <summary>Gets the retained raw origin.</summary>
    public SpatialPosition2 Origin { get; }

    /// <summary>Negates stored-field equality.</summary>
    public static bool operator !=(SpatialRay2 left, SpatialRay2 right) => !(left == right);

    /// <summary>Compares stored fields without coordinate-equivalence inference.</summary>
    public static bool operator ==(SpatialRay2 left, SpatialRay2 right) => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(SpatialRay2 other) => Origin.Equals(other.Origin) && Direction.Equals(other.Direction) && MaxDistance.Equals(other.MaxDistance);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SpatialRay2 other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Origin, Direction, MaxDistance);
}
