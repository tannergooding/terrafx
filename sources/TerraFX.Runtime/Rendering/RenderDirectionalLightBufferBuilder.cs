// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using TerraFX.Numerics;

namespace TerraFX.Rendering;

internal static class RenderDirectionalLightBufferBuilder
{
    internal const int Capacity = Renderer.MaximumLightCount;
    internal const int DirectionOffset = sizeof(uint) * 4;
    internal const int RadianceOffset = DirectionOffset + (Capacity * 4 * sizeof(float));
    internal const int SizeInBytes = RadianceOffset + (Capacity * 4 * sizeof(float));

    internal static byte[] Create(
        ReadOnlySpan<RenderDirectionalLightSnapshot> directionalLights,
        Guid shadowedLightId,
        int uniformBufferOffsetAlignment)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(directionalLights.Length, Capacity);
        var bytes = new byte[GetUniformAllocationStride(uniformBufferOffsetAlignment)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, checked((uint)directionalLights.Length));

        for (var index = 0; index < directionalLights.Length; index++)
        {
            var light = directionalLights[index];
            var radiance = light.Color * light.Illuminance;

            if (!radiance.IsFinite().All())
            {
                throw new InvalidOperationException("The selected directional-light radiance is not representable.");
            }

            WriteVector4(
                bytes,
                DirectionOffset + (index * 4 * sizeof(float)),
                new Vector4(light.Direction, light.LightId == shadowedLightId ? 1.0f : 0.0f));
            WriteVector4(
                bytes,
                RadianceOffset + (index * 4 * sizeof(float)),
                new Vector4(radiance, 0.0f));
        }

        return bytes;
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
}
