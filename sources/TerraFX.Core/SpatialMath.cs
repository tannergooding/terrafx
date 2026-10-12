// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;
using TerraFX.Numerics;

namespace TerraFX;

/// <summary>Provides checked canonicalization, exact unit conversion, and bounded nearby evaluation.</summary>
public static class SpatialMath
{
    /// <summary>Converts an exact cell/dyadic coordinate by the exact decimal length ratio, rounding target offsets once.</summary>
    /// <param name="position">The raw finite source position.</param>
    /// <param name="sourceProfileId">The registered source identity.</param>
    /// <param name="sourceSettings">The matching source declaration.</param>
    /// <param name="targetProfileId">The registered target identity.</param>
    /// <param name="targetSettings">The matching target declaration.</param>
    /// <returns>The canonical target position in the common coordinate basis.</returns>
    /// <exception cref="ArgumentException">An argument is invalid, unregistered, or mismatched.</exception>
    /// <exception cref="OverflowException">An exact or rounded target carry exceeds the signed chunk domain.</exception>
    public static SpatialPosition2 ConvertPosition(SpatialPosition2 position, SpatialProfileId sourceProfileId, SpatialSettings sourceSettings, SpatialProfileId targetProfileId, SpatialSettings targetSettings)
    {
        ValidateConversion(sourceProfileId, sourceSettings, targetProfileId, targetSettings);
        ThrowHelper.ThrowIfNotFinite(position.Offset, nameof(position));

        if (sourceSettings == targetSettings)
        {
            return Normalize(position, sourceSettings);
        }

        SpatialKernel.ConvertAxis(position.Chunk.X, position.Offset.X, sourceSettings.ChunkSizeExponent, targetSettings.ChunkSizeExponent, sourceSettings.MetersPerUnitExponent, targetSettings.MetersPerUnitExponent, out var x, out var ox);
        SpatialKernel.ConvertAxis(position.Chunk.Y, position.Offset.Y, sourceSettings.ChunkSizeExponent, targetSettings.ChunkSizeExponent, sourceSettings.MetersPerUnitExponent, targetSettings.MetersPerUnitExponent, out var y, out var oy);

        return new SpatialPosition2(new ChunkIndex2(x, y), new Vector2(ox, oy));
    }

    /// <summary>Converts an exact cell/dyadic coordinate by the exact decimal length ratio, rounding target offsets once.</summary>
    /// <param name="position">The raw finite source position.</param>
    /// <param name="sourceProfileId">The registered source identity.</param>
    /// <param name="sourceSettings">The matching source declaration.</param>
    /// <param name="targetProfileId">The registered target identity.</param>
    /// <param name="targetSettings">The matching target declaration.</param>
    /// <returns>The canonical target position in the common coordinate basis.</returns>
    /// <exception cref="ArgumentException">An argument is invalid, unregistered, or mismatched.</exception>
    /// <exception cref="OverflowException">An exact or rounded target carry exceeds the signed chunk domain.</exception>
    public static SpatialPosition3 ConvertPosition(SpatialPosition3 position, SpatialProfileId sourceProfileId, SpatialSettings sourceSettings, SpatialProfileId targetProfileId, SpatialSettings targetSettings)
    {
        ValidateConversion(sourceProfileId, sourceSettings, targetProfileId, targetSettings);
        ThrowHelper.ThrowIfNotFinite(position.Offset, nameof(position));

        if (sourceSettings == targetSettings)
        {
            return Normalize(position, sourceSettings);
        }

        SpatialKernel.ConvertAxis(position.Chunk.X, position.Offset.X, sourceSettings.ChunkSizeExponent, targetSettings.ChunkSizeExponent, sourceSettings.MetersPerUnitExponent, targetSettings.MetersPerUnitExponent, out var x, out var ox);
        SpatialKernel.ConvertAxis(position.Chunk.Y, position.Offset.Y, sourceSettings.ChunkSizeExponent, targetSettings.ChunkSizeExponent, sourceSettings.MetersPerUnitExponent, targetSettings.MetersPerUnitExponent, out var y, out var oy);
        SpatialKernel.ConvertAxis(position.Chunk.Z, position.Offset.Z, sourceSettings.ChunkSizeExponent, targetSettings.ChunkSizeExponent, sourceSettings.MetersPerUnitExponent, targetSettings.MetersPerUnitExponent, out var z, out var oz);

        return new SpatialPosition3(new ChunkIndex3(x, y, z), new Vector3(ox, oy, oz));
    }

