// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using BigInteger = System.Numerics.BigInteger;
using TerraFX;
using TerraFX.Graphics;
using TerraFX.Numerics;

namespace TerraFX.Rendering;

internal static class RenderCameraViewport
{
    internal static RenderArea CreateArea(Vector4 viewport, PixelSize targetExtent)
    {
        ArgumentOutOfRangeException.ThrowIfZero(targetExtent.Width);
        ArgumentOutOfRangeException.ThrowIfZero(targetExtent.Height);

        if (!float.IsFinite(viewport.X)
            || !float.IsFinite(viewport.Y)
            || !float.IsFinite(viewport.Z)
            || !float.IsFinite(viewport.W)
            || (viewport.X < 0.0f)
            || (viewport.Y < 0.0f)
            || (viewport.Z <= 0.0f)
            || (viewport.W <= 0.0f)
            || (viewport.X > 1.0f)
            || (viewport.Y > 1.0f)
            || (viewport.Z > 1.0f)
            || (viewport.W > 1.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(viewport));
        }

        // Keep shared normalized edges exact until they are rounded to the physical pixel grid.
        var left = GetDyadic(viewport.X);
        var top = GetDyadic(viewport.Y);
        var right = Add(left, GetDyadic(viewport.Z));
        var bottom = Add(top, GetDyadic(viewport.W));

        if (!IsWithinOne(right) || !IsWithinOne(bottom))
        {
            throw new ArgumentOutOfRangeException(nameof(viewport), "The normalized camera viewport must remain within the target.");
        }

        var x = RoundScaledToEven(left, targetExtent.Width);
        var y = RoundScaledToEven(top, targetExtent.Height);
        var rightPixel = RoundScaledToEven(right, targetExtent.Width);
        var bottomPixel = RoundScaledToEven(bottom, targetExtent.Height);

        if ((rightPixel <= x) || (bottomPixel <= y))
        {
            throw new ArgumentOutOfRangeException(nameof(viewport), "The normalized viewport collapses to an empty pixel area.");
        }

        return new RenderArea(x, y, rightPixel - x, bottomPixel - y);
    }

    private static Dyadic Add(Dyadic left, Dyadic right)
    {
        var denominatorExponent = Math.Max(left.DenominatorExponent, right.DenominatorExponent);
        return new Dyadic(
            (left.Numerator << (denominatorExponent - left.DenominatorExponent))
                + (right.Numerator << (denominatorExponent - right.DenominatorExponent)),
            denominatorExponent);
    }

    private static Dyadic GetDyadic(float value)
    {
        var bits = BitConverter.SingleToUInt32Bits(value) & 0x7FFFFFFF;
        var exponentBits = checked((int)((bits >> 23) & 0xFF));
        var fraction = bits & 0x7FFFFF;

        var significand = (exponentBits == 0)
            ? fraction
            : ((1u << 23) | fraction);

        if (significand == 0)
        {
            return new Dyadic(BigInteger.Zero, 0);
        }

        var exponent = (exponentBits == 0) ? -149 : (exponentBits - 150);

        // Reduce trailing powers of two so common viewport fractions stay in the small-integer range.
        while ((significand & 1) == 0)
        {
            significand >>= 1;
            exponent++;
        }

        var denominatorExponent = -exponent;
        return new Dyadic(new BigInteger(significand), denominatorExponent);
    }

    private static bool IsWithinOne(Dyadic value)
        => value.Numerator <= (BigInteger.One << value.DenominatorExponent);

    private static int RoundScaledToEven(Dyadic value, int scale)
    {
        var numerator = value.Numerator * scale;
        var denominator = BigInteger.One << value.DenominatorExponent;
        var integer = BigInteger.DivRem(numerator, denominator, out var remainder);
        var halfComparison = (remainder << 1).CompareTo(denominator);

        if ((halfComparison > 0) || ((halfComparison == 0) && !integer.IsEven))
        {
            integer += BigInteger.One;
        }

        return checked((int)integer);
    }

    private readonly record struct Dyadic(BigInteger Numerator, int DenominatorExponent);
}
