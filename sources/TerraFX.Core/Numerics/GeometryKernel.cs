// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace TerraFX.Numerics;

internal static class GeometryKernel
{
    /// <summary>Checks whether the exact inverse residual is within the declared tolerance.</summary>
    internal static bool InverseResidual(ReadOnlySpan<float> left, ReadOnlySpan<float> right, int dimension)
    {
        for (var row = 0; row < dimension; row++)
        {
            for (var column = 0; column < dimension; column++)
            {
                if (TryInverseResidualFast(left, right, dimension, row, column, out var accepted))
                {
                    if (!accepted)
                    {
                        return false;
                    }
                }
                else if (!InverseResidualExact(left, right, dimension, row, column))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Checks whether a recomposed matrix is within the declared relative residual tolerance.</summary>
    internal static bool RecompositionResidual(Matrix4x4 original, Matrix4x4 recomposed)
    {
        Unsafe.SkipInit(out InlineArray16<ulong> maximumStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> valueStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> recomposedValueStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> residualStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> residualMagnitudeStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> scaledResidualStorage);
        Span<ulong> maximumLimbs = maximumStorage;

        var maximum = new BoundedInteger(maximumLimbs);
        var value = new BoundedInteger(valueStorage);
        var recomposedValue = new BoundedInteger(recomposedValueStorage);
        var residual = new BoundedInteger(residualStorage);
        var residualMagnitude = new BoundedInteger(residualMagnitudeStorage);
        var scaledResidual = new BoundedInteger(scaledResidualStorage);

        maximum.SetPowerOfTwo(149);

        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                value.SetSingle(original[row, column]);
                residualMagnitude.SetAbsolute(value);

                if (residualMagnitude.CompareMagnitudeTo(maximum) > 0)
                {
                    maximum.CopyFrom(residualMagnitude);
                }
            }
        }

        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                value.SetSingle(original[row, column]);
                recomposedValue.SetSingle(recomposed[row, column]);
                value.Subtract(recomposedValue);
                residual.SetAbsolute(value);
                scaledResidual.CopyFrom(residual);
                scaledResidual.MultiplySmall(100000);

                if (scaledResidual.CompareMagnitudeTo(maximum) > 0)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Computes a quaternion inverse using bounded exact rational arithmetic.</summary>
    internal static bool TryInvertQuaternion(Quaternion value, out Quaternion inverse)
    {
        Unsafe.SkipInit(out InlineArray16<ulong> xStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> yStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> zStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> wStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> denominatorStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> termStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> numeratorStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> scratchStorage);
        Span<ulong> scratch = scratchStorage;

        var x = new BoundedInteger(xStorage);
        var y = new BoundedInteger(yStorage);
        var z = new BoundedInteger(zStorage);
        var w = new BoundedInteger(wStorage);
        var denominator = new BoundedInteger(denominatorStorage);
        var term = new BoundedInteger(termStorage);
        var numerator = new BoundedInteger(numeratorStorage);

        x.SetSingle(value.X);
        y.SetSingle(value.Y);
        z.SetSingle(value.Z);
        w.SetSingle(value.W);

        term.Multiply(x, x, scratch);
        denominator.CopyFrom(term);

        term.Multiply(y, y, scratch);
        denominator.Add(term);

        term.Multiply(z, z, scratch);
        denominator.Add(term);

        term.Multiply(w, w, scratch);
        denominator.Add(term);

        numerator.CopyFrom(x);
        numerator.ShiftLeft(149);
        numerator.Negate();

        if (!TryRound(numerator, denominator, out var ix, BitConverter.SingleToInt32Bits(value.X) >= 0))
        {
            inverse = default;
            return false;
        }

        numerator.CopyFrom(y);
        numerator.ShiftLeft(149);
        numerator.Negate();

        if (!TryRound(numerator, denominator, out var iy, BitConverter.SingleToInt32Bits(value.Y) >= 0))
        {
            inverse = default;
            return false;
        }

        numerator.CopyFrom(z);
        numerator.ShiftLeft(149);
        numerator.Negate();

        if (!TryRound(numerator, denominator, out var iz, BitConverter.SingleToInt32Bits(value.Z) >= 0))
        {
            inverse = default;
            return false;
        }

        numerator.CopyFrom(w);
        numerator.ShiftLeft(149);

        if (!TryRound(numerator, denominator, out var iw, BitConverter.SingleToInt32Bits(value.W) < 0))
        {
            inverse = default;
            return false;
        }

        inverse = new Quaternion(ix, iy, iz, iw);
        return true;
    }

    /// <summary>Builds a view matrix from a position, target, and up vector.</summary>
    internal static bool TryLookAt(Vector3 position, Vector3 target, Vector3 up, out Matrix4x4 view, out string failureParameter)
    {
        Unsafe.SkipInit(out InlineArray16<ulong> dxStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> dyStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> dzStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> positionComponentStorage);

        var dx = new BoundedInteger(dxStorage);
        var dy = new BoundedInteger(dyStorage);
        var dz = new BoundedInteger(dzStorage);
        var positionComponent = new BoundedInteger(positionComponentStorage);

        dx.SetSingle(target.X);
        positionComponent.SetSingle(position.X);
        dx.Subtract(positionComponent);

        dy.SetSingle(target.Y);
        positionComponent.SetSingle(position.Y);
        dy.Subtract(positionComponent);

        dz.SetSingle(target.Z);
        positionComponent.SetSingle(position.Z);
        dz.Subtract(positionComponent);

        return TryLook(position, dx, dy, dz, up, nameof(target), out view, out failureParameter);
    }

    /// <summary>Builds a view matrix from a position, direction, and up vector.</summary>
    internal static bool TryLookTo(Vector3 position, Vector3 direction, Vector3 up, out Matrix4x4 view, out string failureParameter)
    {
        Unsafe.SkipInit(out InlineArray16<ulong> dxStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> dyStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> dzStorage);

        var dx = new BoundedInteger(dxStorage);
        var dy = new BoundedInteger(dyStorage);
        var dz = new BoundedInteger(dzStorage);

        dx.SetSingle(direction.X);
        dy.SetSingle(direction.Y);
        dz.SetSingle(direction.Z);

        return TryLook(position, dx, dy, dz, up, nameof(direction), out view, out failureParameter);
    }

    /// <summary>Normalizes four packed components after scaling by their largest magnitude.</summary>
    internal static bool TryNormalize(Vector128<float> value, out Vector128<float> normalized)
    {
        var magnitude = Vector128.Abs(value);
        var maximum = float.Max(float.Max(magnitude.GetElement(0), magnitude.GetElement(1)), float.Max(magnitude.GetElement(2), magnitude.GetElement(3)));

        if (maximum == 0.0f)
        {
            normalized = default;
            return false;
        }

        var scaled = value / Vector128.Create(maximum);
        var squares = scaled * scaled;
        // Retain the declared X/Y/Z/W reduction order rather than a pairwise SIMD dot.
        var sum = squares.GetElement(0) + squares.GetElement(1) + squares.GetElement(2) + squares.GetElement(3);
        normalized = scaled / Vector128.Create(float.Sqrt(sum));

        return true;
    }

    /// <summary>Computes the orthographic depth scale and offset with bounded exact arithmetic.</summary>
    internal static bool TryOrthographicDepth(float nearPlane, float farPlane, out float depth, out float offset)
    {
        Unsafe.SkipInit(out InlineArray16<ulong> nearStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> farStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> differenceStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> numeratorStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> oneStorage);

        var near = new BoundedInteger(nearStorage);
        var far = new BoundedInteger(farStorage);
        var difference = new BoundedInteger(differenceStorage);
        var numerator = new BoundedInteger(numeratorStorage);
        var one = new BoundedInteger(oneStorage);

        near.SetSingle(nearPlane);
        far.SetSingle(farPlane);

        difference.CopyFrom(far);
        difference.Subtract(near);

        one.SetPowerOfTwo(149);
        numerator.CopyFrom(one);
        numerator.Negate();

        if (!TryRound(numerator, difference, out depth))
        {
            offset = default;
            return false;
        }

        return TryRound(far, difference, out offset);
    }

    /// <summary>Computes the perspective depth scale and offset with bounded exact arithmetic.</summary>
    internal static bool TryPerspectiveDepth(float nearPlane, float farPlane, out float depth, out float offset)
    {
        Unsafe.SkipInit(out InlineArray16<ulong> nearStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> farStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> differenceStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> numeratorStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> productStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> scaledDenominatorStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> scratchStorage);
        Span<ulong> scratch = scratchStorage;

        var near = new BoundedInteger(nearStorage);
        var far = new BoundedInteger(farStorage);
        var difference = new BoundedInteger(differenceStorage);
        var numerator = new BoundedInteger(numeratorStorage);
        var product = new BoundedInteger(productStorage);
        var scaledDenominator = new BoundedInteger(scaledDenominatorStorage);

        near.SetSingle(nearPlane);
        far.SetSingle(farPlane);

        difference.CopyFrom(far);
        difference.Subtract(near);
        numerator.CopyFrom(near);
        numerator.Negate();

        if (!TryRound(numerator, difference, out depth))
        {
            offset = default;
            return false;
        }

        product.Multiply(far, near, scratch);
        scaledDenominator.CopyFrom(difference);
        scaledDenominator.ShiftLeft(149);
        return TryRound(product, scaledDenominator, out offset);
    }

    /// <summary>Computes the horizontal and vertical perspective projection scales.</summary>
    internal static bool TryPerspectiveScale(float aspectRatio, float tangent, out Vector2 scale)
    {
        Unsafe.SkipInit(out InlineArray16<ulong> aspectStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> tangentStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> productStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> xNumeratorStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> yNumeratorStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> scratchStorage);
        Span<ulong> scratch = scratchStorage;

        var aspect = new BoundedInteger(aspectStorage);
        var tangentValue = new BoundedInteger(tangentStorage);
        var product = new BoundedInteger(productStorage);
        var xNumerator = new BoundedInteger(xNumeratorStorage);
        var yNumerator = new BoundedInteger(yNumeratorStorage);

        aspect.SetSingle(aspectRatio);
        tangentValue.SetSingle(tangent);

        product.Multiply(aspect, tangentValue, scratch);
        xNumerator.SetPowerOfTwo(298);
        yNumerator.SetPowerOfTwo(149);

        if (!TryRound(xNumerator, product, out var x) || !TryRound(yNumerator, tangentValue, out var y))
        {
            scale = default;
            return false;
        }

        scale = new Vector2(x, y);
        return true;
    }

    /// <summary>Rounds a bounded signed rational to binary32.</summary>
    internal static bool TryRound(scoped in BoundedInteger numerator, scoped in BoundedInteger denominator, out float result, bool negativeZero = false)
    {
        Unsafe.SkipInit(out InlineArray16<ulong> numeratorStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> denominatorStorage);
        Span<ulong> numeratorLimbs = numeratorStorage;
        Span<ulong> denominatorLimbs = denominatorStorage;

        numerator.CopyMagnitudeTo(numeratorLimbs);
        denominator.CopyMagnitudeTo(denominatorLimbs);

        if (denominator.IsNegative || denominator.IsZero)
        {
            ThrowHelper.ThrowInvalidOperationException("The bounded geometry denominator must be positive.");
        }

        Unsafe.SkipInit(out InlineBuffer64<ulong> workspaceStorage); // SetFraction fills operands; TryRound initializes its scratch.

        Span<ulong> workspace = workspaceStorage;
        var rational = new BoundedRational(workspace);
        rational.SetFraction(numeratorLimbs, denominatorLimbs, numerator.IsNegative);
        return rational.TryRoundToSingle(out result, negativeZero);
    }

    private static void GetAbsoluteBounds(double lower, double upper, out double absoluteLower, out double absoluteUpper)
    {
        if (lower > 0.0)
        {
            absoluteLower = double.BitDecrement(lower);
            absoluteUpper = double.BitIncrement(upper);
        }
        else if (upper < 0.0)
        {
            absoluteLower = double.BitDecrement(-upper);
            absoluteUpper = double.BitIncrement(-lower);
        }
        else
        {
            absoluteLower = 0.0;
            absoluteUpper = double.BitIncrement(double.Max(double.Abs(lower), double.Abs(upper)));
        }
    }

    private static bool InverseResidualExact(ReadOnlySpan<float> left, ReadOnlySpan<float> right, int dimension, int row, int column)
    {
        Unsafe.SkipInit(out InlineArray16<ulong> leftStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> rightStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> termStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> scratchStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> productStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> magnitudeStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> residualMagnitudeStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> oneStorage);
        Span<ulong> scratch = scratchStorage;

        var leftValue = new BoundedInteger(leftStorage);
        var rightValue = new BoundedInteger(rightStorage);
        var term = new BoundedInteger(termStorage);
        var product = new BoundedInteger(productStorage);
        var magnitude = new BoundedInteger(magnitudeStorage);
        var residualMagnitude = new BoundedInteger(residualMagnitudeStorage);
        var one = new BoundedInteger(oneStorage);

        one.SetPowerOfTwo(298);

        for (var index = 0; index < dimension; index++)
        {
            leftValue.SetSingle(left[(row * dimension) + index]);
            rightValue.SetSingle(right[(index * dimension) + column]);
            term.Multiply(leftValue, rightValue, scratch);
            product.Add(term);
            magnitude.AddMagnitude(term);
        }

        if (row == column)
        {
            product.Subtract(one);
        }

        residualMagnitude.SetAbsolute(product);
        residualMagnitude.MultiplySmall(100000);

        if (residualMagnitude.CompareMagnitudeTo(one) <= 0)
        {
            return true;
        }

        // Reuse the absolute-floor scale: 5 * 100000 is the exact relative denominator.
        residualMagnitude.MultiplySmall(5);
        return residualMagnitude.CompareMagnitudeTo(magnitude) <= 0;
    }

    private static bool IsProperFrame(Vector3 right, Vector3 up, Vector3 forward)
    {
        if (!right.IsFinite().All() || !up.IsFinite().All() || !forward.IsFinite().All())
        {
            return false;
        }

        Unsafe.SkipInit(out InlineArray16<ulong> crossXStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> crossYStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> crossZStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> determinantStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> rightComponentStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> termStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> scratchStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> oneStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> determinantOneStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> residualStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> residualMagnitudeStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> scaledResidualStorage);
        Span<ulong> scratch = scratchStorage;

        var crossX = new BoundedInteger(crossXStorage);
        var crossY = new BoundedInteger(crossYStorage);
        var crossZ = new BoundedInteger(crossZStorage);
        var determinant = new BoundedInteger(determinantStorage);
        var rightComponent = new BoundedInteger(rightComponentStorage);
        var term = new BoundedInteger(termStorage);
        var one = new BoundedInteger(oneStorage);
        var determinantOne = new BoundedInteger(determinantOneStorage);
        var residual = new BoundedInteger(residualStorage);
        var residualMagnitude = new BoundedInteger(residualMagnitudeStorage);
        var scaledResidual = new BoundedInteger(scaledResidualStorage);

        one.SetPowerOfTwo(298);

        if (!IsWithinDotTolerance(right, right, true, one)
            || !IsWithinDotTolerance(up, up, true, one)
            || !IsWithinDotTolerance(forward, forward, true, one)
            || !IsWithinDotTolerance(right, up, false, one)
            || !IsWithinDotTolerance(right, forward, false, one)
            || !IsWithinDotTolerance(up, forward, false, one))
        {
            return false;
        }

        SetCross3(ref crossX, ref crossY, ref crossZ, up, forward);

        rightComponent.SetSingle(right.X);
        term.Multiply(rightComponent, crossX, scratch);
        determinant.CopyFrom(term);

        rightComponent.SetSingle(right.Y);
        term.Multiply(rightComponent, crossY, scratch);
        determinant.Add(term);

        rightComponent.SetSingle(right.Z);
        term.Multiply(rightComponent, crossZ, scratch);
        determinant.Add(term);

        determinantOne.SetPowerOfTwo(447);

        if (determinant.IsNegative)
        {
            return false;
        }

        residual.CopyFrom(determinant);
        residual.Subtract(determinantOne);
        residualMagnitude.SetAbsolute(residual);
        scaledResidual.CopyFrom(residualMagnitude);
        scaledResidual.MultiplySmall(500000);
        return scaledResidual.CompareMagnitudeTo(determinantOne) <= 0;
    }

    private static bool IsWithinDotTolerance(Vector3 left, Vector3 right, bool compareToOne, scoped in BoundedInteger one)
    {
        Unsafe.SkipInit(out InlineArray16<ulong> dotStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> residualMagnitudeStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> termStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> leftStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> rightStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> scratchStorage);
        Span<ulong> scratch = scratchStorage;

        var dot = new BoundedInteger(dotStorage);
        var residualMagnitude = new BoundedInteger(residualMagnitudeStorage);
        var term = new BoundedInteger(termStorage);
        var leftValue = new BoundedInteger(leftStorage);
        var rightValue = new BoundedInteger(rightStorage);

        leftValue.SetSingle(left.X);
        rightValue.SetSingle(right.X);
        term.Multiply(leftValue, rightValue, scratch);
        dot.CopyFrom(term);

        leftValue.SetSingle(left.Y);
        rightValue.SetSingle(right.Y);
        term.Multiply(leftValue, rightValue, scratch);
        dot.Add(term);

        leftValue.SetSingle(left.Z);
        rightValue.SetSingle(right.Z);
        term.Multiply(leftValue, rightValue, scratch);
        dot.Add(term);

        if (compareToOne)
        {
            dot.Subtract(one);
        }

        residualMagnitude.SetAbsolute(dot);
        residualMagnitude.MultiplySmall(500000);
        return residualMagnitude.CompareMagnitudeTo(one) <= 0;
    }

    private static Vector3 Scaled32(scoped in BoundedInteger x, scoped in BoundedInteger y, scoped in BoundedInteger z)
    {
        var bits = Math.Max(x.BitLength, Math.Max(y.BitLength, z.BitLength));

        Unsafe.SkipInit(out InlineArray16<ulong> denominatorStorage);
        var denominator = new BoundedInteger(denominatorStorage);
        denominator.SetPowerOfTwo(bits);

        if (!TryRound(x, denominator, out var sx))
        {
            ThrowHelper.ThrowInvalidOperationException("A bounded scaled direction must be representable.");
        }

        if (!TryRound(y, denominator, out var sy))
        {
            ThrowHelper.ThrowInvalidOperationException("A bounded scaled direction must be representable.");
        }

        if (!TryRound(z, denominator, out var sz))
        {
            ThrowHelper.ThrowInvalidOperationException("A bounded scaled direction must be representable.");
        }

        return new Vector3(sx, sy, sz);
    }

    private static void SetCross3(scoped ref BoundedInteger crossX, scoped ref BoundedInteger crossY, scoped ref BoundedInteger crossZ, Vector3 left, Vector3 right)
    {
        Unsafe.SkipInit(out InlineArray16<ulong> leftStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> rightStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> termStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> scratchStorage);
        Span<ulong> scratch = scratchStorage;

        var leftValue = new BoundedInteger(leftStorage);
        var rightValue = new BoundedInteger(rightStorage);
        var term = new BoundedInteger(termStorage);

        leftValue.SetSingle(left.Y);
        rightValue.SetSingle(right.Z);
        term.Multiply(leftValue, rightValue, scratch);
        crossX.CopyFrom(termStorage, term.IsNegative);

        leftValue.SetSingle(left.Z);
        rightValue.SetSingle(right.Y);
        term.Multiply(leftValue, rightValue, scratch);
        crossX.Subtract(termStorage, term.IsNegative);

        leftValue.SetSingle(left.Z);
        rightValue.SetSingle(right.X);
        term.Multiply(leftValue, rightValue, scratch);
        crossY.CopyFrom(termStorage, term.IsNegative);

        leftValue.SetSingle(left.X);
        rightValue.SetSingle(right.Z);
        term.Multiply(leftValue, rightValue, scratch);
        crossY.Subtract(termStorage, term.IsNegative);

        leftValue.SetSingle(left.X);
        rightValue.SetSingle(right.Y);
        term.Multiply(leftValue, rightValue, scratch);
        crossZ.CopyFrom(termStorage, term.IsNegative);

        leftValue.SetSingle(left.Y);
        rightValue.SetSingle(right.X);
        term.Multiply(leftValue, rightValue, scratch);
        crossZ.Subtract(termStorage, term.IsNegative);
    }

    private static void SetCross3(scoped ref BoundedInteger crossX, scoped ref BoundedInteger crossY, scoped ref BoundedInteger crossZ, scoped in BoundedInteger leftX, scoped in BoundedInteger leftY, scoped in BoundedInteger leftZ, scoped in BoundedInteger rightX, scoped in BoundedInteger rightY, scoped in BoundedInteger rightZ, Span<ulong> scratch, scoped ref BoundedInteger firstProduct, scoped ref BoundedInteger secondProduct)
    {
        firstProduct.Multiply(leftY, rightZ, scratch);
        secondProduct.Multiply(leftZ, rightY, scratch);
        crossX.CopyFrom(firstProduct);
        crossX.Subtract(secondProduct);

        firstProduct.Multiply(leftZ, rightX, scratch);
        secondProduct.Multiply(leftX, rightZ, scratch);
        crossY.CopyFrom(firstProduct);
        crossY.Subtract(secondProduct);

        firstProduct.Multiply(leftX, rightY, scratch);
        secondProduct.Multiply(leftY, rightX, scratch);
        crossZ.CopyFrom(firstProduct);
        crossZ.Subtract(secondProduct);
    }

    private static void SetDot3(scoped ref BoundedInteger dot, Vector3 left, Vector3 right, Span<ulong> scratch, scoped ref BoundedInteger leftComponent, scoped ref BoundedInteger rightComponent, scoped ref BoundedInteger term)
    {
        leftComponent.SetSingle(left.X);
        rightComponent.SetSingle(right.X);
        term.Multiply(leftComponent, rightComponent, scratch);
        dot.CopyFrom(term);

        leftComponent.SetSingle(left.Y);
        rightComponent.SetSingle(right.Y);
        term.Multiply(leftComponent, rightComponent, scratch);
        dot.Add(term);

        leftComponent.SetSingle(left.Z);
        rightComponent.SetSingle(right.Z);
        term.Multiply(leftComponent, rightComponent, scratch);
        dot.Add(term);
    }

    private static bool TryInverseResidualFast(ReadOnlySpan<float> left, ReadOnlySpan<float> right, int dimension, int row, int column, out bool accepted)
    {
        // Binary32 products are exact in binary64; outward-rounded sums certify residuals away from either threshold.
        var sumLower = 0.0;
        var sumUpper = 0.0;
        var magnitudeLower = 0.0;
        var magnitudeUpper = 0.0;

        for (var index = 0; index < dimension; index++)
        {
            var term = (double)left[(row * dimension) + index] * right[(index * dimension) + column];
            sumLower = double.BitDecrement(sumLower + term);
            sumUpper = double.BitIncrement(sumUpper + term);
            magnitudeLower = double.BitDecrement(magnitudeLower + double.Abs(term));
            magnitudeUpper = double.BitIncrement(magnitudeUpper + double.Abs(term));
        }

        if (row == column)
        {
            sumLower = double.BitDecrement(sumLower - 1.0);
            sumUpper = double.BitIncrement(sumUpper - 1.0);
        }

        GetAbsoluteBounds(sumLower, sumUpper, out var residualLower, out var residualUpper);

        if (double.BitIncrement(residualUpper * 100000) <= 1.0
            || (double.BitIncrement(residualUpper * 500000) <= magnitudeLower))
        {
            accepted = true;
            return true;
        }

        if ((double.BitDecrement(residualLower * 100000) > 1.0)
            && (double.BitDecrement(residualLower * 500000) > magnitudeUpper))
        {
            accepted = false;
            return true;
        }

        accepted = false;
        return false;
    }

    private static bool TryLook(Vector3 position, scoped in BoundedInteger dx, scoped in BoundedInteger dy, scoped in BoundedInteger dz, Vector3 up, string directionParameter, out Matrix4x4 view, out string failureParameter)
    {
        view = default;
        failureParameter = directionParameter;

        if (dx.IsZero && dy.IsZero && dz.IsZero)
        {
            return false;
        }

        Unsafe.SkipInit(out InlineArray16<ulong> uxStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> uyStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> uzStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> crossXStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> crossYStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> crossZStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> firstProductStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> secondProductStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> scratchStorage);
        Span<ulong> scratch = scratchStorage;

        var ux = new BoundedInteger(uxStorage);
        var uy = new BoundedInteger(uyStorage);
        var uz = new BoundedInteger(uzStorage);
        var cx = new BoundedInteger(crossXStorage);
        var cy = new BoundedInteger(crossYStorage);
        var cz = new BoundedInteger(crossZStorage);
        var firstProduct = new BoundedInteger(firstProductStorage);
        var secondProduct = new BoundedInteger(secondProductStorage);
        ux.SetSingle(up.X);
        uy.SetSingle(up.Y);
        uz.SetSingle(up.Z);

        SetCross3(ref cx, ref cy, ref cz, ux, uy, uz, dx, dy, dz, scratch, ref firstProduct, ref secondProduct);

        if (cx.IsZero && cy.IsZero && cz.IsZero)
        {
            failureParameter = nameof(up);
            return false;
        }

        var forward = Vector3.Normalize(Scaled32(dx, dy, dz));
        var right = Vector3.Normalize(Scaled32(cx, cy, cz));

        if (!Vector3.TryNormalize(Vector3.Cross(forward, right), out var frameUp) || !IsProperFrame(right, frameUp, forward))
        {
            return false;
        }

        if (!TryTranslation(position, right, out var tx)
            || !TryTranslation(position, frameUp, out var ty)
            || !TryTranslation(position, forward, out var tz))
        {
            failureParameter = nameof(position);
            return false;
        }

        view = new Matrix4x4(
            new Vector4(right.X, frameUp.X, forward.X, 0.0f),
            new Vector4(right.Y, frameUp.Y, forward.Y, 0.0f),
            new Vector4(right.Z, frameUp.Z, forward.Z, 0.0f),
            new Vector4(tx, ty, tz, 1.0f));
        return true;
    }

    private static bool TryTranslation(Vector3 position, Vector3 axis, out float translation)
    {
        Unsafe.SkipInit(out InlineArray16<ulong> dotStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> leftStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> rightStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> termStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> scratchStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> denominatorStorage);
        Span<ulong> scratch = scratchStorage;

        var dot = new BoundedInteger(dotStorage);
        var leftComponent = new BoundedInteger(leftStorage);
        var rightComponent = new BoundedInteger(rightStorage);
        var term = new BoundedInteger(termStorage);
        var denominator = new BoundedInteger(denominatorStorage);
        SetDot3(ref dot, position, axis, scratch, ref leftComponent, ref rightComponent, ref term);
        dot.Negate();
        denominator.SetPowerOfTwo(298);
        return TryRound(dot, denominator, out translation);
    }
}
