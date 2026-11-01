// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Describes one field in a reflected shader record layout.</summary>
public readonly struct ShaderFieldLayout : IEquatable<ShaderFieldLayout>
{
    /// <summary>Initializes a reflected shader record field.</summary>
    /// <param name="name">The nonempty field name.</param>
    /// <param name="valueType">The reflected value type.</param>
    /// <param name="byteOffset">The nonnegative byte offset in the record.</param>
    /// <param name="byteLength">The positive element byte length.</param>
    /// <param name="arrayCount">The positive fixed array element count.</param>
    /// <param name="arrayStride">Zero for one element, or the positive aligned array stride.</param>
    /// <exception cref="ArgumentException">The array stride is inconsistent with its element count.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A field extent or enum value is invalid.</exception>
    public ShaderFieldLayout(
        string name,
        ShaderValueType valueType,
        int byteOffset,
        int byteLength,
        int arrayCount,
        int arrayStride)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if ((valueType == ShaderValueType.None) || !Enum.IsDefined(valueType))
        {
            throw new ArgumentOutOfRangeException(nameof(valueType));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(byteOffset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(arrayCount);

        if (arrayCount == 1)
        {
            if (arrayStride != 0)
            {
                throw new ArgumentException("A scalar field cannot declare an array stride.", nameof(arrayStride));
            }
        }
        else if ((arrayStride < byteLength) || ((arrayStride & 15) != 0))
        {
            throw new ArgumentException("An array field stride must cover its element and be 16-byte aligned.", nameof(arrayStride));
        }

        Name = name;
        ValueType = valueType;
        ByteOffset = byteOffset;
        ByteLength = byteLength;
        ArrayCount = arrayCount;
        ArrayStride = arrayStride;
    }

    /// <summary>Gets the positive fixed array element count.</summary>
    public int ArrayCount { get; }

    /// <summary>Gets the array stride, or zero when <see cref="ArrayCount"/> is one.</summary>
    public int ArrayStride { get; }

    /// <summary>Gets the positive byte length of one element.</summary>
    public int ByteLength { get; }

    /// <summary>Gets the byte offset in the containing record.</summary>
    public int ByteOffset { get; }

    /// <summary>Gets the field name.</summary>
    public string Name { get; }

    /// <summary>Gets the reflected value type.</summary>
    public ShaderValueType ValueType { get; }

    /// <summary>Negates field layout equality.</summary>
    public static bool operator !=(ShaderFieldLayout left, ShaderFieldLayout right) => !left.Equals(right);

    /// <summary>Compares field layout values.</summary>
    public static bool operator ==(ShaderFieldLayout left, ShaderFieldLayout right) => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(ShaderFieldLayout other)
        => StringComparer.Ordinal.Equals(Name, other.Name)
        && (ValueType == other.ValueType)
        && (ByteOffset == other.ByteOffset)
        && (ByteLength == other.ByteLength)
        && (ArrayCount == other.ArrayCount)
        && (ArrayStride == other.ArrayStride);

    /// <inheritdoc />
    public override bool Equals(object? obj) => (obj is ShaderFieldLayout other) && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Name, ValueType, ByteOffset, ByteLength, ArrayCount, ArrayStride);
}
