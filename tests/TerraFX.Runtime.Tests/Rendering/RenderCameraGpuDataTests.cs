// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using NUnit.Framework;
using TerraFX;
using TerraFX.Graphics;
using TerraFX.Numerics;
using TerraFX.Rendering;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Rendering;

[TestFixture]
internal sealed class RenderCameraGpuDataTests
{
    private static CameraSnapshot CreateCamera(
        CameraProjectionKind projectionKind,
        float near,
        float? far,
        float orthographicHeight,
        CameraFarMode farMode = CameraFarMode.Finite,
        float? aspectRatioOverride = null,
        float? verticalFieldOfView = null,
        SpatialPosition3? frameOrigin = null,
        SpatialPosition3? position = null,
        Quaternion? rotation = null)
    {
        var origin = frameOrigin ?? default;
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
            position ?? origin,
            rotation ?? Quaternion.Identity,
            projectionKind,
            verticalFieldOfView ?? (float.Pi / 2.0f),
            orthographicHeight,
            near,
            farMode,
            far,
            aspectRatioOverride,
            new Vector4(0.0f, 0.0f, 1.0f, 1.0f),
            CameraClearFlags.Color,
            Vector4.Zero,
            ulong.MaxValue);
    }

    private static Vector4 ProjectClip(Matrix4x4 matrix, Vector3 position)
        => new(
            (position.X * matrix[0, 0]) + (position.Y * matrix[1, 0]) + (position.Z * matrix[2, 0]) + matrix[3, 0],
            (position.X * matrix[0, 1]) + (position.Y * matrix[1, 1]) + (position.Z * matrix[2, 1]) + matrix[3, 1],
            (position.X * matrix[0, 2]) + (position.Y * matrix[1, 2]) + (position.Z * matrix[2, 2]) + matrix[3, 2],
            (position.X * matrix[0, 3]) + (position.Y * matrix[1, 3]) + (position.Z * matrix[2, 3]) + matrix[3, 3]);

    private static float ProjectDepth(Matrix4x4 matrix, float z)
    {
        var clipZ = (z * matrix[2, 2]) + matrix[3, 2];
        var clipW = (z * matrix[2, 3]) + matrix[3, 3];
        return clipZ / clipW;
    }

    private static Matrix4x4 ReadMatrix(ReadOnlySpan<byte> bytes)
        => new(
            new Vector4(ReadSingle(bytes, 0), ReadSingle(bytes, 4), ReadSingle(bytes, 8), ReadSingle(bytes, 12)),
            new Vector4(ReadSingle(bytes, 16), ReadSingle(bytes, 20), ReadSingle(bytes, 24), ReadSingle(bytes, 28)),
            new Vector4(ReadSingle(bytes, 32), ReadSingle(bytes, 36), ReadSingle(bytes, 40), ReadSingle(bytes, 44)),
            new Vector4(ReadSingle(bytes, 48), ReadSingle(bytes, 52), ReadSingle(bytes, 56), ReadSingle(bytes, 60)));

    private static float ReadSingle(ReadOnlySpan<byte> bytes, int offset)
        => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset, sizeof(float))));

    [Test]
    public void AspectOverrideChangesProjectionWithoutChangingPixelArea()
    {
        var camera = CreateCamera(
            CameraProjectionKind.Perspective,
            near: 0.1f,
            far: 100.0f,
            orthographicHeight: 4.0f,
            aspectRatioOverride: 2.0f);
        var area = new RenderArea(2, 3, 10, 12);
        var data = RenderCameraGpuData.Create(camera, area);
        var bytes = new byte[RenderCameraGpuData.SizeInBytes];
        data.WriteTo(bytes);
        var projection = ReadMatrix(bytes);

        Assert.Multiple(() => {
            Assert.That(projection[0, 0], Is.EqualTo(0.5f).Within(1.0e-6f));
            Assert.That(projection[1, 1], Is.EqualTo(1.0f).Within(1.0e-6f));
            Assert.That(ReadSingle(bytes, 64), Is.EqualTo((float)area.X));
            Assert.That(ReadSingle(bytes, 68), Is.EqualTo((float)area.Y));
            Assert.That(ReadSingle(bytes, 72), Is.EqualTo((float)area.Width));
            Assert.That(ReadSingle(bytes, 76), Is.EqualTo((float)area.Height));
        });
    }

    [TestCase(1.0e-30f, 2.0e-30f)]
    [TestCase(2.0f, 3.0e38f)]
    public void BuildsFinitePerspectiveDepthWithStableIntermediates(float near, float far)
    {
        var camera = CreateCamera(CameraProjectionKind.Perspective, near, far, orthographicHeight: 4.0f);
        var data = RenderCameraGpuData.Create(camera, new RenderArea(0, 0, 16, 16));
        var bytes = new byte[RenderCameraGpuData.SizeInBytes];
        data.WriteTo(bytes);
        var projection = ReadMatrix(bytes);

        Assert.Multiple(() => {
            Assert.That(ProjectDepth(projection, near), Is.EqualTo(1.0f).Within(1.0e-6f));
            Assert.That(ProjectDepth(projection, far), Is.EqualTo(0.0f).Within(1.0e-6f));
        });
    }

    [Test]
    public void BuildsInfinitePerspectiveDepth()
    {
        var camera = CreateCamera(
            CameraProjectionKind.Perspective,
            near: 0.1f,
            far: null,
            orthographicHeight: 4.0f,
            farMode: CameraFarMode.Infinite);
        var data = RenderCameraGpuData.Create(camera, new RenderArea(0, 0, 16, 16));
        var bytes = new byte[RenderCameraGpuData.SizeInBytes];
        data.WriteTo(bytes);
        var projection = ReadMatrix(bytes);
        var twiceNearClip = ProjectClip(projection, new Vector3(0.0f, 0.0f, camera.Near * 2.0f));

        Assert.Multiple(() => {
            Assert.That(ProjectDepth(projection, camera.Near), Is.EqualTo(1.0f).Within(1.0e-6f));
            Assert.That(ProjectDepth(projection, camera.Near * 2.0f), Is.EqualTo(0.5f).Within(1.0e-6f));
            Assert.That(twiceNearClip.W, Is.GreaterThan(0.0f));
        });
    }

    [Test]
    public void BuildsReversedZOrthographicProjectionWithSignedNearPlane()
    {
        var camera = CreateCamera(CameraProjectionKind.Orthographic, near: -2.0f, far: 10.0f, orthographicHeight: 4.0f);
        var data = RenderCameraGpuData.Create(camera, new RenderArea(0, 0, 16, 16));
        var bytes = new byte[RenderCameraGpuData.SizeInBytes];
        data.WriteTo(bytes);
        var projection = ReadMatrix(bytes);
        var far = camera.Far
            ?? throw new InvalidOperationException("The test orthographic camera has no far plane.");

        Assert.Multiple(() => {
            Assert.That(ProjectDepth(projection, camera.Near), Is.EqualTo(1.0f).Within(1.0e-6f));
            Assert.That(ProjectDepth(projection, far), Is.EqualTo(0.0f).Within(1.0e-6f));
            Assert.That(ReadSingle(bytes, 0), Is.EqualTo(0.5f));
            Assert.That(ReadSingle(bytes, 20), Is.EqualTo(0.5f));
        });
    }

    [Test]
    public void BuildsReversedZPerspectiveForThePhysicalPixelArea()
    {
        var camera = CreateCamera(CameraProjectionKind.Perspective, near: 0.1f, far: 100.0f, orthographicHeight: 4.0f);
        var data = RenderCameraGpuData.Create(camera, new RenderArea(2, 3, 10, 12));
        var bytes = new byte[RenderCameraGpuData.SizeInBytes];
        data.WriteTo(bytes);
        var projection = ReadMatrix(bytes);
        var far = camera.Far
            ?? throw new InvalidOperationException("The test perspective camera has no far plane.");

        Assert.Multiple(() => {
            Assert.That(ProjectDepth(projection, camera.Near), Is.EqualTo(1.0f).Within(1.0e-6f));
            Assert.That(ProjectDepth(projection, far), Is.EqualTo(0.0f).Within(1.0e-6f));
            Assert.That(projection[0, 0], Is.EqualTo(1.2f).Within(1.0e-6f));
            Assert.That(projection[1, 1], Is.EqualTo(1.0f).Within(1.0e-6f));
            Assert.That(ReadSingle(bytes, 64), Is.EqualTo(2.0f));
            Assert.That(ReadSingle(bytes, 68), Is.EqualTo(3.0f));
            Assert.That(ReadSingle(bytes, 72), Is.EqualTo(10.0f));
            Assert.That(ReadSingle(bytes, 76), Is.EqualTo(12.0f));
        });
    }

    [Test]
    public void BuildsTranslatedRotatedCameraProjectionFromNonzeroFrameOrigin()
    {
        var frameOrigin = new SpatialPosition3(default, new Vector3(10, 20, 30));
        var cameraPosition = new SpatialPosition3(default, new Vector3(11, 22, 33));
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, float.Pi / 2.0f);
        var camera = CreateCamera(
            CameraProjectionKind.Perspective,
            near: 0.1f,
            far: 100.0f,
            orthographicHeight: 4.0f,
            frameOrigin: frameOrigin,
            position: cameraPosition,
            rotation: rotation);
        var data = RenderCameraGpuData.Create(camera, new RenderArea(0, 0, 16, 16));
        var bytes = new byte[RenderCameraGpuData.SizeInBytes];
        data.WriteTo(bytes);
        var viewProjection = ReadMatrix(bytes);
        var forward = ProjectClip(viewProjection, new Vector3(6, 2, 3));
        var right = ProjectClip(viewProjection, new Vector3(6, 2, 2));
        var up = ProjectClip(viewProjection, new Vector3(6, 3, 3));

        Assert.Multiple(() => {
            Assert.That(forward.W, Is.EqualTo(5.0f).Within(1.0e-5f));
            Assert.That(forward.X / forward.W, Is.EqualTo(0.0f).Within(1.0e-6f));
            Assert.That(forward.Y / forward.W, Is.EqualTo(0.0f).Within(1.0e-6f));
            Assert.That(right.X / right.W, Is.GreaterThan(0.0f));
            Assert.That(right.Y / right.W, Is.EqualTo(0.0f).Within(1.0e-6f));
            Assert.That(up.X / up.W, Is.EqualTo(0.0f).Within(1.0e-6f));
            Assert.That(up.Y / up.W, Is.GreaterThan(0.0f));
        });
    }

    [Test]
    public void RejectsSingularPerspectiveProjection()
    {
        var camera = CreateCamera(
            CameraProjectionKind.Perspective,
            near: 0.1f,
            far: 100.0f,
            orthographicHeight: 4.0f,
            aspectRatioOverride: float.MaxValue,
            verticalFieldOfView: MathF.BitDecrement(float.Pi));

        _ = Assert.Throws<InvalidOperationException>(
            () => RenderCameraGpuData.Create(camera, new RenderArea(0, 0, 16, 16)));
    }
}
