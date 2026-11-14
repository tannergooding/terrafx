// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Linq;
using NUnit.Framework;
using TerraFX.Graphics;
using TerraFX.Numerics;
using TerraFX.Rendering;

namespace TerraFX.Runtime.Tests.Rendering;

[TestFixture]
internal sealed class RenderMeshC0BoneBufferBuilderTests
{
    private static float ReadSingle(ReadOnlySpan<byte> source, int offset)
        => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(source.Slice(offset, sizeof(float))));

    [Test]
    public void PacksSkinAndInverseTransposeNormalRows()
    {
        var palettes = new[] {
            new RenderMeshPalette(
                Guid.NewGuid(),
                1,
                2,
                [Matrix4x4.CreateScale(new Vector3(2.0f, 1.0f, 0.5f))]),
        };
        var offsets = new int[1];
        var bytes = RenderMeshC0BoneBufferBuilder.Create(palettes, offsets);

        Assert.Multiple(() => {
            Assert.That(offsets, Is.EqualTo([0]));
            Assert.That(bytes, Has.Length.EqualTo(RenderMeshC0BoneBufferBuilder.Stride));
            Assert.That(ReadSingle(bytes, 0), Is.EqualTo(2.0f));
            Assert.That(ReadSingle(bytes, 20), Is.EqualTo(1.0f));
            Assert.That(ReadSingle(bytes, 40), Is.EqualTo(0.5f));
            Assert.That(ReadSingle(bytes, 64), Is.EqualTo(0.5f));
            Assert.That(ReadSingle(bytes, 84), Is.EqualTo(1.0f));
            Assert.That(ReadSingle(bytes, 104), Is.EqualTo(2.0f));
        });
    }

    [Test]
    public void RejectsSingularBoneLinearTransforms()
    {
        var palettes = new[] {
            new RenderMeshPalette(
                Guid.NewGuid(),
                1,
                2,
                [Matrix4x4.CreateScale(new Vector3(1.0f, 0.0f, 1.0f))]),
        };

        _ = Assert.Throws<InvalidOperationException>(
            () => RenderMeshC0BoneBufferBuilder.Create(palettes, new int[1]));
    }

    [Test]
    public void SkinnedVertexReflectionIsSeparateFromBaseline()
    {
        Assert.That(
            RenderMeshC0ShaderReflection.Vertex.Resources.Any(
                static resource => resource.Kind is ShaderResourceKind.StorageBuffer),
            Is.False);
        Assert.That(
            RenderMeshC0ShaderReflection.SkinnedVertex.Resources.Any(
                static resource => resource.Kind is ShaderResourceKind.StorageBuffer
                    && resource.Access is ShaderResourceAccess.ReadWrite),
            Is.True);
    }
}
