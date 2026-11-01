// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using NUnit.Framework;
using TerraFX;
using TerraFX.Numerics;

namespace TerraFX.Platform.Tests;

[TestFixture]
internal sealed class WindowOptionsTests
{
    [Test]
    public void DefaultsMatchTheWindowContract()
    {
        var options = new WindowOptions(string.Empty);

        Assert.That((int)WindowMode.Windowed, Is.EqualTo(0));
        Assert.That((int)WindowMode.BorderlessFullscreen, Is.EqualTo(1));
        Assert.That((int)WindowMode.ExclusiveFullscreen, Is.EqualTo(2));
        Assert.That(options.Title, Is.Empty);
        Assert.That(options.ClientSize, Is.EqualTo(new Vector2(1280, 720)));
        Assert.That(options.MinimumClientSize, Is.EqualTo(new Vector2(1, 1)));
        Assert.That(options.Resizable, Is.True);
        Assert.That(options.Visible, Is.True);
        Assert.That(options.Mode, Is.EqualTo(WindowMode.Windowed));
        Assert.That(options.Monitor, Is.Null);
    }

    [Test]
    public void RejectsNullOrNullTerminatedTitles()
    {
        _ = Assert.Throws<System.ArgumentNullException>(() => new WindowOptions(null!));
        _ = Assert.Throws<System.ArgumentException>(() => new WindowOptions("a\0b"));
    }

    [Test]
    public void ValidatesClientExtentsModeAndMonitorBeforeNativeAdmission()
    {
        _ = Assert.Throws<System.ArgumentOutOfRangeException>(
            () => _ = new WindowOptions("invalid") { ClientSize = new Vector2(0, 1) });
        _ = Assert.Throws<System.ArgumentOutOfRangeException>(
            () => _ = new WindowOptions("invalid") { MinimumClientSize = new Vector2(float.NaN, 1) });
        _ = Assert.Throws<System.ArgumentOutOfRangeException>(
            () => _ = new WindowOptions("invalid") { Mode = (WindowMode)int.MaxValue });
        _ = Assert.Throws<System.ArgumentException>(
            () => _ = new WindowOptions("invalid") { Monitor = default(MonitorHandle) });
    }
}
