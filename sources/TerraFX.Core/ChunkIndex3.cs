// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Numerics;
using TerraFX.Numerics;

namespace TerraFX;

/// <summary>Identifies a three-dimensional chunk by signed integer coordinates.</summary>
public readonly struct ChunkIndex3
    : IComparable<ChunkIndex3>,
      IEquatable<ChunkIndex3>,
      IComparisonOperators<ChunkIndex3, ChunkIndex3, bool>
{
    /// <summary>Initializes a chunk identity.</summary>
    /// <param name="x">The X coordinate.</param>
    /// <param name="y">The Y coordinate.</param>
    /// <param name="z">The Z coordinate.</param>
    public ChunkIndex3(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>Gets the X coordinate.</summary>
    public int X { get; }

    /// <summary>Gets the Y coordinate.</summary>
    public int Y { get; }

    /// <summary>Gets the Z coordinate.</summary>
    public int Z { get; }

    /// <summary>Negates stored-coordinate equality.</summary>
    public static bool operator !=(ChunkIndex3 left, ChunkIndex3 right) => !(left == right);

    /// <summary>Compares chunk indices by magnitude, sign orthant, then direction.</summary>
    public static bool operator <(ChunkIndex3 left, ChunkIndex3 right) => left.CompareTo(right) < 0;

    /// <summary>Compares chunk indices by magnitude, sign orthant, then direction.</summary>
    public static bool operator <=(ChunkIndex3 left, ChunkIndex3 right) => left.CompareTo(right) <= 0;

    /// <summary>Compares stored chunk coordinates.</summary>
    public static bool operator ==(ChunkIndex3 left, ChunkIndex3 right) => left.Equals(right);

    /// <summary>Compares chunk indices by magnitude, sign orthant, then direction.</summary>
    public static bool operator >(ChunkIndex3 left, ChunkIndex3 right) => left.CompareTo(right) > 0;

    /// <summary>Compares chunk indices by magnitude, sign orthant, then direction.</summary>
    public static bool operator >=(ChunkIndex3 left, ChunkIndex3 right) => left.CompareTo(right) >= 0;

    /// <inheritdoc />
    public int CompareTo(ChunkIndex3 other) => MagnitudeOrdering.Compare(X, Y, Z, other.X, other.Y, other.Z);

    /// <inheritdoc />
    public bool Equals(ChunkIndex3 other) => X == other.X && Y == other.Y && Z == other.Z;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ChunkIndex3 other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
}
