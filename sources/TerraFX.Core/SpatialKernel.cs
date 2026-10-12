// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;
using TerraFX.Numerics;

namespace TerraFX;

internal static class SpatialKernel
{
    /// <summary>Compares two exact spatial coordinates along one chunked axis.</summary>
    internal static int CompareAxis(int fromChunk, float fromOffset, int toChunk, float toOffset, int exponent)
    {
        Unsafe.SkipInit(out InlineArray16<ulong> differenceStorage);

        var difference = new BoundedInteger(differenceStorage);
        Difference(fromChunk, fromOffset, toChunk, toOffset, exponent, ref difference);

        if (difference.IsNegative)
        {
            return -1;
        }

        return difference.IsZero ? 0 : 1;
    }

    // Float dyadics and the admitted decimal scales fit the fixed 1024-bit integer workspace.
    /// <summary>Converts a chunked coordinate to another chunk and unit scale.</summary>
    internal static void ConvertAxis(int chunk, float offset, int sourceChunkExponent, int targetChunkExponent, int sourceUnitExponent, int targetUnitExponent, out int targetChunk, out float targetOffset)
    {
        Unsafe.SkipInit(out InlineArray16<ulong> absoluteStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> chunkStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> offsetStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> scaleNumeratorStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> scaleDenominatorStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> numeratorStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> productScratchStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> denominatorStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> cellWidthStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> remainderStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> offsetDenominatorStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> carryNumeratorStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> carryDenominatorStorage);
        Unsafe.SkipInit(out InlineBuffer64<ulong> carryWorkspaceStorage);

        var absolute = new BoundedInteger(absoluteStorage);
        var chunkValue = new BoundedInteger(chunkStorage);
        var offsetValue = new BoundedInteger(offsetStorage);
        var scaleNumerator = new BoundedInteger(scaleNumeratorStorage);
        var scaleDenominator = new BoundedInteger(scaleDenominatorStorage);
        var numerator = new BoundedInteger(numeratorStorage);
        Span<ulong> productScratch = productScratchStorage;
        var denominator = new BoundedInteger(denominatorStorage);
        var cellWidth = new BoundedInteger(cellWidthStorage);
        var offsetDenominator = new BoundedInteger(offsetDenominatorStorage);

        GetUnitScale(sourceUnitExponent, targetUnitExponent, 1, ref scaleNumerator, ref scaleDenominator);
        chunkValue.SetInt64(chunk);
        chunkValue.ShiftLeft(149 + sourceChunkExponent);
        offsetValue.SetSingle(offset);

        absolute.CopyFrom(chunkValue);
        absolute.Add(offsetValue);

        numerator.Multiply(absolute, scaleNumerator, productScratch);
        denominator.CopyFrom(scaleDenominator);
        denominator.ShiftLeft(149);
        cellWidth.CopyFrom(denominator);

        if (targetChunkExponent >= 0)
        {
            cellWidth.ShiftLeft(targetChunkExponent);
        }
        else
        {
            cellWidth.ShiftRight(-targetChunkExponent);
        }

        offsetDenominator.CopyFrom(denominator);

        Span<ulong> carryWorkspace = carryWorkspaceStorage;
        var carry = new BoundedRational(carryWorkspace);
        numerator.CopyMagnitudeTo(carryNumeratorStorage);
        cellWidth.CopyMagnitudeTo(carryDenominatorStorage);
        carry.SetFraction(carryNumeratorStorage, carryDenominatorStorage, numerator.IsNegative);

        if (!carry.TryFloorDivideToInt32(out targetChunk))
        {
            ThrowHelper.ThrowOverflowException("The converted spatial chunk is outside the signed integer domain.");
        }

        carry.CopyNumeratorTo(remainderStorage);
        targetOffset = RoundFraction(remainderStorage, offsetDenominatorStorage, false);
        CarryRoundedEndpoint(float.ScaleB(1.0f, targetChunkExponent), ref targetChunk, ref targetOffset);
    }

