// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace TerraFX.Numerics;

internal static class MagnitudeOrdering
{
    private const uint FastPathUlpLimit = 32;
    private const int MagnitudeLimbCount = 9;

    internal static int Compare(
        System.Numerics.Vector3 left,
        System.Numerics.Vector3 right)
    {
        var leftLengthSquared = left.LengthSquared();
        var rightLengthSquared = right.LengthSquared();

        if (float.IsFinite(leftLengthSquared)
            && float.IsFinite(rightLengthSquared)
            && TryCompareLengthSquared(leftLengthSquared, rightLengthSquared, out var fastComparison))
        {
            return fastComparison;
        }

        return CompareSlow(left.X, left.Y, left.Z, right.X, right.Y, right.Z, leftLengthSquared, rightLengthSquared);
    }

    internal static int Compare(
        Vector128<float> left,
        Vector128<float> right)
    {
        var leftLengthSquared = Vector128.Dot(left, left);
        var rightLengthSquared = Vector128.Dot(right, right);

        if (float.IsFinite(leftLengthSquared)
            && float.IsFinite(rightLengthSquared)
            && TryCompareLengthSquared(leftLengthSquared, rightLengthSquared, out var fastComparison))
        {
            return fastComparison;
        }

        return CompareSlow(left.GetElement(0), left.GetElement(1), left.GetElement(2), left.GetElement(3),
            right.GetElement(0), right.GetElement(1), right.GetElement(2), right.GetElement(3), leftLengthSquared, rightLengthSquared);
    }

    internal static int Compare(System.Numerics.Vector2 left, System.Numerics.Vector2 right)
    {
        var leftLengthSquared = left.LengthSquared();
        var rightLengthSquared = right.LengthSquared();

        if (float.IsFinite(leftLengthSquared)
            && float.IsFinite(rightLengthSquared)
            && TryCompareLengthSquared(leftLengthSquared, rightLengthSquared, out var fastComparison))
        {
            return fastComparison;
        }

        return CompareSlow(left.X, left.Y, right.X, right.Y, leftLengthSquared, rightLengthSquared);
    }

    internal static int Compare(int leftX, int leftY, int leftZ, int rightX, int rightY, int rightZ)
    {
        var leftMagnitudeX = GetMagnitude(leftX);
        var leftMagnitudeY = GetMagnitude(leftY);
        var leftMagnitudeZ = GetMagnitude(leftZ);
        var rightMagnitudeX = GetMagnitude(rightX);
        var rightMagnitudeY = GetMagnitude(rightY);
        var rightMagnitudeZ = GetMagnitude(rightZ);
        var leftMagnitude = ((UInt128)leftMagnitudeX * leftMagnitudeX)
            + ((UInt128)leftMagnitudeY * leftMagnitudeY)
            + ((UInt128)leftMagnitudeZ * leftMagnitudeZ);
        var rightMagnitude = ((UInt128)rightMagnitudeX * rightMagnitudeX)
            + ((UInt128)rightMagnitudeY * rightMagnitudeY)
            + ((UInt128)rightMagnitudeZ * rightMagnitudeZ);
        var magnitudeComparison = leftMagnitude.CompareTo(rightMagnitude);

        if (magnitudeComparison != 0)
        {
            return magnitudeComparison;
        }

        var signComparison = GetSignCode(leftX, leftY, leftZ).CompareTo(GetSignCode(rightX, rightY, rightZ));
        return signComparison != 0 ? signComparison : CompareDirection(leftX, leftY, leftZ, rightX, rightY, rightZ);
    }

    internal static int Compare(int leftX, int leftY, int rightX, int rightY)
    {
        var leftMagnitudeX = GetMagnitude(leftX);
        var leftMagnitudeY = GetMagnitude(leftY);
        var rightMagnitudeX = GetMagnitude(rightX);
        var rightMagnitudeY = GetMagnitude(rightY);
        var leftMagnitude = ((UInt128)leftMagnitudeX * leftMagnitudeX) + ((UInt128)leftMagnitudeY * leftMagnitudeY);
        var rightMagnitude = ((UInt128)rightMagnitudeX * rightMagnitudeX) + ((UInt128)rightMagnitudeY * rightMagnitudeY);
        var magnitudeComparison = leftMagnitude.CompareTo(rightMagnitude);

        if (magnitudeComparison != 0)
        {
            return magnitudeComparison;
        }

        var signComparison = GetSignCode(leftX, leftY).CompareTo(GetSignCode(rightX, rightY));
        return signComparison != 0 ? signComparison : CompareDirection(leftX, leftY, rightX, rightY);
    }

