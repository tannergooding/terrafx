// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using TerraFX.Numerics;

namespace TerraFX.Rendering;

internal static class RenderMeshC0BoneBufferBuilder
{
    internal const int Stride = 7 * 4 * sizeof(float);

    internal static byte[] Create(
        ReadOnlySpan<RenderMeshPalette> palettes,
        Span<int> paletteOffsets)
    {
        if (paletteOffsets.Length != palettes.Length)
        {
            throw new ArgumentException("Bone palette offsets must match the palette count.", nameof(paletteOffsets));
        }

        var boneCount = 0;

        for (var paletteIndex = 0; paletteIndex < palettes.Length; paletteIndex++)
        {
            var skinMatrices = palettes[paletteIndex].SkinMatrices;

            if (skinMatrices.IsDefaultOrEmpty)
            {
                throw new ArgumentException("A Mesh bone palette must contain initialized skin matrices.", nameof(palettes));
            }

            paletteOffsets[paletteIndex] = boneCount;
            boneCount = checked(boneCount + skinMatrices.Length);
        }

        var hasBones = boneCount != 0;
        var buffer = new byte[checked(Math.Max(boneCount, 1) * Stride)];

        if (!hasBones)
        {
            WriteSkinMatrix(buffer, 0, Matrix4x4.Identity);
            WriteNormalMatrix(buffer, 64, Matrix4x4.Identity);
            return buffer;
        }

        for (var paletteIndex = 0; paletteIndex < palettes.Length; paletteIndex++)
        {
            var skinMatrices = palettes[paletteIndex].SkinMatrices;
            var paletteOffset = checked(paletteOffsets[paletteIndex] * Stride);

            for (var boneIndex = 0; boneIndex < skinMatrices.Length; boneIndex++)
            {
                var skinMatrix = skinMatrices[boneIndex];

                if (!skinMatrix.IsFinite().All())
                {
                    throw new InvalidOperationException("A Mesh bone palette contains a nonfinite skin matrix.");
                }

                var linearMatrix = new Matrix4x4(
                    new Vector4(skinMatrix.Row1.X, skinMatrix.Row1.Y, skinMatrix.Row1.Z, 0.0f),
                    new Vector4(skinMatrix.Row2.X, skinMatrix.Row2.Y, skinMatrix.Row2.Z, 0.0f),
                    new Vector4(skinMatrix.Row3.X, skinMatrix.Row3.Y, skinMatrix.Row3.Z, 0.0f),
                    new Vector4(0.0f, 0.0f, 0.0f, 1.0f));

                if (!Matrix4x4.TryInvert(linearMatrix, out var inverseLinear))
                {
                    throw new InvalidOperationException("A Mesh bone palette contains a singular linear transform.");
                }

                var normalMatrix = Matrix4x4.Transpose(inverseLinear);

                if (!normalMatrix.IsFinite().All())
                {
                    throw new InvalidOperationException("A Mesh bone palette contains a nonfinite normal transform.");
                }

                var offset = checked(paletteOffset + (boneIndex * Stride));
                WriteSkinMatrix(buffer, offset, skinMatrix);
                WriteNormalMatrix(buffer, offset + 64, normalMatrix);
            }
        }

        return buffer;
    }

    private static void WriteNormalMatrix(Span<byte> destination, int offset, Matrix4x4 matrix)
    {
        WriteVector4(destination, offset, matrix.Row1);
        WriteVector4(destination, offset + 16, matrix.Row2);
        WriteVector4(destination, offset + 32, matrix.Row3);
    }

    private static void WriteSingle(Span<byte> destination, int offset, float value)
        => BinaryPrimitives.WriteInt32LittleEndian(
            destination.Slice(offset, sizeof(float)),
            BitConverter.SingleToInt32Bits(value));

    private static void WriteSkinMatrix(Span<byte> destination, int offset, Matrix4x4 matrix)
    {
        WriteVector4(destination, offset, matrix.Row1);
        WriteVector4(destination, offset + 16, matrix.Row2);
        WriteVector4(destination, offset + 32, matrix.Row3);
        WriteVector4(destination, offset + 48, matrix.Row4);
    }

    private static void WriteVector4(Span<byte> destination, int offset, Vector4 value)
    {
        WriteSingle(destination, offset, value.X);
        WriteSingle(destination, offset + sizeof(float), value.Y);
        WriteSingle(destination, offset + (2 * sizeof(float)), value.Z);
        WriteSingle(destination, offset + (3 * sizeof(float)), value.W);
    }
}
