// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using NUnit.Framework;
using TerraFX.Graphics;
using TerraFX.Numerics;
using TerraFX.Rendering;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Rendering;

[TestFixture]
internal sealed class RenderMeshLodSelectionKernelsTests
{
    private static RenderMeshBounds BoundsForPixelSize(float size)
        => new(new Vector3(-size, 0, 0), new Vector3(size, 0, 0), Vector3.Zero, 1);

    private static RenderMeshLodSelectionKernels.RenderMeshLodSelectionResult SelectAtPixelSize(
        float size,
        ImmutableArray<float> thresholds,
        byte? previousLevel,
        bool boundsQualified = true,
        CameraProjectionKind projectionKind = CameraProjectionKind.Orthographic)
        => RenderMeshLodSelectionKernels.SelectLevel(
            BoundsForPixelSize(size),
            thresholds,
            Matrix4x4.Identity,
            Matrix4x4.Identity,
            new RenderArea(0, 0, 100, 100),
            projectionKind,
            previousLevel,
            boundsQualified);

    [Test]
    public void ExactHysteresisBoundariesUseStrictRationalComparisons()
    {
        var thresholds = ImmutableArray.Create(0.625f);
        var lower = 0.5625f;
        var upper = 0.6875f;

        Assert.Multiple(() => {
            Assert.That(SelectAtPixelSize(float.BitDecrement(lower), thresholds, previousLevel: 0).LevelIndex, Is.EqualTo(1));
            Assert.That(SelectAtPixelSize(lower, thresholds, previousLevel: 0).LevelIndex, Is.Zero);
            Assert.That(SelectAtPixelSize(float.BitIncrement(lower), thresholds, previousLevel: 0).LevelIndex, Is.Zero);
            Assert.That(SelectAtPixelSize(float.BitDecrement(upper), thresholds, previousLevel: 1).LevelIndex, Is.EqualTo(1));
            Assert.That(SelectAtPixelSize(upper, thresholds, previousLevel: 1).LevelIndex, Is.EqualTo(1));
            Assert.That(SelectAtPixelSize(float.BitIncrement(upper), thresholds, previousLevel: 1).LevelIndex, Is.Zero);
        });
    }

    [Test]
    public void HysteresisUsesAdjacentMovesForLargeJumps()
    {
        var thresholds = ImmutableArray.Create(0.8f, 0.4f, 0.2f);

        Assert.Multiple(() => {
            Assert.That(SelectAtPixelSize(0.1f, thresholds, previousLevel: 0).LevelIndex, Is.EqualTo(3));
            Assert.That(SelectAtPixelSize(1.0f, thresholds, previousLevel: 3).LevelIndex, Is.Zero);
        });
    }

    [Test]
    public void InitialSelectionUsesTheFirstSatisfiedThreshold()
    {
        var thresholds = ImmutableArray.Create(0.75f, 0.4f, 0.2f);

        Assert.Multiple(() => {
            Assert.That(SelectAtPixelSize(0.75f, thresholds, previousLevel: null).LevelIndex, Is.Zero);
            Assert.That(SelectAtPixelSize(0.5f, thresholds, previousLevel: null).LevelIndex, Is.EqualTo(1));
            Assert.That(SelectAtPixelSize(0.3f, thresholds, previousLevel: null).LevelIndex, Is.EqualTo(2));
            Assert.That(SelectAtPixelSize(0.1f, thresholds, previousLevel: null).LevelIndex, Is.EqualTo(3));
        });
    }

    [Test]
    public void InitialSelectionUsesTheQuantizedCameraViewport()
    {
        var bounds = new RenderMeshBounds(
            new Vector3(-0.5f, -0.25f, 0),
            new Vector3(0.5f, 0.25f, 0),
            Vector3.Zero,
            1);
        var thresholds = ImmutableArray.Create(0.75f);

        var wide = RenderMeshLodSelectionKernels.SelectLevel(
            bounds,
            thresholds,
            Matrix4x4.Identity,
            Matrix4x4.Identity,
            new RenderArea(0, 0, 800, 400),
            CameraProjectionKind.Orthographic,
            previousLevel: null,
            boundsQualified: true);
        var square = RenderMeshLodSelectionKernels.SelectLevel(
            bounds,
            thresholds,
            Matrix4x4.Identity,
            Matrix4x4.Identity,
            new RenderArea(0, 0, 400, 400),
            CameraProjectionKind.Orthographic,
            previousLevel: null,
            boundsQualified: true);

        Assert.That(wide.LevelIndex, Is.Zero);
        Assert.That(square.LevelIndex, Is.EqualTo(1));
    }