    private static void AddSquare(float value, Span<ulong> magnitude)
    {
        var bits = BitConverter.SingleToUInt32Bits(value) & 0x7FFF_FFFF;
        var exponentBits = (int)((bits >> 23) & 0xFF);
        var significand = (ulong)(bits & 0x007F_FFFF);
        var exponent = -149;

        if (exponentBits != 0)
        {
            significand |= 0x0080_0000;
            exponent = exponentBits - 150;
        }

        var square = (UInt128)significand * significand;
        // Every binary32 square is an integer multiple of 2^-298; nine limbs cover four maximum finite squares.
        var shift = (2 * exponent) + 298;
        var limbIndex = shift / 64;
        var bitOffset = shift % 64;
        var shiftedSquare = square << bitOffset;

        AddWord(magnitude, limbIndex, unchecked((ulong)shiftedSquare));
        AddWord(magnitude, limbIndex + 1, unchecked((ulong)(shiftedSquare >> 64)));
    }

    private static void AddWord(Span<ulong> value, int index, ulong word)
    {
        while (word != 0)
        {
            var previous = value[index];
            value[index] = unchecked(previous + word);

            if (value[index] >= previous)
            {
                return;
            }

            word = 1;
            index++;
        }
    }

    private static int CompareAbsoluteComponents(
        float leftX,
        float leftY,
        float leftZ,
        float leftW,
        float rightX,
        float rightY,
        float rightZ,
        float rightW)
    {
        var comparison = float.Abs(leftX).CompareTo(float.Abs(rightX));

        if (comparison != 0)
        {
            return comparison;
        }

        comparison = float.Abs(leftY).CompareTo(float.Abs(rightY));

        if (comparison != 0)
        {
            return comparison;
        }

        comparison = float.Abs(leftZ).CompareTo(float.Abs(rightZ));
        return comparison != 0 ? comparison : float.Abs(leftW).CompareTo(float.Abs(rightW));
    }

    private static int CompareAbsoluteComponents(float leftX, float leftY, float rightX, float rightY)
    {
        var comparison = float.Abs(leftX).CompareTo(float.Abs(rightX));
        return comparison != 0 ? comparison : float.Abs(leftY).CompareTo(float.Abs(rightY));
    }

    private static int CompareAbsoluteComponents(int leftX, int leftY, int rightX, int rightY)
    {
        var comparison = GetMagnitude(leftX).CompareTo(GetMagnitude(rightX));
        return comparison != 0 ? comparison : GetMagnitude(leftY).CompareTo(GetMagnitude(rightY));
    }

    private static int CompareComponents(
        float leftX,
        float leftY,
        float leftZ,
        float leftW,
        float rightX,
        float rightY,
        float rightZ,
        float rightW)
    {
        var comparison = leftX.CompareTo(rightX);

        if (comparison != 0)
        {
            return comparison;
        }

        comparison = leftY.CompareTo(rightY);

        if (comparison != 0)
        {
            return comparison;
        }

        comparison = leftZ.CompareTo(rightZ);
        return comparison != 0 ? comparison : leftW.CompareTo(rightW);
    }

    private static int CompareComponents(float leftX, float leftY, float leftZ, float rightX, float rightY, float rightZ)
    {
        var comparison = leftX.CompareTo(rightX);

        if (comparison != 0)
        {
            return comparison;
        }

        comparison = leftY.CompareTo(rightY);
        return comparison != 0 ? comparison : leftZ.CompareTo(rightZ);
    }

    private static int CompareComponents(float leftX, float leftY, float rightX, float rightY)
    {
        var comparison = leftX.CompareTo(rightX);
        return comparison != 0 ? comparison : leftY.CompareTo(rightY);
    }

    private static int CompareDirection(
        float leftX,
        float leftY,
        float leftZ,
        float leftW,
        float rightX,
        float rightY,
        float rightZ,
        float rightW)
    {
        var comparison = float.Abs(rightX).CompareTo(float.Abs(leftX));

        if (comparison != 0)
        {
            return comparison;
        }

        comparison = float.Abs(rightY).CompareTo(float.Abs(leftY));

        if (comparison != 0)
        {
            return comparison;
        }

        var pairComparison = CompareDirectionPair(leftZ, leftW, rightZ, rightW);
        return pairComparison != 0 ? pairComparison : CompareAbsoluteComponents(leftX, leftY, leftZ, leftW, rightX, rightY, rightZ, rightW);
    }

