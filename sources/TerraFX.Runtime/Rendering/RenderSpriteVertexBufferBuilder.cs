// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using TerraFX.Numerics;

namespace TerraFX.Rendering;

internal static class RenderSpriteVertexBufferBuilder
{
    internal const int VertexStride = 5 * sizeof(float);

    internal static byte[] Create(ReadOnlySpan<RenderSpriteSnapshot> sprites)
    {
        var buffer = new byte[checked(sprites.Length * 4 * VertexStride)];

        for (var spriteIndex = 0; spriteIndex < sprites.Length; spriteIndex++)
        {
            var quad = sprites[spriteIndex].Quad;
            WriteVertex(buffer, GetVertexOffset(spriteIndex, 0), quad.Vertex0);
            WriteVertex(buffer, GetVertexOffset(spriteIndex, 1), quad.Vertex1);
            WriteVertex(buffer, GetVertexOffset(spriteIndex, 2), quad.Vertex2);
            WriteVertex(buffer, GetVertexOffset(spriteIndex, 3), quad.Vertex3);
        }

        return buffer;
    }

    private static int GetVertexOffset(int spriteIndex, int vertexIndex)
        => checked(((spriteIndex * 4) + vertexIndex) * VertexStride);

    private static void WriteSingle(Span<byte> destination, int offset, float value)
        => BinaryPrimitives.WriteInt32LittleEndian(
            destination.Slice(offset, sizeof(float)),
            BitConverter.SingleToInt32Bits(value));

    private static void WriteVertex(Span<byte> destination, int offset, RenderSpriteVertex vertex)
    {
        WriteSingle(destination, offset, vertex.Position.X);
        WriteSingle(destination, offset + sizeof(float), vertex.Position.Y);
        WriteSingle(destination, offset + (2 * sizeof(float)), vertex.Position.Z);
        WriteSingle(destination, offset + (3 * sizeof(float)), vertex.TextureCoordinate.X);
        WriteSingle(destination, offset + (4 * sizeof(float)), vertex.TextureCoordinate.Y);
    }
}
