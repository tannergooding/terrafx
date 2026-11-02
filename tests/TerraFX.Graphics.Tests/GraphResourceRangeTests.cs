// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;

namespace TerraFX.Graphics.Tests;

[TestFixture]
internal sealed class GraphResourceRangeTests
{
    [Test]
    public void BufferRangeRejectsEmptyNegativeAndOverflowingExtents()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new GraphBufferRange(0, 0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new GraphBufferRange(-1, 1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new GraphBufferRange(long.MaxValue, 1));
    }

    [Test]
    public void BufferRangeUsesCheckedNonemptyHalfOpenIntervals()
    {
        var range = new GraphBufferRange(20, 12);

        Assert.That(range.IsValid, Is.True);
        Assert.That(range.EndExclusive, Is.EqualTo(32));
        Assert.That(range.IsWithin(new GraphBufferRange(0, 32)), Is.True);
        Assert.That(range.IsWithin(new GraphBufferRange(0, 31)), Is.False);
        Assert.That(range.Overlaps(new GraphBufferRange(31, 4)), Is.True);
        Assert.That(range.Overlaps(new GraphBufferRange(32, 1)), Is.False);
        Assert.That(default(GraphBufferRange).IsValid, Is.False);
    }

    [Test]
    public void TextureRangeChecksEveryMipLayerAndAspectDimension()
    {
        var range = new GraphTextureRange(2, 3, 4, 2, TextureAspect.Color);

        Assert.That(range.IsValid, Is.True);
        Assert.That(range.IsWithin(new GraphTextureRange(0, 8, 0, 8, TextureAspect.Color)), Is.True);
        Assert.That(range.IsWithin(new GraphTextureRange(2, 2, 4, 2, TextureAspect.Color)), Is.False);
        Assert.That(range.IsWithin(new GraphTextureRange(2, 3, 4, 2, TextureAspect.Depth)), Is.False);
        Assert.That(range.Overlaps(new GraphTextureRange(4, 2, 5, 1, TextureAspect.Color)), Is.True);
        Assert.That(range.Overlaps(new GraphTextureRange(5, 1, 4, 2, TextureAspect.Color)), Is.False);
        Assert.That(default(GraphTextureRange).IsValid, Is.False);
    }

    [Test]
    public void TextureRangeRejectsEmptyNegativeOverflowingAndUndefinedValues()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new GraphTextureRange(0, 0, 0, 1, TextureAspect.Color));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new GraphTextureRange(0, 1, 0, 0, TextureAspect.Color));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new GraphTextureRange(-1, 1, 0, 1, TextureAspect.Color));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new GraphTextureRange(int.MaxValue, 1, 0, 1, TextureAspect.Color));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new GraphTextureRange(0, 1, int.MaxValue, 1, TextureAspect.Color));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new GraphTextureRange(0, 1, 0, 1, (TextureAspect)2));
    }
}
