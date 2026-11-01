// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Graphics;

namespace TerraFX.Graphics.Tests;

[TestFixture]
internal sealed class TextureTransferValueTests
{
    [Test]
    public void TextureDataLayoutRequiresPositivePitches()
    {
        var layout = new TextureDataLayout(256, 1024);

        Assert.That(layout.RowPitch, Is.EqualTo((nint)256));
        Assert.That(layout.SlicePitch, Is.EqualTo((nint)1024));
        Assert.That(layout, Is.EqualTo(new TextureDataLayout(256, 1024)));
        Assert.That(layout != new TextureDataLayout(256, 2048), Is.True);
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new TextureDataLayout(0, 1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new TextureDataLayout(1, 0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new TextureDataLayout(-1, 1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new TextureDataLayout(1, -1));
    }

    [Test]
    public void TextureSubresourceRequiresNonnegativeIndicesAndKnownAspect()
    {
        var subresource = new TextureSubresource(2, 3, TextureAspect.Depth);

        Assert.That(subresource.MipLevel, Is.EqualTo(2));
        Assert.That(subresource.ArrayLayer, Is.EqualTo(3));
        Assert.That(subresource.Aspect, Is.EqualTo(TextureAspect.Depth));
        Assert.That(subresource, Is.EqualTo(new TextureSubresource(2, 3, TextureAspect.Depth)));
        Assert.That(subresource != new TextureSubresource(2, 4, TextureAspect.Depth), Is.True);
        Assert.That(default(TextureSubresource).Aspect, Is.EqualTo(TextureAspect.Color));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new TextureSubresource(-1, 0, TextureAspect.Color));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new TextureSubresource(0, -1, TextureAspect.Color));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new TextureSubresource(0, 0, (TextureAspect)2));
    }
}
