// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using TerraFX.Numerics;

namespace TerraFX.Rendering;

internal static class RenderMeshC0VertexBufferBuilder
{
    internal const int VertexStride = 12 * sizeof(float);

    internal static byte[] Create(RenderMeshAsset mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        if (mesh.Normals.IsDefault)
        {
            throw new NotSupportedException("Mesh C0 requires an authored Normal vertex stream.");
        }

        var bytes = new byte[checked(mesh.VertexCount * VertexStride)];

        for (var index = 0; index < mesh.VertexCount; index++)
        {
            var offset = checked(index * VertexStride);
            WriteVector3(bytes, offset, mesh.Positions[index]);
            WriteVector3(bytes, offset + (3 * sizeof(float)), mesh.Normals[index]);

            if (!mesh.Uv0.IsDefault)
            {
                WriteVector2(bytes, offset + (6 * sizeof(float)), mesh.Uv0[index]);
            }

            if (!mesh.Tangents.IsDefault)
            {
                WriteVector4(bytes, offset + (8 * sizeof(float)), mesh.Tangents[index]);
            }
        }

        return bytes;
    }

    private static void WriteSingle(Span<byte> destination, int offset, float value)
        => BinaryPrimitives.WriteInt32LittleEndian(
            destination.Slice(offset, sizeof(float)),
            BitConverter.SingleToInt32Bits(value));

    private static void WriteVector2(Span<byte> destination, int offset, Vector2 value)
    {
        WriteSingle(destination, offset, value.X);
        WriteSingle(destination, offset + sizeof(float), value.Y);
    }

    private static void WriteVector3(Span<byte> destination, int offset, Vector3 value)
    {
        WriteSingle(destination, offset, value.X);
        WriteSingle(destination, offset + sizeof(float), value.Y);
        WriteSingle(destination, offset + (2 * sizeof(float)), value.Z);
    }

    private static void WriteVector4(Span<byte> destination, int offset, Vector4 value)
    {
        WriteSingle(destination, offset, value.X);
        WriteSingle(destination, offset + sizeof(float), value.Y);
        WriteSingle(destination, offset + (2 * sizeof(float)), value.Z);
        WriteSingle(destination, offset + (3 * sizeof(float)), value.W);
    }
}
