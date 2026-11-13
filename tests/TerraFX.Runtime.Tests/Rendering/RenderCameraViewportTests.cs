// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX;
using TerraFX.Graphics;
using TerraFX.Numerics;
using TerraFX.Rendering;

namespace TerraFX.Runtime.Tests.Rendering;

[TestFixture]
internal sealed class RenderCameraViewportTests
{
    [Test]
    public void MapsExactSharedViewportEdgesWithNearestEvenRounding()
    {
        var target = new PixelSize(3, 5);
        var left = RenderCameraViewport.CreateArea(new Vector4(0.0f, 0.0f, 0.5f, 1.0f), target);
        var right = RenderCameraViewport.CreateArea(new Vector4(0.5f, 0.0f, 0.5f, 1.0f), target);

        Assert.That(left, Is.EqualTo(new RenderArea(0, 0, 2, 5)));
        Assert.That(right, Is.EqualTo(new RenderArea(2, 0, 1, 5)));
    }

    [Test]
    public void MapsFullNormalizedViewportToFullTarget()
    {
        var area = RenderCameraViewport.CreateArea(
            new Vector4(0.0f, 0.0f, 1.0f, 1.0f),
            new PixelSize(1920, 1080));

        Assert.That(area, Is.EqualTo(new RenderArea(0, 0, 1920, 1080)));
    }

    [Test]
    public void RejectsExactViewportEdgeBeyondOneWhenFloatSumRoundsToOne()
    {
        var viewport = new Vector4(0.75f, 0.0f, 0.25000003f, 1.0f);
        Assert.That(viewport.X + viewport.Z, Is.EqualTo(1.0f));

        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => RenderCameraViewport.CreateArea(viewport, new PixelSize(16, 16)));
    }

    [Test]
    public void RejectsOutOfRangeViewportComponentsAndTargetExtents()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => RenderCameraViewport.CreateArea(new Vector4(0.0f, 0.0f, 2.0f, 1.0f), new PixelSize(16, 16)));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => RenderCameraViewport.CreateArea(new Vector4(0.0f, 0.0f, 1.0f, 1.0f), default));
    }

    [Test]
    public void RejectsViewportThatQuantizesToAnEmptyPixelArea()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => RenderCameraViewport.CreateArea(
                new Vector4(0.0f, 0.0f, 0.1f, 1.0f),
                new PixelSize(1, 8)));
    }
}
