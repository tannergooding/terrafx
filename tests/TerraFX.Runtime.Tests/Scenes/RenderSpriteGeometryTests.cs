// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Numerics;
using TerraFX.Rendering;

namespace TerraFX.Runtime.Tests.Scenes;

[TestFixture]
internal sealed class RenderSpriteGeometryTests
{
    [Test]
    public void ComposesPivotedQuadAndAtlasUvCorners()
    {
        var frame = new RenderAtlasFrame(Guid.NewGuid(), Page: 0, X: 4, Y: 8, Width: 2, Height: 4, Padding: 2);
        var quad = RenderSpriteGeometry.Create(
            new Vector2(0.25f, 0.75f),
            new Vector2(4.0f, 2.0f),
            flipMask: 0,
            frame,
            pageWidth: 16,
            pageHeight: 32);

        Assert.That(quad.Vertex0.Position, Is.EqualTo(new Vector3(-1.0f, -1.5f, 0.0f)));
        Assert.That(quad.Vertex1.Position, Is.EqualTo(new Vector3(-1.0f, 0.5f, 0.0f)));
        Assert.That(quad.Vertex2.Position, Is.EqualTo(new Vector3(3.0f, 0.5f, 0.0f)));
        Assert.That(quad.Vertex3.Position, Is.EqualTo(new Vector3(3.0f, -1.5f, 0.0f)));
        Assert.That(quad.Vertex0.TextureCoordinate, Is.EqualTo(new Vector2(0.25f, 0.375f)));
        Assert.That(quad.Vertex1.TextureCoordinate, Is.EqualTo(new Vector2(0.25f, 0.25f)));
        Assert.That(quad.Vertex2.TextureCoordinate, Is.EqualTo(new Vector2(0.375f, 0.25f)));
        Assert.That(quad.Vertex3.TextureCoordinate, Is.EqualTo(new Vector2(0.375f, 0.375f)));
        Assert.That(RenderSpriteGeometry.Indices, Is.EqualTo([0, 1, 2, 0, 2, 3]));
    }

    [Test]
    public void FlipsAtlasUvsWithoutChangingQuadWinding()
    {
        var frame = new RenderAtlasFrame(Guid.NewGuid(), Page: 0, X: 4, Y: 8, Width: 2, Height: 4, Padding: 2);
        var unflipped = RenderSpriteGeometry.Create(
            new Vector2(0.5f, 0.5f),
            new Vector2(1.0f, 1.0f),
            flipMask: 0,
            frame,
            pageWidth: 16,
            pageHeight: 32);
        var flipped = RenderSpriteGeometry.Create(
            new Vector2(0.5f, 0.5f),
            new Vector2(1.0f, 1.0f),
            flipMask: 0x03,
            frame,
            pageWidth: 16,
            pageHeight: 32);

        Assert.That(flipped.Vertex0.Position, Is.EqualTo(unflipped.Vertex0.Position));
        Assert.That(flipped.Vertex1.Position, Is.EqualTo(unflipped.Vertex1.Position));
        Assert.That(flipped.Vertex2.Position, Is.EqualTo(unflipped.Vertex2.Position));
        Assert.That(flipped.Vertex3.Position, Is.EqualTo(unflipped.Vertex3.Position));
        Assert.That(flipped.Vertex0.TextureCoordinate, Is.EqualTo(new Vector2(0.375f, 0.25f)));
        Assert.That(flipped.Vertex1.TextureCoordinate, Is.EqualTo(new Vector2(0.375f, 0.375f)));
        Assert.That(flipped.Vertex2.TextureCoordinate, Is.EqualTo(new Vector2(0.25f, 0.375f)));
        Assert.That(flipped.Vertex3.TextureCoordinate, Is.EqualTo(new Vector2(0.25f, 0.25f)));
        Assert.That(RenderSpriteGeometry.Indices, Is.EqualTo([0, 1, 2, 0, 2, 3]));
    }

    [Test]
    public void RejectsAtlasFramesOutsideTheirPinnedPage()
    {
        var frame = new RenderAtlasFrame(Guid.NewGuid(), Page: 0, X: 14, Y: 8, Width: 2, Height: 4, Padding: 2);

        _ = Assert.Throws<ArgumentException>(
            () => RenderSpriteGeometry.Create(
                new Vector2(0.5f, 0.5f),
                new Vector2(1.0f, 1.0f),
                flipMask: 0,
                frame,
                pageWidth: 16,
                pageHeight: 32));
    }
}
