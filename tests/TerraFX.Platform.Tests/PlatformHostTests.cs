// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Numerics;

namespace TerraFX.Platform.Tests;

[TestFixture]
internal sealed class PlatformHostTests
{
    [Test]
    public void ClipboardServiceStatusValuesAreStable()
    {
        Assert.That((int)ClipboardServiceStatus.NotRequested, Is.EqualTo(0));
        Assert.That((int)ClipboardServiceStatus.Ready, Is.EqualTo(1));
        Assert.That((int)ClipboardServiceStatus.Unsupported, Is.EqualTo(2));
    }

    [Test]
    public void HeadlessHostReportsNoMonitorsAndUnrequestedClipboard()
    {
        var host = new PlatformHost(1, PlatformBackend.Headless, ClipboardServicePolicy.NotRequested);

        try
        {
            Assert.That(host.ClipboardStatus, Is.EqualTo(ClipboardServiceStatus.NotRequested));
            Assert.That(host.GetMonitorHandles(), Is.Empty);
            host.Pump();
        }
        finally
        {
            host.DisposeFromOwner();
        }
    }

    [Test]
    public void MonitorSnapshotAndPumpAreOwnerThreadBound()
    {
        var host = new PlatformHost(4, PlatformBackend.Headless, ClipboardServicePolicy.NotRequested);

        try
        {
            _ = Assert.Throws<System.InvalidOperationException>(
                () => Task.Run(host.GetMonitorHandles).GetAwaiter().GetResult());
            _ = Assert.Throws<System.InvalidOperationException>(
                () => Task.Run(host.Pump).GetAwaiter().GetResult());
        }
        finally
        {
            host.DisposeFromOwner();
        }
    }

    [Test]
    public void OptionalClipboardIsUnsupportedAndRequiredAdmissionFails()
    {
        var optional = new PlatformHost(2, PlatformBackend.Headless, ClipboardServicePolicy.Optional);

        try
        {
            Assert.That(optional.ClipboardStatus, Is.EqualTo(ClipboardServiceStatus.Unsupported));
            var clipboardText = optional.ClipboardText
                ?? throw new System.InvalidOperationException("The optional clipboard service was not exposed.");
            var response = clipboardText.ReadTextAsync(
                new ClipboardReadRequest(new TerraFX.WindowHandle(2, index: 0, generation: 1), 16, gestureToken: null))
                .GetAwaiter()
                .GetResult();
            Assert.That(response.Status, Is.EqualTo(ClipboardStatus.Unsupported));
        }
        finally
        {
            optional.DisposeFromOwner();
        }

        _ = Assert.Throws<System.NotSupportedException>(
            () => _ = new PlatformHost(3, PlatformBackend.Headless, ClipboardServicePolicy.Required));
    }

    [Test]
    public void SetClientSizeCommandValidatesAndForwardsLogicalExtent()
    {
        var window = new WindowHandle(ownerId: 5, index: 0, generation: 1);
        var callCount = 0;
        var receivedSize = default(Vector2);
        var platform = new PlatformHost(
            ownerId: 5,
            PlatformBackend.Headless,
            ClipboardServicePolicy.NotRequested,
            setClientSizeAsync: (requestedWindow, logicalClientSize, _) => {
                callCount++;
                receivedSize = logicalClientSize;
                return Task.FromResult(new WindowCommandReceipt(
                    WindowCommandKind.SetClientSize,
                    requestedWindow,
                    WindowCommandStatus.Applied,
                    ownerSequence: 11,
                    diagnostic: null));
            });

        try
        {
            var receipt = platform.SetClientSizeAsync(window, new Vector2(640.0f, 360.0f)).GetAwaiter().GetResult();
            Assert.That(receipt.Status, Is.EqualTo(WindowCommandStatus.Applied));
            Assert.That(receipt.Operation, Is.EqualTo(WindowCommandKind.SetClientSize));
            Assert.That(receipt.Window, Is.EqualTo(window));
            Assert.That(receipt.OwnerSequence, Is.EqualTo(11));
            Assert.That(receivedSize, Is.EqualTo(new Vector2(640.0f, 360.0f)));
            Assert.That(callCount, Is.EqualTo(1));
            _ = Assert.Throws<ArgumentOutOfRangeException>(
                () => platform.SetClientSizeAsync(window, new Vector2(0.0f, 360.0f)));
            _ = Assert.Throws<ArgumentException>(
                () => platform.SetClientSizeAsync(
                    new WindowHandle(ownerId: 6, index: 0, generation: 1),
                    new Vector2(640.0f, 360.0f)));
            Assert.That(callCount, Is.EqualTo(1));
        }
        finally
        {
            platform.DisposeFromOwner();
        }
    }

