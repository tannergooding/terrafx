// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using GameInputSharp.Abstractions;
using GameInputSharp.Devices;
using NUnit.Framework;

namespace TerraFX.Platform.Tests;

[TestFixture]
[NonParallelizable]
[SupportedOSPlatform("windows")]
internal sealed class WindowsGameInputCaptureTests
{
    [Test]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "The MTA probe transfers exact GameInput initialization or enumeration failures to the NUnit test thread.")]
    public void ManualReportsMtaGameInputDeviceInventory()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("The GameInput adapter targets Windows.");
        }

        if (!StringComparer.Ordinal.Equals(
            Environment.GetEnvironmentVariable("TERRAFX_RUN_M04_GAMEPAD"),
            "1"))
        {
            Assert.Ignore("Set TERRAFX_RUN_M04_GAMEPAD=1 to run the MTA GameInput inventory probe.");
        }

        var result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => {
            try
            {
                using var manager = new GameInputManager(logger: null, loadOnlyFromSystem32: true);
                var (initialized, rawDeviceCount) = manager.GetDiagnostics();
                var deviceKinds = new List<string>();

                foreach (var device in manager.GetDevices())
                {
                    deviceKinds.Add(device is GamepadDevice ? "Gamepad" : device.GetType().Name);
                    device.Dispose();
                }

                _ = result.TrySetResult(
                    $"Initialized={initialized}; RawDevices={rawDeviceCount}; Devices=[{string.Join(", ", deviceKinds)}]");
            }
            catch (Exception exception)
            {
                _ = result.TrySetException(exception);
            }
        }) {
            IsBackground = true,
            Name = "TerraFX GameInput inventory probe",
        };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();

        var report = result.Task.GetAwaiter().GetResult();
        TestContext.WriteLine(report);
        Assert.That(report, Does.StartWith("Initialized=True;"));
    }

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

    [Test]
    public void ReusesTheProcessGameInputAcrossCaptureLifetimes()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("The GameInput adapter targets Windows.");
        }

        for (var captureIndex = 0; captureIndex < 3; captureIndex++)
        {
            var samples = new PlatformInputSampleQueue(Environment.CurrentManagedThreadId);
            using var capture = new WindowsGameInputCapture(samples, TimeProvider.System);

            Assert.That(
                capture.GamepadStatus,
                Is.AnyOf(PlatformServiceStatus.Available, PlatformServiceStatus.Unavailable));
            Assert.DoesNotThrow(capture.Pump);
            Assert.That(samples.Drain().Samples.IsDefault, Is.False);
        }
    }
}
