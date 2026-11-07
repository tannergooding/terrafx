// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;

namespace TerraFX.Platform.Tests;

[TestFixture]
internal sealed class HeadlessPlatformBackendTests
{
    private static IPlatformBackend CreateX11Backend() =>
        PlatformBackendFactory.Create(
            PlatformBackend.X11Only,
            ownerId: 7,
            ClipboardServicePolicy.NotRequested);

    [Test]
    public void HeadlessBackendMarksNativeServicesNotRequestedAndOptionalClipboardUnsupported()
    {
        using var headless = PlatformBackendFactory.Create(PlatformBackend.Headless, 1, ClipboardServicePolicy.NotRequested);
        using var optionalClipboard = PlatformBackendFactory.Create(PlatformBackend.Headless, 2, ClipboardServicePolicy.Optional);

        Assert.That(headless.Backend, Is.EqualTo(PlatformBackend.Headless));
        Assert.That(headless.DisplayStatus, Is.EqualTo(PlatformServiceStatus.NotRequested));
        Assert.That(headless.PhysicalInputStatus, Is.EqualTo(PlatformServiceStatus.NotRequested));
        Assert.That(headless.DrainInputSamples().Samples, Is.Empty);
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
            () => PlatformBackendFactory.Create(PlatformBackend.WaylandOnly, 1, ClipboardServicePolicy.NotRequested));
        _ = Assert.Throws<PlatformNotSupportedException>(
            () => PlatformBackendFactory.Create(PlatformBackend.WaylandThenX11, 1, ClipboardServicePolicy.NotRequested));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => PlatformBackendFactory.Create(PlatformBackend.None, 1, ClipboardServicePolicy.NotRequested));
    }

    [Test]
    public void X11OnlyCreatesAndRetiresAnInvisibleNativeWindowWhenDisplayIsAvailable()
    {
        if (!OperatingSystem.IsLinux()
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            Assert.Ignore("The X11 native window witness requires a Linux X11 display.");
            return;
        }

        IPlatformBackend? backend = null;

        try
        {
            try
            {
                backend = CreateX11Backend();
            }
            catch (DllNotFoundException exception)
            {
                Assert.Ignore($"The X11 client library is unavailable: {exception.Message}");
                return;
            }
            catch (PlatformNotSupportedException exception)
            {
                Assert.Ignore($"The X11 display is unavailable: {exception.Message}");
                return;
            }

            Assert.That(backend.Backend, Is.EqualTo(PlatformBackend.X11Only));
            Assert.That(backend.DisplayStatus, Is.EqualTo(PlatformServiceStatus.Available));
            Assert.That(backend.PhysicalInputStatus, Is.EqualTo(PlatformServiceStatus.Unsupported));
            Assert.That(backend.GamepadStatus, Is.EqualTo(PlatformServiceStatus.Unsupported));
            Assert.That(backend.ClipboardStatus, Is.EqualTo(ClipboardServiceStatus.NotRequested));

            var window = new WindowHandle(ownerId: 7, index: 0, generation: 1);
            backend.CreateWindow(window, new WindowOptions("TerraFX X11 test") { Visible = false }, nativeMonitorHandle: 0);

            try
            {
                var metrics = backend.GetNativeWindowMetrics(window);
                Assert.That(metrics.ClientPixelSize.Width, Is.EqualTo(1280));
                Assert.That(metrics.ClientPixelSize.Height, Is.EqualTo(720));
                Assert.That(backend.TryGetNativeWindowBinding(window, out var binding), Is.True);
                Assert.That(binding.Kind, Is.EqualTo(PlatformNativeSurfaceKind.X11));
                Assert.That(binding.NativeDisplayOrInstance, Is.Not.Zero);
                Assert.That(binding.NativeWindowOrSurface, Is.Not.Zero);
                Assert.That(backend.DrainInputSamples().Samples, Is.Empty);
            }
            finally
            {
                backend.CloseWindow(window);
            }

            Assert.That(backend.TryGetNativeWindowBinding(window, out _), Is.False);
        }
        finally
        {
            backend?.Dispose();
        }
    }
}
