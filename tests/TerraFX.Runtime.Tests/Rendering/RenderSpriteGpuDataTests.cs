// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using NUnit.Framework;
using TerraFX;
using TerraFX.Numerics;
using TerraFX.Rendering;

namespace TerraFX.Runtime.Tests.Rendering;

[TestFixture]
internal sealed class RenderSpriteGpuDataTests
{
    private static void AssertMatrix(ReadOnlySpan<byte> bytes, Matrix4x4 expected)
    {
        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                var actual = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(
                    bytes.Slice(((row * 4) + column) * sizeof(float))));
                Assert.That(actual, Is.EqualTo(expected[row, column]));
            }
        }
    }

    private static void AssertVector4(ReadOnlySpan<byte> bytes, Vector4 expected)
    {
        Assert.That(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes)), Is.EqualTo(expected.X));
        Assert.That(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(4))), Is.EqualTo(expected.Y));
        Assert.That(BitConverter.SingleToUInt32Bits(BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(8)))), Is.EqualTo(BitConverter.SingleToUInt32Bits(expected.Z)));
        Assert.That(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(12))), Is.EqualTo(expected.W));
    }

    private static SpatialFrame3 CreateFrame(SpatialPosition3 origin, Vector3 minimum, Vector3 maximum)
        => new(
            1,
            SpatialProfileId.Default,
            SpatialSettings.Default,
            origin,
            1,
            minimum,
            maximum);

    private static RenderSpriteSnapshot CreateSprite(bool pixelSnap = false)
    {
        var frame = CreateFrame(
            default,
            new Vector3(-4096),
            new Vector3(4096));
        return new RenderSpriteSnapshot(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            frame,
            default,
            Matrix4x4.Identity,
            AtlasAssetId: 1,
            Guid.NewGuid(),
            Vector2.Zero,
            Vector2.One,
            Vector4.One,
            FlipMask: 0,
            SortingLayer: 0,
            SortingOrder: 0,
            PixelSnap: pixelSnap,
            MaterialAssetId: null,
            default,
            HasPreviousTransform: false);
    }

    [Test]
    public void BuildsAlignedPerSpriteRecordsWithZeroedPadding()
    {
        RenderSpriteSnapshot[] sprites = [CreateSprite(pixelSnap: true), CreateSprite()];

        var buffer = RenderSpriteGpuDataBufferBuilder.CreateObjectBuffer(
            sprites,
            vulkanAlignment: 256,
            out var uniformStride);

        Assert.Multiple(() => {
            Assert.That(uniformStride, Is.EqualTo(256));
            Assert.That(buffer.Length, Is.EqualTo(2 * uniformStride));
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(80)), Is.EqualTo(1));
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(uniformStride + 80)), Is.Zero);
            Assert.That(buffer.AsSpan(RenderSpriteGpuData.SizeInBytes, uniformStride - RenderSpriteGpuData.SizeInBytes).ToArray(), Is.EqualTo(new byte[uniformStride - RenderSpriteGpuData.SizeInBytes]));
        });
    }

    [Test]
    public void RejectsSpriteOutsideCapturedSpatialFrame()
    {
        var frame = CreateFrame(
            default,
            new Vector3(-1),
            new Vector3(1));
        var sprite = new RenderSpriteSnapshot(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            frame,
            new SpatialPosition3(default, new Vector3(2, 0, 0)),
            Matrix4x4.Identity,
            AtlasAssetId: 1,
            Guid.NewGuid(),
            Vector2.Zero,
            Vector2.One,
            Vector4.One,
            FlipMask: 0,
            SortingLayer: 0,
            SortingOrder: 0,
            PixelSnap: false,
            MaterialAssetId: null,
            default,
            HasPreviousTransform: false);

        _ = Assert.Throws<InvalidOperationException>(() => RenderSpriteGpuData.Create(sprite));
    }

    [Test]
    public void RequiresCompleteGpuRecordDestination()
    {
        var data = RenderSpriteGpuData.Create(CreateSprite());

        _ = Assert.Throws<ArgumentException>(() => data.WriteTo(new byte[RenderSpriteGpuData.SizeInBytes - 1]));
    }

    [Test]
    public void WritesFrameRelativeWorldTintAndPixelSnapSlots()
    {
        var frame = CreateFrame(
            new SpatialPosition3(default, new Vector3(7, 2, 5)),
            new Vector3(-4096),
            new Vector3(4096));
        var position = new SpatialPosition3(default, new Vector3(9, 5, 9));
        var linearTransform = Matrix4x4.CreateScale(new Vector3(2, 3, 4));
        var tint = new Vector4(1.25f, 0.5f, -0.0f, 0.75f);
        var sprite = new RenderSpriteSnapshot(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            frame,
            position,
            linearTransform,
            AtlasAssetId: 1,
            Guid.NewGuid(),
            Vector2.Zero,
            Vector2.One,
            tint,
            FlipMask: 0,
            SortingLayer: 0,
            SortingOrder: 0,
            PixelSnap: true,
            MaterialAssetId: null,
            default,
            HasPreviousTransform: false);
        var data = RenderSpriteGpuData.Create(sprite);
        var bytes = new byte[RenderSpriteGpuData.SizeInBytes];
        Array.Fill(bytes, (byte)0xA5);
        data.WriteTo(bytes);
        var expectedWorld = linearTransform * Matrix4x4.CreateTranslation(new Vector3(2, 3, 4));
        var noPixelSnapData = RenderSpriteGpuData.Create(CreateSprite());
        var noPixelSnapBytes = new byte[RenderSpriteGpuData.SizeInBytes];
        Array.Fill(noPixelSnapBytes, (byte)0xA5);
        noPixelSnapData.WriteTo(noPixelSnapBytes);

        Assert.Multiple(() => {
            Assert.That(RenderSpriteGpuData.SizeInBytes, Is.EqualTo(96));
            AssertMatrix(bytes.AsSpan(0, 64), expectedWorld);
            AssertVector4(bytes.AsSpan(64, 16), tint);
            Assert.That(
                bytes.AsSpan(80, 16).ToArray(),
                Is.EqualTo(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }));
            Assert.That(noPixelSnapBytes.AsSpan(80, 16).ToArray(), Is.EqualTo(new byte[16]));
            Assert.That(RenderSpriteGpuData.GetUniformAllocationStride(256), Is.EqualTo(256));
        });
    }
}
