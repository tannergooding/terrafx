// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using TerraFX.Numerics;

namespace TerraFX.Rendering;

internal readonly struct RenderMeshC0MaterialData
{
    internal const int SizeInBytes = 64;

    private readonly Vector4 _baseColor;
    private readonly Vector4 _emissive;
    private readonly Vector4 _factors;
    private readonly Vector4 _renderOptions;

    internal RenderMeshC0MaterialData(
        RenderMaterialAsset material,
        bool hasBaseColorTexture,
        bool hasEmissiveTexture,
        bool hasPackedMetalRoughOcclusionTexture)
    {
        ArgumentNullException.ThrowIfNull(material);
        _baseColor = material.Parameters.Color;
        _factors = new Vector4(
            material.Parameters.Metallic,
            material.Parameters.Roughness,
            hasBaseColorTexture ? 1.0f : 0.0f,
            hasEmissiveTexture ? 1.0f : 0.0f);
        _emissive = new Vector4(material.Parameters.Emissive, hasPackedMetalRoughOcclusionTexture ? 1.0f : 0.0f);
        _renderOptions = new Vector4(
            material.AlphaCutoff,
            material.AlphaMode is RenderMaterialAsset.MaterialAlphaMode.Mask ? 1.0f : 0.0f,
            0.0f,
            0.0f);
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
            throw new ArgumentException("The destination is smaller than the Mesh Material GPU record.", nameof(destination));
        }

        WriteVector4(destination, 0, _baseColor);
        WriteVector4(destination, 16, _factors);
        WriteVector4(destination, 32, _emissive);
        WriteVector4(destination, 48, _renderOptions);
    }
}
