// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Identifies one field-tag or FixedArray-index component property-path segment.</summary>
public readonly struct ComponentPropertySegment : IEquatable<ComponentPropertySegment>
{
    private readonly ComponentPropertySegmentKind _kind;
    private readonly uint _value;

    private ComponentPropertySegment(ComponentPropertySegmentKind kind, uint value)
    {
        _kind = kind;
        _value = value;
    }

    /// <summary>Gets the segment kind.</summary>
    public ComponentPropertySegmentKind Kind => _kind;

    /// <summary>Gets the field tag or zero-based array index.</summary>
    public uint Value => _value;

    internal bool IsValid => _kind switch {
        ComponentPropertySegmentKind.FieldTag => _value != 0,
        ComponentPropertySegmentKind.FixedArrayIndex => true,
        _ => false,
    };

    /// <summary>Negates equality of the complete path segment.</summary>
    public static bool operator !=(ComponentPropertySegment left, ComponentPropertySegment right) => !(left == right);

    /// <summary>Compares the complete path segment.</summary>
    public static bool operator ==(ComponentPropertySegment left, ComponentPropertySegment right) => left.Equals(right);

    /// <summary>Creates a field-tag segment.</summary>
    /// <param name="tag">The positive EBIN field tag.</param>
    /// <returns>The field-tag segment.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tag"/> is zero.</exception>
    public static ComponentPropertySegment FieldTag(uint tag)
    {
        ArgumentOutOfRangeException.ThrowIfZero(tag);
        return new ComponentPropertySegment(ComponentPropertySegmentKind.FieldTag, tag);
    }

    /// <summary>Creates a zero-based FixedArray-index segment.</summary>
    /// <param name="index">The zero-based array index.</param>
    /// <returns>The FixedArray-index segment.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative.</exception>
    public static ComponentPropertySegment FixedArrayIndex(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return new ComponentPropertySegment(ComponentPropertySegmentKind.FixedArrayIndex, checked((uint)index));
    }

    /// <inheritdoc />
    public bool Equals(ComponentPropertySegment other) => _kind == other._kind && _value == other._value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ComponentPropertySegment other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_kind, _value);
}
