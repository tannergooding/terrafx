// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;

namespace TerraFX.Graphics.Tests;

[TestFixture]
internal sealed class GraphResourceVersionTests
{
    private static BufferDescription CreateBufferDescription()
        => new(256, BufferUsage.Vertex | BufferUsage.CopyDestination, GpuMemoryKind.DeviceLocal, null, string.Empty);

    private static TextureDescription CreateDepthTextureDescription()
        => new(TextureDimension.Dimension2D, 64, 32, 1, 1, 6, TextureFormat.D32Float, TextureUsage.DepthAttachment, string.Empty);

    private static TextureDescription CreateTextureDescription()
        => new(TextureDimension.Dimension2D, 64, 32, 1, 4, 6, TextureFormat.Rgba8Unorm, TextureUsage.Sampled, string.Empty);

    [Test]
    public void BufferVersionRejectsInvalidIdentityAndOutOfResourceRange()
    {
        var description = CreateBufferDescription();

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new GraphBufferVersion(0, 1, 0, description, new GraphBufferRange(0, 1)));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new GraphBufferVersion(1, 0, 0, description, new GraphBufferRange(0, 1)));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new GraphBufferVersion(1, 1, -1, description, new GraphBufferRange(0, 1)));
        _ = Assert.Throws<ArgumentException>(() => new GraphBufferVersion(1, 1, 0, description, new GraphBufferRange(256, 1)));
    }

    [Test]
    public void BufferVersionRetainsDescriptionRangeAndEpoch()
    {
        var description = CreateBufferDescription();
        var range = new GraphBufferRange(16, 128);
        var version = new GraphBufferVersion(3, 5, 2, description, range);

        Assert.That(version.DeviceEpoch, Is.EqualTo(3));
        Assert.That(version.ResourceId, Is.EqualTo(5));
        Assert.That(version.Version, Is.EqualTo(2));
        Assert.That(version.Description, Is.SameAs(description));
        Assert.That(version.Range, Is.EqualTo(range));
    }

    [Test]
    public void TextureVersionRejectsInvalidIdentityRangeAndAspect()
    {
        var description = CreateTextureDescription();

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new GraphTextureVersion(0, 1, 0, description, new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color)));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new GraphTextureVersion(1, 0, 0, description, new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color)));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new GraphTextureVersion(1, 1, -1, description, new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color)));
        _ = Assert.Throws<ArgumentException>(() => new GraphTextureVersion(1, 1, 0, description, new GraphTextureRange(6, 1, 0, 1, TextureAspect.Color)));
        _ = Assert.Throws<ArgumentException>(() => new GraphTextureVersion(1, 1, 0, description, new GraphTextureRange(0, 1, 0, 1, TextureAspect.Depth)));
    }

    [Test]
    public void TextureVersionRetainsDescriptionRangeAndEpoch()
    {
        var description = CreateDepthTextureDescription();
        var range = new GraphTextureRange(2, 2, 0, 1, TextureAspect.Depth);
        var version = new GraphTextureVersion(3, 5, 1, description, range);

        Assert.That(version.DeviceEpoch, Is.EqualTo(3));
        Assert.That(version.ResourceId, Is.EqualTo(5));
        Assert.That(version.Version, Is.EqualTo(1));
        Assert.That(version.Description, Is.SameAs(description));
        Assert.That(version.Range, Is.EqualTo(range));
    }
}
