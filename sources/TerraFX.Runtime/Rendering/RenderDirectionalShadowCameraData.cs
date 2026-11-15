// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using TerraFX.Numerics;

namespace TerraFX.Rendering;

internal readonly struct RenderDirectionalShadowCameraData
{
    internal const int CascadeCount = 4;
    internal const int MatrixOffset = 4 * sizeof(float);
    internal const int SizeInBytes = MatrixOffset + (CascadeCount * 16 * sizeof(float));

    private readonly bool _isEnabled;
    private readonly Matrix4x4 _viewProjection0;
    private readonly Matrix4x4 _viewProjection1;
    private readonly Matrix4x4 _viewProjection2;
    private readonly Matrix4x4 _viewProjection3;

    private RenderDirectionalShadowCameraData(
        Vector4 cascadeEnds,
        Matrix4x4 viewProjection0,
        Matrix4x4 viewProjection1,
        Matrix4x4 viewProjection2,
        Matrix4x4 viewProjection3)
    {
        CascadeEnds = cascadeEnds;
        _isEnabled = true;
        _viewProjection0 = viewProjection0;
        _viewProjection1 = viewProjection1;
        _viewProjection2 = viewProjection2;
        _viewProjection3 = viewProjection3;
    }

    internal Vector4 CascadeEnds { get; }

    internal bool IsEnabled => _isEnabled;

    internal static RenderDirectionalShadowCameraData Create(
        CameraSnapshot camera,
        Vector3 lightDirection)
    {
        Span<float> splits = stackalloc float[CascadeCount + 1];
        Span<Matrix4x4> viewProjections = stackalloc Matrix4x4[CascadeCount];
        RenderingKernels.CreateDirectionalShadowCascades(camera, lightDirection, splits, viewProjections);

        return new RenderDirectionalShadowCameraData(
            new Vector4(splits[1], splits[2], splits[3], splits[4]),
            viewProjections[0],
            viewProjections[1],
            viewProjections[2],
            viewProjections[3]);
    }

    private static void WriteMatrix4x4(Span<byte> destination, int offset, Matrix4x4 value)
    {
        WriteVector4(destination, offset, value.Row1);
        WriteVector4(destination, offset + (4 * sizeof(float)), value.Row2);
        WriteVector4(destination, offset + (8 * sizeof(float)), value.Row3);
        WriteVector4(destination, offset + (12 * sizeof(float)), value.Row4);
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

    internal Matrix4x4 GetViewProjection(int cascadeIndex)
        => cascadeIndex switch {
            0 => _isEnabled ? _viewProjection0 : Matrix4x4.Identity,
            1 => _isEnabled ? _viewProjection1 : Matrix4x4.Identity,
            2 => _isEnabled ? _viewProjection2 : Matrix4x4.Identity,
            3 => _isEnabled ? _viewProjection3 : Matrix4x4.Identity,
            _ => throw new ArgumentOutOfRangeException(nameof(cascadeIndex)),
        };

    internal void WriteTo(Span<byte> destination, int destinationOffset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(destinationOffset);

        if (destination.Length - destinationOffset < SizeInBytes)
        {
            throw new ArgumentException("The destination is smaller than the directional shadow GPU record.", nameof(destination));
        }

        WriteVector4(destination, destinationOffset, CascadeEnds);
        WriteMatrix4x4(destination, destinationOffset + MatrixOffset, GetViewProjection(0));
        WriteMatrix4x4(destination, destinationOffset + MatrixOffset + (1 * 16 * sizeof(float)), GetViewProjection(1));
        WriteMatrix4x4(destination, destinationOffset + MatrixOffset + (2 * 16 * sizeof(float)), GetViewProjection(2));
        WriteMatrix4x4(destination, destinationOffset + MatrixOffset + (3 * 16 * sizeof(float)), GetViewProjection(3));
    }

    internal void WriteViewProjectionTo(Span<byte> destination, int destinationOffset, int cascadeIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(destinationOffset);

        if (destination.Length - destinationOffset < 16 * sizeof(float))
        {
            throw new ArgumentException("The destination is smaller than the shadow view-projection matrix.", nameof(destination));
        }

        WriteMatrix4x4(destination, destinationOffset, GetViewProjection(cascadeIndex));
    }
}
