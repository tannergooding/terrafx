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
        float far,
        float orthographicHeight)
    {
        var frame = new SpatialFrame3(
            1,
            SpatialProfileId.Default,
            SpatialSettings.Default,
            default,
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
            default,
            Quaternion.Identity,
            projectionKind,
            verticalFieldOfView: float.Pi / 2.0f,
            orthographicHeight,
            near,
            CameraFarMode.Finite,
            far,
            aspectRatioOverride: null,
            new Vector4(0.0f, 0.0f, 1.0f, 1.0f),
            CameraClearFlags.Color,
            Vector4.Zero,
            ulong.MaxValue);
    }

    private static float ProjectDepth(Matrix4x4 matrix, float z)
    {
        var clipZ = (z * matrix[2, 2]) + matrix[3, 2];
        var clipW = (z * matrix[2, 3]) + matrix[3, 3];
        return clipZ / clipW;
    }

    private static float ReadSingle(ReadOnlySpan<byte> bytes, int offset)
        => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset, sizeof(float))));

    [Test]
    public void BuildsReversedZOrthographicProjectionWithSignedNearPlane()
    {
        var camera = CreateCamera(CameraProjectionKind.Orthographic, near: -2.0f, far: 10.0f, orthographicHeight: 4.0f);
        var data = RenderCameraGpuData.Create(camera, new RenderArea(0, 0, 16, 16));
        var bytes = new byte[RenderCameraGpuData.SizeInBytes];
        data.WriteTo(bytes);
        var projection = new Matrix4x4(
            new Vector4(ReadSingle(bytes, 0), ReadSingle(bytes, 4), ReadSingle(bytes, 8), ReadSingle(bytes, 12)),
            new Vector4(ReadSingle(bytes, 16), ReadSingle(bytes, 20), ReadSingle(bytes, 24), ReadSingle(bytes, 28)),
            new Vector4(ReadSingle(bytes, 32), ReadSingle(bytes, 36), ReadSingle(bytes, 40), ReadSingle(bytes, 44)),
            new Vector4(ReadSingle(bytes, 48), ReadSingle(bytes, 52), ReadSingle(bytes, 56), ReadSingle(bytes, 60)));
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
        var projection = new Matrix4x4(
            new Vector4(ReadSingle(bytes, 0), ReadSingle(bytes, 4), ReadSingle(bytes, 8), ReadSingle(bytes, 12)),
            new Vector4(ReadSingle(bytes, 16), ReadSingle(bytes, 20), ReadSingle(bytes, 24), ReadSingle(bytes, 28)),
            new Vector4(ReadSingle(bytes, 32), ReadSingle(bytes, 36), ReadSingle(bytes, 40), ReadSingle(bytes, 44)),
            new Vector4(ReadSingle(bytes, 48), ReadSingle(bytes, 52), ReadSingle(bytes, 56), ReadSingle(bytes, 60)));
        var far = camera.Far
            ?? throw new InvalidOperationException("The test perspective camera has no far plane.");

        Assert.Multiple(() => {
            Assert.That(ProjectDepth(projection, camera.Near), Is.EqualTo(1.0f).Within(1.0e-6f));
            Assert.That(ProjectDepth(projection, far), Is.EqualTo(0.0f).Within(1.0e-6f));
            Assert.That(ReadSingle(bytes, 64), Is.EqualTo(2.0f));
            Assert.That(ReadSingle(bytes, 68), Is.EqualTo(3.0f));
            Assert.That(ReadSingle(bytes, 72), Is.EqualTo(10.0f));
            Assert.That(ReadSingle(bytes, 76), Is.EqualTo(12.0f));
        });
    }
}
