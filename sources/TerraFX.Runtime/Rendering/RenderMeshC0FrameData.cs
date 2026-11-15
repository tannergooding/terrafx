// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using TerraFX;
using TerraFX.Graphics;
using TerraFX.Numerics;

namespace TerraFX.Rendering;

internal readonly struct RenderMeshC0FrameData
{
    internal const int SizeInBytes = 128 + (4 * sizeof(float)) + RenderDirectionalShadowCameraData.SizeInBytes;

    private const int AmbientOffset = 96;
    private const int CameraForwardOffset = 128;
    private const int CameraPositionOffset = 80;
    private const int PointLightCountOffset = 112;
    private const int ShadowDataOffset = 144;
    private readonly Vector4 _ambient;
    private readonly RenderCameraGpuData _camera;
    private readonly Vector4 _cameraForward;
    private readonly Vector4 _cameraPosition;
    private readonly RenderDirectionalShadowCameraData? _directionalShadowData;
    private readonly uint _pointLightCount;

    private RenderMeshC0FrameData(
        RenderCameraGpuData camera,
        Vector4 cameraPosition,
        Vector4 cameraForward,
        Vector4 ambient,
        uint pointLightCount,
        RenderDirectionalShadowCameraData? directionalShadowData)
    {
        _camera = camera;
        _cameraPosition = cameraPosition;
        _cameraForward = cameraForward;
        _ambient = ambient;
        _pointLightCount = pointLightCount;
        _directionalShadowData = directionalShadowData;
    }

    internal static RenderMeshC0FrameData Create(
        CameraSnapshot camera,
        RenderArea area,
        int pointLightCount,
        RenderDirectionalShadowCameraData? directionalShadowData)
    {
        if (!SpatialMath.TryToFrameRelative(camera.Position, camera.Frame3, out var relativeCameraPosition))
        {
            throw new InvalidOperationException("The camera position is outside its captured spatial frame.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(pointLightCount);
        var cameraForward = Vector3.TransformDirection(
            Vector3.UnitZ,
            Matrix4x4.CreateFromQuaternion(camera.Rotation));

        return new RenderMeshC0FrameData(
            RenderCameraGpuData.Create(camera, area),
            new Vector4(relativeCameraPosition, 1.0f),
            new Vector4(cameraForward, 0.0f),
            new Vector4(0.03f, 0.03f, 0.03f, 1.0f),
            checked((uint)pointLightCount),
            directionalShadowData);
    }

    internal static int GetUniformAllocationStride(int uniformBufferOffsetAlignment)
    {
        var stride = ObjectGpuData.GetUniformAllocationStride(uniformBufferOffsetAlignment);

        while (stride < SizeInBytes)
        {
            stride = checked(stride + uniformBufferOffsetAlignment);
        }

        return stride;
    }

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
            throw new ArgumentException("The destination is smaller than the Mesh Frame GPU record.", nameof(destination));
        }

        _camera.WriteTo(destination);
        WriteVector4(destination, CameraPositionOffset, _cameraPosition);
        WriteVector4(destination, AmbientOffset, _ambient);
        WriteVector4(destination, CameraForwardOffset, _cameraForward);
        BinaryPrimitives.WriteUInt32LittleEndian(
            destination.Slice(PointLightCountOffset, sizeof(uint)),
            _pointLightCount);
        _directionalShadowData.GetValueOrDefault().WriteTo(destination, ShadowDataOffset);
    }
}
