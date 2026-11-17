// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;

namespace TerraFX.Animation;

internal static class AnimationTickKernels
{
    // Speed 4 at the minimum admitted rate of one advances at most 40,000,000 ticks per step.
    private const ulong MaximumDeltaTicks = 40_000_000;
    private const int MaximumRate = 1_000;
    private const ulong TicksPerSecond = 10_000_000;

    // Scaling every finite binary32 speed by 2^149 makes its significand integral.
    private const int TimeFractionShift = 149;

    internal static AnimationTickAdvance Advance(
        AnimationTickRemainder192 remainder,
        float speed,
        int ticksPerSecond)
    {
        if (!float.IsFinite(speed))
        {
            throw new ArgumentOutOfRangeException(nameof(speed));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(speed, -4.0f);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(speed, 4.0f);
        ArgumentOutOfRangeException.ThrowIfLessThan(ticksPerSecond, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ticksPerSecond, MaximumRate);

        var denominator = new UInt192(0, 0, checked((ulong)ticksPerSecond << 21));
        var fractionalNumerator = FromRemainder(remainder);

        if (Compare(fractionalNumerator.Magnitude, denominator) >= 0)
        {
            throw new InvalidDataException("An Animation tick remainder must be strictly smaller than one tick.");
        }

        var (speedSign, speedMagnitude) = DecodeSpeed(speed);
        var scaledSpeed = MultiplySmall(speedMagnitude, TicksPerSecond);
        var numerator = AddSigned(fractionalNumerator, new SignedMagnitude(speedSign, scaledSpeed));
        var (deltaMagnitude, remainingMagnitude) = DivideByFixedRate(numerator.Magnitude, ticksPerSecond);

        if (deltaMagnitude > MaximumDeltaTicks)
        {
            throw new OverflowException("An Animation fixed-tick delta exceeds its admitted speed bound.");
        }

        var nextRemainder = ToRemainder(new SignedMagnitude(
            IsZero(remainingMagnitude) ? 0 : numerator.Sign,
            remainingMagnitude));
        var signedDelta = checked((long)deltaMagnitude * numerator.Sign);
        return new AnimationTickAdvance(signedDelta, nextRemainder);
    }

    private static SignedMagnitude AddSigned(SignedMagnitude first, SignedMagnitude second)
    {
        if (first.Sign == 0)
        {
            return second;
        }

        if (second.Sign == 0)
        {
            return first;
        }

        if (first.Sign == second.Sign)
        {
            return new SignedMagnitude(first.Sign, AddUnsigned(first.Magnitude, second.Magnitude));
        }

        var comparison = Compare(first.Magnitude, second.Magnitude);

        if (comparison == 0)
        {
            return default;
        }

        return (comparison > 0)
            ? new SignedMagnitude(first.Sign, SubtractUnsigned(first.Magnitude, second.Magnitude))
            : new SignedMagnitude(second.Sign, SubtractUnsigned(second.Magnitude, first.Magnitude));
    }

    private static UInt192 AddUnsigned(UInt192 first, UInt192 second)
    {
        var low = unchecked(first.Low + second.Low);
        var lowCarry = low < first.Low;
        var middleSum = unchecked(first.Middle + second.Middle);
        var middleCarry = middleSum < first.Middle;
        var middle = unchecked(middleSum + (lowCarry ? 1UL : 0UL));
        middleCarry |= lowCarry && (middle == 0);
        var highSum = unchecked(first.High + second.High);
        var highCarry = highSum < first.High;
        var high = unchecked(highSum + (middleCarry ? 1UL : 0UL));

        if (highCarry || (middleCarry && (high == 0)))
        {
            throw new OverflowException("The signed 192-bit Animation tick numerator overflowed.");
        }

        return new UInt192(low, middle, high);
    }

    private static int Compare(UInt192 first, UInt192 second)
    {
        var highComparison = first.High.CompareTo(second.High);

        if (highComparison != 0)
        {
            return highComparison;
        }

        var middleComparison = first.Middle.CompareTo(second.Middle);
        return (middleComparison != 0) ? middleComparison : first.Low.CompareTo(second.Low);
    }

    private static (int Sign, UInt192 Magnitude) DecodeSpeed(float speed)
    {
        var bits = BitConverter.SingleToUInt32Bits(speed);
        var exponent = checked((int)((bits >> 23) & 0xFF));
        var significand = bits & 0x7F_FFFF;

        if (exponent != 0)
        {
            significand |= 0x80_0000;
        }

        var magnitude = ShiftLeft(significand, (exponent == 0) ? 0 : exponent - 1);
        var sign = IsZero(magnitude) ? 0 : ((bits >> 31) == 0 ? 1 : -1);
        return (sign, magnitude);
    }

    private static (ulong Quotient, UInt192 Remainder) DivideByFixedRate(UInt192 numerator, int ticksPerSecond)
    {
        // Q is an integer multiple of 2^128, so only the high limb participates in the quotient.
        var denominatorHigh = checked((ulong)ticksPerSecond << (TimeFractionShift - 128));
        var quotient = numerator.High / denominatorHigh;
        var remainder = new UInt192(
            numerator.Low,
            numerator.Middle,
            numerator.High % denominatorHigh);
        return (quotient, remainder);
    }

    private static SignedMagnitude FromRemainder(AnimationTickRemainder192 remainder)
    {
        var bits = new UInt192(remainder.Low, remainder.Middle, remainder.High);
        var isNegative = (remainder.High & (1UL << 63)) != 0;

        if (!isNegative)
        {
            return new SignedMagnitude(IsZero(bits) ? 0 : 1, bits);
        }

        return new SignedMagnitude(-1, Negate(bits));
    }

    private static bool IsZero(UInt192 value)
        => (value.Low == 0) && (value.Middle == 0) && (value.High == 0);

    private static UInt192 MultiplySmall(UInt192 value, ulong multiplier)
    {
        var lowProduct = (UInt128)value.Low * multiplier;
        var middleProduct = ((UInt128)value.Middle * multiplier) + (ulong)(lowProduct >> 64);
        var highProduct = ((UInt128)value.High * multiplier) + (ulong)(middleProduct >> 64);

        if ((highProduct >> 64) != 0)
        {
            throw new OverflowException("The signed 192-bit Animation tick product overflowed.");
        }

        return new UInt192(
            (ulong)lowProduct,
            (ulong)middleProduct,
            (ulong)highProduct);
    }

    private static UInt192 Negate(UInt192 value)
    {
        var low = unchecked(~value.Low + 1UL);
        var carry = low == 0;
        var middle = unchecked(~value.Middle + (carry ? 1UL : 0UL));
        carry &= middle == 0;
        var high = unchecked(~value.High + (carry ? 1UL : 0UL));
        return new UInt192(low, middle, high);
    }

    private static UInt192 ShiftLeft(ulong value, int shift)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(shift);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(shift, 192);
        var wordShift = shift / 64;
        var bitShift = shift % 64;

        return wordShift switch {
            0 => new UInt192(
                value << bitShift,
                (bitShift == 0) ? 0 : value >> (64 - bitShift),
                0),
            1 => new UInt192(
                0,
                value << bitShift,
                (bitShift == 0) ? 0 : value >> (64 - bitShift)),
            _ => new UInt192(0, 0, value << bitShift),
        };
    }

    private static UInt192 SubtractUnsigned(UInt192 first, UInt192 second)
    {
        if (Compare(first, second) < 0)
        {
            throw new OverflowException("The Animation tick numerator subtraction underflowed.");
        }

        var low = unchecked(first.Low - second.Low);
        var lowBorrow = first.Low < second.Low;
        var middleDifference = unchecked(first.Middle - second.Middle);
        var middleBorrow = first.Middle < second.Middle;
        var middle = unchecked(middleDifference - (lowBorrow ? 1UL : 0UL));
        middleBorrow |= lowBorrow && (middleDifference == 0);
        var high = unchecked(first.High - second.High - (middleBorrow ? 1UL : 0UL));
        return new UInt192(low, middle, high);
    }

    private static AnimationTickRemainder192 ToRemainder(SignedMagnitude value)
    {
        var bits = (value.Sign < 0) ? Negate(value.Magnitude) : value.Magnitude;
        return new AnimationTickRemainder192(bits.Low, bits.Middle, bits.High);
    }

    private readonly record struct SignedMagnitude(int Sign, UInt192 Magnitude);

    private readonly record struct UInt192(ulong Low, ulong Middle, ulong High);
}
