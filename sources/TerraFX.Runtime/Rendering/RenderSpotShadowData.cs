// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using TerraFX;
using TerraFX.Numerics;

namespace TerraFX.Rendering;

internal readonly struct RenderSpotShadowData
{
    internal const int Capacity = 4;
    internal const float NearRangeRatio = 1.0f / 1024.0f;
    internal const int SizeInBytes = 4 * 16 * sizeof(float);

    internal RenderSpotShadowData(Guid lightId, ulong layerMask, Matrix4x4 viewProjection)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(lightId, Guid.Empty);

        if (!viewProjection.IsFinite().All() || !Matrix4x4.TryInvert(viewProjection, out _))
        {
            throw new ArgumentOutOfRangeException(nameof(viewProjection));
        }

        LightId = lightId;
        LayerMask = layerMask;
        ViewProjection = viewProjection;
    }

    internal ulong LayerMask { get; }

    internal Guid LightId { get; }

    internal Matrix4x4 ViewProjection { get; }

    internal static bool TryCreate(
        RenderSpotLightSnapshot light,
        CameraSnapshot camera,
        out RenderSpotShadowData shadow)
    {
        shadow = default;

        if (!SpatialMath.TryToFrameRelative(light.Position, camera.Frame3, out var position))
        {
            return false;
        }

        var near = MathF.Max(float.Epsilon, light.Range * NearRangeRatio);

        if (!float.IsFinite(near) || (near >= light.Range))
        {
            return false;
        }

        RenderingKernels.GetShadowViewBasis(light.Direction, out _, out var up, out _);
        var view = Matrix4x4.CreateLookTo(position, light.Direction, up);
        var fieldOfView = 2.0f * light.OuterConeAngle;

        if (!float.IsFinite(fieldOfView) || (fieldOfView <= 0.0f) || (fieldOfView >= float.Pi))
        {
            return false;
        }

        var projection = Matrix4x4.CreatePerspectiveFieldOfView(fieldOfView, 1.0f, near, light.Range);
        var viewProjection = view * projection;

        if (!viewProjection.IsFinite().All() || !Matrix4x4.TryInvert(viewProjection, out _))
        {
            return false;
        }

        shadow = new RenderSpotShadowData(light.LightId, light.LayerMask, viewProjection);
        return true;
    }

    internal static void WriteIdentityViewProjectionTo(Span<byte> destination, int destinationOffset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(destinationOffset);

        if (destination.Length - destinationOffset < SizeInBytes)
        {
            throw new ArgumentException("The destination is smaller than the spot shadow matrix.", nameof(destination));
        }

        WriteVector4(destination, destinationOffset, Vector4.UnitX);
        WriteVector4(destination, destinationOffset + (4 * sizeof(float)), Vector4.UnitY);
        WriteVector4(destination, destinationOffset + (8 * sizeof(float)), Vector4.UnitZ);
        WriteVector4(destination, destinationOffset + (12 * sizeof(float)), Vector4.UnitW);
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

    internal void WriteViewProjectionTo(Span<byte> destination, int destinationOffset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(destinationOffset);

        if (destination.Length - destinationOffset < SizeInBytes)
        {
            throw new ArgumentException("The destination is smaller than the spot shadow matrix.", nameof(destination));
        }

        WriteVector4(destination, destinationOffset, ViewProjection.Row1);
        WriteVector4(destination, destinationOffset + (4 * sizeof(float)), ViewProjection.Row2);
        WriteVector4(destination, destinationOffset + (8 * sizeof(float)), ViewProjection.Row3);
        WriteVector4(destination, destinationOffset + (12 * sizeof(float)), ViewProjection.Row4);
    }
}
