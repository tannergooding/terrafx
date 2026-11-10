// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using NUnit.Framework;
using TerraFX;
using TerraFX.Hosting;
using TerraFX.Numerics;
using TerraFX.Platform;

namespace TerraFX.Runtime.Tests.Hosting;

internal sealed partial class EngineHostOptionsTests
{
    [Test]
    public void WaylandClipboardPlatformOwnerTransfersTextWithNativeGestureOnIsolatedCompositor()
    {
        if (!OperatingSystem.IsLinux()
            || (Environment.GetEnvironmentVariable("TERRAFX_TEST_ISOLATED_WAYLAND_X11") != "1")
            || (Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") != "wayland-m04-desktop")
            || (Environment.GetEnvironmentVariable("DISPLAY") != ":124"))
        {
            Assert.Ignore("The Wayland clipboard integration witness requires the private Xvfb-backed Weston compositor.");
            return;
        }

        var requireClipboard = Environment.GetEnvironmentVariable("TERRAFX_TEST_WAYLAND_NO_CLIPBOARD") != "1";
        var (options, _, _) = CreateVulkanSurfaceTestSetup(
            PlatformBackend.WaylandOnly,
            clipboardServicePolicy: requireClipboard
                ? ClipboardServicePolicy.Required
                : ClipboardServicePolicy.NotRequested);
        var packagePath = options.AssetStoreOptions.AbsolutePackagePath;
        PlatformHostOwner? platformOwner = null;
        WindowHandle? window = null;
        nint x11Display = 0;
        Process? externalClipboardSource = null;

        try
        {
            platformOwner = PlatformHostOwner.Create(options);
            window = platformOwner.CreateWindow(new WindowOptions("Wayland clipboard service-only witness") {
                ClientSize = new Vector2(640.0f, 360.0f),
                Mode = WindowMode.BorderlessFullscreen,
                Visible = true,
            });

            if (requireClipboard)
            {
                Assert.That(platformOwner.Platform.ClipboardStatus, Is.EqualTo(ClipboardServiceStatus.Ready));
            }

            using var mappedSurface = WaylandClipboardTestSupport.MapWindowSurface(platformOwner, window.Value);

            if (!requireClipboard)
            {
                x11Display = WaylandClipboardTestSupport.OpenDisplay();
                _ = WaylandClipboardTestSupport.CaptureWaylandPointerGesture(
                    platformOwner,
                    window.Value,
                    x11Display);
                return;
            }

            x11Display = WaylandClipboardTestSupport.OpenDisplay();
            var clipboard = platformOwner.Platform.ClipboardText
                ?? throw new InvalidOperationException("The Wayland clipboard service was not initialized.");
            const string RuntimeText = "private Wayland service write";
            var refusedWrite = clipboard.WriteTextAsync(new ClipboardWriteRequest(
                window.Value,
                RuntimeText,
                maximumUtf8Bytes: 1024,
                gestureToken: null)).GetAwaiter().GetResult();
            Assert.That(refusedWrite.Status, Is.EqualTo(ClipboardStatus.PermissionDenied));

            var refusedRead = clipboard.ReadTextAsync(new ClipboardReadRequest(
                window.Value,
                maximumUtf8Bytes: 1024,
                gestureToken: null)).GetAwaiter().GetResult();
            Assert.That(refusedRead.Status, Is.EqualTo(ClipboardStatus.PermissionDenied));

            var writeGesture = WaylandClipboardTestSupport.CaptureWaylandPointerGesture(
                platformOwner,
                window.Value,
                x11Display);

            if (!clipboard.TryCreateUserGestureToken(writeGesture, out var writeToken))
            {
                throw new InvalidOperationException("The captured Wayland pointer event did not issue a clipboard gesture token.");
            }

            var writeResponse = WaylandClipboardTestSupport.WaitForClipboardResponse(
                platformOwner,
                clipboard.WriteTextAsync(new ClipboardWriteRequest(
                    window.Value,
                    RuntimeText,
                    maximumUtf8Bytes: 1024,
                    gestureToken: writeToken)),
                TimeSpan.FromSeconds(5));
            Assert.That(writeResponse.Status, Is.EqualTo(ClipboardStatus.Completed));

            var pastedText = WaylandClipboardTestSupport.RunWaylandClipboardClientOnOwner(
                platformOwner,
                "--no-newline");
            Assert.That(pastedText, Is.EqualTo(RuntimeText));

            const string ExternalText = "private Wayland service read";
            externalClipboardSource = WaylandClipboardTestSupport.StartWaylandClipboardSource(ExternalText);
            Thread.Sleep(100);
            var readGesture = WaylandClipboardTestSupport.CaptureWaylandPointerGesture(
                platformOwner,
                window.Value,
                x11Display);

            if (!clipboard.TryCreateUserGestureToken(readGesture, out var readToken))
            {
                throw new InvalidOperationException("The captured Wayland pointer event did not issue a clipboard gesture token.");
            }

            var readResponse = WaylandClipboardTestSupport.WaitForClipboardResponse(
                platformOwner,
                clipboard.ReadTextAsync(new ClipboardReadRequest(
                    window.Value,
                    maximumUtf8Bytes: 1024,
                    gestureToken: readToken)),
                TimeSpan.FromSeconds(5));
            Assert.That(readResponse.Status, Is.EqualTo(ClipboardStatus.Completed));
            Assert.That(readResponse.Text, Is.EqualTo(ExternalText));

            if (!externalClipboardSource.WaitForExit(milliseconds: 5_000))
            {
                externalClipboardSource.Kill(entireProcessTree: true);
                _ = externalClipboardSource.WaitForExit(milliseconds: 5_000);
                throw new TimeoutException("The private Wayland clipboard source did not complete its transfer.");
            }

            Assert.That(externalClipboardSource.HasExited, Is.True);
            externalClipboardSource.Dispose();
            externalClipboardSource = null;

            const string CancellationText = "private Wayland service cancellation";
            externalClipboardSource = WaylandClipboardTestSupport.StartWaylandClipboardSource(CancellationText, pasteOnce: false);
            var cancellationTypes = WaylandClipboardTestSupport.RunWaylandClipboardClientOnOwner(
                platformOwner,
                "--list-types");
            Assert.That(cancellationTypes, Does.Contain("text/plain"));
            WaylandClipboardTestSupport.SuspendWaylandClipboardSource(externalClipboardSource);

            var cancellationGesture = WaylandClipboardTestSupport.CaptureWaylandPointerGesture(
                platformOwner,
                window.Value,
                x11Display);

            if (!clipboard.TryCreateUserGestureToken(cancellationGesture, out var cancellationGestureToken))
            {
                throw new InvalidOperationException("The captured Wayland pointer event did not issue a clipboard gesture token.");
            }

            using var cancellationSource = new CancellationTokenSource();
            var cancellationTask = clipboard.ReadTextAsync(new ClipboardReadRequest(
                window.Value,
                maximumUtf8Bytes: 1024,
                gestureToken: cancellationGestureToken), cancellationSource.Token);
            _ = platformOwner.Pump();
            cancellationSource.Cancel();
            _ = Assert.Catch<OperationCanceledException>(() => WaylandClipboardTestSupport.WaitForClipboardResponse(
                platformOwner,
                cancellationTask,
                TimeSpan.FromSeconds(5)));

            externalClipboardSource.Kill(entireProcessTree: true);

            if (!externalClipboardSource.WaitForExit(milliseconds: 5_000))
            {
                throw new TimeoutException("The canceled Wayland clipboard source did not stop after termination.");
            }

            externalClipboardSource.Dispose();
            externalClipboardSource = null;
            _ = platformOwner.Pump();

            const string TimeoutText = "private Wayland service timeout";
            externalClipboardSource = WaylandClipboardTestSupport.StartWaylandClipboardSource(TimeoutText, pasteOnce: false);
            var timeoutTypes = WaylandClipboardTestSupport.RunWaylandClipboardClientOnOwner(
                platformOwner,
                "--list-types");
            Assert.That(timeoutTypes, Does.Contain("text/plain"));
            WaylandClipboardTestSupport.SuspendWaylandClipboardSource(externalClipboardSource);

            var timeoutGesture = WaylandClipboardTestSupport.CaptureWaylandPointerGesture(
                platformOwner,
                window.Value,
                x11Display);

            if (!clipboard.TryCreateUserGestureToken(timeoutGesture, out var timeoutGestureToken))
            {
                throw new InvalidOperationException("The captured Wayland pointer event did not issue a clipboard gesture token.");
            }

            var timeoutResponse = WaylandClipboardTestSupport.WaitForClipboardResponse(
                platformOwner,
                clipboard.ReadTextAsync(new ClipboardReadRequest(
                    window.Value,
                    maximumUtf8Bytes: 1024,
                    gestureToken: timeoutGestureToken)),
                TimeSpan.FromSeconds(7));
            Assert.That(timeoutResponse.Status, Is.EqualTo(ClipboardStatus.TimedOut));
        }
        finally
        {
            if (externalClipboardSource is { HasExited: false } clipboardSource)
            {
                clipboardSource.Kill(entireProcessTree: true);
                _ = clipboardSource.WaitForExit(milliseconds: 5_000);
            }

            externalClipboardSource?.Dispose();

            if (x11Display != 0)
            {
                WaylandClipboardTestSupport.CloseDisplay(x11Display);
            }

            try
            {
                if ((platformOwner is not null) && (window is WindowHandle activeWindow))
                {
                    platformOwner.CloseWindow(activeWindow);
                }
            }
            finally
            {
                try
                {
                    platformOwner?.DisposeFromOwner();
                }
                finally
                {
                    File.Delete(packagePath);
                }
            }
        }
    }
}