    /// <summary>Converts a finite foot-based quantity to the requested decimal unit exponent.</summary>
    internal static float ConvertFeetQuantity(float value, int targetExponent, int lengthDimension)
    {
        ValidateUnitExponent(targetExponent);
        ValidateLengthDimension(lengthDimension);
        ThrowHelper.ThrowIfNotFinite(value);

        Unsafe.SkipInit(out InlineArray16<ulong> numeratorStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> denominatorStorage);
        var numerator = new BoundedInteger(numeratorStorage);
        var denominator = new BoundedInteger(denominatorStorage);

        // A foot^d quantity scales by (381/1250)^d; negative dimensions invert that ratio.
        var degree = Math.Abs(lengthDimension);
        SetSmallPower(ref numerator, lengthDimension < 0 ? 1250u : 381u, degree);
        SetSmallPower(ref denominator, lengthDimension < 0 ? 381u : 1250u, degree);
        var decimalPower = -(lengthDimension * targetExponent);

        if (decimalPower > 0)
        {
            MultiplyPowerOfTen(ref numerator, decimalPower);
        }
        else if (decimalPower < 0)
        {
            MultiplyPowerOfTen(ref denominator, -decimalPower);
        }

        return ConvertScaled(value, numerator, denominator);
    }

    /// <summary>Converts a finite quantity between decimal unit exponents.</summary>
    internal static float ConvertQuantity(float value, int sourceExponent, int targetExponent, int lengthDimension)
    {
        Unsafe.SkipInit(out InlineArray16<ulong> numeratorStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> denominatorStorage);

        var numerator = new BoundedInteger(numeratorStorage);
        var denominator = new BoundedInteger(denominatorStorage);

        GetUnitScale(sourceExponent, targetExponent, lengthDimension, ref numerator, ref denominator);
        ThrowHelper.ThrowIfNotFinite(value);
        return ConvertScaled(value, numerator, denominator);
    }

    /// <summary>Converts an exact rational quantity between decimal unit exponents.</summary>
    internal static float ConvertQuantity(long numerator, long denominator, int sourceExponent, int targetExponent, int lengthDimension)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(denominator);

        Unsafe.SkipInit(out InlineArray16<ulong> quantityNumeratorStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> quantityDenominatorStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> scaleNumeratorStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> scaleDenominatorStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> productScratchStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> exactNumeratorStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> exactDenominatorStorage);

        var quantityNumerator = new BoundedInteger(quantityNumeratorStorage);
        var quantityDenominator = new BoundedInteger(quantityDenominatorStorage);
        var scaleNumerator = new BoundedInteger(scaleNumeratorStorage);
        var scaleDenominator = new BoundedInteger(scaleDenominatorStorage);
        Span<ulong> productScratch = productScratchStorage;
        var exactNumerator = new BoundedInteger(exactNumeratorStorage);
        var exactDenominator = new BoundedInteger(exactDenominatorStorage);