    [Test]
    public void InvalidSourceMatricesRejectAndDerivedOverflowFallsBack()
    {
        var invalidMatrix = new Matrix4x4(
            new Vector4(float.NaN, 0, 0, 0),
            Vector4.UnitY,
            Vector4.UnitZ,
            Vector4.UnitW);

        _ = Assert.Throws<ArgumentException>(() => RenderMeshLodSelectionKernels.SelectLevel(
            BoundsForPixelSize(0.5f),
            [0.5f],
            invalidMatrix,
            Matrix4x4.Identity,
            new RenderArea(0, 0, 100, 100),
            CameraProjectionKind.Perspective,
            previousLevel: null,
            boundsQualified: true));

        var projectiveInstance = new Matrix4x4(
            new Vector4(1, 0, 0, 0.25f),
            Vector4.UnitY,
            Vector4.UnitZ,
            Vector4.UnitW);
        _ = Assert.Throws<ArgumentException>(() => RenderMeshLodSelectionKernels.SelectLevel(
            BoundsForPixelSize(0.5f),
            [0.5f],
            projectiveInstance,
            Matrix4x4.Identity,
            new RenderArea(0, 0, 100, 100),
            CameraProjectionKind.Orthographic,
            previousLevel: null,
            boundsQualified: true));

        var largeScale = new Matrix4x4(
            new Vector4(float.MaxValue, 0, 0, 0),
            Vector4.UnitY,
            Vector4.UnitZ,
            Vector4.UnitW);
        var overflowingBounds = new RenderMeshBounds(
            new Vector3(0, 0, 0),
            new Vector3(2, 0, 0),
            Vector3.Zero,
            1);
        var result = RenderMeshLodSelectionKernels.SelectLevel(
            overflowingBounds,
            [0.5f],
            largeScale,
            Matrix4x4.Identity,
            new RenderArea(0, 0, 100, 100),
            CameraProjectionKind.Orthographic,
            previousLevel: null,
            boundsQualified: true);

        Assert.That(result.LevelIndex, Is.Zero);
        Assert.That(result.Diagnostic, Is.EqualTo(RenderMeshLodSelectionKernels.RenderMeshLodSelectionDiagnostic.UnrepresentableProjection));
    }

    [Test]
    public void PerspectiveNearPlaneAndUnqualifiedBoundsConservativelySelectFinest()
    {
        var nearPlaneBounds = new RenderMeshBounds(
            new Vector3(-0.5f, -0.5f, 0),
            new Vector3(0.5f, 0.5f, 1),
            Vector3.Zero,
            1);
        var perspective = RenderMeshLodSelectionKernels.SelectLevel(
            nearPlaneBounds,
            [0.5f],
            Matrix4x4.Identity,
            Matrix4x4.Identity,
            new RenderArea(0, 0, 100, 100),
            CameraProjectionKind.Perspective,
            previousLevel: null,
            boundsQualified: true);
        var unqualified = SelectAtPixelSize(
            0.25f,
            [0.5f],
            previousLevel: null,
            boundsQualified: false);
        var orthographic = RenderMeshLodSelectionKernels.SelectLevel(
            nearPlaneBounds,
            [0.5f],
            Matrix4x4.Identity,
            Matrix4x4.Identity,
            new RenderArea(0, 0, 100, 100),
            CameraProjectionKind.Orthographic,
            previousLevel: null,
            boundsQualified: true);

        Assert.Multiple(() => {
            Assert.That(perspective.LevelIndex, Is.Zero);
            Assert.That(perspective.Diagnostic, Is.EqualTo(RenderMeshLodSelectionKernels.RenderMeshLodSelectionDiagnostic.PerspectiveNearPlane));
            Assert.That(unqualified.LevelIndex, Is.Zero);
            Assert.That(unqualified.Diagnostic, Is.EqualTo(RenderMeshLodSelectionKernels.RenderMeshLodSelectionDiagnostic.UnqualifiedBounds));
            Assert.That(orthographic.Diagnostic, Is.EqualTo(RenderMeshLodSelectionKernels.RenderMeshLodSelectionDiagnostic.None));
        });
    }

    [Test]
    public void SingleBoundaryHysteresisMatchesTheOwnerSequence()
    {
        var thresholds = ImmutableArray.Create(0.5f);
        var sizes = new[] { 0.50f, 0.46f, 0.44f, 0.54f, 0.56f };
        var expected = new byte[] { 0, 0, 1, 1, 0 };
        byte? previousLevel = null;

        for (var index = 0; index < sizes.Length; index++)
        {
            var result = SelectAtPixelSize(sizes[index], thresholds, previousLevel);
            Assert.That(result.LevelIndex, Is.EqualTo(expected[index]));
            previousLevel = result.LevelIndex;
        }
    }
}
