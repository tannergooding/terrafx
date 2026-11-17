// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using BigInteger = System.Numerics.BigInteger;
using TerraFX.Graphics;
using TerraFX.Numerics;
using TerraFX.Scenes;

namespace TerraFX.Rendering;

internal static class RenderMeshLodSelectionKernels
{
    internal static RenderMeshLodSelectionResult SelectLevel(
        RenderMeshBounds bounds,
        ImmutableArray<float> thresholds,
        Matrix4x4 localToWorld,
        Matrix4x4 viewProjection,
        RenderArea viewport,
        CameraProjectionKind projectionKind,
        byte? previousLevel,
        bool boundsQualified)
    {
        ValidateBounds(bounds);
        ValidateThresholds(thresholds);

        if (!viewport.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(viewport));
        }

        if (!Enum.IsDefined(projectionKind))
        {
            throw new ArgumentOutOfRangeException(nameof(projectionKind));
        }

        if (!localToWorld.IsFinite().All())
        {
            throw new ArgumentException("The Mesh instance transform must be finite.", nameof(localToWorld));
        }

        if ((localToWorld.Row1.W != 0.0f)
            || (localToWorld.Row2.W != 0.0f)
            || (localToWorld.Row3.W != 0.0f)
            || (localToWorld.Row4.W != 1.0f))
        {
            throw new ArgumentException("The Mesh instance transform must be affine.", nameof(localToWorld));
        }

        if (!viewProjection.IsFinite().All())
        {
            throw new ArgumentException("The camera view-projection matrix must be finite.", nameof(viewProjection));
        }

        if (previousLevel is byte prior && (prior > thresholds.Length))
        {
            throw new ArgumentOutOfRangeException(nameof(previousLevel));
        }

        if (!boundsQualified)
        {
            return new RenderMeshLodSelectionResult(0, RenderMeshLodSelectionDiagnostic.UnqualifiedBounds);
        }

