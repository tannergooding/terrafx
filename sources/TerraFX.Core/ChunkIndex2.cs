// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Numerics;
using TerraFX.Numerics;

namespace TerraFX;

/// <summary>Identifies a two-dimensional chunk by signed integer coordinates.</summary>
public readonly struct ChunkIndex2
    : IComparable<ChunkIndex2>,
      IEquatable<ChunkIndex2>,
      IComparisonOperators<ChunkIndex2, ChunkIndex2, bool>
{
    /// <summary>Initializes a chunk identity.</summary>
    /// <param name="x">The X coordinate.</param>
    /// <param name="y">The Y coordinate.</param>
    public ChunkIndex2(int x, int y)
    {
        X = x;
        Y = y;
    }

    /// <summary>Gets the X coordinate.</summary>
    public int X { get; }

    /// <summary>Gets the Y coordinate.</summary>
    public int Y { get; }

    /// <summary>Negates stored-coordinate equality.</summary>
    public static bool operator !=(ChunkIndex2 left, ChunkIndex2 right) => !(left == right);

    /// <summary>Compares chunk indices by magnitude, sign orthant, then direction.</summary>
    public static bool operator <(ChunkIndex2 left, ChunkIndex2 right) => left.CompareTo(right) < 0;

    /// <summary>Compares chunk indices by magnitude, sign orthant, then direction.</summary>
    public static bool operator <=(ChunkIndex2 left, ChunkIndex2 right) => left.CompareTo(right) <= 0;

    /// <summary>Compares stored chunk coordinates.</summary>
    public static bool operator ==(ChunkIndex2 left, ChunkIndex2 right) => left.Equals(right);

    /// <summary>Compares chunk indices by magnitude, sign orthant, then direction.</summary>
    public static bool operator >(ChunkIndex2 left, ChunkIndex2 right) => left.CompareTo(right) > 0;

    /// <summary>Compares chunk indices by magnitude, sign orthant, then direction.</summary>
    public static bool operator >=(ChunkIndex2 left, ChunkIndex2 right) => left.CompareTo(right) >= 0;

    /// <inheritdoc />
    public int CompareTo(ChunkIndex2 other) => MagnitudeOrdering.Compare(X, Y, other.X, other.Y);

    /// <inheritdoc />
    public bool Equals(ChunkIndex2 other) => X == other.X && Y == other.Y;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ChunkIndex2 other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(X, Y);
}
