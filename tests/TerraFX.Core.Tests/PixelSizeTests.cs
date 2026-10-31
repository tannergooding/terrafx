// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using NUnit.Framework;
using TerraFX;

namespace TerraFX.Core.Tests;

[TestFixture]
internal sealed class PixelSizeTests
{
    [Test]
    public void DimensionsAreNonnegativeAndEqualityUsesBothAxes()
    {
        var size = new PixelSize(1280, 720);

        Assert.That(size.Width, Is.EqualTo(1280));
        Assert.That(size.Height, Is.EqualTo(720));
        Assert.That(default(PixelSize), Is.EqualTo(new PixelSize(0, 0)));
        Assert.That(new PixelSize(0, 720), Is.Not.EqualTo(new PixelSize(1, 720)));
        Assert.That(new PixelSize(1280, 720).GetHashCode(), Is.EqualTo(size.GetHashCode()));
        Assert.That(() => new PixelSize(-1, 0), Throws.TypeOf<System.ArgumentOutOfRangeException>());
        Assert.That(() => new PixelSize(0, -1), Throws.TypeOf<System.ArgumentOutOfRangeException>());
    }
}