    [Test]
    public void SetTitleAndCloseCommandsForwardOwnerResults()
    {
        var window = new WindowHandle(ownerId: 7, index: 0, generation: 1);
        var receivedTitle = string.Empty;
        var closeCount = 0;
        var platform = new PlatformHost(
            ownerId: 7,
            PlatformBackend.Headless,
            ClipboardServicePolicy.NotRequested,
            setTitleAsync: (requestedWindow, requestedTitle, _) => {
                receivedTitle = requestedTitle;
                return Task.FromResult(new WindowCommandReceipt(
                    WindowCommandKind.SetTitle,
                    requestedWindow,
                    WindowCommandStatus.Applied,
                    ownerSequence: 13,
                    diagnostic: null));
            },
            closeWindowAsync: (requestedWindow, _) => {
                closeCount++;
                return Task.FromResult(new WindowCommandReceipt(
                    WindowCommandKind.Close,
                    requestedWindow,
                    WindowCommandStatus.Applied,
                    ownerSequence: 14,
                    diagnostic: null));
            });

        try
        {
            var titleReceipt = platform.SetTitleAsync(window, "Resizable window").GetAwaiter().GetResult();
            var closeReceipt = platform.CloseWindowAsync(window).GetAwaiter().GetResult();

            Assert.That(titleReceipt.Operation, Is.EqualTo(WindowCommandKind.SetTitle));
            Assert.That(titleReceipt.OwnerSequence, Is.EqualTo(13));
            Assert.That(closeReceipt.Operation, Is.EqualTo(WindowCommandKind.Close));
            Assert.That(closeReceipt.OwnerSequence, Is.EqualTo(14));
            Assert.That(receivedTitle, Is.EqualTo("Resizable window"));
            Assert.That(closeCount, Is.EqualTo(1));
            _ = Assert.Throws<ArgumentException>(() => platform.SetTitleAsync(
                new WindowHandle(ownerId: 8, index: 0, generation: 1),
                "foreign"));
            _ = Assert.Throws<ArgumentException>(() => platform.SetTitleAsync(window, "bad\0title"));
        }
        finally
        {
            platform.DisposeFromOwner();
        }
    }

    [Test]
    public void WaylandFallbackDiagnosticIsDrainedOnce()
    {
        if (!OperatingSystem.IsLinux()
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY"))
            || (Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") != "terrafx-unavailable")
            || (Environment.GetEnvironmentVariable("TERRAFX_TEST_WAYLAND_UNAVAILABLE") != "1"))
        {
            Assert.Ignore("The fallback witness requires Linux X11 and an explicitly unavailable Wayland display.");
            return;
        }

        var host = new PlatformHost(5, PlatformBackend.WaylandThenX11, ClipboardServicePolicy.NotRequested);

        try
        {
            var diagnostics = host.DrainStartupDiagnostics();

            Assert.That(diagnostics, Has.Length.EqualTo(1));
            Assert.That(diagnostics[0].Code, Is.EqualTo("platform-wayland-fallback-x11"));
            Assert.That(diagnostics[0].Message, Does.Contain("selected the X11 backend"));
            Assert.That(host.DrainStartupDiagnostics(), Is.Empty);
        }
        finally
        {
            host.DisposeFromOwner();
        }
    }
}
