// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Input;

namespace TerraFX.Platform.Tests;

[TestFixture]
internal sealed class LinuxEvdevGamepadCaptureTests
{
    [Test]
    public void InitializesAndPumpsLinuxGamepadCaptureOnItsOwnerThread()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Ignore("The evdev gamepad capture path requires Linux.");
            return;
        }

        var samples = new PlatformInputSampleQueue(Environment.CurrentManagedThreadId);
        using var capture = new LinuxEvdevGamepadCapture(samples, TimeProvider.System);

        Assert.That(
            capture.GamepadStatus,
            Is.AnyOf(PlatformServiceStatus.Available, PlatformServiceStatus.PermissionDenied));

        capture.Pump();

        var batch = samples.Drain();

        foreach (var sample in batch.Samples)
        {
            Assert.That(sample.NativeDeviceIdentity, Is.GreaterThan((nint)0));
            Assert.That(sample.Window, Is.Null);
            Assert.That(sample.DeviceKind, Is.EqualTo(PlatformInputDeviceKind.Gamepad));
        }

        TestContext.WriteLine(
            $"Evdev gamepad status: {capture.GamepadStatus}; captured samples: {batch.Samples.Length}.");
    }
}