    /// <summary>Converts a finite binary32 quantity between exact decimal unit scales.</summary>
    /// <param name="value">The exact dyadic value represented by the finite binary32 input.</param>
    /// <param name="sourceExponent">The source unit's meters-per-unit exponent.</param>
    /// <param name="targetExponent">The target unit's meters-per-unit exponent.</param>
    /// <param name="lengthDimension">The quantity's length dimension, in the range [-3, 3].</param>
    /// <returns>The converted quantity rounded once to binary32 using nearest-even.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An exponent or length dimension is outside its admitted range.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not finite.</exception>
    /// <exception cref="OverflowException">The converted value rounds to infinity.</exception>
    public static float ConvertQuantity(float value, int sourceExponent, int targetExponent, int lengthDimension)
    {
        return SpatialKernel.ConvertQuantity(value, sourceExponent, targetExponent, lengthDimension);
    }

    /// <summary>Converts an exact rational quantity between decimal unit scales.</summary>
    /// <param name="numerator">The signed numerator of the exact input quantity.</param>
    /// <param name="denominator">The positive denominator of the exact input quantity.</param>
    /// <param name="sourceExponent">The source unit's meters-per-unit exponent.</param>
    /// <param name="targetExponent">The target unit's meters-per-unit exponent.</param>
    /// <param name="lengthDimension">The quantity's length dimension, in the range [-3, 3].</param>
    /// <returns>The converted quantity rounded once to binary32 using nearest-even.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The denominator, an exponent, or the length dimension is invalid.</exception>
    /// <exception cref="OverflowException">The converted value rounds to infinity.</exception>
    public static float ConvertQuantity(long numerator, long denominator, int sourceExponent, int targetExponent, int lengthDimension)
    {
        return SpatialKernel.ConvertQuantity(numerator, denominator, sourceExponent, targetExponent, lengthDimension);
    }

    /// <summary>Produces offsets in [0, C) with positive zero using exact dyadic carries and nearest-even remainder rounding.</summary>
    /// <param name="position">The raw finite position.</param>
    /// <param name="settings">The valid spatial declaration.</param>
    /// <returns>The canonical position.</returns>
    /// <exception cref="ArgumentException">An argument is invalid or nonfinite.</exception>
    /// <exception cref="OverflowException">An exact or rounded carry exceeds the signed chunk domain.</exception>
    public static SpatialPosition2 Normalize(SpatialPosition2 position, SpatialSettings settings)
    {
        SpatialKernel.ValidateSettings(settings);
        ThrowHelper.ThrowIfNotFinite(position.Offset, nameof(position));

        SpatialKernel.NormalizeAxis(position.Chunk.X, position.Offset.X, settings.ChunkSizeExponent, out var x, out var ox);
        SpatialKernel.NormalizeAxis(position.Chunk.Y, position.Offset.Y, settings.ChunkSizeExponent, out var y, out var oy);

        return new SpatialPosition2(new ChunkIndex2(x, y), new Vector2(ox, oy));
    }

    /// <summary>Produces offsets in [0, C) with positive zero using exact dyadic carries and nearest-even remainder rounding.</summary>
    /// <param name="position">The raw finite position.</param>
    /// <param name="settings">The valid spatial declaration.</param>
    /// <returns>The canonical position.</returns>
    /// <exception cref="ArgumentException">An argument is invalid or nonfinite.</exception>
    /// <exception cref="OverflowException">An exact or rounded carry exceeds the signed chunk domain.</exception>
    public static SpatialPosition3 Normalize(SpatialPosition3 position, SpatialSettings settings)
    {
        SpatialKernel.ValidateSettings(settings);
        ThrowHelper.ThrowIfNotFinite(position.Offset, nameof(position));

        SpatialKernel.NormalizeAxis(position.Chunk.X, position.Offset.X, settings.ChunkSizeExponent, out var x, out var ox);
        SpatialKernel.NormalizeAxis(position.Chunk.Y, position.Offset.Y, settings.ChunkSizeExponent, out var y, out var oy);
        SpatialKernel.NormalizeAxis(position.Chunk.Z, position.Offset.Z, settings.ChunkSizeExponent, out var z, out var oz);

        return new SpatialPosition3(new ChunkIndex3(x, y, z), new Vector3(ox, oy, oz));
    }

