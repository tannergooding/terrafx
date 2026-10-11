// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Numerics;

internal ref struct BoundedInteger
{
    internal const int MaximumLimbCount = BoundedRational.MaximumLimbCount;

    private readonly Span<ulong> _limbs;
    private bool _isNegative;

    internal BoundedInteger(Span<ulong> limbs)
    {
        if (limbs.IsEmpty || limbs.Length > MaximumLimbCount)
        {
            ThrowHelper.ThrowArgumentException("The bounded integer workspace has an invalid size.", nameof(limbs));
        }

        _limbs = limbs;
        _limbs.Clear();
        _isNegative = false;
    }

    internal readonly int BitLength
    {
        get
        {
            for (var index = _limbs.Length - 1; index >= 0; index--)
            {
                if (_limbs[index] != 0)
                {
                    return (index * 64) + 64 - System.Numerics.BitOperations.LeadingZeroCount(_limbs[index]);
                }
            }

            return 0;
        }
    }

    internal readonly bool IsNegative => _isNegative;

    internal readonly bool IsZero => BitLength == 0;

    private static int GetBitLength(ReadOnlySpan<ulong> value)
    {
        for (var index = value.Length - 1; index >= 0; index--)
        {
            if (value[index] != 0)
            {
                return (index * 64) + 64 - System.Numerics.BitOperations.LeadingZeroCount(value[index]);
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

    private static void SubtractMagnitude(Span<ulong> minuend, ReadOnlySpan<ulong> subtrahend)
    {
        ulong borrow = 0;

        for (var index = 0; index < minuend.Length; index++)
        {
            var right = (UInt128)subtrahend[index] + borrow;
            var left = minuend[index];
            minuend[index] = unchecked((ulong)((UInt128)left - right));
            borrow = (UInt128)left < right ? 1ul : 0ul;
        }

        if (borrow != 0)
        {
            ThrowHelper.ThrowInvalidOperationException("The bounded integer subtraction underflowed.");
        }
    }

    internal void Add(scoped ReadOnlySpan<ulong> other, bool isNegative) => Add(other, isNegative, false);

    internal void Add(scoped in BoundedInteger other) => Add(other._limbs, other._isNegative, false);

    internal readonly void AddMagnitude(scoped in BoundedInteger other)
    {
        if (_isNegative)
        {
            ThrowHelper.ThrowInvalidOperationException("A magnitude accumulator must be nonnegative.");
        }

        AddSameSignMagnitude(other._limbs);
    }

    internal void Clear()
    {
        _limbs.Clear();
        _isNegative = false;
    }

    internal readonly int CompareMagnitudeTo(scoped in BoundedInteger other)
    {
        var comparison = BitLength.CompareTo(other.BitLength);

        if (comparison != 0)
        {
            return comparison;
        }

        for (var index = _limbs.Length - 1; index >= 0; index--)
        {
            comparison = _limbs[index].CompareTo(other._limbs[index]);

            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    internal readonly int CompareTo(scoped in BoundedInteger other)
    {
        if (_isNegative != other._isNegative)
        {
            return _isNegative ? -1 : 1;
        }

        var comparison = CompareMagnitudeTo(other);
        return _isNegative ? -comparison : comparison;
    }

    internal void CopyFrom(scoped ReadOnlySpan<ulong> value, bool isNegative)
    {
        _limbs.Clear();
        value.CopyTo(_limbs);
        _isNegative = isNegative && !IsZero;
    }

    internal void CopyFrom(scoped in BoundedInteger value)
    {
        value._limbs.CopyTo(_limbs);
        _isNegative = value._isNegative;
    }

    internal readonly void CopyMagnitudeTo(Span<ulong> destination)
    {
        destination.Clear();

        if (_limbs.Length > destination.Length)
        {
            for (var index = destination.Length; index < _limbs.Length; index++)
            {
                if (_limbs[index] != 0)
                {
                    ThrowHelper.ThrowInvalidOperationException("The bounded integer destination is too small.");
                }
            }
        }

        _limbs[..Math.Min(_limbs.Length, destination.Length)].CopyTo(destination);
    }

    internal void Multiply(scoped in BoundedInteger left, scoped in BoundedInteger right, Span<ulong> scratch)
    {
        if ((scratch.Length != _limbs.Length) || scratch.Overlaps(_limbs))
        {
            ThrowHelper.ThrowArgumentException("The bounded integer product workspace is invalid.", nameof(scratch));
        }

        scratch.Clear();
        var leftLength = GetLimbLength(left._limbs);
        var rightLength = GetLimbLength(right._limbs);

        for (var leftIndex = 0; leftIndex < leftLength; leftIndex++)
        {
            ulong carry = 0;

            for (var rightIndex = 0; rightIndex < rightLength; rightIndex++)
            {
                var destinationIndex = leftIndex + rightIndex;
                EnsureIndex(destinationIndex);

                var product = ((UInt128)left._limbs[leftIndex] * right._limbs[rightIndex]) + scratch[destinationIndex] + carry;
                scratch[destinationIndex] = unchecked((ulong)product);
                carry = (ulong)(product >> 64);
            }

            var carryIndex = leftIndex + rightLength;

            while (carry != 0)
            {
                EnsureIndex(carryIndex);
                var sum = (UInt128)scratch[carryIndex] + carry;
                scratch[carryIndex] = unchecked((ulong)sum);
                carry = (ulong)(sum >> 64);
                carryIndex++;
            }
        }

        scratch.CopyTo(_limbs);
        _isNegative = !IsZero && (left._isNegative != right._isNegative);
    }

    internal void MultiplySmall(ulong multiplier)
    {
        UInt128 carry = 0;

        for (var index = 0; index < _limbs.Length; index++)
        {
            var product = ((UInt128)_limbs[index] * multiplier) + carry;
            _limbs[index] = unchecked((ulong)product);
            carry = product >> 64;
        }

        if (carry != 0)
        {
            ThrowHelper.ThrowInvalidOperationException("The bounded integer workspace overflowed.");
        }

        if (IsZero)
        {
            _isNegative = false;
        }
    }

    internal void Negate()
    {
        if (!IsZero)
        {
            _isNegative = !_isNegative;
        }
    }

    internal void SetAbsolute(scoped in BoundedInteger value)
    {
        value._limbs.CopyTo(_limbs);
        _isNegative = false;
    }

    internal void SetInt64(long value)
    {
        var negative = value < 0;
        var magnitude = negative ? unchecked((ulong)-(value + 1) + 1) : (ulong)value;

        _limbs.Clear();
        _limbs[0] = magnitude;
        _isNegative = negative && (magnitude != 0);
    }

    internal void SetPowerOfTwo(int bitCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bitCount);
        var limbIndex = bitCount / 64;

        if (limbIndex >= _limbs.Length)
        {
            ThrowHelper.ThrowInvalidOperationException("The bounded integer workspace overflowed.");
        }

        _limbs.Clear();
        _limbs[limbIndex] = 1ul << (bitCount % 64);
        _isNegative = false;
    }

    internal void SetSingle(float value)
    {
        if (!float.IsFinite(value))
        {
            ThrowHelper.ThrowArgumentException("The value must be finite.", nameof(value));
        }

        var bits = BitConverter.SingleToUInt32Bits(value);
        var magnitude = bits & 0x7FFF_FFFF;
        var exponentBits = (int)((magnitude >> 23) & 0xFF);
        var significand = magnitude & 0x007F_FFFF;

        if (exponentBits != 0)
        {
            significand |= 0x0080_0000;
        }

        _limbs.Clear();
        _limbs[0] = significand;

        if (exponentBits > 1)
        {
            ShiftLeft(exponentBits - 1);
        }

        _isNegative = (bits & 0x8000_0000) != 0 && significand != 0;
    }

    internal readonly void ShiftLeft(int bitCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bitCount);

        if (bitCount == 0 || IsZero)
        {
            return;
        }

        if (BitLength + bitCount > _limbs.Length * 64)
        {
            ThrowHelper.ThrowInvalidOperationException("The bounded integer workspace overflowed.");
        }

        var wordShift = bitCount / 64;
        var bitShift = bitCount % 64;

        for (var destinationIndex = _limbs.Length - 1; destinationIndex >= 0; destinationIndex--)
        {
            var sourceIndex = destinationIndex - wordShift;

            if (sourceIndex < 0)
            {
                _limbs[destinationIndex] = 0;
                continue;
            }

            var shifted = _limbs[sourceIndex] << bitShift;

            if (bitShift != 0 && sourceIndex > 0)
            {
                shifted |= _limbs[sourceIndex - 1] >> (64 - bitShift);
            }

            _limbs[destinationIndex] = shifted;
        }
    }

    internal void ShiftRight(int bitCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bitCount);

        if (bitCount == 0 || IsZero)
        {
            return;
        }

        if (bitCount >= BitLength)
        {
            Clear();
            return;
        }

        var wordShift = bitCount / 64;
        var bitShift = bitCount % 64;

        for (var destinationIndex = 0; destinationIndex < _limbs.Length; destinationIndex++)
        {
            var sourceIndex = destinationIndex + wordShift;

            if (sourceIndex >= _limbs.Length)
            {
                _limbs[destinationIndex] = 0;
                continue;
            }

            var shifted = _limbs[sourceIndex] >> bitShift;

            if (bitShift != 0 && sourceIndex + 1 < _limbs.Length)
            {
                shifted |= _limbs[sourceIndex + 1] << (64 - bitShift);
            }

            _limbs[destinationIndex] = shifted;
        }
    }

    internal void Subtract(scoped ReadOnlySpan<ulong> other, bool isNegative) => Add(other, isNegative, true);

    internal void Subtract(scoped in BoundedInteger other) => Add(other._limbs, other._isNegative, true);

    private void Add(scoped ReadOnlySpan<ulong> otherLimbs, bool otherIsNegative, bool negateOther)
    {
        otherIsNegative ^= negateOther;

        if (IsZero)
        {
            otherLimbs.CopyTo(_limbs);
            _isNegative = otherIsNegative && !IsZero;
            return;
        }

        if (GetLimbLength(otherLimbs) == 0)
        {
            return;
        }

        if (_isNegative == otherIsNegative)
        {
            AddSameSignMagnitude(otherLimbs);
            return;
        }

        var comparison = CompareMagnitudeTo(otherLimbs);

        if (comparison == 0)
        {
            _limbs.Clear();
            _isNegative = false;
        }
        else if (comparison > 0)
        {
            SubtractMagnitude(_limbs, otherLimbs);
        }
        else
        {
            SubtractMagnitudeFromOther(otherLimbs, _limbs);
            _isNegative = otherIsNegative;
        }
    }

    private readonly void AddSameSignMagnitude(scoped ReadOnlySpan<ulong> addend)
    {
        UInt128 carry = 0;

        for (var index = 0; index < _limbs.Length; index++)
        {
            var sum = (UInt128)_limbs[index] + addend[index] + carry;
            _limbs[index] = unchecked((ulong)sum);
            carry = sum >> 64;
        }

        if (carry != 0)
        {
            ThrowHelper.ThrowInvalidOperationException("The bounded integer workspace overflowed.");
        }
    }

    private readonly int CompareMagnitudeTo(ReadOnlySpan<ulong> other)
    {
        var comparison = BitLength.CompareTo(GetBitLength(other));

        if (comparison != 0)
        {
            return comparison;
        }

        for (var index = _limbs.Length - 1; index >= 0; index--)
        {
            comparison = _limbs[index].CompareTo(other[index]);

            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    private readonly void EnsureIndex(int index)
    {
        if (index >= _limbs.Length)
        {
            ThrowHelper.ThrowInvalidOperationException("The bounded integer workspace overflowed.");
        }
    }

    private readonly void SubtractMagnitudeFromOther(scoped ReadOnlySpan<ulong> minuend, scoped ReadOnlySpan<ulong> subtrahend)
    {
        ulong borrow = 0;

        for (var index = 0; index < _limbs.Length; index++)
        {
            var right = (UInt128)subtrahend[index] + borrow;
            var left = minuend[index];
            _limbs[index] = unchecked((ulong)((UInt128)left - right));
            borrow = (UInt128)left < right ? 1ul : 0ul;
        }

        if (borrow != 0)
        {
            ThrowHelper.ThrowInvalidOperationException("The bounded integer subtraction underflowed.");
        }
    }
}
