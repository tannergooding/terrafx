// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Physics;

/// <summary>Stores a positive measure as a normalized binary32 significand and binary exponent.</summary>
public readonly struct ShapeMeasure : IEquatable<ShapeMeasure>
{
    internal ShapeMeasure(float significand, int binaryExponent)
    {
        if (!float.IsFinite(significand) || significand < 1.0f || significand >= 2.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(significand));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(binaryExponent, -512);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(binaryExponent, 512);
        Significand = significand;
        BinaryExponent = binaryExponent;
    }

    /// <summary>Gets the binary exponent of the represented measure.</summary>
    public int BinaryExponent { get; }

    /// <summary>Gets the normalized significand in [1, 2), or zero for the default zero measure.</summary>
    public float Significand { get; }

    /// <summary>Negates stored measure equality.</summary>
    public static bool operator !=(ShapeMeasure left, ShapeMeasure right) => !(left == right);

    /// <summary>Compares the canonical significand and exponent pair.</summary>
    public static bool operator ==(ShapeMeasure left, ShapeMeasure right) => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(ShapeMeasure other) => Significand == other.Significand && BinaryExponent == other.BinaryExponent;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ShapeMeasure other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Significand, BinaryExponent);

    /// <summary>Rounds the stored pair to binary32 using nearest-even rounding.</summary>
    /// <param name="value">Receives the finite result, or positive zero when conversion fails.</param>
    /// <returns><see langword="true"/> when the pair converts to a finite nonzero value or canonical zero.</returns>
    public bool TryGetSingle(out float value)
    {
        value = 0.0f;

        if (Significand == 0.0f)
        {
            return BinaryExponent == 0;
        }

        if (!float.IsFinite(Significand) || Significand < 1.0f || Significand >= 2.0f
            || BinaryExponent is < -512 or > 512)
        {
            return false;
        }

        if (BinaryExponent > 127)
        {
            return false;
        }

        var significandBits = unchecked((uint)BitConverter.SingleToInt32Bits(Significand));
        var significand = (significandBits & 0x007F_FFFFU) | 0x0080_0000U;

        if (BinaryExponent >= -126)
        {
            var exponentBits = checked((uint)(BinaryExponent + 127));
            value = BitConverter.UInt32BitsToSingle((exponentBits << 23) | (significandBits & 0x007F_FFFFU));
            return true;
        }

        var rightShift = -(BinaryExponent + 126);

        if (rightShift >= 25)
        {
            return false;
        }

        var rounded = significand >> rightShift;
        var mask = (1U << rightShift) - 1U;
        var remainder = significand & mask;
        var halfway = 1U << (rightShift - 1);

        if (remainder > halfway || (remainder == halfway && (rounded & 1U) != 0))
        {
            rounded++;
        }

        if (rounded == 0)
        {
            return false;
        }

        value = BitConverter.UInt32BitsToSingle(rounded);
        return true;
    }
}
