// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX;
using TerraFX.Graphics;
using TerraFX.Numerics;
using TerraFX.Rendering;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Rendering;

[TestFixture]
internal sealed class RenderingKernelsTests
{
    private static void ComputeCascadeSplitsWithEmptyRange()
    {
        Span<float> splits = stackalloc float[5];
        RenderingKernels.ComputeCascadeSplits(10.0f, 10.0f, perspective: false, splits);
    }

    private static void ComputeCascadeSplitsWithShortDestination()
    {
        Span<float> splits = stackalloc float[4];
        RenderingKernels.ComputeCascadeSplits(1.0f, 10.0f, perspective: false, splits);
    }

    private static void ComputePerspectiveSplitsWithNegativeNear()
    {
        Span<float> splits = stackalloc float[5];
        RenderingKernels.ComputeCascadeSplits(-1.0f, 10.0f, perspective: true, splits);
    }

    private static CameraSnapshot CreateCascadeCamera(
        CameraProjectionKind projectionKind,
        float near,
        float far,
        float orthographicHeight,
        float aspectRatio,
        SpatialPosition3 position,
        Quaternion? rotation = null,
        CameraFarMode farMode = CameraFarMode.Finite)
    {
        var origin = default(SpatialPosition3);
        var frame = new SpatialFrame3(
            1,
            SpatialProfileId.Default,
            SpatialSettings.Default,
            origin,
            1,
            new Vector3(-4096),
            new Vector3(4096));
        return new CameraSnapshot(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            order: 0,
            frame,
            new PixelSize(16, 16),
            position,
            rotation ?? Quaternion.Identity,
            projectionKind,
            (float)Math.PI / 2.0f,
            orthographicHeight,
            near,
            farMode,
            farMode is CameraFarMode.Finite ? far : null,
            aspectRatio,
            new Vector4(0.0f, 0.0f, 1.0f, 1.0f),
            CameraClearFlags.Color,
            Vector4.Zero,
            ulong.MaxValue);
    }