        return GetExactPixelSize(bounds, thresholds, localToWorld, viewProjection, viewport, projectionKind, previousLevel);
    }

    private static RenderMeshLodSelectionResult ChooseLevel(
        ImmutableArray<float> thresholds,
        ExactRational pixelSize,
        byte? previousLevel)
    {
        if (previousLevel is not byte prior)
        {
            for (var index = 0; index < thresholds.Length; index++)
            {
                if (pixelSize.CompareTo(ExactRational.FromSingle(thresholds[index])) >= 0)
                {
                    return new RenderMeshLodSelectionResult(checked((byte)index), RenderMeshLodSelectionDiagnostic.None);
                }
            }

            return new RenderMeshLodSelectionResult(checked((byte)thresholds.Length), RenderMeshLodSelectionDiagnostic.None);
        }

        var level = checked((int)prior);

        while (level < thresholds.Length)
        {
            var threshold = ExactRational.FromSingle(thresholds[level]);

            if (pixelSize.Multiply(10).CompareTo(threshold.Multiply(9)) >= 0)
            {
                break;
            }

            level++;
        }

        while (level > 0)
        {
            var threshold = ExactRational.FromSingle(thresholds[level - 1]);

            if (pixelSize.Multiply(10).CompareTo(threshold.Multiply(11)) <= 0)
            {
                break;
            }

            level--;
        }

        return new RenderMeshLodSelectionResult(checked((byte)level), RenderMeshLodSelectionDiagnostic.None);
    }

    private static (int Significand, int Exponent) DecodeSingle(float value)
    {
        var bits = BitConverter.SingleToUInt32Bits(value);
        var exponentBits = checked((int)((bits >> 23) & 0xFF));
        var fraction = bits & 0x007F_FFFF;

        if (exponentBits == 0xFF)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Exact Mesh LOD comparisons require finite binary32 values.");
        }

        var significand = (exponentBits == 0)
            ? checked((int)fraction)
            : checked((int)(fraction | 0x0080_0000));
        var exponent = (exponentBits == 0) ? -149 : (exponentBits - 127 - 23);

        if ((bits & 0x8000_0000) != 0)
        {
            significand = -significand;
        }

        return (significand, significand == 0 ? 0 : exponent);
    }

    private static RenderMeshLodSelectionResult GetExactPixelSize(
        RenderMeshBounds bounds,
        ImmutableArray<float> thresholds,
        Matrix4x4 localToWorld,
        Matrix4x4 viewProjection,
        RenderArea viewport,
        CameraProjectionKind projectionKind,
        byte? previousLevel)
    {
        var minimumX = default(ExactRational);
        var maximumX = default(ExactRational);
        var minimumY = default(ExactRational);
        var maximumY = default(ExactRational);

        for (var corner = 0; corner < 8; corner++)
        {
            var point = new Vector4(
                ((corner & 1) == 0) ? bounds.Minimum.X : bounds.Maximum.X,
                ((corner & 2) == 0) ? bounds.Minimum.Y : bounds.Maximum.Y,
                ((corner & 4) == 0) ? bounds.Minimum.Z : bounds.Maximum.Z,
                1.0f);
            var world = Vector4.Transform(point, localToWorld);

            if (!world.IsFinite().All())
            {
                return new RenderMeshLodSelectionResult(0, RenderMeshLodSelectionDiagnostic.UnrepresentableProjection);
            }

            var clip = Vector4.Transform(world, viewProjection);

            if (!clip.IsFinite().All())
            {
                return new RenderMeshLodSelectionResult(0, RenderMeshLodSelectionDiagnostic.UnrepresentableProjection);
            }

            if (clip.W <= 0.0f)
            {
                return new RenderMeshLodSelectionResult(
                    0,
                    projectionKind is CameraProjectionKind.Perspective
                        ? RenderMeshLodSelectionDiagnostic.PerspectiveNearPlane
                        : RenderMeshLodSelectionDiagnostic.UnrepresentableProjection);
            }

            if ((projectionKind is CameraProjectionKind.Perspective) && (clip.Z >= clip.W))
            {
                return new RenderMeshLodSelectionResult(0, RenderMeshLodSelectionDiagnostic.PerspectiveNearPlane);
            }

            var ndcX = ExactRational.FromRatio(clip.X, clip.W);
            var ndcY = ExactRational.FromRatio(clip.Y, clip.W);

            if (corner == 0)
            {
                minimumX = maximumX = ndcX;
                minimumY = maximumY = ndcY;
            }
            else
            {
                if (ndcX.CompareTo(minimumX) < 0)
                {
                    minimumX = ndcX;
                }

                if (ndcX.CompareTo(maximumX) > 0)
                {
                    maximumX = ndcX;
                }

                if (ndcY.CompareTo(minimumY) < 0)
                {
                    minimumY = ndcY;
                }

                if (ndcY.CompareTo(maximumY) > 0)
                {
                    maximumY = ndcY;
                }
            }
        }

        var horizontalPixels = maximumX.Subtract(minimumX).Multiply(viewport.Width);
        var verticalPixels = maximumY.Subtract(minimumY).Multiply(viewport.Height);
        var maximumPixels = horizontalPixels.CompareTo(verticalPixels) >= 0 ? horizontalPixels : verticalPixels;
        var size = maximumPixels.Divide(2L * viewport.Height);
        return ChooseLevel(thresholds, size, previousLevel);
    }

    private static void ValidateBounds(RenderMeshBounds bounds)
    {
        if (!bounds.Minimum.IsFinite().All()
            || !bounds.Maximum.IsFinite().All()
            || (bounds.Minimum.X > bounds.Maximum.X)
            || (bounds.Minimum.Y > bounds.Maximum.Y)
            || (bounds.Minimum.Z > bounds.Maximum.Z))
        {
            throw new ArgumentException("The Mesh LOD bounds must be finite and ordered.", nameof(bounds));
        }
    }

    private static void ValidateThresholds(ImmutableArray<float> thresholds)
    {
        if (thresholds.IsDefault || (thresholds.Length > 7))
        {
            throw new ArgumentException("Mesh LOD thresholds must be initialized and contain at most seven boundaries.", nameof(thresholds));
        }

        var previousThreshold = float.PositiveInfinity;

        foreach (var threshold in thresholds)
        {
            if (!float.IsFinite(threshold) || (threshold <= 0.0f) || (threshold >= previousThreshold))
            {
                throw new ArgumentException("Mesh LOD thresholds must be finite, positive, and strictly decreasing.", nameof(thresholds));
            }

            previousThreshold = threshold;
        }
    }

    internal enum RenderMeshLodSelectionDiagnostic : byte
    {
        None,
        UnqualifiedBounds,
        PerspectiveNearPlane,
        UnrepresentableProjection,
    }

    internal readonly record struct RenderMeshLodSelectionResult(
        byte LevelIndex,
        RenderMeshLodSelectionDiagnostic Diagnostic);

    private readonly struct ExactRational
    {
        private readonly BigInteger _denominator;
        private readonly BigInteger _numerator;

        private ExactRational(BigInteger numerator, BigInteger denominator)
        {
            _numerator = numerator;
            _denominator = denominator;
        }

        public static ExactRational FromRatio(float numerator, float denominator)
        {
            var (numeratorSignificand, numeratorExponent) = DecodeSingle(numerator);
            var (denominatorSignificand, denominatorExponent) = DecodeSingle(denominator);
            var numeratorValue = new BigInteger(numeratorSignificand);
            var denominatorValue = new BigInteger(denominatorSignificand);
            var exponent = numeratorExponent - denominatorExponent;

            if (exponent >= 0)
            {
                numeratorValue <<= exponent;
            }
            else
            {
                denominatorValue <<= -exponent;
            }

            return new ExactRational(numeratorValue, denominatorValue);
        }

        public static ExactRational FromSingle(float value)
        {
            var (significandValue, exponent) = DecodeSingle(value);
            var significand = new BigInteger(significandValue);

            return (exponent >= 0)
                ? new ExactRational(significand << exponent, BigInteger.One)
                : new ExactRational(significand, BigInteger.One << -exponent);
        }

        public int CompareTo(ExactRational other)
            => (_numerator * other._denominator).CompareTo(other._numerator * _denominator);

        public ExactRational Divide(long divisor)
            => new(_numerator, _denominator * divisor);

        public ExactRational Multiply(long multiplier)
            => new(_numerator * multiplier, _denominator);

        public ExactRational Subtract(ExactRational other)
            => new(
                (_numerator * other._denominator) - (other._numerator * _denominator),
                _denominator * other._denominator);
    }
}