    private static int CompareDirection(float leftX, float leftY, float leftZ, float rightX, float rightY, float rightZ)
    {
        var comparison = float.Abs(rightX).CompareTo(float.Abs(leftX));
        return comparison != 0 ? comparison : CompareDirectionPair(leftY, leftZ, rightY, rightZ);
    }

    private static int CompareDirection(float leftX, float leftY, float rightX, float rightY)
    {
        var pairComparison = CompareDirectionPair(leftX, leftY, rightX, rightY);
        return pairComparison != 0 ? pairComparison : CompareAbsoluteComponents(leftX, leftY, rightX, rightY);
    }

    private static int CompareDirection(int leftX, int leftY, int leftZ, int rightX, int rightY, int rightZ)
    {
        var comparison = GetMagnitude(rightX).CompareTo(GetMagnitude(leftX));
        return comparison != 0 ? comparison : CompareDirectionPair(leftY, leftZ, rightY, rightZ);
    }

    private static int CompareDirection(int leftX, int leftY, int rightX, int rightY)
    {
        var pairComparison = CompareDirectionPair(leftX, leftY, rightX, rightY);
        return pairComparison != 0 ? pairComparison : CompareAbsoluteComponents(leftX, leftY, rightX, rightY);
    }

    private static int CompareDirectionPair(float leftFirst, float leftSecond, float rightFirst, float rightSecond)
    {
        var leftMagnitude = float.Abs(leftFirst);
        var rightMagnitude = float.Abs(rightFirst);
        return HasOddNegativeSign(leftFirst, leftSecond)
            ? leftMagnitude.CompareTo(rightMagnitude)
            : rightMagnitude.CompareTo(leftMagnitude);
    }

    private static int CompareDirectionPair(int leftFirst, int leftSecond, int rightFirst, int rightSecond)
    {
        var leftMagnitude = GetMagnitude(leftFirst);
        var rightMagnitude = GetMagnitude(rightFirst);
        return HasOddNegativeSign(leftFirst, leftSecond)
            ? leftMagnitude.CompareTo(rightMagnitude)
            : rightMagnitude.CompareTo(leftMagnitude);
    }

    private static int CompareFloatTieBreak(
        float leftX,
        float leftY,
        float leftZ,
        float leftW,
        float rightX,
        float rightY,
        float rightZ,
        float rightW)
    {
        var signComparison = GetSignCode(leftX, leftY, leftZ, leftW).CompareTo(GetSignCode(rightX, rightY, rightZ, rightW));
        return signComparison != 0
            ? signComparison
            : CompareDirection(leftX, leftY, leftZ, leftW, rightX, rightY, rightZ, rightW);
    }

    private static int CompareFloatTieBreak(float leftX, float leftY, float leftZ, float rightX, float rightY, float rightZ)
    {
        var signComparison = GetSignCode(leftX, leftY, leftZ).CompareTo(GetSignCode(rightX, rightY, rightZ));
        return signComparison != 0 ? signComparison : CompareDirection(leftX, leftY, leftZ, rightX, rightY, rightZ);
    }

    private static int CompareFloatTieBreak(float leftX, float leftY, float rightX, float rightY)
    {
        var signComparison = GetSignCode(leftX, leftY).CompareTo(GetSignCode(rightX, rightY));
        return signComparison != 0 ? signComparison : CompareDirection(leftX, leftY, rightX, rightY);
    }