    /// <summary>Evaluates a nearby displacement after exact Euclidean distance admission.</summary>
    /// <param name="from">The raw finite start.</param>
    /// <param name="to">The raw finite end.</param>
    /// <param name="settings">The valid common spatial declaration.</param>
    /// <param name="maximumDistance">The finite admitted Euclidean distance; negative zero is rejected.</param>
    /// <param name="displacement">The nearby displacement, or default on false.</param>
    /// <returns>False only when the exact distance exceeds the bound.</returns>
    /// <exception cref="ArgumentException">An argument is invalid or nonfinite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The distance is negative, including negative zero.</exception>
    /// <exception cref="OverflowException">Source canonicalization exceeds the signed chunk domain.</exception>
    public static bool TryGetDisplacement(SpatialPosition2 from, SpatialPosition2 to, SpatialSettings settings, float maximumDistance, out Vector2 displacement)
    {
        SpatialKernel.ValidateDistance(maximumDistance);
        _ = Normalize(from, settings);
        _ = Normalize(to, settings);

        Unsafe.SkipInit(out InlineArray16<ulong> dxStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> dyStorage);

        var dx = new BoundedInteger(dxStorage);
        var dy = new BoundedInteger(dyStorage);

        SpatialKernel.Difference(from.Chunk.X, from.Offset.X, to.Chunk.X, to.Offset.X, settings.ChunkSizeExponent, ref dx);
        SpatialKernel.Difference(from.Chunk.Y, from.Offset.Y, to.Chunk.Y, to.Offset.Y, settings.ChunkSizeExponent, ref dy);

        if (!SpatialKernel.WithinDistance(dx, dy, maximumDistance))
        {
            displacement = default;
            return false;
        }

        var width = settings.ChunkWidth;
        displacement = new Vector2(
            SpatialKernel.Nearby(from.Chunk.X, from.Offset.X, to.Chunk.X, to.Offset.X, width, dx),
            SpatialKernel.Nearby(from.Chunk.Y, from.Offset.Y, to.Chunk.Y, to.Offset.Y, width, dy));
        return true;
    }

    /// <summary>Evaluates a nearby displacement after exact Euclidean distance admission.</summary>
    /// <param name="from">The raw finite start.</param>
    /// <param name="to">The raw finite end.</param>
    /// <param name="settings">The valid common spatial declaration.</param>
    /// <param name="maximumDistance">The finite admitted Euclidean distance; negative zero is rejected.</param>
    /// <param name="displacement">The nearby displacement, or default on false.</param>
    /// <returns>False only when the exact distance exceeds the bound.</returns>
    /// <exception cref="ArgumentException">An argument is invalid or nonfinite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The distance is negative, including negative zero.</exception>
    /// <exception cref="OverflowException">Source canonicalization exceeds the signed chunk domain.</exception>
    public static bool TryGetDisplacement(SpatialPosition3 from, SpatialPosition3 to, SpatialSettings settings, float maximumDistance, out Vector3 displacement)
    {
        SpatialKernel.ValidateDistance(maximumDistance);
        _ = Normalize(from, settings);
        _ = Normalize(to, settings);

        Unsafe.SkipInit(out InlineArray16<ulong> dxStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> dyStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> dzStorage);

        var dx = new BoundedInteger(dxStorage);
        var dy = new BoundedInteger(dyStorage);
        var dz = new BoundedInteger(dzStorage);

        SpatialKernel.Difference(from.Chunk.X, from.Offset.X, to.Chunk.X, to.Offset.X, settings.ChunkSizeExponent, ref dx);
        SpatialKernel.Difference(from.Chunk.Y, from.Offset.Y, to.Chunk.Y, to.Offset.Y, settings.ChunkSizeExponent, ref dy);
        SpatialKernel.Difference(from.Chunk.Z, from.Offset.Z, to.Chunk.Z, to.Offset.Z, settings.ChunkSizeExponent, ref dz);

        if (!SpatialKernel.WithinDistance(dx, dy, dz, maximumDistance))
        {
            displacement = default;
            return false;
        }

        var width = settings.ChunkWidth;
        displacement = new Vector3(
            SpatialKernel.Nearby(from.Chunk.X, from.Offset.X, to.Chunk.X, to.Offset.X, width, dx),
            SpatialKernel.Nearby(from.Chunk.Y, from.Offset.Y, to.Chunk.Y, to.Offset.Y, width, dy),
            SpatialKernel.Nearby(from.Chunk.Z, from.Offset.Z, to.Chunk.Z, to.Offset.Z, width, dz));
        return true;
    }

