// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX.Graphics;
using TerraFX.Numerics;
using TerraFX.Scenes;

namespace TerraFX.Rendering;

internal static class RenderMeshLodSelectionKernels
{
    // Exact cross-products of finite binary32 clip ratios and Int32 viewport dimensions fit in 18 limbs.
    private const int ExactWorkspaceLength = BoundedRational.MaximumLimbCount * BoundedRational.WorkspaceRegionCount;

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
        BoundedRational pixelSize,
        byte? previousLevel)
    {
        Span<ulong> thresholdWorkspace = stackalloc ulong[ExactWorkspaceLength];
        var threshold = new BoundedRational(thresholdWorkspace);

        if (previousLevel is not byte prior)
        {
            for (var index = 0; index < thresholds.Length; index++)
            {
                threshold.SetSingle(thresholds[index]);

                if (pixelSize.CompareTo(threshold) >= 0)
                {
                    return new RenderMeshLodSelectionResult(checked((byte)index), RenderMeshLodSelectionDiagnostic.None);
                }
            }

            return new RenderMeshLodSelectionResult(checked((byte)thresholds.Length), RenderMeshLodSelectionDiagnostic.None);
        }

        Span<ulong> scaledPixelWorkspace = stackalloc ulong[ExactWorkspaceLength];
        Span<ulong> scaledThresholdWorkspace = stackalloc ulong[ExactWorkspaceLength];
        var scaledPixel = new BoundedRational(scaledPixelWorkspace);
        var scaledThreshold = new BoundedRational(scaledThresholdWorkspace);
        var level = checked((int)prior);

        while (level < thresholds.Length)
        {
            scaledPixel.CopyFrom(pixelSize);
            scaledPixel.MultiplyNumerator(10);
            scaledThreshold.SetSingle(thresholds[level]);
            scaledThreshold.MultiplyNumerator(9);

            if (scaledPixel.CompareTo(scaledThreshold) >= 0)
            {
                break;
            }

            level++;
        }

        while (level > 0)
        {
            scaledPixel.CopyFrom(pixelSize);
            scaledPixel.MultiplyNumerator(10);
            scaledThreshold.SetSingle(thresholds[level - 1]);
            scaledThreshold.MultiplyNumerator(11);

            if (scaledPixel.CompareTo(scaledThreshold) <= 0)
            {
                break;
            }

            level--;
        }

        return new RenderMeshLodSelectionResult(checked((byte)level), RenderMeshLodSelectionDiagnostic.None);
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
        Span<ulong> minimumXWorkspace = stackalloc ulong[ExactWorkspaceLength];
        Span<ulong> maximumXWorkspace = stackalloc ulong[ExactWorkspaceLength];
        Span<ulong> minimumYWorkspace = stackalloc ulong[ExactWorkspaceLength];
        Span<ulong> maximumYWorkspace = stackalloc ulong[ExactWorkspaceLength];
        Span<ulong> candidateWorkspace = stackalloc ulong[ExactWorkspaceLength];
        Span<ulong> horizontalWorkspace = stackalloc ulong[ExactWorkspaceLength];
        Span<ulong> verticalWorkspace = stackalloc ulong[ExactWorkspaceLength];
        var minimumX = new BoundedRational(minimumXWorkspace);
        var maximumX = new BoundedRational(maximumXWorkspace);
        var minimumY = new BoundedRational(minimumYWorkspace);
        var maximumY = new BoundedRational(maximumYWorkspace);
        var candidate = new BoundedRational(candidateWorkspace);
        var horizontal = new BoundedRational(horizontalWorkspace);
        var vertical = new BoundedRational(verticalWorkspace);
        var hasProjection = false;

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

            candidate.SetRatio(clip.X, clip.W);

            if (!hasProjection)
            {
                minimumX.CopyFrom(candidate);
                maximumX.CopyFrom(candidate);
            }
            else
            {
                if (candidate.CompareTo(minimumX) < 0)
                {
                    minimumX.CopyFrom(candidate);
                }

                if (candidate.CompareTo(maximumX) > 0)
                {
                    maximumX.CopyFrom(candidate);
                }
            }

            candidate.SetRatio(clip.Y, clip.W);

            if (!hasProjection)
            {
                minimumY.CopyFrom(candidate);
                maximumY.CopyFrom(candidate);
            }
            else
            {
                if (candidate.CompareTo(minimumY) < 0)
                {
                    minimumY.CopyFrom(candidate);
                }

                if (candidate.CompareTo(maximumY) > 0)
                {
                    maximumY.CopyFrom(candidate);
                }
            }

            hasProjection = true;
        }

        horizontal.SetDifference(maximumX, minimumX);
        horizontal.MultiplyNumerator((ulong)viewport.Width);
        vertical.SetDifference(maximumY, minimumY);
        vertical.MultiplyNumerator((ulong)viewport.Height);
        var viewportDiameter = checked((ulong)(2L * viewport.Height));

        if (horizontal.CompareTo(vertical) >= 0)
        {
            horizontal.MultiplyDenominator(viewportDiameter);
            return ChooseLevel(thresholds, horizontal, previousLevel);
        }

        vertical.MultiplyDenominator(viewportDiameter);
        return ChooseLevel(thresholds, vertical, previousLevel);
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

}
