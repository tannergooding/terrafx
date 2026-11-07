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

    [Test]
    public void ReconcilesAfterSynchronizationLossWithoutProcessingDroppedEvents()
    {
        var isResynchronizing = false;
        var synchronizationDropped = new LinuxEvdevInputEvent(0, 0, 0x0000, 0x0003, 0);
        var droppedButton = new LinuxEvdevInputEvent(0, 0, 0x0001, 0x0130, 1);
        var synchronizationReport = new LinuxEvdevInputEvent(0, 0, 0x0000, 0x0000, 0);
        var nextButton = new LinuxEvdevInputEvent(0, 0, 0x0001, 0x0130, 1);

        Assert.That(
            LinuxEvdevGamepadCapture.ClassifyInputEvent(synchronizationDropped, ref isResynchronizing),
            Is.EqualTo(LinuxEvdevGamepadCapture.InputEventAction.Ignore));
        Assert.That(isResynchronizing, Is.True);
        Assert.That(
            LinuxEvdevGamepadCapture.ClassifyInputEvent(droppedButton, ref isResynchronizing),
            Is.EqualTo(LinuxEvdevGamepadCapture.InputEventAction.Ignore));
        Assert.That(
            LinuxEvdevGamepadCapture.ClassifyInputEvent(synchronizationReport, ref isResynchronizing),
            Is.EqualTo(LinuxEvdevGamepadCapture.InputEventAction.Reconcile));
        Assert.That(isResynchronizing, Is.False);
        Assert.That(
            LinuxEvdevGamepadCapture.ClassifyInputEvent(nextButton, ref isResynchronizing),
            Is.EqualTo(LinuxEvdevGamepadCapture.InputEventAction.Process));
    }
}
