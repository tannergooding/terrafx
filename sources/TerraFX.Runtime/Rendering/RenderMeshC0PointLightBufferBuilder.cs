// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using TerraFX;
using TerraFX.Numerics;

namespace TerraFX.Rendering;

internal static class RenderMeshC0PointLightBufferBuilder
{
    internal const int Capacity = 256;
    internal const int PositionRangeOffset = 0;
    internal const int RadianceOffset = Capacity * sizeof(float) * 4;
    internal const int SizeInBytes = 2 * Capacity * sizeof(float) * 4;

    internal static byte[] Create(
        ReadOnlySpan<RenderPointLightSnapshot> lights,
        SpatialFrame3 frame3)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(lights.Length, Capacity);
        var buffer = new byte[SizeInBytes];

        for (var index = 0; index < lights.Length; index++)
        {
            var light = lights[index];

            if (!SpatialMath.TryToFrameRelative(light.Position, frame3, out var position))
            {
                throw new InvalidOperationException("A point light is outside the camera spatial frame.");
            }

            var intensity = light.LuminousFlux / (4.0f * float.Pi);
            var radiance = light.Color * intensity;

            if (!radiance.IsFinite().All())
            {
                throw new InvalidOperationException("A point-light radiance is not representable.");
            }

            var positionOffset = checked(PositionRangeOffset + (index * 4 * sizeof(float)));
            WriteVector4(buffer, positionOffset, new Vector4(position, light.Range));
            var radianceOffset = checked(RadianceOffset + (index * 4 * sizeof(float)));
            WriteVector4(buffer, radianceOffset, new Vector4(radiance, 0.0f));
        }

        return buffer;
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
}
