// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Identifies a nonempty half-open range of stable system partitions.</summary>
public readonly struct SystemPartitionRange : IEquatable<SystemPartitionRange>
{
    /// <summary>Initializes a checked half-open partition range.</summary>
    /// <param name="start">The first partition ordinal.</param>
    /// <param name="length">The number of partition ordinals.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="start"/> or <paramref name="length"/> is invalid, or the exclusive end would overflow.</exception>
    public SystemPartitionRange(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, int.MaxValue - start, nameof(length));

        Start = start;
        Length = length;
        EndExclusive = start + length;
    }

    /// <summary>Gets the exclusive end partition ordinal.</summary>
    public int EndExclusive { get; }

    /// <summary>Gets whether this value represents a valid nonempty range.</summary>
    public bool IsValid => (Start >= 0) && (Length > 0) && (Length <= (int.MaxValue - Start));

    /// <summary>Gets the number of partition ordinals in the range.</summary>
    public int Length { get; }

    /// <summary>Gets the first partition ordinal in the range.</summary>
    public int Start { get; }

    /// <summary>Negates range equality.</summary>
    public static bool operator !=(SystemPartitionRange left, SystemPartitionRange right) => !(left == right);

    /// <summary>Compares the complete range endpoints.</summary>
    public static bool operator ==(SystemPartitionRange left, SystemPartitionRange right) => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(SystemPartitionRange other) => (Start == other.Start) && (Length == other.Length);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SystemPartitionRange other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Start, Length);
}
