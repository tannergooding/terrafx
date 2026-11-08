// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX;
using TerraFX.Numerics;

namespace TerraFX.Platform.Tests;

[TestFixture]
internal sealed class WaylandDisplayConnectionTests
{
    [Test]
    public void BindsAndDestroysXdgWmBaseProxy()
    {
        if (!OperatingSystem.IsLinux()
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            Assert.Ignore("The xdg-shell witness requires a configured Linux Wayland display.");
            return;
        }

        using var connection = WaylandDisplayConnection.Connect();
        using var metadata = WaylandProtocolMetadata.Create(connection);
        var xdgWmBase = connection.BindGlobal(
            connection.GetGlobals("xdg_wm_base")[0],
            metadata.XdgWmBaseInterface,
            version: 1);

        try
        {
            connection.SendRequest(xdgWmBase, opcode: 0);
            connection.Pump();
        }
        finally
        {
            connection.DestroyProxy(xdgWmBase);
        }
    }

    [Test]
    public void ConnectsToWaylandRegistryAndFindsRequiredGlobals()
    {
        if (!OperatingSystem.IsLinux()
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            Assert.Ignore("The Wayland registry witness requires a configured Linux Wayland display.");
            return;
        }

        using var connection = WaylandDisplayConnection.Connect();

        Assert.That(connection.HasGlobal("wl_compositor"), Is.True);
        Assert.That(connection.HasGlobal("xdg_wm_base"), Is.True);
        Assert.That(connection.GetGlobals("wl_output"), Is.Not.Empty);
        Assert.That(connection.BindGlobal("wl_compositor", maximumVersion: 4), Is.Not.Zero);
        connection.Pump();
    }

    [Test]
    public void WaylandBackendCreatesConfiguresAndDestroysWindow()
    {
        if (!OperatingSystem.IsLinux()
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            Assert.Ignore("The Wayland window witness requires a configured Linux Wayland display.");
            return;
        }

        using var backend = PlatformBackendFactory.Create(
            PlatformBackend.WaylandOnly,
            ownerId: 1,
            ClipboardServicePolicy.NotRequested);
        var window = new WindowHandle(ownerId: 1, index: 0, generation: 1);
        var windowCreated = false;

        try
        {
            Assert.That(backend.Backend, Is.EqualTo(PlatformBackend.WaylandOnly));
            Assert.That(backend.DisplayStatus, Is.EqualTo(PlatformServiceStatus.Available));

            backend.CreateWindow(
                window,
                new WindowOptions("TerraFX Wayland test") {
                    ClientSize = new Vector2(640.0f, 360.0f),
                    Visible = false,
                },
                nativeMonitorHandle: 0);
            windowCreated = true;

            Assert.That(backend.TryGetNativeWindowBinding(window, out var binding), Is.True);
            Assert.That(binding.Kind, Is.EqualTo(PlatformNativeSurfaceKind.Wayland));

            var metrics = backend.GetNativeWindowMetrics(window);
            Assert.That(metrics.ClientPixelSize.Width, Is.GreaterThan(0));
            Assert.That(metrics.ClientPixelSize.Height, Is.GreaterThan(0));
            Assert.That(backend.SetWindowTitle(window, "TerraFX Wayland configured"), Is.EqualTo(WindowCommandStatus.Applied));
            Assert.That(backend.SetWindowClientSize(window, new Vector2(640.0f, 360.0f)), Is.EqualTo(WindowCommandStatus.Unsupported));
            backend.Pump();
        }
        finally
        {
            if (windowCreated)
            {
                backend.CloseWindow(window);
            }
        }

        Assert.That(backend.TryGetNativeWindowBinding(window, out _), Is.False);
    }
}
