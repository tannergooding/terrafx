// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace TerraFX.Platform.Tests;

[TestFixture]
internal sealed class HeadlessPlatformBackendTests
{
    [Test]
    public void HeadlessBackendMarksNativeServicesNotRequestedAndOptionalClipboardUnsupported()
    {
        using var headless = PlatformBackendFactory.Create(PlatformBackend.Headless, 1, ClipboardServicePolicy.NotRequested);
        using var optionalClipboard = PlatformBackendFactory.Create(PlatformBackend.Headless, 2, ClipboardServicePolicy.Optional);

        Assert.That(headless.Backend, Is.EqualTo(PlatformBackend.Headless));
        Assert.That(headless.DisplayStatus, Is.EqualTo(PlatformServiceStatus.NotRequested));
        Assert.That(headless.PhysicalInputStatus, Is.EqualTo(PlatformServiceStatus.NotRequested));
        Assert.That(headless.AudioStatus, Is.EqualTo(PlatformServiceStatus.NotRequested));
        Assert.That(headless.ClipboardStatus, Is.EqualTo(ClipboardServiceStatus.NotRequested));
        Assert.That(optionalClipboard.ClipboardStatus, Is.EqualTo(ClipboardServiceStatus.Unsupported));
        Assert.That(
            () => PlatformBackendFactory.Create(PlatformBackend.Headless, 3, ClipboardServicePolicy.Required),
            Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void PlatformBackendSelectorsHaveStableValues()
    {
        Assert.That((int)PlatformBackend.None, Is.EqualTo(0));
        Assert.That((int)PlatformBackend.Headless, Is.EqualTo(1));
        Assert.That((int)PlatformBackend.WindowsNative, Is.EqualTo(2));
        Assert.That((int)PlatformBackend.WaylandOnly, Is.EqualTo(3));
        Assert.That((int)PlatformBackend.WaylandThenX11, Is.EqualTo(4));
        Assert.That((int)PlatformBackend.X11Only, Is.EqualTo(5));
    }

    [Test]
    public void PumpAndDisposeRequireTheHostOwnerThread()
    {
        var headless = PlatformBackendFactory.Create(PlatformBackend.Headless, 1, ClipboardServicePolicy.NotRequested);

        try
        {
            Assert.That(headless.OwnerId, Is.EqualTo(1));
            _ = Assert.Throws<InvalidOperationException>(() => Task.Run(headless.Pump).GetAwaiter().GetResult());
            _ = Assert.Throws<InvalidOperationException>(() => Task.Run(headless.Dispose).GetAwaiter().GetResult());
            headless.Dispose();
            headless.Dispose();
            _ = Assert.Throws<ObjectDisposedException>(headless.Pump);
        }
        finally
        {
            headless.Dispose();
        }
    }

    [Test]
    public void UnimplementedNativeBackendFailsWithoutHeadlessFallback()
    {
        _ = Assert.Throws<PlatformNotSupportedException>(
            () => PlatformBackendFactory.Create(PlatformBackend.WindowsNative, 1, ClipboardServicePolicy.NotRequested));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => PlatformBackendFactory.Create(PlatformBackend.None, 1, ClipboardServicePolicy.NotRequested));
    }
}
