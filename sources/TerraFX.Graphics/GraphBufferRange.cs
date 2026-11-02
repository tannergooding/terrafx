// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Defines one half-open byte interval in a graph buffer version.</summary>
public readonly struct GraphBufferRange : IEquatable<GraphBufferRange>
{
    /// <summary>Initializes a checked, nonempty byte interval.</summary>
    /// <param name="byteOffset">The nonnegative byte offset.</param>
    /// <param name="byteLength">The positive interval length.</param>
    /// <exception cref="ArgumentOutOfRangeException">The interval is negative, empty, or its end overflows.</exception>
    public GraphBufferRange(long byteOffset, long byteLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(byteOffset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(byteOffset, long.MaxValue - byteLength);
        ByteLength = byteLength;
        ByteOffset = byteOffset;
    }

    /// <summary>Gets the interval length in bytes.</summary>
    public long ByteLength { get; }

    /// <summary>Gets the first byte offset in the interval.</summary>
    public long ByteOffset { get; }

    internal readonly long EndExclusive => checked(ByteOffset + ByteLength);

    internal readonly bool IsValid => (ByteOffset >= 0) && (ByteLength > 0) && (EndExclusive >= ByteOffset);

    /// <summary>Negates range equality.</summary>
    public static bool operator !=(GraphBufferRange left, GraphBufferRange right) => !left.Equals(right);

    /// <summary>Compares byte intervals.</summary>
    public static bool operator ==(GraphBufferRange left, GraphBufferRange right) => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(GraphBufferRange other)
        => (ByteOffset == other.ByteOffset) && (ByteLength == other.ByteLength);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GraphBufferRange other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(ByteOffset, ByteLength);

    internal readonly bool IsWithin(GraphBufferRange enclosing)
        => IsValid
            && enclosing.IsValid
            && (ByteOffset >= enclosing.ByteOffset)
            && (EndExclusive <= enclosing.EndExclusive);

    internal readonly bool Overlaps(GraphBufferRange other)
        => IsValid
            && other.IsValid
            && (ByteOffset < other.EndExclusive)
            && (other.ByteOffset < EndExclusive);

}
