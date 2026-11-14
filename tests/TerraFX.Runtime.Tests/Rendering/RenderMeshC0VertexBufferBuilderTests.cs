// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using NUnit.Framework;
using TerraFX.Assets;
using TerraFX.Numerics;
using TerraFX.Rendering;

namespace TerraFX.Runtime.Tests.Rendering;

[TestFixture]
internal sealed class RenderMeshC0VertexBufferBuilderTests
{
    private static float ReadSingle(ReadOnlySpan<byte> source, int offset)
        => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(source.Slice(offset, sizeof(float))));

    [Test]
    public void PacksJointIndicesAndRenormalizesFourWeights()
    {
        var mesh = new RenderMeshAsset(
            new AssetId(1),
            vertexCount: 1,
            [Vector3.Zero],
            [Vector3.UnitZ],
            tangents: default,
            uv0: default,
            uv1: default,
            colors: default,
            [0, 1, 2, 3],
            [new Vector4(0.25f, 0.25f, 0.25f, 0.249999f)],
            RenderMeshIndexFormat.UInt16,
            [0, 0, 0],
            [new RenderMeshGroup(0, 3, 0)],
            [new AssetId(2)],
            new AssetId(3),
            new RenderMeshBounds(Vector3.Zero, Vector3.One, new Vector3(0.5f), 1.0f),
            [1]);

        var bytes = RenderMeshC0VertexBufferBuilder.Create(mesh);
        Assert.That(bytes, Has.Length.EqualTo(RenderMeshC0VertexBufferBuilder.VertexStride));
        Assert.Multiple(() => {
            Assert.That(ReadSingle(bytes, 48), Is.EqualTo(0.0f));
            Assert.That(ReadSingle(bytes, 52), Is.EqualTo(1.0f));
            Assert.That(ReadSingle(bytes, 56), Is.EqualTo(2.0f));
            Assert.That(ReadSingle(bytes, 60), Is.EqualTo(3.0f));
            Assert.That(ReadSingle(bytes, 64), Is.EqualTo(0.25f / 0.999999f).Within(1.0e-7f));
            Assert.That(ReadSingle(bytes, 76), Is.EqualTo(0.249999f / 0.999999f).Within(1.0e-7f));
        });
    }
}
