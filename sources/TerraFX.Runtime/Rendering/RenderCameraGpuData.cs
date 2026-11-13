// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using TerraFX;
using TerraFX.Graphics;
using TerraFX.Numerics;
using TerraFX.Scenes;

namespace TerraFX.Rendering;

internal readonly struct RenderCameraGpuData
{
    internal const int SizeInBytes = 80;

    private const int RenderAreaOffset = 64;

    private readonly RenderArea _area;
    private readonly Matrix4x4 _viewProjection;

    private RenderCameraGpuData(Matrix4x4 viewProjection, RenderArea area)
    {
        _area = area;
        _viewProjection = viewProjection;
    }

    internal static RenderCameraGpuData Create(CameraSnapshot camera, RenderArea area)
    {
        if (!area.IsValid
            || (area.X >= camera.TargetExtent.Width)
            || (area.Y >= camera.TargetExtent.Height)
            || (area.Width > (camera.TargetExtent.Width - area.X))
            || (area.Height > (camera.TargetExtent.Height - area.Y)))
        {
            throw new ArgumentOutOfRangeException(nameof(area));
        }

        if (!SpatialMath.TryToFrameRelative(camera.Position, camera.Frame3, out var cameraPosition))
        {
            throw new InvalidOperationException("The camera position is outside its captured spatial frame.");
        }

        var cameraRotation = Matrix4x4.CreateFromQuaternion(camera.Rotation);

        if (!Matrix4x4.TryInvert(cameraRotation, out var worldToCamera))
        {
            throw new InvalidOperationException("The captured camera rotation cannot be inverted.");
        }

        var view = Matrix4x4.CreateTranslation(new Vector3(-cameraPosition.X, -cameraPosition.Y, -cameraPosition.Z))
            * worldToCamera;
        var aspectRatio = camera.AspectRatioOverride ?? ((float)area.Width / area.Height);
        Matrix4x4 projection;

        if (camera.ProjectionKind is CameraProjectionKind.Perspective)
        {
            var yScale = 1.0f / MathF.Tan(camera.VerticalFieldOfView * 0.5f);
            var xScale = yScale / aspectRatio;
            var near = camera.Near;
            float zScale;
            float zOffset;

            if (camera.Far is float far)
            {
                var depthRange = far - near;

                if (!float.IsFinite(depthRange) || (depthRange <= 0.0f))
                {
                    throw new InvalidOperationException("The perspective camera depth range is not representable.");
                }

                zScale = -near / depthRange;
                zOffset = near * far / depthRange;
            }
            else
            {
                zScale = 0.0f;
                zOffset = near;
            }

            projection = new Matrix4x4(
                new Vector4(xScale, 0.0f, 0.0f, 0.0f),
                new Vector4(0.0f, yScale, 0.0f, 0.0f),
                new Vector4(0.0f, 0.0f, zScale, 1.0f),
                new Vector4(0.0f, 0.0f, zOffset, 0.0f));
        }
        else
        {
            var width = camera.OrthographicHeight * aspectRatio;
            var far = camera.Far
                ?? throw new InvalidOperationException("The orthographic camera has no finite far plane.");
            var depthRange = far - camera.Near;

            if (!float.IsFinite(width)
                || (width <= 0.0f)
                || !float.IsFinite(depthRange)
                || (depthRange <= 0.0f))
            {
                throw new InvalidOperationException("The orthographic camera extent is not representable.");
            }

            projection = new Matrix4x4(
                new Vector4(2.0f / width, 0.0f, 0.0f, 0.0f),
                new Vector4(0.0f, 2.0f / camera.OrthographicHeight, 0.0f, 0.0f),
                new Vector4(0.0f, 0.0f, -1.0f / depthRange, 0.0f),
                new Vector4(0.0f, 0.0f, far / depthRange, 1.0f));
        }

        var viewProjection = view * projection;

        if (!viewProjection.IsFinite().All())
        {
            throw new InvalidOperationException("The camera view-projection matrix is not finite.");
        }

        return new RenderCameraGpuData(viewProjection, area);
    }

    internal static int GetUniformAllocationStride(int vulkanAlignment)
        => ObjectGpuData.GetUniformAllocationStride(vulkanAlignment);

    private static void WriteSingle(Span<byte> destination, int offset, float value)
        => BinaryPrimitives.WriteInt32LittleEndian(
            destination.Slice(offset, sizeof(float)),
            BitConverter.SingleToInt32Bits(value));

    private static void WriteVector4(Span<byte> destination, int offset, Vector4 value)
    {
        WriteSingle(destination, offset, value.X);
        WriteSingle(destination, offset + sizeof(float), value.Y);
        WriteSingle(destination, offset + (2 * sizeof(float)), value.Z);
        WriteSingle(destination, offset + (3 * sizeof(float)), value.W);
    }

    internal void WriteTo(Span<byte> destination)
    {
        if (destination.Length < SizeInBytes)
        {
            throw new ArgumentException("The destination is smaller than the camera GPU record.", nameof(destination));
        }

        WriteVector4(destination, 0, _viewProjection.Row1);
        WriteVector4(destination, 16, _viewProjection.Row2);
        WriteVector4(destination, 32, _viewProjection.Row3);
        WriteVector4(destination, 48, _viewProjection.Row4);
        WriteSingle(destination, RenderAreaOffset, _area.X);
        WriteSingle(destination, RenderAreaOffset + sizeof(float), _area.Y);
        WriteSingle(destination, RenderAreaOffset + (2 * sizeof(float)), _area.Width);
        WriteSingle(destination, RenderAreaOffset + (3 * sizeof(float)), _area.Height);
    }
}
