// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using TerraFX;
using TerraFX.Numerics;

namespace TerraFX.Rendering;

internal static class RenderMeshC0SpotLightBufferBuilder
{
    internal const int Capacity = Renderer.MaximumLightCount;
    internal const int CountOffset = 0;
    internal const int DirectionOuterCosineOffset = (4 * sizeof(uint)) + (Capacity * 4 * sizeof(float));
    internal const int InnerCosineShadowSlotOffset = RadianceOffset + (Capacity * 4 * sizeof(float));
    internal const int PositionRangeOffset = 4 * sizeof(uint);
    internal const int RadianceOffset = DirectionOuterCosineOffset + (Capacity * 4 * sizeof(float));
    internal const int SizeInBytes = InnerCosineShadowSlotOffset + (Capacity * 4 * sizeof(float));

    internal static byte[] Create(
        ImmutableArray<RenderSpotLightSnapshot> lights,
        SpatialFrame3 frame3,
        ImmutableArray<RenderSpotShadowData> shadows,
        int uniformBufferOffsetAlignment)
    {
        if (lights.IsDefault || shadows.IsDefault)
        {
            throw new ArgumentException("Spot-light and shadow selections must be initialized.");
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(lights.Length, Capacity);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(shadows.Length, RenderSpotShadowData.Capacity);
        var buffer = new byte[GetUniformAllocationStride(uniformBufferOffsetAlignment)];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(CountOffset, sizeof(uint)), checked((uint)lights.Length));

        for (var index = 0; index < lights.Length; index++)
        {
            var light = lights[index];

            if (!SpatialMath.TryToFrameRelative(light.Position, frame3, out var position))
            {
                throw new InvalidOperationException("A spot light is outside the camera spatial frame.");
            }

            var intensity = light.LuminousFlux / (4.0f * float.Pi);
            var radiance = light.Color * intensity;

            if (!radiance.IsFinite().All())
            {
                throw new InvalidOperationException("A spot-light radiance is not representable.");
            }

            var shadowSlot = -1;

            for (var shadowIndex = 0; shadowIndex < shadows.Length; shadowIndex++)
            {
                if (shadows[shadowIndex].LightId == light.LightId)
                {
                    shadowSlot = shadowIndex;
                    break;
                }
            }

            WriteVector4(
                buffer,
                PositionRangeOffset + (index * 4 * sizeof(float)),
                new Vector4(position, light.Range));
            WriteVector4(
                buffer,
                DirectionOuterCosineOffset + (index * 4 * sizeof(float)),
                new Vector4(light.Direction, MathF.Cos(light.OuterConeAngle)));
            WriteVector4(
                buffer,
                RadianceOffset + (index * 4 * sizeof(float)),
                new Vector4(radiance, 0.0f));
            WriteVector4(
                buffer,
                InnerCosineShadowSlotOffset + (index * 4 * sizeof(float)),
                new Vector4(MathF.Cos(light.InnerConeAngle), shadowSlot + 1, 0.0f, 0.0f));
        }

        return buffer;
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
