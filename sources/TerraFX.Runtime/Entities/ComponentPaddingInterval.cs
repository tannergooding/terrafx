// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Describes one unused byte interval in a component layout.</summary>
public readonly struct ComponentPaddingInterval : IEquatable<ComponentPaddingInterval>
{
    /// <summary>Initializes a component padding interval.</summary>
    /// <param name="byteOffset">The nonnegative starting byte offset.</param>
    /// <param name="byteExtent">The positive byte extent.</param>
    /// <exception cref="ArgumentOutOfRangeException">An offset or extent is invalid.</exception>
    public ComponentPaddingInterval(int byteOffset, int byteExtent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(byteOffset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteExtent);

        ByteOffset = byteOffset;
        ByteExtent = byteExtent;
    }

    /// <summary>Gets the occupied byte extent.</summary>
    public int ByteExtent { get; }

    /// <summary>Gets the starting byte offset.</summary>
    public int ByteOffset { get; }

    /// <summary>Negates equality of the complete padding interval.</summary>
    public static bool operator !=(ComponentPaddingInterval left, ComponentPaddingInterval right) => !(left == right);

    /// <summary>Compares the complete padding interval.</summary>
    public static bool operator ==(ComponentPaddingInterval left, ComponentPaddingInterval right) => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(ComponentPaddingInterval other) => ByteOffset == other.ByteOffset && ByteExtent == other.ByteExtent;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ComponentPaddingInterval other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(ByteOffset, ByteExtent);
}
