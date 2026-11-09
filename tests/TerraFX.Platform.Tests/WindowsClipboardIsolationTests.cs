// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using NUnit.Framework;
using TerraFX;

namespace TerraFX.Platform.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class WindowsClipboardIsolationTests
{
    [Test]
    [SupportedOSPlatform("windows10.0.22000")]
    public void ReadsAndWritesClipboardOnATestOwnedWindowStation()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Assert.Ignore("The isolated Windows clipboard witness requires Windows 11.");
            return;
        }

        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            Assert.Ignore("The Windows Platform backend requires x64.");
            return;
        }

        using var testGate = new Mutex(initiallyOwned: false, @"Local\TerraFX.WindowsClipboardIsolationTests");
        var gateAcquired = false;

        try
        {
            try
            {
                gateAcquired = testGate.WaitOne(TimeSpan.FromMinutes(5));
            }
            catch (AbandonedMutexException)
            {
                gateAcquired = true;
            }

            if (!gateAcquired)
            {
                throw new TimeoutException("Timed out waiting for the isolated Windows clipboard test mutex.");
            }

            using var station = WindowsClipboardTestWindowStation.TryCreate(out var failureMessage);

            if (station is null)
            {
                Assert.Ignore(failureMessage ?? "A private Windows clipboard station could not be created.");
                return;
            }

            var root = WindowsClipboardTestProcess.FindRepositoryRoot(AppContext.BaseDirectory);
            var configuration = new System.IO.DirectoryInfo(AppContext.BaseDirectory).Parent?.Name
                ?? throw new InvalidOperationException("The test output configuration could not be determined.");
            var projectPath = System.IO.Path.Combine(root, "tests", "TerraFX.Platform.Tests", "TerraFX.Platform.Tests.csproj");

            WindowsClipboardTestProcess.RunClipboardChild(
                projectPath,
                root,
                configuration,
                station.WindowStationName,
                station.DesktopName);
        }
        finally
        {
            if (gateAcquired)
            {
                testGate.ReleaseMutex();
            }
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    [SupportedOSPlatform("windows10.0.22000")]
    public void ReadsAndWritesClipboardOnTestOwnedStationInChild()
    {
        if (Environment.GetEnvironmentVariable(WindowsClipboardTestProcess.ChildMarker) != "1")
        {
            Assert.Ignore("This witness runs only in the child process on a test-owned window station.");
            return;
        }

        WindowsClipboardTestWindowStation.VerifyCurrentStation(
            Environment.GetEnvironmentVariable(WindowsClipboardTestProcess.StationMarker)
                ?? throw new InvalidOperationException("The child process omitted its station identity."),
            Environment.GetEnvironmentVariable(WindowsClipboardTestProcess.DesktopMarker)
                ?? throw new InvalidOperationException("The child process omitted its desktop identity."));

        var ownerId = OwnerIdAllocator.Allocate();
        var window = new WindowHandle(ownerId, index: 0, generation: 1);
        using var backend = new WindowsPlatformBackend(ownerId, ClipboardServicePolicy.Optional);
        backend.CreateWindow(
            window,
            new WindowOptions("TerraFX isolated clipboard witness") {
                Visible = false,
            },
            nativeMonitorHandle: 0);

        const string text = "TerraFX \u03A9 \uD83D\uDE80";
        var writeResponse = backend.WriteClipboardText(new ClipboardWriteRequest(
            window,
            text,
            maximumUtf8Bytes: 64,
            gestureToken: null));
        Assert.That(writeResponse.Status, Is.EqualTo(ClipboardStatus.Completed), writeResponse.Diagnostic?.Message);

        var readResponse = backend.ReadClipboardText(new ClipboardReadRequest(
            window,
            maximumUtf8Bytes: 64,
            gestureToken: null));
        Assert.That(readResponse.Status, Is.EqualTo(ClipboardStatus.Completed), readResponse.Diagnostic?.Message);
        Assert.That(readResponse.Text, Is.EqualTo(text));
    }
}