    private static int CompareMagnitude(ReadOnlySpan<ulong> left, ReadOnlySpan<ulong> right)
    {
        for (var index = MagnitudeLimbCount - 1; index >= 0; index--)
        {
            var comparison = left[index].CompareTo(right[index]);

            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    private static int CompareSlow(
        float leftX,
        float leftY,
        float leftZ,
        float leftW,
        float rightX,
        float rightY,
        float rightZ,
        float rightW,
        float leftLengthSquared,
        float rightLengthSquared)
    {
        if (!float.IsFinite(leftLengthSquared) || !float.IsFinite(rightLengthSquared))
        {
            var leftHasNaN = float.IsNaN(leftX) || float.IsNaN(leftY) || float.IsNaN(leftZ) || float.IsNaN(leftW);
            var rightHasNaN = float.IsNaN(rightX) || float.IsNaN(rightY) || float.IsNaN(rightZ) || float.IsNaN(rightW);

            if (leftHasNaN || rightHasNaN)
            {
                if (leftHasNaN != rightHasNaN)
                {
                    return leftHasNaN ? 1 : -1;
                }

                return CompareComponents(leftX, leftY, leftZ, leftW, rightX, rightY, rightZ, rightW);
            }

            var leftHasInfinity = float.IsInfinity(leftX) || float.IsInfinity(leftY) || float.IsInfinity(leftZ) || float.IsInfinity(leftW);
            var rightHasInfinity = float.IsInfinity(rightX) || float.IsInfinity(rightY) || float.IsInfinity(rightZ) || float.IsInfinity(rightW);

            if (leftHasInfinity != rightHasInfinity)
            {
                return leftHasInfinity ? 1 : -1;
            }

            if (leftHasInfinity)
            {
                return CompareFloatTieBreak(leftX, leftY, leftZ, leftW, rightX, rightY, rightZ, rightW);
            }
        }

        Unsafe.SkipInit(out InlineArray16<ulong> leftMagnitudeStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> rightMagnitudeStorage);
        var leftMagnitude = leftMagnitudeStorage[..MagnitudeLimbCount];
        var rightMagnitude = rightMagnitudeStorage[..MagnitudeLimbCount];
        leftMagnitude.Clear();
        rightMagnitude.Clear();
        AddSquare(leftX, leftMagnitude);
        AddSquare(leftY, leftMagnitude);
        AddSquare(leftZ, leftMagnitude);
        AddSquare(leftW, leftMagnitude);
        AddSquare(rightX, rightMagnitude);
        AddSquare(rightY, rightMagnitude);
        AddSquare(rightZ, rightMagnitude);
        AddSquare(rightW, rightMagnitude);

        var magnitudeComparison = CompareMagnitude(leftMagnitude, rightMagnitude);
        return magnitudeComparison != 0
            ? magnitudeComparison
            : CompareFloatTieBreak(leftX, leftY, leftZ, leftW, rightX, rightY, rightZ, rightW);
    }

    private static int CompareSlow(
        float leftX,
        float leftY,
        float leftZ,
        float rightX,
        float rightY,
        float rightZ,
        float leftLengthSquared,
        float rightLengthSquared)
    {
        if (!float.IsFinite(leftLengthSquared) || !float.IsFinite(rightLengthSquared))
        {
            var leftHasNaN = float.IsNaN(leftX) || float.IsNaN(leftY) || float.IsNaN(leftZ);
            var rightHasNaN = float.IsNaN(rightX) || float.IsNaN(rightY) || float.IsNaN(rightZ);

            if (leftHasNaN || rightHasNaN)
            {
                if (leftHasNaN != rightHasNaN)
                {
                    return leftHasNaN ? 1 : -1;
                }

                return CompareComponents(leftX, leftY, leftZ, rightX, rightY, rightZ);
            }

            var leftHasInfinity = float.IsInfinity(leftX) || float.IsInfinity(leftY) || float.IsInfinity(leftZ);
            var rightHasInfinity = float.IsInfinity(rightX) || float.IsInfinity(rightY) || float.IsInfinity(rightZ);

            if (leftHasInfinity != rightHasInfinity)
            {
                return leftHasInfinity ? 1 : -1;
            }

            if (leftHasInfinity)
            {
                return CompareFloatTieBreak(leftX, leftY, leftZ, rightX, rightY, rightZ);
            }
        }

        Unsafe.SkipInit(out InlineArray16<ulong> leftMagnitudeStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> rightMagnitudeStorage);
        var leftMagnitude = leftMagnitudeStorage[..MagnitudeLimbCount];
        var rightMagnitude = rightMagnitudeStorage[..MagnitudeLimbCount];
        leftMagnitude.Clear();
        rightMagnitude.Clear();
        AddSquare(leftX, leftMagnitude);
        AddSquare(leftY, leftMagnitude);
        AddSquare(leftZ, leftMagnitude);
        AddSquare(rightX, rightMagnitude);
        AddSquare(rightY, rightMagnitude);
        AddSquare(rightZ, rightMagnitude);

        var magnitudeComparison = CompareMagnitude(leftMagnitude, rightMagnitude);
        return magnitudeComparison != 0 ? magnitudeComparison : CompareFloatTieBreak(leftX, leftY, leftZ, rightX, rightY, rightZ);
    }

    private static int CompareSlow(
        float leftX,
        float leftY,
        float rightX,
        float rightY,
        float leftLengthSquared,
        float rightLengthSquared)
    {
        if (!float.IsFinite(leftLengthSquared) || !float.IsFinite(rightLengthSquared))
        {
            var leftHasNaN = float.IsNaN(leftX) || float.IsNaN(leftY);
            var rightHasNaN = float.IsNaN(rightX) || float.IsNaN(rightY);

            if (leftHasNaN || rightHasNaN)
            {
                if (leftHasNaN != rightHasNaN)
                {
                    return leftHasNaN ? 1 : -1;
                }

                return CompareComponents(leftX, leftY, rightX, rightY);
            }

            var leftHasInfinity = float.IsInfinity(leftX) || float.IsInfinity(leftY);
            var rightHasInfinity = float.IsInfinity(rightX) || float.IsInfinity(rightY);

            if (leftHasInfinity != rightHasInfinity)
            {
                return leftHasInfinity ? 1 : -1;
            }

            if (leftHasInfinity)
            {
                return CompareFloatTieBreak(leftX, leftY, rightX, rightY);
            }
        }

        Unsafe.SkipInit(out InlineArray16<ulong> leftMagnitudeStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> rightMagnitudeStorage);
        var leftMagnitude = leftMagnitudeStorage[..MagnitudeLimbCount];
        var rightMagnitude = rightMagnitudeStorage[..MagnitudeLimbCount];
        leftMagnitude.Clear();
        rightMagnitude.Clear();
        AddSquare(leftX, leftMagnitude);
        AddSquare(leftY, leftMagnitude);
        AddSquare(rightX, rightMagnitude);
        AddSquare(rightY, rightMagnitude);

        var magnitudeComparison = CompareMagnitude(leftMagnitude, rightMagnitude);
        return magnitudeComparison != 0 ? magnitudeComparison : CompareFloatTieBreak(leftX, leftY, rightX, rightY);
    }

    private static uint GetMagnitude(int value) => value < 0 ? (uint)-(long)value : (uint)value;

    private static int GetSignCode(float x, float y) => (IsNonnegativeOrZero(x) ? 1 : 0) | (IsNonnegativeOrZero(y) ? 2 : 0);

    private static int GetSignCode(float x, float y, float z) =>
        (IsNonnegativeOrZero(x) ? 1 : 0) | (IsNonnegativeOrZero(y) ? 2 : 0) | (IsNonnegativeOrZero(z) ? 4 : 0);

    private static int GetSignCode(float x, float y, float z, float w) =>
        (IsNonnegativeOrZero(x) ? 1 : 0)
        | (IsNonnegativeOrZero(y) ? 2 : 0)
        | (IsNonnegativeOrZero(z) ? 4 : 0)
        | (IsNonnegativeOrZero(w) ? 8 : 0);

    private static int GetSignCode(int x, int y) => (x >= 0 ? 1 : 0) | (y >= 0 ? 2 : 0);

    private static int GetSignCode(int x, int y, int z) => (x >= 0 ? 1 : 0) | (y >= 0 ? 2 : 0) | (z >= 0 ? 4 : 0);

    private static bool HasOddNegativeSign(float first, float second) =>
        (float.IsNegative(first) && (first != 0.0f)) != (float.IsNegative(second) && (second != 0.0f));

    private static bool HasOddNegativeSign(int first, int second) => (first < 0) != (second < 0);

    private static bool IsNonnegativeOrZero(float value) => !float.IsNegative(value) || (value == 0.0f);

    private static bool TryCompareLengthSquared(float left, float right, out int comparison)
    {
        comparison = 0;

        if (left == right)
        {
            return false;
        }

        var leftBits = BitConverter.SingleToUInt32Bits(left);
        var rightBits = BitConverter.SingleToUInt32Bits(right);
        var difference = leftBits > rightBits ? leftBits - rightBits : rightBits - leftBits;

        // Four nonnegative products and three additions contribute at most 3.5 result ulps,
        // including gradual underflow. A >32 encoding-ulp gap exceeds both error bounds
        // even across a binade boundary, so component normality checks are unnecessary.
        if (difference <= FastPathUlpLimit)
        {
            return false;
        }

        comparison = left < right ? -1 : 1;
        return true;
    }
}