        quantityNumerator.SetInt64(numerator);
        quantityDenominator.SetInt64(denominator);
        GetUnitScale(sourceExponent, targetExponent, lengthDimension, ref scaleNumerator, ref scaleDenominator);
        exactNumerator.Multiply(quantityNumerator, scaleNumerator, productScratch);
        exactDenominator.Multiply(quantityDenominator, scaleDenominator, productScratch);
        return RoundFraction(exactNumeratorStorage, exactDenominatorStorage, exactNumerator.IsNegative);
    }

    /// <summary>Computes the exact difference between two chunked coordinates along one axis.</summary>
    internal static void Difference(int fromChunk, float fromOffset, int toChunk, float toOffset, int exponent, scoped ref BoundedInteger difference)
    {
        Unsafe.SkipInit(out InlineArray16<ulong> chunkStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> fromOffsetStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> toOffsetStorage);

        var chunkDifference = new BoundedInteger(chunkStorage);
        var fromOffsetValue = new BoundedInteger(fromOffsetStorage);
        var toOffsetValue = new BoundedInteger(toOffsetStorage);

        chunkDifference.SetInt64((long)toChunk - fromChunk);
        chunkDifference.ShiftLeft(149 + exponent);

        fromOffsetValue.SetSingle(fromOffset);
        toOffsetValue.SetSingle(toOffset);

        difference.CopyFrom(chunkStorage, chunkDifference.IsNegative);
        difference.Add(toOffsetStorage, toOffsetValue.IsNegative);
        difference.Subtract(fromOffsetStorage, fromOffsetValue.IsNegative);
    }

    /// <summary>Checks whether an offset is canonical within its chunk width.</summary>
    internal static bool IsCanonical(float offset, float width) =>
        offset >= 0.0f && offset < width && (offset != 0.0f || BitConverter.SingleToInt32Bits(offset) == 0);

    /// <summary>Computes a nearby displacement, using the exact difference when canonical subtraction is unsafe.</summary>
    internal static float Nearby(int fromChunk, float fromOffset, int toChunk, float toOffset, float width, scoped in BoundedInteger exactDifference)
    {
        // Raw aliases can require a rounded canonical remainder. Keep their exact
        // source difference rather than feeding that publication rounding back in.
        if (IsCanonical(fromOffset, width) && IsCanonical(toOffset, width))
        {
            switch ((long)toChunk - fromChunk)
            {
                case 0:
                {
                    return toOffset - fromOffset;
                }

                case 1:
                {
                    return width - fromOffset + toOffset;
                }

                case -1:
                {
                    return -(width - toOffset + fromOffset);
                }

                default:
                {
                    break;
                }
            }
        }

        return RoundDyadic(exactDifference);
    }

    /// <summary>Normalizes one chunked coordinate to a canonical chunk and offset.</summary>
    internal static void NormalizeAxis(int chunk, float offset, int exponent, out int normalizedChunk, out float normalizedOffset)
    {
        var width = float.ScaleB(1.0f, exponent);

        if (offset >= 0.0f && offset < width)
        {
            normalizedChunk = chunk;
            normalizedOffset = offset == 0.0f ? 0.0f : offset;
            return;
        }

        if (offset == width)
        {
            normalizedChunk = checked(chunk + 1);
            normalizedOffset = 0.0f;
            return;
        }

        Unsafe.SkipInit(out InlineArray16<ulong> numeratorStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> denominatorStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> cellWidthStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> remainderStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> carryNumeratorStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> carryDenominatorStorage);
        Unsafe.SkipInit(out InlineBuffer64<ulong> workspaceStorage);

        var numerator = new BoundedInteger(numeratorStorage);
        var denominator = new BoundedInteger(denominatorStorage);
        var cellWidth = new BoundedInteger(cellWidthStorage);
        Span<ulong> workspace = workspaceStorage;
        var carry = new BoundedRational(workspace);

        numerator.SetSingle(offset);
        denominator.SetPowerOfTwo(149);
        cellWidth.SetPowerOfTwo(149 + exponent);

        numerator.CopyMagnitudeTo(carryNumeratorStorage);
        cellWidth.CopyMagnitudeTo(carryDenominatorStorage);
        carry.SetFraction(carryNumeratorStorage, carryDenominatorStorage, numerator.IsNegative);

        if (!carry.TryFloorDivideToInt64(out var chunkCarry))
        {
            ThrowHelper.ThrowOverflowException("The normalized spatial chunk is outside the signed integer domain.");
        }

        carry.CopyNumeratorTo(remainderStorage);
        normalizedChunk = checked((int)checked(chunk + chunkCarry));
        normalizedOffset = RoundFraction(remainderStorage, denominatorStorage, false);
        CarryRoundedEndpoint(width, ref normalizedChunk, ref normalizedOffset);
    }

    /// <summary>Rounds an exact dyadic integer to binary32.</summary>
    internal static float RoundDyadic(scoped in BoundedInteger numerator)
    {
        Unsafe.SkipInit(out InlineArray16<ulong> numeratorStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> denominatorStorage);

        var denominator = new BoundedInteger(denominatorStorage);
        denominator.SetPowerOfTwo(149);

        numerator.CopyMagnitudeTo(numeratorStorage);
        return RoundFraction(numeratorStorage, denominatorStorage, numerator.IsNegative);
    }

    /// <summary>Validates a finite nonnegative spatial distance.</summary>
    internal static void ValidateDistance(float maximumDistance, [CallerArgumentExpression(nameof(maximumDistance))] string? parameter = null)
    {
        ThrowHelper.ThrowIfNotFinite(maximumDistance, parameter);
        ThrowHelper.ThrowIfNegative(maximumDistance, "The distance must be nonnegative.", parameter);
    }

    /// <summary>Validates a two-dimensional spatial frame.</summary>
    internal static void ValidateFrame(SpatialFrame2 frame) =>
        ValidateFrame(frame.OwnerId, frame.ProfileId, frame.Settings, frame.Origin, frame.Revision, frame.Minimum, frame.Maximum);

    /// <summary>Validates a three-dimensional spatial frame.</summary>
    internal static void ValidateFrame(SpatialFrame3 frame) =>
        ValidateFrame(frame.OwnerId, frame.ProfileId, frame.Settings, frame.Origin, frame.Revision, frame.Minimum, frame.Maximum);

    /// <summary>Validates the identity, origin, and extents of a two-dimensional frame.</summary>
    internal static void ValidateFrame(uint ownerId, SpatialProfileId profileId, SpatialSettings settings, SpatialPosition2 origin, long revision, Vector2 minimum, Vector2 maximum)
    {
        ValidateFrameIdentity(ownerId, profileId, settings, revision);
        var width = settings.ChunkWidth;

        if (!IsCanonical(origin.Offset.X, width) || !IsCanonical(origin.Offset.Y, width))
        {
            ThrowHelper.ThrowArgumentException("The frame origin must be canonical, including positive zero.", nameof(origin));
        }

        ThrowHelper.ThrowIfNotFinite(minimum);
        ThrowHelper.ThrowIfNotFinite(maximum);

        if (minimum.X > maximum.X || minimum.Y > maximum.Y)
        {
            ThrowHelper.ThrowArgumentException("The frame extents must be ordered on every axis.", nameof(maximum));
        }
    }

    /// <summary>Validates the identity, origin, and extents of a three-dimensional frame.</summary>
    internal static void ValidateFrame(uint ownerId, SpatialProfileId profileId, SpatialSettings settings, SpatialPosition3 origin, long revision, Vector3 minimum, Vector3 maximum)
    {
        ValidateFrameIdentity(ownerId, profileId, settings, revision);
        var width = settings.ChunkWidth;

        if (!IsCanonical(origin.Offset.X, width) || !IsCanonical(origin.Offset.Y, width) || !IsCanonical(origin.Offset.Z, width))
        {
            ThrowHelper.ThrowArgumentException("The frame origin must be canonical, including positive zero.", nameof(origin));
        }

        ThrowHelper.ThrowIfNotFinite(minimum);
        ThrowHelper.ThrowIfNotFinite(maximum);

        if (minimum.X > maximum.X || minimum.Y > maximum.Y || minimum.Z > maximum.Z)
        {
            ThrowHelper.ThrowArgumentException("The frame extents must be ordered on every axis.", nameof(maximum));
        }
    }

    /// <summary>Validates that a spatial profile identity is nonempty.</summary>
    internal static void ValidateId(SpatialProfileId id, [CallerArgumentExpression(nameof(id))] string? parameter = null)
    {
        if (!id.IsValid)
        {
            ThrowHelper.ThrowArgumentException("A nonempty spatial profile identity is required.", parameter);
        }
    }

    /// <summary>Validates that a profile identity is registered with the supplied settings.</summary>
    internal static void ValidateProfile(SpatialProfileId id, SpatialSettings settings,
        [CallerArgumentExpression(nameof(id))] string? idParameter = null,
        [CallerArgumentExpression(nameof(settings))] string? settingsParameter = null)
    {
        ValidateId(id, idParameter);
        ValidateSettings(settings, settingsParameter);

        if (!SpatialProfiles.TryGetSettings(id, out var registered))
        {
            ThrowHelper.ThrowArgumentException("The spatial profile identity is not registered.", idParameter);
        }

        if (registered != settings)
        {
            ThrowHelper.ThrowArgumentException("The settings disagree with the registered spatial profile.", settingsParameter);
        }
    }

    /// <summary>Validates that a ray direction has finite components and unit length within tolerance.</summary>
    internal static void ValidateRayDirection(float x, float y, float z, string parameter)
    {
        Unsafe.SkipInit(out InlineArray16<ulong> xStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> yStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> zStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> squareStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> termStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> scratchStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> oneStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> minimumStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> maximumStorage);

        var xValue = new BoundedInteger(xStorage);
        var yValue = new BoundedInteger(yStorage);
        var zValue = new BoundedInteger(zStorage);
        var square = new BoundedInteger(squareStorage);
        var term = new BoundedInteger(termStorage);
        Span<ulong> scratch = scratchStorage;
        var one = new BoundedInteger(oneStorage);
        var minimum = new BoundedInteger(minimumStorage);
        var maximum = new BoundedInteger(maximumStorage);

        xValue.SetSingle(x);
        yValue.SetSingle(y);
        zValue.SetSingle(z);

        square.Multiply(xValue, xValue, scratch);

        term.Multiply(yValue, yValue, scratch);
        square.Add(term);

        term.Multiply(zValue, zValue, scratch);
        square.Add(term);

        square.MultiplySmall(10_000_000_000ul);

        one.SetPowerOfTwo(298);
        minimum.CopyFrom(one);
        minimum.MultiplySmall(9_999_800_001ul);
        maximum.CopyFrom(one);
        maximum.MultiplySmall(10_000_200_001ul);

        // The exact squared interval is (1 +/- 1/100000)^2.
        if (square.CompareTo(minimum) < 0 || square.CompareTo(maximum) > 0)
        {
            ThrowHelper.ThrowArgumentException("The ray direction must have norm within 1e-5 of one.", parameter);
        }
    }

    /// <summary>Validates the admitted unit and chunk-size exponents.</summary>
    internal static void ValidateSettings(SpatialSettings settings, [CallerArgumentExpression(nameof(settings))] string? parameter = null)
    {
        if (settings.MetersPerUnitExponent is < -30 or > 30 || settings.ChunkSizeExponent is < 8 or > 16)
        {
            ThrowHelper.ThrowArgumentException("A valid spatial declaration is required; zero initialization is not configured.", parameter);
        }
    }

    /// <summary>Checks an exact two-dimensional displacement against a Euclidean distance bound.</summary>
    internal static bool WithinDistance(scoped in BoundedInteger dx, scoped in BoundedInteger dy, float maximumDistance)
    {
        Unsafe.SkipInit(out InlineArray16<ulong> sumStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> squareStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> scratchStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> maximumStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> maximumSquaredStorage);

        var sum = new BoundedInteger(sumStorage);
        var square = new BoundedInteger(squareStorage);
        Span<ulong> scratch = scratchStorage;
        var maximum = new BoundedInteger(maximumStorage);
        var maximumSquared = new BoundedInteger(maximumSquaredStorage);

        square.Multiply(dx, dx, scratch);
        sum.CopyFrom(square);

        square.Multiply(dy, dy, scratch);
        sum.Add(square);

        maximum.SetSingle(maximumDistance);
        maximumSquared.Multiply(maximum, maximum, scratch);
        return sum.CompareTo(maximumSquared) <= 0;
    }

    /// <summary>Checks an exact three-dimensional displacement against a Euclidean distance bound.</summary>
    internal static bool WithinDistance(scoped in BoundedInteger dx, scoped in BoundedInteger dy, scoped in BoundedInteger dz, float maximumDistance)
    {
        Unsafe.SkipInit(out InlineArray16<ulong> sumStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> squareStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> scratchStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> maximumStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> maximumSquaredStorage);

        var sum = new BoundedInteger(sumStorage);
        var square = new BoundedInteger(squareStorage);
        Span<ulong> scratch = scratchStorage;
        var maximum = new BoundedInteger(maximumStorage);
        var maximumSquared = new BoundedInteger(maximumSquaredStorage);

        square.Multiply(dx, dx, scratch);
        sum.CopyFrom(square);

        square.Multiply(dy, dy, scratch);
        sum.Add(square);

        square.Multiply(dz, dz, scratch);
        sum.Add(square);

        maximum.SetSingle(maximumDistance);
        maximumSquared.Multiply(maximum, maximum, scratch);
        return sum.CompareTo(maximumSquared) <= 0;
    }

    /// <summary>Checks whether an exact displacement lies within a closed interval.</summary>
    internal static bool WithinExtent(scoped in BoundedInteger difference, float minimum, float maximum)
    {
        Unsafe.SkipInit(out InlineArray16<ulong> minimumStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> maximumStorage);

        var minimumValue = new BoundedInteger(minimumStorage);
        var maximumValue = new BoundedInteger(maximumStorage);

        minimumValue.SetSingle(minimum);
        maximumValue.SetSingle(maximum);
        return difference.CompareTo(minimumValue) >= 0 && difference.CompareTo(maximumValue) <= 0;
    }

    private static void CarryRoundedEndpoint(float width, ref int chunk, ref float offset)
    {
        if (offset == width)
        {
            chunk = checked(chunk + 1);
            offset = 0.0f;
        }
    }

    private static float ConvertScaled(float value, scoped in BoundedInteger scaleNumerator, scoped in BoundedInteger scaleDenominator)
    {
        if (scaleNumerator.CompareMagnitudeTo(scaleDenominator) == 0)
        {
            return value;
        }

        // The validated decimal power is at most 180; combined with binary32 and
        // the degree-three foot ratio, the exact fraction fits 1024 bits.
        Unsafe.SkipInit(out InlineArray16<ulong> valueStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> numeratorStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> scratchStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> denominatorStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> rationalNumeratorStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> rationalDenominatorStorage);

        var valueInteger = new BoundedInteger(valueStorage);
        var numerator = new BoundedInteger(numeratorStorage);
        Span<ulong> scratch = scratchStorage;
        var denominator = new BoundedInteger(denominatorStorage);

        valueInteger.SetSingle(value);
        numerator.Multiply(valueInteger, scaleNumerator, scratch);

        denominator.CopyFrom(scaleDenominator);
        denominator.ShiftLeft(149);

        numerator.CopyMagnitudeTo(rationalNumeratorStorage);
        denominator.CopyMagnitudeTo(rationalDenominatorStorage);

        var negativeZero = value == 0.0f && BitConverter.SingleToInt32Bits(value) < 0;
        return RoundFraction(rationalNumeratorStorage, rationalDenominatorStorage, valueInteger.IsNegative, negativeZero);
    }

    private static void GetUnitScale(int sourceExponent, int targetExponent, int lengthDimension, scoped ref BoundedInteger numerator, scoped ref BoundedInteger denominator)
    {
        ValidateUnitExponent(sourceExponent);
        ValidateUnitExponent(targetExponent);
        ValidateLengthDimension(lengthDimension);

        var power = lengthDimension * (sourceExponent - targetExponent);
        numerator.SetPowerOfTwo(0);
        denominator.SetPowerOfTwo(0);

        if (power > 0)
        {
            MultiplyPowerOfTen(ref numerator, power);
        }
        else if (power < 0)
        {
            MultiplyPowerOfTen(ref denominator, -power);
        }
    }

    private static void MultiplyPowerOfTen(scoped ref BoundedInteger value, int exponent)
    {
        for (var index = 0; index < exponent; index++)
        {
            value.MultiplySmall(10);
        }
    }

    private static float RoundFraction(ReadOnlySpan<ulong> numerator, ReadOnlySpan<ulong> denominator, bool negative, bool negativeZero = false)
    {
        Unsafe.SkipInit(out InlineBuffer64<ulong> workspaceStorage);

        Span<ulong> workspace = workspaceStorage;
        var rational = new BoundedRational(workspace);

        rational.SetFraction(numerator, denominator, negative);

        if (!rational.TryRoundToSingle(out var result, negativeZero))
        {
            ThrowHelper.ThrowOverflowException("The exact spatial value rounds to infinity.");
        }

        return result;
    }

    private static void SetSmallPower(scoped ref BoundedInteger result, uint value, int exponent)
    {
        result.SetPowerOfTwo(0);

        for (var index = 0; index < exponent; index++)
        {
            result.MultiplySmall(value);
        }
    }

    private static void ValidateFrameIdentity(uint ownerId, SpatialProfileId profileId, SpatialSettings settings, long revision)
    {
        ArgumentOutOfRangeException.ThrowIfZero(ownerId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(revision);
        ValidateProfile(profileId, settings, nameof(profileId), nameof(settings));
    }

    private static void ValidateLengthDimension(int lengthDimension)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(lengthDimension, -3);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(lengthDimension, 3);
    }

    private static void ValidateUnitExponent(int exponent, [CallerArgumentExpression(nameof(exponent))] string? parameter = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(exponent, -30, parameter);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(exponent, 30, parameter);
    }
}
