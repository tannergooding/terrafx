// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX;

/// <summary>Identifies one field tag or fixed-array index in a schema diagnostic path.</summary>
public readonly struct BinarySchemaPathSegment : IEquatable<BinarySchemaPathSegment>
{
    private readonly BinarySchemaPathSegmentKind _kind;
    private readonly uint _value;

    private BinarySchemaPathSegment(BinarySchemaPathSegmentKind kind, uint value)
    {
        _kind = kind;
        _value = value;
    }

    /// <summary>Gets the path segment kind.</summary>
    public BinarySchemaPathSegmentKind Kind => _kind;

    /// <summary>Gets the field tag or nonnegative array index.</summary>
    public uint Value => _value;

    /// <summary>Negates equality of the complete path segment.</summary>
    public static bool operator !=(BinarySchemaPathSegment left, BinarySchemaPathSegment right) => !(left == right);

    /// <summary>Compares the complete path segment.</summary>
    public static bool operator ==(BinarySchemaPathSegment left, BinarySchemaPathSegment right) => left.Equals(right);

    /// <summary>Creates a fixed-array index segment.</summary>
    /// <param name="index">The nonnegative array index.</param>
    /// <returns>The array index path segment.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative.</exception>
    public static BinarySchemaPathSegment ArrayIndex(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return new BinarySchemaPathSegment(BinarySchemaPathSegmentKind.ArrayIndex, checked((uint)index));
    }

    /// <summary>Creates a field-tag segment.</summary>
    /// <param name="tag">The nonzero field tag.</param>
    /// <returns>The field-tag path segment.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tag"/> is zero.</exception>
    public static BinarySchemaPathSegment FieldTag(uint tag)
    {
        ArgumentOutOfRangeException.ThrowIfZero(tag);
        return new BinarySchemaPathSegment(BinarySchemaPathSegmentKind.FieldTag, tag);
    }

    /// <inheritdoc />
    public bool Equals(BinarySchemaPathSegment other) => (_kind == other._kind) && (_value == other._value);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is BinarySchemaPathSegment other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_kind, _value);
}
