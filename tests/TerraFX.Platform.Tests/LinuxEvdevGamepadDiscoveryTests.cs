// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using TerraFX.Input;

namespace TerraFX.Platform.Tests;

[TestFixture]
internal sealed class LinuxEvdevGamepadDiscoveryTests
{
    [Test]
    public void EnumeratesUdevGamepadsWhenRunningOnLinux()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Ignore("The udev gamepad discovery path requires Linux.");
            return;
        }

        using var monitor = new LinuxEvdevGamepadMonitor();
        var devices = monitor.InitialDevices;
        var sysPaths = new HashSet<string>(StringComparer.Ordinal);
        var deviceNodes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var device in devices)
        {
            Assert.That(device.SysPath, Is.Not.Empty);
            Assert.That(Path.IsPathRooted(device.DeviceNode), Is.True);
            Assert.That(device.DeviceNode.StartsWith("/dev/input/event", StringComparison.Ordinal), Is.True);
            Assert.That(sysPaths.Add(device.SysPath), Is.True);
            Assert.That(deviceNodes.Add(device.DeviceNode), Is.True);
        }

        var changes = monitor.Pump();

        foreach (var change in changes)
        {
            Assert.That(
                change.Kind,
                Is.AnyOf(PhysicalInputEventKind.DeviceAdded, PhysicalInputEventKind.DeviceRemoved));
            Assert.That(change.Device.SysPath, Is.Not.Empty);
            Assert.That(change.Device.DeviceNode.StartsWith("/dev/input/event", StringComparison.Ordinal), Is.True);
        }

        TestContext.WriteLine(
            $"Discovered {devices.Length} udev gamepad event nodes and observed {changes.Length} hotplug changes.");
    }
}
