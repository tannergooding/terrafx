// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.Versioning;
using NUnit.Framework;

namespace TerraFX.Platform.Tests;

[TestFixture]
[SupportedOSPlatform("windows")]
internal sealed class WindowsGameInputCaptureTests
{
    [Test]
    public void ReportsRuntimeCapabilityAndKeepsPollingOnTheOwnerThread()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("The GameInput adapter targets Windows.");
        }

        var samples = new PlatformInputSampleQueue(Environment.CurrentManagedThreadId);
        using var capture = new WindowsGameInputCapture(samples, TimeProvider.System);

        Assert.That(
            capture.GamepadStatus,
            Is.AnyOf(PlatformServiceStatus.Available, PlatformServiceStatus.Unavailable));
        Assert.DoesNotThrow(capture.Pump);
        Assert.That(samples.Drain().Samples.IsDefault, Is.False);
    }
}
