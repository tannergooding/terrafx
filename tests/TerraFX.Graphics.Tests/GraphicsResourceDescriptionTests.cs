// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Graphics;

namespace TerraFX.Graphics.Tests;

[TestFixture]
internal sealed class GraphicsResourceDescriptionTests
{
    [Test]
    public void BufferDescriptionAcceptsValidUsageAndStructuredLayout()
    {
        var buffer = new BufferDescription(
            1024,
            BufferUsage.Storage | BufferUsage.CopySource,
            GpuMemoryKind.DeviceLocal,
            structuredStride: 16,
            debugName: string.Empty);

        Assert.That(buffer.ByteLength, Is.EqualTo(1024));
        Assert.That(buffer.Usage, Is.EqualTo(BufferUsage.Storage | BufferUsage.CopySource));
        Assert.That(buffer.MemoryKind, Is.EqualTo(GpuMemoryKind.DeviceLocal));
        Assert.That(buffer.StructuredStride, Is.EqualTo(16));
        Assert.That(buffer.DebugName, Is.Empty);
    }

    [Test]
    public void BufferDescriptionRejectsInvalidLengthUsageMemoryKindAndStride()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new BufferDescription(
            0,
            BufferUsage.Vertex,
            GpuMemoryKind.DeviceLocal,
            structuredStride: null,
            debugName: "invalid"));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new BufferDescription(
            16,
            BufferUsage.None,
            GpuMemoryKind.DeviceLocal,
            structuredStride: null,
            debugName: "invalid"));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new BufferDescription(
            16,
            (BufferUsage)64,
            GpuMemoryKind.DeviceLocal,
            structuredStride: null,
            debugName: "invalid"));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new BufferDescription(
            16,
            BufferUsage.Vertex,
            GpuMemoryKind.None,
            structuredStride: null,
            debugName: "invalid"));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new BufferDescription(
            16,
            BufferUsage.Vertex,
            (GpuMemoryKind)255,
            structuredStride: null,
            debugName: "invalid"));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new BufferDescription(
            16,
            BufferUsage.Storage,
            GpuMemoryKind.DeviceLocal,
            structuredStride: 0,
            debugName: "invalid"));
        _ = Assert.Throws<ArgumentException>(() => new BufferDescription(
            18,
            BufferUsage.Storage,
            GpuMemoryKind.DeviceLocal,
            structuredStride: 4,
            debugName: "invalid"));
    }

    [Test]
    public void BufferDescriptionRestrictsUploadAndReadbackUsage()
    {
        var upload = new BufferDescription(
            256,
            BufferUsage.Vertex | BufferUsage.CopySource,
            GpuMemoryKind.Upload,
            structuredStride: null,
            debugName: "upload");
        var readback = new BufferDescription(
            256,
            BufferUsage.CopyDestination,
            GpuMemoryKind.Readback,
            structuredStride: null,
            debugName: "readback");

        Assert.That(upload.MemoryKind, Is.EqualTo(GpuMemoryKind.Upload));
        Assert.That(readback.Usage, Is.EqualTo(BufferUsage.CopyDestination));
        _ = Assert.Throws<ArgumentException>(() => new BufferDescription(
            256,
            BufferUsage.Storage,
            GpuMemoryKind.Upload,
            structuredStride: null,
            debugName: "invalid"));
        _ = Assert.Throws<ArgumentException>(() => new BufferDescription(
            256,
            BufferUsage.CopyDestination,
            GpuMemoryKind.Upload,
            structuredStride: null,
            debugName: "invalid"));
        _ = Assert.Throws<ArgumentException>(() => new BufferDescription(
            256,
            BufferUsage.CopyDestination | BufferUsage.Vertex,
            GpuMemoryKind.Readback,
            structuredStride: null,
            debugName: "invalid"));
    }

    [Test]
    public void TextureDescriptionAcceptsValidTwoDimensionalAndThreeDimensionalExtents()
    {
        var texture2D = new TextureDescription(
            TextureDimension.Dimension2D,
            width: 1024,
            height: 512,
            depth: 1,
            arrayLayers: 4,
            mipLevels: 11,
            TextureFormat.Rgba8Unorm,
            TextureUsage.Sampled | TextureUsage.ColorAttachment,
            debugName: string.Empty);
        var texture3D = new TextureDescription(
            TextureDimension.Dimension3D,
            width: 256,
            height: 128,
            depth: 64,
            arrayLayers: 1,
            mipLevels: 9,
            TextureFormat.Rgba16Float,
            TextureUsage.Sampled | TextureUsage.Storage,
            debugName: "volume");

        Assert.That(texture2D.ArrayLayers, Is.EqualTo(4));
        Assert.That(texture2D.MipLevels, Is.EqualTo(11));
        Assert.That(texture2D.Depth, Is.EqualTo(1));
        Assert.That(texture3D.Dimension, Is.EqualTo(TextureDimension.Dimension3D));
        Assert.That(texture3D.Depth, Is.EqualTo(64));
    }

    [Test]
    public void TextureDescriptionRejectsInvalidShapeMipFormatAndUsage()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new TextureDescription(
            TextureDimension.None,
            width: 1,
            height: 1,
            depth: 1,
            arrayLayers: 1,
            mipLevels: 1,
            TextureFormat.Rgba8Unorm,
            TextureUsage.Sampled,
            debugName: "invalid"));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new TextureDescription(
            TextureDimension.Dimension2D,
            width: 16,
            height: 16,
            depth: 2,
            arrayLayers: 1,
            mipLevels: 1,
            TextureFormat.Rgba8Unorm,
            TextureUsage.Sampled,
            debugName: "invalid"));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new TextureDescription(
            TextureDimension.Dimension3D,
            width: 16,
            height: 16,
            depth: 16,
            arrayLayers: 2,
            mipLevels: 1,
            TextureFormat.Rgba8Unorm,
            TextureUsage.Sampled,
            debugName: "invalid"));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new TextureDescription(
            TextureDimension.Dimension2D,
            width: 16,
            height: 8,
            depth: 1,
            arrayLayers: 1,
            mipLevels: 6,
            TextureFormat.Rgba8Unorm,
            TextureUsage.Sampled,
            debugName: "invalid"));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new TextureDescription(
            TextureDimension.Dimension2D,
            width: 16,
            height: 8,
            depth: 1,
            arrayLayers: 1,
            mipLevels: 1,
            TextureFormat.None,
            TextureUsage.Sampled,
            debugName: "invalid"));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new TextureDescription(
            TextureDimension.Dimension2D,
            width: 16,
            height: 8,
            depth: 1,
            arrayLayers: 1,
            mipLevels: 1,
            TextureFormat.Rgba8Unorm,
            (TextureUsage)64,
            debugName: "invalid"));
        _ = Assert.Throws<ArgumentException>(() => new TextureDescription(
            TextureDimension.Dimension2D,
            width: 16,
            height: 8,
            depth: 1,
            arrayLayers: 1,
            mipLevels: 1,
            TextureFormat.D32Float,
            TextureUsage.Storage,
            debugName: "invalid"));
        _ = Assert.Throws<ArgumentException>(() => new TextureDescription(
            TextureDimension.Dimension2D,
            width: 16,
            height: 8,
            depth: 1,
            arrayLayers: 1,
            mipLevels: 1,
            TextureFormat.Rgba8Unorm,
            TextureUsage.DepthAttachment,
            debugName: "invalid"));
    }
}
