// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Numerics;

namespace TerraFX.Numerics;

internal ref struct BoundedRational
{
    // Every operation rejects overflow instead of truncating.
    internal const int MaximumLimbCount = 16;

    // The workspace partitions equally sized numerator, denominator, scratch, and product regions.
    internal const int WorkspaceRegionCount = 4;

    private readonly Span<ulong> _numerator;
    private readonly Span<ulong> _denominator;
    private readonly Span<ulong> _scratch;
    private readonly Span<ulong> _product;
    private bool _isNegative;

    internal BoundedRational(Span<ulong> workspace)
    {
        if ((workspace.Length < WorkspaceRegionCount)
            || ((workspace.Length % WorkspaceRegionCount) != 0)
            || (workspace.Length > MaximumLimbCount * WorkspaceRegionCount))
        {
            ThrowHelper.ThrowArgumentException("The bounded rational workspace has an invalid size.", nameof(workspace));
        }

        var limbCount = workspace.Length / WorkspaceRegionCount;
        _numerator = workspace[..limbCount];
        _denominator = workspace.Slice(limbCount, limbCount);
        _scratch = workspace.Slice(limbCount * 2, limbCount);
        _product = workspace.Slice(limbCount * 3, limbCount);
        _isNegative = false;
    }

    internal readonly bool IsNegative => _isNegative;

    internal readonly bool NumeratorIsZero => GetBitLength(_numerator) == 0;

    internal static void ParseHex(ReadOnlySpan<char> value, Span<ulong> destination)
    {
        if (destination.IsEmpty || (destination.Length > MaximumLimbCount))
        {
            ThrowHelper.ThrowArgumentException("The bounded rational destination has an invalid size.", nameof(destination));
        }

        destination.Clear();

        foreach (var character in value)
        {
            var digit = character switch {
                >= '0' and <= '9' => character - '0',
                >= 'A' and <= 'F' => character - 'A' + 10,
                >= 'a' and <= 'f' => character - 'a' + 10,
                _ => -1,
            };

            if (digit < 0)
            {
                ThrowHelper.ThrowFormatException("The value is not hexadecimal.");
            }

            ShiftLeft(destination, 4);
            AddSmall(destination, (ulong)digit);
        }
    }

    private static void AddSmall(Span<ulong> value, ulong addend)
    {
        UInt128 carry = addend;

        for (var index = 0; (index < value.Length) && (carry != 0); index++)
        {
            var sum = (UInt128)value[index] + carry;
            value[index] = unchecked((ulong)sum);
            carry = sum >> 64;
        }

        if (carry != 0)
        {
            ThrowHelper.ThrowInvalidOperationException("The bounded rational workspace overflowed.");
        }
    }

    private static int CompareShifted(ReadOnlySpan<ulong> left, ReadOnlySpan<ulong> right, int rightShift)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rightShift);

        var leftBitLength = GetBitLength(left);
        var rightBitLength = GetBitLength(right);

        if (rightBitLength == 0)
        {
            return leftBitLength == 0 ? 0 : 1;
        }

        var shiftedRightBitLength = rightBitLength + rightShift;

        if (leftBitLength != shiftedRightBitLength)
        {
            return leftBitLength < shiftedRightBitLength ? -1 : 1;
        }

        for (var index = left.Length - 1; index >= 0; index--)
        {
            var leftLimb = left[index];
            var rightLimb = GetShiftedLimb(right, index, rightShift);

            if (leftLimb != rightLimb)
            {
                return leftLimb < rightLimb ? -1 : 1;
            }
        }

        return 0;
    }

    private static int CompareTwice(ReadOnlySpan<ulong> left, ReadOnlySpan<ulong> right)
    {
        return CompareTwiceWithShifted(left, right, 0);
    }

    private static int CompareTwiceWithShifted(ReadOnlySpan<ulong> left, ReadOnlySpan<ulong> right, int rightShift)
    {
        var leftBitLength = GetBitLength(left);
        var doubledLeftBitLength = leftBitLength == 0 ? 0 : leftBitLength + 1;
        var shiftedRightBitLength = GetBitLength(right) + rightShift;

        if (doubledLeftBitLength != shiftedRightBitLength)
        {
            return doubledLeftBitLength < shiftedRightBitLength ? -1 : 1;
        }

        for (var index = left.Length - 1; index >= 0; index--)
        {
            var doubledLeft = left[index] << 1;

            if (index > 0)
            {
                doubledLeft |= left[index - 1] >> 63;
            }

            var shiftedRight = GetShiftedLimb(right, index, rightShift);

            if (doubledLeft != shiftedRight)
            {
                return doubledLeft < shiftedRight ? -1 : 1;
            }
        }

        return 0;
    }

    private static int GetBitLength(ReadOnlySpan<ulong> value)
    {
        for (var index = value.Length - 1; index >= 0; index--)
        {
            if (value[index] != 0)
            {
                return (index * 64) + 64 - BitOperations.LeadingZeroCount(value[index]);
            }
        }

        return 0;
    }

    private static int GetLimbLength(ReadOnlySpan<ulong> value)
    {
        for (var index = value.Length - 1; index >= 0; index--)
        {
            if (value[index] != 0)
            {
                return index + 1;
            }
        }

        return 0;
    }

    private static ulong GetShiftedLimb(ReadOnlySpan<ulong> value, int destinationIndex, int bitCount)
    {
        var wordShift = bitCount / 64;
        var bitShift = bitCount % 64;
        var sourceIndex = destinationIndex - wordShift;

        if (sourceIndex < 0)
        {
            return 0;
        }

        var result = value[sourceIndex] << bitShift;

        if (bitShift != 0 && sourceIndex > 0)
        {
            result |= value[sourceIndex - 1] >> (64 - bitShift);
        }

        return result;
    }

    private static void Multiply(ReadOnlySpan<ulong> left, ReadOnlySpan<ulong> right, Span<ulong> destination)
    {
        destination.Clear();
        var leftLength = GetLimbLength(left);
        var rightLength = GetLimbLength(right);

        for (var leftIndex = 0; leftIndex < leftLength; leftIndex++)
        {
            ulong carry = 0;

            for (var rightIndex = 0; rightIndex < rightLength; rightIndex++)
            {
                var destinationIndex = leftIndex + rightIndex;

                if (destinationIndex >= destination.Length)
                {
                    ThrowHelper.ThrowInvalidOperationException("The bounded rational workspace overflowed.");
                }

                var product = ((UInt128)left[leftIndex] * right[rightIndex]) + destination[destinationIndex] + carry;
                destination[destinationIndex] = unchecked((ulong)product);
                carry = (ulong)(product >> 64);
            }

            var carryIndex = leftIndex + rightLength;

            while (carry != 0)
            {
                if (carryIndex >= destination.Length)
                {
                    ThrowHelper.ThrowInvalidOperationException("The bounded rational workspace overflowed.");
                }

                var sum = (UInt128)destination[carryIndex] + carry;
                destination[carryIndex] = unchecked((ulong)sum);
                carry = (ulong)(sum >> 64);
                carryIndex++;
            }
        }
    }

    private static void MultiplySmall(Span<ulong> value, ulong multiplier)
    {
        UInt128 carry = 0;

        for (var index = 0; index < value.Length; index++)
        {
            var product = ((UInt128)value[index] * multiplier) + carry;
            value[index] = unchecked((ulong)product);
            carry = product >> 64;
        }

        if (carry != 0)
        {
            ThrowHelper.ThrowInvalidOperationException("The bounded rational workspace overflowed.");
        }
    }

    private static void ShiftLeft(Span<ulong> value, int bitCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bitCount);

        if (bitCount == 0)
        {
            return;
        }

        var bitLength = GetBitLength(value);

        if (bitLength == 0)
        {
            return;
        }

        if (bitLength + bitCount > value.Length * 64)
        {
            ThrowHelper.ThrowInvalidOperationException("The bounded rational workspace overflowed.");
        }

        var wordShift = bitCount / 64;
        var bitShift = bitCount % 64;

        for (var destinationIndex = value.Length - 1; destinationIndex >= 0; destinationIndex--)
        {
            var sourceIndex = destinationIndex - wordShift;

            if (sourceIndex < 0)
            {
                value[destinationIndex] = 0;
                continue;
            }

            var shifted = value[sourceIndex] << bitShift;

            if (bitShift != 0 && sourceIndex > 0)
            {
                shifted |= value[sourceIndex - 1] >> (64 - bitShift);
            }

            value[destinationIndex] = shifted;
        }
    }

    private static void Subtract(Span<ulong> left, ReadOnlySpan<ulong> right)
    {
        ulong borrow = 0;

        for (var index = 0; index < left.Length; index++)
        {
            var subtrahend = (UInt128)right[index] + borrow;
            var minuend = left[index];
            left[index] = unchecked((ulong)((UInt128)minuend - subtrahend));
            borrow = (UInt128)minuend < subtrahend ? 1ul : 0ul;
        }

        if (borrow != 0)
        {
            ThrowHelper.ThrowInvalidOperationException("The bounded rational subtraction underflowed.");
        }
    }

    private static void SubtractShifted(Span<ulong> left, ReadOnlySpan<ulong> right, int rightShift)
    {
        ulong borrow = 0;

        for (var index = 0; index < left.Length; index++)
        {
            var subtrahend = (UInt128)GetShiftedLimb(right, index, rightShift) + borrow;
            var minuend = left[index];
            left[index] = unchecked((ulong)((UInt128)minuend - subtrahend));
            borrow = (UInt128)minuend < subtrahend ? 1ul : 0ul;
        }

        if (borrow != 0)
        {
            ThrowHelper.ThrowInvalidOperationException("The bounded rational subtraction underflowed.");
        }
    }

    internal readonly void CopyNumeratorTo(Span<ulong> destination)
    {
        destination.Clear();

        if (_numerator.Length > destination.Length)
        {
            for (var index = destination.Length; index < _numerator.Length; index++)
            {
                if (_numerator[index] != 0)
                {
                    ThrowHelper.ThrowInvalidOperationException("The bounded rational destination is too small.");
                }
            }
        }

        _numerator[..Math.Min(_numerator.Length, destination.Length)].CopyTo(destination);
    }

    internal readonly void MultiplyDenominator(ReadOnlySpan<ulong> multiplier)
    {
        if (GetBitLength(multiplier) == 0)
        {
            ThrowHelper.ThrowArgumentException("The denominator multiplier must be nonzero.", nameof(multiplier));
        }

        Multiply(_denominator, multiplier, _product);
        _product.CopyTo(_denominator);
    }

    internal readonly void MultiplyDenominator(ulong multiplier)
    {
        ArgumentOutOfRangeException.ThrowIfZero(multiplier);

        MultiplySmall(_denominator, multiplier);
    }

    internal readonly void MultiplyNumerator(ReadOnlySpan<ulong> multiplier)
    {
        Multiply(_numerator, multiplier, _product);
        _product.CopyTo(_numerator);
    }

    internal readonly void MultiplyNumerator(ulong multiplier)
    {
        MultiplySmall(_numerator, multiplier);
    }

    internal readonly bool NumeratorTimesPowerOfTwoEqualsDenominator(int bitCount)
    {
        return CompareShifted(_denominator, _numerator, bitCount) == 0;
    }

    internal void ReduceModuloDenominator(ulong denominatorMultiplier)
    {
        if (denominatorMultiplier != 1)
        {
            MultiplyDenominator(denominatorMultiplier);
        }

        _numerator.CopyTo(_scratch);

        var shift = GetBitLength(_scratch) - GetBitLength(_denominator);

        for (; shift >= 0; shift--)
        {
            if (CompareShifted(_scratch, _denominator, shift) >= 0)
            {
                SubtractShifted(_scratch, _denominator, shift);
            }
        }

        if (CompareTwice(_scratch, _denominator) > 0)
        {
            _denominator.CopyTo(_product);
            Subtract(_product, _scratch);
            _product.CopyTo(_numerator);

            _isNegative = !_isNegative;
        }
        else
        {
            _scratch.CopyTo(_numerator);
        }
    }

    internal readonly float RoundToSingle(bool negativeZero = false)
    {
        if (!TryRoundToSingle(out var result, negativeZero))
        {
            ThrowHelper.ThrowOverflowException("The converted value rounds to infinity.");
        }

        return result;
    }

    internal void SetFraction(ReadOnlySpan<ulong> numerator, ReadOnlySpan<ulong> denominator, bool negative)
    {
        if (numerator.Length != _numerator.Length || denominator.Length != _denominator.Length || GetBitLength(denominator) == 0)
        {
            ThrowHelper.ThrowArgumentException("The fraction must have bounded magnitudes and a positive denominator.");
        }

        numerator.CopyTo(_numerator);
        denominator.CopyTo(_denominator);

        _isNegative = negative;
    }

    internal void SetSingle(float value)
    {
        if (!float.IsFinite(value))
        {
            ThrowHelper.ThrowArgumentException("The value must be finite.", nameof(value));
        }

        _numerator.Clear();
        _denominator.Clear();

        var bits = BitConverter.SingleToUInt32Bits(value);
        var magnitude = bits & 0x7FFF_FFFF;
        var exponentBits = (int)((magnitude >> 23) & 0xFF);
        var fractionBits = magnitude & 0x007F_FFFF;
        var significand = exponentBits == 0 ? fractionBits : 0x0080_0000u | fractionBits;
        var exponent = exponentBits == 0 ? -149 : exponentBits - 150;

        _numerator[0] = significand;
        _denominator[0] = 1;

        if (exponent >= 0)
        {
            ShiftLeft(_numerator, exponent);
        }
        else
        {
            ShiftLeft(_denominator, -exponent);
        }

        _isNegative = (bits & 0x8000_0000) != 0;
    }

    internal readonly void ShiftDenominatorLeft(int bitCount)
    {
        ShiftLeft(_denominator, bitCount);
    }

    internal readonly void ShiftNumeratorLeft(int bitCount)
    {
        ShiftLeft(_numerator, bitCount);
    }

    internal bool TryFloorDivideToInt32(out int quotient)
    {
        if (!TryFloorDivideToInt64(out var value) || value < int.MinValue || value > int.MaxValue)
        {
            quotient = default;
            return false;
        }

        quotient = (int)value;
        return true;
    }

    internal bool TryFloorDivideToInt64(out long quotient)
    {
        var numeratorBitLength = GetBitLength(_numerator);
        var denominatorBitLength = GetBitLength(_denominator);
        var shift = numeratorBitLength - denominatorBitLength;

        if (shift > 63)
        {
            quotient = default;
            return false;
        }

        var negative = _isNegative;
        ulong magnitude = 0;

        for (; shift >= 0; shift--)
        {
            if (CompareShifted(_numerator, _denominator, shift) >= 0)
            {
                SubtractShifted(_numerator, _denominator, shift);
                magnitude |= 1ul << shift;
            }
        }

        // Floor division keeps the remainder nonnegative, including for negative nonintegral inputs.
        if (negative && (GetBitLength(_numerator) != 0))
        {
            if (magnitude == 0x8000_0000_0000_0000ul)
            {
                quotient = default;
                return false;
            }

            magnitude++;
            _denominator.CopyTo(_product);
            Subtract(_product, _numerator);
            _product.CopyTo(_numerator);
        }

        _isNegative = false;

        if (negative)
        {
            if (magnitude > 0x8000_0000_0000_0000ul)
            {
                quotient = default;
                return false;
            }

            quotient = unchecked(-(long)magnitude);
        }
        else
        {
            if (magnitude > long.MaxValue)
            {
                quotient = default;
                return false;
            }

            quotient = (long)magnitude;
        }

        return true;
    }

    internal readonly bool TryRoundToSingle(out float result, bool negativeZero = false)
    {
        var numeratorBitLength = GetBitLength(_numerator);
        var negative = numeratorBitLength == 0 ? negativeZero : _isNegative;
        var sign = negative ? 0x8000_0000u : 0;

        if (numeratorBitLength == 0)
        {
            result = BitConverter.UInt32BitsToSingle(sign);
            return true;
        }

        var exponent = numeratorBitLength - GetBitLength(_denominator);

        if (CompareToPowerOfTwo(exponent) < 0)
        {
            exponent--;
        }

        if (exponent >= -126)
        {
            var significand = RoundQuotient(23 - exponent);

            if (significand == (1ul << 24))
            {
                significand >>= 1;
                exponent++;
            }

            if (exponent > 127)
            {
                result = default;
                return false;
            }

            var encodedExponent = (uint)(exponent + 127) << 23;
            var encodedSignificand = (uint)(significand - (1ul << 23));
            result = BitConverter.UInt32BitsToSingle(sign | encodedExponent | encodedSignificand);
            return true;
        }

        var subnormalSignificand = RoundQuotient(149);

        if (subnormalSignificand == 0)
        {
            result = BitConverter.UInt32BitsToSingle(sign);
            return true;
        }

        if (subnormalSignificand >= (1ul << 23))
        {
            result = BitConverter.UInt32BitsToSingle(sign | 0x0080_0000u);
            return true;
        }

        result = BitConverter.UInt32BitsToSingle(sign | (uint)subnormalSignificand);
        return true;
    }

    private readonly int CompareToPowerOfTwo(int exponent)
    {
        if (exponent >= 0)
        {
            return CompareShifted(_numerator, _denominator, exponent);
        }

        return -CompareShifted(_denominator, _numerator, -exponent);
    }

    private readonly ulong RoundQuotient(int binaryShift)
    {
        var numeratorShift = Math.Max(binaryShift, 0);
        var denominatorShift = Math.Max(-binaryShift, 0);
        _numerator.CopyTo(_scratch);
        ShiftLeft(_scratch, numeratorShift);

        var quotientShift = GetBitLength(_scratch) - (GetBitLength(_denominator) + denominatorShift);

        if (quotientShift > 31)
        {
            ThrowHelper.ThrowInvalidOperationException("The bounded rational quotient exceeds its rounding range.");
        }

        ulong quotient = 0;

        for (var shift = quotientShift; shift >= 0; shift--)
        {
            var denominatorBitShift = denominatorShift + shift;

            if (CompareShifted(_scratch, _denominator, denominatorBitShift) >= 0)
            {
                SubtractShifted(_scratch, _denominator, denominatorBitShift);
                quotient |= 1ul << shift;
            }
        }

        var halfComparison = CompareTwiceWithShifted(_scratch, _denominator, denominatorShift);

        if (halfComparison > 0 || (halfComparison == 0 && (quotient & 1) != 0))
        {
            quotient++;
        }

        return quotient;
    }
}