    /// <summary>Interprets a raw position under a valid frame and admits its exact closed relative coordinates.</summary>
    /// <param name="position">The raw finite spatial position.</param>
    /// <param name="frame">The configured frame.</param>
    /// <param name="relativePosition">The nearby relative position, or default on false.</param>
    /// <returns>False only when the exact position is outside the frame's closed extent.</returns>
    /// <exception cref="ArgumentException">The position or frame is invalid.</exception>
    /// <exception cref="OverflowException">Source canonicalization exceeds the signed chunk domain.</exception>
    public static bool TryToFrameRelative(SpatialPosition2 position, SpatialFrame2 frame, out Vector2 relativePosition)
    {
        SpatialKernel.ValidateFrame(frame);
        _ = Normalize(position, frame.Settings);

        var origin = frame.Origin;
        Unsafe.SkipInit(out InlineArray16<ulong> dxStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> dyStorage);

        var dx = new BoundedInteger(dxStorage);
        var dy = new BoundedInteger(dyStorage);

        SpatialKernel.Difference(origin.Chunk.X, origin.Offset.X, position.Chunk.X, position.Offset.X, frame.Settings.ChunkSizeExponent, ref dx);
        SpatialKernel.Difference(origin.Chunk.Y, origin.Offset.Y, position.Chunk.Y, position.Offset.Y, frame.Settings.ChunkSizeExponent, ref dy);

        if (!SpatialKernel.WithinExtent(dx, frame.Minimum.X, frame.Maximum.X) || !SpatialKernel.WithinExtent(dy, frame.Minimum.Y, frame.Maximum.Y))
        {
            relativePosition = default;
            return false;
        }

        var width = frame.Settings.ChunkWidth;
        relativePosition = new Vector2(
            SpatialKernel.Nearby(origin.Chunk.X, origin.Offset.X, position.Chunk.X, position.Offset.X, width, dx),
            SpatialKernel.Nearby(origin.Chunk.Y, origin.Offset.Y, position.Chunk.Y, position.Offset.Y, width, dy));
        return true;
    }

    /// <summary>Interprets a raw position under a valid frame and admits its exact closed relative coordinates.</summary>
    /// <param name="position">The raw finite spatial position.</param>
    /// <param name="frame">The configured frame.</param>
    /// <param name="relativePosition">The nearby relative position, or default on false.</param>
    /// <returns>False only when the exact position is outside the frame's closed extent.</returns>
    /// <exception cref="ArgumentException">The position or frame is invalid.</exception>
    /// <exception cref="OverflowException">Source canonicalization exceeds the signed chunk domain.</exception>
    public static bool TryToFrameRelative(SpatialPosition3 position, SpatialFrame3 frame, out Vector3 relativePosition)
    {
        SpatialKernel.ValidateFrame(frame);
        _ = Normalize(position, frame.Settings);

        var origin = frame.Origin;
        Unsafe.SkipInit(out InlineArray16<ulong> dxStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> dyStorage);
        Unsafe.SkipInit(out InlineArray16<ulong> dzStorage);

        var dx = new BoundedInteger(dxStorage);
        var dy = new BoundedInteger(dyStorage);
        var dz = new BoundedInteger(dzStorage);

        SpatialKernel.Difference(origin.Chunk.X, origin.Offset.X, position.Chunk.X, position.Offset.X, frame.Settings.ChunkSizeExponent, ref dx);
        SpatialKernel.Difference(origin.Chunk.Y, origin.Offset.Y, position.Chunk.Y, position.Offset.Y, frame.Settings.ChunkSizeExponent, ref dy);
        SpatialKernel.Difference(origin.Chunk.Z, origin.Offset.Z, position.Chunk.Z, position.Offset.Z, frame.Settings.ChunkSizeExponent, ref dz);

        if (!SpatialKernel.WithinExtent(dx, frame.Minimum.X, frame.Maximum.X)
            || !SpatialKernel.WithinExtent(dy, frame.Minimum.Y, frame.Maximum.Y)
            || !SpatialKernel.WithinExtent(dz, frame.Minimum.Z, frame.Maximum.Z))
        {
            relativePosition = default;
            return false;
        }

        var width = frame.Settings.ChunkWidth;
        relativePosition = new Vector3(
            SpatialKernel.Nearby(origin.Chunk.X, origin.Offset.X, position.Chunk.X, position.Offset.X, width, dx),
            SpatialKernel.Nearby(origin.Chunk.Y, origin.Offset.Y, position.Chunk.Y, position.Offset.Y, width, dy),
            SpatialKernel.Nearby(origin.Chunk.Z, origin.Offset.Z, position.Chunk.Z, position.Offset.Z, width, dz));
        return true;
    }

    private static void ValidateConversion(SpatialProfileId sourceProfileId, SpatialSettings sourceSettings, SpatialProfileId targetProfileId, SpatialSettings targetSettings)
    {
        SpatialKernel.ValidateProfile(sourceProfileId, sourceSettings);
        SpatialKernel.ValidateProfile(targetProfileId, targetSettings);
    }
}
