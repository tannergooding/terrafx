// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;

namespace TerraFX.Platform.Tests;

[TestFixture]
internal sealed class WaylandDisplayConnectionTests
{
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
        connection.Pump();
    }
}
