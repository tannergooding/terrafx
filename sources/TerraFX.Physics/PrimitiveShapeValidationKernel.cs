// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Numerics;
using BigInteger = System.Numerics.BigInteger;

namespace TerraFX.Physics;

internal static class PrimitiveShapeValidationKernel
{
    private const uint MaximumFiniteSingleBits = 0x7F7F_FFFF;
    // The formula identity treats this binary32 value as an exact dyadic coefficient.
    private const uint Pi32Bits = 0x4049_0FDB;
    private const uint SignificandBits = 0x0080_0000;

    internal static ShapeMeasure CalculateBoxArea(float halfExtentX, float halfExtentY)
    {
        var x = DecodePositiveSignificand(halfExtentX, out var xExponent);
        var y = DecodePositiveSignificand(halfExtentY, out var yExponent);
        return RoundMeasure(x * y, BigInteger.One, xExponent + yExponent + 2);
    }

    internal static ShapeMeasure CalculateBoxVolume(float halfExtentX, float halfExtentY, float halfExtentZ)
    {
        var x = DecodePositiveSignificand(halfExtentX, out var xExponent);
        var y = DecodePositiveSignificand(halfExtentY, out var yExponent);
        var z = DecodePositiveSignificand(halfExtentZ, out var zExponent);
        return RoundMeasure(x * y * z, BigInteger.One, xExponent + yExponent + zExponent + 3);
    }

    internal static ShapeMeasure CalculateCircleArea(float radius)
    {
        var pi = DecodePositiveSignificand(BitConverter.UInt32BitsToSingle(Pi32Bits), out var piExponent);
        var r = DecodePositiveSignificand(radius, out var radiusExponent);
        return RoundMeasure(pi * r * r, BigInteger.One, piExponent + (2 * radiusExponent));
    }

    internal static ShapeMeasure CalculateSphereVolume(float radius)
    {
        var pi = DecodePositiveSignificand(BitConverter.UInt32BitsToSingle(Pi32Bits), out var piExponent);
        var r = DecodePositiveSignificand(radius, out var radiusExponent);
        return RoundMeasure(4 * pi * r * r * r, 3, piExponent + (3 * radiusExponent));
    }

    internal static bool IsBeyondLocalExtent(Vector2 halfExtents, float maximum, out float enclosingRadius)
    {
        var x = EncodeUnits(halfExtents.X);
        var y = EncodeUnits(halfExtents.Y);
        var maximumUnits = EncodeUnits(maximum);
        var squaredNorm = (x * x) + (y * y);
        enclosingRadius = CeilingSquareRoot(squaredNorm);
        return squaredNorm > (maximumUnits * maximumUnits);
    }

    internal static bool IsBeyondLocalExtent(Vector3 halfExtents, float maximum, out float enclosingRadius)
    {
        var x = EncodeUnits(halfExtents.X);
        var y = EncodeUnits(halfExtents.Y);
        var z = EncodeUnits(halfExtents.Z);
        var maximumUnits = EncodeUnits(maximum);
        var squaredNorm = (x * x) + (y * y) + (z * z);
        enclosingRadius = CeilingSquareRoot(squaredNorm);
        return squaredNorm > (maximumUnits * maximumUnits);
    }

    private static float CeilingSquareRoot(BigInteger squaredUnits)
    {
        var low = 0U;
        var high = MaximumFiniteSingleBits;

        // Positive binary32 bit patterns are ordered by value; find the smallest whose exact square encloses the radicand.
        while (low < high)
        {
            var middle = low + ((high - low) >> 1);
            var candidate = EncodeUnits(BitConverter.UInt32BitsToSingle(middle));

            if ((candidate * candidate) >= squaredUnits)
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }

        return BitConverter.UInt32BitsToSingle(low);
    }

    private static BigInteger DecodePositiveSignificand(float value, out int exponent)
    {
        var bits = unchecked((uint)BitConverter.SingleToInt32Bits(value));
        var storedExponent = (int)((bits >> 23) & 0xFF);
        var significand = bits & 0x007F_FFFF;

        // Decode as an integer significand times an exact power of two.
        if (storedExponent == 0)
        {
            exponent = -149;
        }
        else
        {
            significand |= SignificandBits;
            exponent = storedExponent - 150;
        }

        return new BigInteger(significand);
    }

    private static BigInteger EncodeUnits(float value)
    {
        var bits = unchecked((uint)BitConverter.SingleToInt32Bits(value));

        if ((bits & 0x8000_0000U) != 0 || (bits & 0x7F80_0000U) == 0x7F80_0000U)
        {
            throw new InvalidOperationException("The exact primitive extent must be finite and nonnegative.");
        }

        var storedExponent = (int)((bits >> 23) & 0xFF);
        var significand = bits & 0x007F_FFFF;
        var exponent = -149;

        if (storedExponent != 0)
        {
            significand |= SignificandBits;
            exponent = storedExponent - 150;
        }

        return new BigInteger(significand) << (exponent + 149);
    }

    private static ShapeMeasure RoundMeasure(BigInteger numerator, BigInteger denominator, int binaryExponent)
    {
        if (numerator.Sign <= 0 || denominator.Sign <= 0)
        {
            throw new InvalidOperationException("A primitive measure must be a positive exact rational.");
        }

        // Keep the rational exact until the normalized 24-bit significand is rounded once below.
        var ratioExponent = checked((int)(numerator.GetBitLength() - denominator.GetBitLength()));
        var atLeastPowerOfTwo = ratioExponent >= 0
            ? numerator >= (denominator << ratioExponent)
            : (numerator << -ratioExponent) >= denominator;
        var resultExponent = checked(binaryExponent + ratioExponent - (atLeastPowerOfTwo ? 0 : 1));
        var shift = checked(23 + binaryExponent - resultExponent);
        var scaledNumerator = numerator;
        var scaledDenominator = denominator;

        if (shift >= 0)
        {
            scaledNumerator <<= shift;
        }
        else
        {
            scaledDenominator <<= -shift;
        }

        var roundedSignificand = BigInteger.DivRem(scaledNumerator, scaledDenominator, out var remainder);
        var comparison = (remainder << 1).CompareTo(scaledDenominator);

        if (comparison > 0 || (comparison == 0 && !roundedSignificand.IsEven))
        {
            roundedSignificand++;
        }

        if (roundedSignificand == (BigInteger.One << 24))
        {
            roundedSignificand >>= 1;
            resultExponent++;
        }

        if (roundedSignificand < SignificandBits || roundedSignificand >= (BigInteger.One << 24)
            || resultExponent is < -512 or > 512)
        {
            throw new InvalidOperationException("The primitive measure is outside the bounded ShapeMeasure domain.");
        }

        var significandBits = 0x3F80_0000U | checked((uint)(roundedSignificand - SignificandBits));
        return new ShapeMeasure(BitConverter.UInt32BitsToSingle(significandBits), resultExponent);
    }
}