    [Test]
    public void AmbientAndBrdfKernelsRespectFiniteAndGrazingGuards()
    {
        var ambient = RenderingKernels.EvaluateAmbientDiffuse(
            new Vector3(0.8f, 0.4f, 0.2f),
            0.0f,
            1.0f,
            new Vector3(MathF.PI));
        Assert.That(ambient, Is.EqualTo(new Vector3(0.8f, 0.4f, 0.2f)));

        var normal = Vector3.UnitZ;
        var radiance = new Vector3(2.0f, 3.0f, 4.0f);
        var specularOnly = RenderingKernels.EvaluateBrdf(normal, normal, normal, Vector3.One, 1.0f, 1.0f, radiance);
        Assert.That(specularOnly.X, Is.EqualTo(2.0f / (4.0f * MathF.PI)).Within(1.0e-6f));
        Assert.That(specularOnly.Y, Is.EqualTo(3.0f / (4.0f * MathF.PI)).Within(1.0e-6f));
        Assert.That(specularOnly.Z, Is.EqualTo(4.0f / (4.0f * MathF.PI)).Within(1.0e-6f));

        const float minimumRoughness = 0.045f;
        var minimumRoughnessResult = RenderingKernels.EvaluateBrdf(
            normal,
            normal,
            normal,
            Vector3.One,
            1.0f,
            minimumRoughness,
            Vector3.One);
        Assert.That(float.IsFinite(minimumRoughnessResult.X), Is.True);
        Assert.That(minimumRoughnessResult.X, Is.GreaterThan(0.0f));

        Assert.That(
            RenderingKernels.EvaluateBrdf(normal, Vector3.UnitX, normal, Vector3.One, 0.0f, 0.045f, radiance),
            Is.EqualTo(Vector3.Zero));
        Assert.That(
            RenderingKernels.EvaluateBrdf(normal, normal, -normal, Vector3.One, 0.5f, 1.0f, radiance),
            Is.EqualTo(Vector3.Zero));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => RenderingKernels.EvaluateBrdf(normal, normal, normal, Vector3.One, 0.0f, 0.044f, radiance));
        _ = Assert.Throws<ArgumentException>(() => RenderingKernels.EvaluateBrdf(
            normal,
            normal,
            normal,
            new Vector3(float.MaxValue),
            0.0f,
            1.0f,
            new Vector3(float.MaxValue)));
    }

    [Test]
    public void CascadeSplitsSupportPerspectiveAndSignedOrthographicNear()
    {
        Span<float> splits = stackalloc float[5];
        RenderingKernels.ComputeCascadeSplits(1.0f, 10.0f, perspective: false, splits);
        float[] positiveExpected = [1.0f, 3.25f, 5.5f, 7.75f, 10.0f];
        Assert.That(splits.ToArray(), Is.EqualTo(positiveExpected));

        RenderingKernels.ComputeCascadeSplits(-1.0f, 10.0f, perspective: false, splits);
        float[] signedExpected = [-1.0f, 1.75f, 4.5f, 7.25f, 10.0f];
        Assert.That(splits.ToArray(), Is.EqualTo(signedExpected));

        RenderingKernels.ComputeCascadeSplits(1.0f, 10.0f, perspective: true, splits);
        Assert.That(splits[0], Is.EqualTo(1.0f));
        Assert.That(splits[1], Is.EqualTo(2.5141397f).Within(1.0e-6f));
        Assert.That(splits[2], Is.EqualTo(4.3311386f).Within(1.0e-6f));
        Assert.That(splits[3], Is.EqualTo(6.6867065f).Within(1.0e-6f));
        Assert.That(splits[4], Is.EqualTo(10.0f));

        _ = Assert.Throws<ArgumentException>(ComputeCascadeSplitsWithShortDestination);
        _ = Assert.Throws<ArgumentOutOfRangeException>(ComputePerspectiveSplitsWithNegativeNear);
    }

    [Test]
    public void ColorConversionsPremultiplyLinearValuesAndToneMapOnce()
    {
        Assert.That(RenderingKernels.DecodeSrgb(0.04045f), Is.EqualTo(0.04045f / 12.92f).Within(1.0e-7f));
        Assert.That(RenderingKernels.DecodeSrgb(1.0f), Is.EqualTo(1.0f));
        Assert.That(RenderingKernels.EncodeSrgb(0.0031308f), Is.EqualTo(0.0031308f * 12.92f).Within(1.0e-7f));
        Assert.That(
            RenderingKernels.PremultiplyLinear(new Vector4(0.25f, 0.5f, 0.75f, 0.5f)),
            Is.EqualTo(new Vector4(0.125f, 0.25f, 0.375f, 0.5f)));
        Assert.That(RenderingKernels.ToneMapReinhard(new Vector3(0.0f, 1.0f, 3.0f)), Is.EqualTo(new Vector3(0.0f, 0.5f, 0.75f)));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => RenderingKernels.ToneMapReinhard(new Vector3(-0.01f, 0.0f, 0.0f)));
    }

    [Test]
    public void DirectionalCascadeCornersRespectPerspectiveAndSignedOrthographicDepth()
    {
        var corners = new Vector3[8];
        var perspectiveCamera = CreateCascadeCamera(
            CameraProjectionKind.Perspective,
            near: 0.1f,
            far: 10.0f,
            orthographicHeight: 4.0f,
            aspectRatio: 2.0f,
            new SpatialPosition3(default, new Vector3(1.0f, 2.0f, 3.0f)));
        RenderingKernels.CreateDirectionalCascadeCorners(perspectiveCamera, 2.0f, 4.0f, corners);
        Vector3[] perspectiveExpected = [
            new Vector3(-3.0f, 0.0f, 5.0f),
            new Vector3(-3.0f, 4.0f, 5.0f),
            new Vector3(5.0f, 4.0f, 5.0f),
            new Vector3(5.0f, 0.0f, 5.0f),
            new Vector3(-7.0f, -2.0f, 7.0f),
            new Vector3(-7.0f, 6.0f, 7.0f),
            new Vector3(9.0f, 6.0f, 7.0f),
            new Vector3(9.0f, -2.0f, 7.0f),
        ];
        Assert.That(corners, Is.EqualTo(perspectiveExpected));

        var rotatedPerspectiveCamera = CreateCascadeCamera(
            CameraProjectionKind.Perspective,
            near: 0.1f,
            far: 10.0f,
            orthographicHeight: 4.0f,
            aspectRatio: 2.0f,
            default,
            new Quaternion(0.0f, 1.0f, 0.0f, 0.0f));
        RenderingKernels.CreateDirectionalCascadeCorners(rotatedPerspectiveCamera, 2.0f, 4.0f, corners);
        Vector3[] rotatedExpected = [
            new Vector3(4.0f, -2.0f, -2.0f),
            new Vector3(4.0f, 2.0f, -2.0f),
            new Vector3(-4.0f, 2.0f, -2.0f),
            new Vector3(-4.0f, -2.0f, -2.0f),
            new Vector3(8.0f, -4.0f, -4.0f),
            new Vector3(8.0f, 4.0f, -4.0f),
            new Vector3(-8.0f, 4.0f, -4.0f),
            new Vector3(-8.0f, -4.0f, -4.0f),
        ];
        Assert.That(corners, Is.EqualTo(rotatedExpected));

        var orthographicCamera = CreateCascadeCamera(
            CameraProjectionKind.Orthographic,
            near: -1.0f,
            far: 5.0f,
            orthographicHeight: 4.0f,
            aspectRatio: 1.5f,
            default);
        RenderingKernels.CreateDirectionalCascadeCorners(orthographicCamera, -1.0f, 2.0f, corners);
        Vector3[] orthographicExpected = [
            new Vector3(-3.0f, -2.0f, -1.0f),
            new Vector3(-3.0f, 2.0f, -1.0f),
            new Vector3(3.0f, 2.0f, -1.0f),
            new Vector3(3.0f, -2.0f, -1.0f),
            new Vector3(-3.0f, -2.0f, 2.0f),
            new Vector3(-3.0f, 2.0f, 2.0f),
            new Vector3(3.0f, 2.0f, 2.0f),
            new Vector3(3.0f, -2.0f, 2.0f),
        ];
        Assert.That(corners, Is.EqualTo(orthographicExpected));

        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => RenderingKernels.CreateDirectionalCascadeCorners(perspectiveCamera, 0.0f, 4.0f, corners));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => RenderingKernels.CreateDirectionalCascadeCorners(orthographicCamera, 2.0f, 2.0f, corners));
    }

    [TestCase(CameraProjectionKind.Perspective, 0.5f, 200.0f, CameraFarMode.Finite, 100.0f)]
    [TestCase(CameraProjectionKind.Perspective, 0.5f, 80.0f, CameraFarMode.Finite, 80.0f)]
    [TestCase(CameraProjectionKind.Perspective, 0.5f, 100.0f, CameraFarMode.Infinite, 100.0f)]
    [TestCase(CameraProjectionKind.Orthographic, -1.0f, 50.0f, CameraFarMode.Finite, 50.0f)]
    public void DirectionalShadowCascadesClampDistanceAndBuildEveryProjection(
        CameraProjectionKind projectionKind,
        float near,
        float far,
        CameraFarMode farMode,
        float expectedEnd)
    {
        var camera = CreateCascadeCamera(
            projectionKind,
            near,
            far,
            orthographicHeight: 8.0f,
            aspectRatio: 1.5f,
            position: new SpatialPosition3(default, new Vector3(0.0f, 0.0f, -5.0f)),
            farMode: farMode);
        Span<float> splits = stackalloc float[5];
        Span<Matrix4x4> viewProjections = stackalloc Matrix4x4[4];

        RenderingKernels.CreateDirectionalShadowCascades(
            camera,
            new Vector3(1.0f, -2.0f, 3.0f),
            splits,
            viewProjections);

        Assert.That(splits[0], Is.EqualTo(near));
        Assert.That(splits[^1], Is.EqualTo(expectedEnd));
        Span<Vector3> corners = stackalloc Vector3[8];

        for (var cascadeIndex = 0; cascadeIndex < viewProjections.Length; cascadeIndex++)
        {
            Assert.That(splits[cascadeIndex + 1], Is.GreaterThan(splits[cascadeIndex]));
            Assert.That(viewProjections[cascadeIndex].IsFinite().All(), Is.True);

            RenderingKernels.CreateDirectionalCascadeCorners(
                camera,
                splits[cascadeIndex],
                splits[cascadeIndex + 1],
                corners);

            foreach (var corner in corners)
            {
                var clip = Vector4.Transform(new Vector4(corner, 1.0f), viewProjections[cascadeIndex]);

                Assert.That(clip.X, Is.InRange(-1.00001f, 1.00001f));
                Assert.That(clip.Y, Is.InRange(-1.00001f, 1.00001f));
                Assert.That(clip.Z, Is.InRange(-0.00001f, 1.00001f));
            }
        }
    }

    [Test]
    public void DirectionalShadowCascadesRejectInvalidRangeWithoutChangingOutputs()
    {
        var camera = CreateCascadeCamera(
            CameraProjectionKind.Perspective,
            near: 101.0f,
            far: 150.0f,
            orthographicHeight: 8.0f,
            aspectRatio: 1.5f,
            position: default);
        var splits = new float[5];
        Array.Fill(splits, -7.0f);
        var viewProjections = new Matrix4x4[4];
        var sentinel = Matrix4x4.CreateScale(2.0f);
        Array.Fill(viewProjections, sentinel);

        _ = Assert.Throws<InvalidOperationException>(
            () => RenderingKernels.CreateDirectionalShadowCascades(
                camera,
                Vector3.UnitZ,
                splits,
                viewProjections));

        Assert.That(splits, Is.EqualTo([-7.0f, -7.0f, -7.0f, -7.0f, -7.0f]));

        foreach (var viewProjection in viewProjections)
        {
            Assert.That(viewProjection, Is.EqualTo(sentinel));
        }
    }

    [Test]
    public void DirectionalShadowFitSnapsCenterAndAddsCasterMargin()
    {
        Vector3[] corners =
        [
            new Vector3(2.25f, -3.5f, -1.0f),
            new Vector3(4.25f, -3.5f, -1.0f),
            new Vector3(2.25f, 0.5f, -1.0f),
            new Vector3(4.25f, 0.5f, -1.0f),
            new Vector3(2.25f, -3.5f, 5.0f),
            new Vector3(4.25f, -3.5f, 5.0f),
            new Vector3(2.25f, 0.5f, 5.0f),
            new Vector3(4.25f, 0.5f, 5.0f)
        ];
        var (View, Center, Extent, Near, Far) = RenderingKernels.FitDirectionalShadow(corners, Vector3.UnitZ);
        var texelWorldSize = Extent / 1024.0f;

        Assert.That(View.IsFinite().All(), Is.True);
        Assert.That(Extent, Is.EqualTo(4.0f / (1.0f - (3.0f / 1024.0f))).Within(1.0e-6f));
        Assert.That(Center.X / texelWorldSize, Is.EqualTo(MathF.Round(Center.X / texelWorldSize)));
        Assert.That(Center.Y / texelWorldSize, Is.EqualTo(MathF.Round(Center.Y / texelWorldSize)));
        Assert.That(Near, Is.EqualTo(-11.0f));
        Assert.That(Far, Is.EqualTo(5.0f));
    }

    [TestCase(CameraProjectionKind.Perspective, 0.5f, 100.0f, 3.0f)]
    [TestCase(CameraProjectionKind.Orthographic, -1.0f, 50.0f, -1.0f)]
    public void DirectionalShadowViewProjectionContainsCascadeCorners(
        CameraProjectionKind projectionKind,
        float near,
        float far,
        float cascadeStart)
    {
        var camera = CreateCascadeCamera(
            projectionKind,
            near,
            far,
            orthographicHeight: 8.0f,
            aspectRatio: 1.5f,
            position: new SpatialPosition3(default, new Vector3(0.0f, 0.0f, -5.0f)));
        Span<Vector3> corners = stackalloc Vector3[8];
        RenderingKernels.CreateDirectionalCascadeCorners(camera, cascadeStart, 12.0f, corners);
        var viewProjection = RenderingKernels.CreateDirectionalShadowViewProjection(
            corners,
            new Vector3(1.0f, -2.0f, 3.0f));

        foreach (var corner in corners)
        {
            var clip = Vector4.Transform(new Vector4(corner, 1.0f), viewProjection);

            Assert.That(clip.IsFinite().All(), Is.True);
            Assert.That(clip.W, Is.EqualTo(1.0f));
            Assert.That(clip.X, Is.InRange(-1.0f, 1.0f));
            Assert.That(clip.Y, Is.InRange(-1.0f, 1.0f));
            Assert.That(clip.Z, Is.InRange(0.0f, 1.0f));
        }
    }

    [Test]
    public void DirectionalShadowViewProjectionMapsFittedDepthToReversedZ()
    {
        Vector3[] corners =
        [
            new Vector3(-2.0f, -3.0f, -1.0f),
            new Vector3(2.0f, -3.0f, -1.0f),
            new Vector3(-2.0f, 3.0f, -1.0f),
            new Vector3(2.0f, 3.0f, -1.0f),
            new Vector3(-2.0f, -3.0f, 5.0f),
            new Vector3(2.0f, -3.0f, 5.0f),
            new Vector3(-2.0f, 3.0f, 5.0f),
            new Vector3(2.0f, 3.0f, 5.0f),
        ];
        var (fitView, center, _, near, far) = RenderingKernels.FitDirectionalShadow(corners, Vector3.UnitZ);
        var lightView = fitView
            * Matrix4x4.CreateTranslation(new Vector3(-center.X, -center.Y, 0.0f));
        Assert.That(Matrix4x4.TryInvert(lightView, out var inverseLightView), Is.True);
        var viewProjection = RenderingKernels.CreateDirectionalShadowViewProjection(corners, Vector3.UnitZ);
        var nearWorld = Vector3.TransformPoint(new Vector3(0.0f, 0.0f, near), inverseLightView);
        var farWorld = Vector3.TransformPoint(new Vector3(0.0f, 0.0f, far), inverseLightView);
        var nearClip = Vector4.Transform(new Vector4(nearWorld, 1.0f), viewProjection);
        var farClip = Vector4.Transform(new Vector4(farWorld, 1.0f), viewProjection);

        Assert.That(nearClip.Z, Is.EqualTo(1.0f).Within(1.0e-5f));
        Assert.That(farClip.Z, Is.EqualTo(0.0f).Within(1.0e-5f));
    }

    [Test]
    public void NormalTransformsUseInverseTransposeAndReportMirroring()
    {
        var world = Matrix4x4.CreateScale(new Vector3(2.0f, 1.0f, 0.5f));
        var transformed = RenderingKernels.TransformNormal(Vector3.Normalize(new Vector3(1.0f, 1.0f, 0.0f)), world);
        var expected = Vector3.Normalize(new Vector3(0.5f, 1.0f, 0.0f));
        Assert.That(transformed.X, Is.EqualTo(expected.X).Within(1.0e-6f));
        Assert.That(transformed.Y, Is.EqualTo(expected.Y).Within(1.0e-6f));
        Assert.That(transformed.Z, Is.EqualTo(expected.Z).Within(1.0e-6f));
        Assert.That(RenderingKernels.GetTransformParity(world), Is.EqualTo(1));
        Assert.That(RenderingKernels.GetTransformParity(Matrix4x4.CreateScale(new Vector3(-1.0f, 1.0f, 1.0f))), Is.EqualTo(-1));
        _ = Assert.Throws<ArgumentException>(() => RenderingKernels.TransformNormal(Vector3.UnitX, Matrix4x4.CreateScale(0.0f)));
    }

    [Test]
    public void PointAndSpotLightingUseDeclaredAttenuationAndHardCutoff()
    {
        Assert.That(RenderingKernels.EvaluatePointIntensity(4.0f * MathF.PI), Is.EqualTo(1.0f));
        Assert.That(RenderingKernels.EvaluatePointRangeAttenuation(0.0f, 10.0f), Is.EqualTo(10000.0f));
        Assert.That(RenderingKernels.EvaluatePointRangeAttenuation(10.0f, 10.0f), Is.EqualTo(0.0f));
        Assert.That(RenderingKernels.EvaluatePointRangeAttenuation(5.0f, 10.0f), Is.EqualTo(0.03515625f).Within(1.0e-7f));

        var cutoff = MathF.Cos(0.25f);
        Assert.That(RenderingKernels.EvaluateSpotFactor(cutoff, 0.25f, 0.25f), Is.EqualTo(1.0f));
        Assert.That(RenderingKernels.EvaluateSpotFactor(cutoff - 0.001f, 0.25f, 0.25f), Is.EqualTo(0.0f));
        Assert.That(RenderingKernels.EvaluateSpotFactor(1.0f, 0.1f, 0.4f), Is.EqualTo(1.0f));
        Assert.That(RenderingKernels.EvaluateSpotFactor(-1.0f, 0.1f, 0.4f), Is.EqualTo(0.0f));
        _ = Assert.Throws<ArgumentException>(() => RenderingKernels.EvaluateSpotFactor(
            1.0f,
            float.Epsilon,
            MathF.BitIncrement(float.Epsilon)));
    }

    [Test]
    public void ShadowBasisAtlasPcfAndReversedZBiasMatchTheContract()
    {
        RenderingKernels.GetShadowViewBasis(Vector3.UnitY, out var right, out var up, out var forward);
        Assert.That(right, Is.EqualTo(new Vector3(0.0f, 0.0f, 1.0f)));
        Assert.That(up, Is.EqualTo(new Vector3(1.0f, 0.0f, 0.0f)));
        Assert.That(forward, Is.EqualTo(Vector3.UnitY));

        RenderingKernels.GetShadowAtlasCellOrigin(0, out var x, out var y);
        Assert.That((x, y), Is.EqualTo((0, 0)));
        RenderingKernels.GetShadowAtlasCellOrigin(4, out x, out y);
        Assert.That((x, y), Is.EqualTo((0, 1024)));
        RenderingKernels.GetShadowAtlasCellOrigin(7, out x, out y);
        Assert.That((x, y), Is.EqualTo((3072, 1024)));

        var minimum = 0.5f / 4096.0f;
        var maximum = 1023.5f / 4096.0f;
        Assert.That(RenderingKernels.GetShadowPcfSampleUv(Vector2.Zero, 0, -1, -1), Is.EqualTo(new Vector2(minimum, minimum)));
        Assert.That(RenderingKernels.GetShadowPcfSampleUv(Vector2.One, 0, 1, 1), Is.EqualTo(new Vector2(maximum, maximum)));

        var bias = RenderingKernels.ComputeReceiverBias(0.001f, 1.0f, 0.05f, 0.0f, 0.0f);
        Assert.That(bias, Is.EqualTo(0.001f));
        Assert.That(RenderingKernels.IsShadowTapLit(0.5f, bias, 0.5f), Is.True);
        Assert.That(RenderingKernels.IsShadowTapLit(0.5f, bias, 0.5005f), Is.True);
        Assert.That(RenderingKernels.IsShadowTapLit(0.5f, bias, 0.7f), Is.False);
        Assert.That(RenderingKernels.ComputeReceiverBias(0.05f, 4.0f, 0.05f, 1.0f, -2.0f), Is.EqualTo(0.05f));
    }

    [Test]
    public void ShadowInputsRejectEmptyRangesAndInvalidLightTransforms()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(ComputeCascadeSplitsWithEmptyRange);
        _ = Assert.Throws<ArgumentException>(() => RenderingKernels.GetShadowViewBasis(Vector3.Zero, out _, out _, out _));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => RenderingKernels.GetShadowAtlasCellOrigin(16, out _, out _));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => RenderingKernels.ComputeReceiverBias(0.051f, 1.0f, 0.05f, 0.0f, 0.0f));
    }

    [Test]
    public void SpriteVerticesAndUvFlipsPreserveCanonicalWinding()
    {
        var pivot = new Vector2(0.25f, 0.75f);
        var size = new Vector2(4.0f, 2.0f);
        Assert.That(RenderingKernels.GetSpriteVertex(0, pivot, size), Is.EqualTo(new Vector3(-1.0f, -1.5f, 0.0f)));
        Assert.That(RenderingKernels.GetSpriteVertex(1, pivot, size), Is.EqualTo(new Vector3(-1.0f, 0.5f, 0.0f)));
        Assert.That(RenderingKernels.GetSpriteVertex(2, pivot, size), Is.EqualTo(new Vector3(3.0f, 0.5f, 0.0f)));
        Assert.That(RenderingKernels.GetSpriteVertex(3, pivot, size), Is.EqualTo(new Vector3(3.0f, -1.5f, 0.0f)));
        var firstEdge = RenderingKernels.GetSpriteVertex(1, pivot, size) - RenderingKernels.GetSpriteVertex(0, pivot, size);
        var secondEdge = RenderingKernels.GetSpriteVertex(2, pivot, size) - RenderingKernels.GetSpriteVertex(0, pivot, size);
        Assert.That(Vector3.Cross(firstEdge, secondEdge).Z, Is.LessThan(0.0f));
        Assert.That(RenderingKernels.GetSpriteUv(0, flipX: false, flipY: false), Is.EqualTo(new Vector2(0.0f, 1.0f)));
        Assert.That(RenderingKernels.GetSpriteUv(0, flipX: true, flipY: false), Is.EqualTo(new Vector2(1.0f, 1.0f)));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => RenderingKernels.GetSpriteVertex(4, pivot, size));
    }
}
