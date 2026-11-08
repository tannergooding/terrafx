// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Input;
using TerraFX.Numerics;

namespace TerraFX.Platform.Tests;

[TestFixture]
internal sealed partial class HeadlessPlatformBackendTests
{
    private static IPlatformBackend CreateX11Backend() =>
        PlatformBackendFactory.Create(
            PlatformBackend.X11Only,
            ownerId: 7,
            ClipboardServicePolicy.NotRequested);

    [Test]
    public void HeadlessBackendMarksNativeServicesNotRequestedAndOptionalClipboardUnsupported()
    {
        using var headless = PlatformBackendFactory.Create(PlatformBackend.Headless, 1, ClipboardServicePolicy.NotRequested);
        using var optionalClipboard = PlatformBackendFactory.Create(PlatformBackend.Headless, 2, ClipboardServicePolicy.Optional);

        Assert.That(headless.Backend, Is.EqualTo(PlatformBackend.Headless));
        Assert.That(headless.DisplayStatus, Is.EqualTo(PlatformServiceStatus.NotRequested));
        Assert.That(headless.PhysicalInputStatus, Is.EqualTo(PlatformServiceStatus.NotRequested));
        Assert.That(headless.DrainInputSamples().Samples, Is.Empty);
        Assert.That(headless.AudioStatus, Is.EqualTo(PlatformServiceStatus.NotRequested));
        Assert.That(headless.ClipboardStatus, Is.EqualTo(ClipboardServiceStatus.NotRequested));
        Assert.That(optionalClipboard.ClipboardStatus, Is.EqualTo(ClipboardServiceStatus.Unsupported));
        Assert.That(
            () => PlatformBackendFactory.Create(PlatformBackend.Headless, 3, ClipboardServicePolicy.Required),
            Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void PlatformBackendSelectorsHaveStableValues()
    {
        Assert.That((int)PlatformBackend.None, Is.EqualTo(0));
        Assert.That((int)PlatformBackend.Headless, Is.EqualTo(1));
        Assert.That((int)PlatformBackend.WindowsNative, Is.EqualTo(2));
        Assert.That((int)PlatformBackend.WaylandOnly, Is.EqualTo(3));
        Assert.That((int)PlatformBackend.WaylandThenX11, Is.EqualTo(4));
        Assert.That((int)PlatformBackend.X11Only, Is.EqualTo(5));
    }

    [Test]
    public void PumpAndDisposeRequireTheHostOwnerThread()
    {
        var headless = PlatformBackendFactory.Create(PlatformBackend.Headless, 1, ClipboardServicePolicy.NotRequested);

        try
        {
            Assert.That(headless.OwnerId, Is.EqualTo(1));
            _ = Assert.Throws<InvalidOperationException>(() => Task.Run(headless.Pump).GetAwaiter().GetResult());
            _ = Assert.Throws<InvalidOperationException>(() => Task.Run(headless.Dispose).GetAwaiter().GetResult());
            headless.Dispose();
            headless.Dispose();
            _ = Assert.Throws<ObjectDisposedException>(headless.Pump);
        }
        finally
        {
            headless.Dispose();
        }
    }

    [Test]
    public void WaylandOnlySelectsWaylandWhenDisplayIsConfigured()
    {
        if (OperatingSystem.IsLinux()
            && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            using var wayland = PlatformBackendFactory.Create(
                PlatformBackend.WaylandOnly,
                1,
                ClipboardServicePolicy.NotRequested);

            Assert.That(wayland.Backend, Is.EqualTo(PlatformBackend.WaylandOnly));
        }
        else
        {
            _ = Assert.Throws<PlatformNotSupportedException>(
                () => PlatformBackendFactory.Create(PlatformBackend.WaylandOnly, 1, ClipboardServicePolicy.NotRequested));
        }

        if (!OperatingSystem.IsLinux())
        {
            _ = Assert.Throws<PlatformNotSupportedException>(
                () => PlatformBackendFactory.Create(PlatformBackend.WaylandThenX11, 1, ClipboardServicePolicy.NotRequested));
            _ = Assert.Throws<PlatformNotSupportedException>(
                () => PlatformBackendFactory.Create(PlatformBackend.X11Only, 1, ClipboardServicePolicy.NotRequested));
        }

        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => PlatformBackendFactory.Create(PlatformBackend.None, 1, ClipboardServicePolicy.NotRequested));
    }

    [Test]
    public void WaylandThenX11SelectsX11AndRetainsWaylandFailureWhenDisplayIsAvailable()
    {
        if (!OperatingSystem.IsLinux()
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY"))
            || (Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") != "terrafx-unavailable")
            || (Environment.GetEnvironmentVariable("TERRAFX_TEST_WAYLAND_UNAVAILABLE") != "1"))
        {
            Assert.Ignore("The fallback witness requires Linux X11 and an explicitly unavailable Wayland display.");
            return;
        }

        using var backend = PlatformBackendFactory.Create(
            PlatformBackend.WaylandThenX11,
            ownerId: 1,
            ClipboardServicePolicy.NotRequested,
            out var startupDiagnostic);

        Assert.That(backend.Backend, Is.EqualTo(PlatformBackend.X11Only));
        Assert.That(startupDiagnostic, Is.Not.Null);
        Assert.That(startupDiagnostic!.Code, Is.EqualTo("platform-wayland-fallback-x11"));
        Assert.That(startupDiagnostic.HostId, Is.EqualTo(1));
        Assert.That(startupDiagnostic.Message, Does.Contain("Wayland startup failed"));
        Assert.That(startupDiagnostic.Message, Does.Contain("selected the X11 backend"));
    }

    [Test]
    public void X11DpiSettingsParserUsesOnlyFinitePositiveXftDpiValues()
    {
        Assert.That(X11PlatformBackend.ParseXftDpiSettings("Xft.dpi: 144\n"), Is.EqualTo(144.0f));
        Assert.That(X11PlatformBackend.ParseXftDpiSettings("Xft.dpi: 144\n\0\0"), Is.EqualTo(144.0f));
        Assert.That(
            X11PlatformBackend.ParseXftDpiSettings("Xft.dpi: 120\nXft.dpi=168\n"),
            Is.EqualTo(168.0f));
        Assert.That(X11PlatformBackend.ParseXftDpiSettings("Xft.antialias: 1\n"), Is.Null);
        _ = Assert.Throws<InvalidOperationException>(
            () => X11PlatformBackend.ParseXftDpiSettings("Xft.dpi: NaN\n"));
    }

    [Test]
    public void X11OnlyCreatesAndRetiresAnInvisibleNativeWindowWhenDisplayIsAvailable()
    {
        if (!OperatingSystem.IsLinux()
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            Assert.Ignore("The X11 native window witness requires a Linux X11 display.");
            return;
        }

        IPlatformBackend? backend = null;

        try
        {
            try
            {
                backend = CreateX11Backend();
            }
            catch (DllNotFoundException exception)
            {
                Assert.Ignore($"The X11 client library is unavailable: {exception.Message}");
                return;
            }
            catch (PlatformNotSupportedException exception)
            {
                Assert.Ignore($"The X11 display is unavailable: {exception.Message}");
                return;
            }

            Assert.That(backend.Backend, Is.EqualTo(PlatformBackend.X11Only));
            Assert.That(backend.DisplayStatus, Is.EqualTo(PlatformServiceStatus.Available));
            Assert.That(backend.PhysicalInputStatus, Is.EqualTo(PlatformServiceStatus.Available));
            Assert.That(
                backend.GamepadStatus,
                Is.AnyOf(
                    PlatformServiceStatus.Available,
                    PlatformServiceStatus.PermissionDenied,
                    PlatformServiceStatus.Unsupported));
            Assert.That(backend.ClipboardStatus, Is.EqualTo(ClipboardServiceStatus.NotRequested));
            var x11Backend = backend as X11PlatformBackend
                ?? throw new InvalidOperationException("The selected X11 backend has an unexpected implementation.");
            TestContext.WriteLine(
                $"XInput2={x11Backend.XInput2Available}; XKB keymap={x11Backend.HasKeyboardKeymap}.");

            var nativeMonitors = backend.GetMonitorInfo();
            MonitorHandle? monitorHandle = null;
            nint nativeMonitorHandle = 0;

            if (!nativeMonitors.IsEmpty)
            {
                monitorHandle = new MonitorHandle(ownerId: 7, index: 0, generation: 1);
                backend.SetMonitorHandles([monitorHandle.Value]);
                nativeMonitorHandle = nativeMonitors[0].NativeHandle;
            }

            var window = new WindowHandle(ownerId: 7, index: 0, generation: 1);
            backend.CreateWindow(
                window,
                new WindowOptions("TerraFX X11 test") {
                    Visible = false,
                    Monitor = monitorHandle,
                },
                nativeMonitorHandle);

            try
            {
                var metrics = backend.GetNativeWindowMetrics(window);
                Assert.That(
                    metrics.ClientPixelSize.Width,
                    Is.EqualTo(checked((int)Math.Round(1280.0f * metrics.ContentScale.X, MidpointRounding.ToEven))));
                Assert.That(
                    metrics.ClientPixelSize.Height,
                    Is.EqualTo(checked((int)Math.Round(720.0f * metrics.ContentScale.Y, MidpointRounding.ToEven))));
                var dpiLabel = metrics.EffectiveDpi?.ToString(CultureInfo.InvariantCulture) ?? "none";
                TestContext.WriteLine($"X11 DPI source={metrics.DpiSource}; effective DPI={dpiLabel}.");

                if (metrics.EffectiveDpi is float effectiveDpi)
                {
                    Assert.That(metrics.DpiSource, Is.EqualTo(DpiSource.X11Settings));
                    Assert.That(metrics.DpiConfidence, Is.EqualTo(DpiConfidence.Reported));
                    Assert.That(metrics.ContentScale.X, Is.EqualTo(effectiveDpi / 96.0f).Within(0.0001f));
                    Assert.That(metrics.ContentScale.Y, Is.EqualTo(effectiveDpi / 96.0f).Within(0.0001f));
                }
                else
                {
                    Assert.That(metrics.DpiSource, Is.EqualTo(DpiSource.Unknown));
                    Assert.That(metrics.DpiConfidence, Is.EqualTo(DpiConfidence.Assumed96Dpi));
                    Assert.That(metrics.ContentScale.X, Is.EqualTo(1.0f));
                    Assert.That(metrics.ContentScale.Y, Is.EqualTo(1.0f));
                }

                Assert.That(
                    metrics.LogicalClientSize.X * metrics.ContentScale.X,
                    Is.EqualTo(metrics.ClientPixelSize.Width).Within(0.01f));
                Assert.That(
                    metrics.LogicalClientSize.Y * metrics.ContentScale.Y,
                    Is.EqualTo(metrics.ClientPixelSize.Height).Within(0.01f));

                var resizeStatus = backend.SetWindowClientSize(window, new Vector2(640.0f, 360.0f));
                Assert.That(resizeStatus, Is.EqualTo(WindowCommandStatus.Applied));
                var resizedMetrics = backend.GetNativeWindowMetrics(window);
                Assert.That(
                    resizedMetrics.ClientPixelSize.Width,
                    Is.EqualTo(checked((int)Math.Round(640.0f * resizedMetrics.ContentScale.X, MidpointRounding.ToEven))));
                Assert.That(
                    resizedMetrics.ClientPixelSize.Height,
                    Is.EqualTo(checked((int)Math.Round(360.0f * resizedMetrics.ContentScale.Y, MidpointRounding.ToEven))));
                Assert.That(
                    resizedMetrics.LogicalClientSize.X,
                    Is.EqualTo(640.0f).Within(0.51f / resizedMetrics.ContentScale.X));
                Assert.That(
                    resizedMetrics.LogicalClientSize.Y,
                    Is.EqualTo(360.0f).Within(0.51f / resizedMetrics.ContentScale.Y));
                Assert.That(
                    backend.SetWindowTitle(window, "TerraFX X11 resized test"),
                    Is.EqualTo(WindowCommandStatus.Applied));

                Assert.That(backend.TryGetNativeWindowBinding(window, out var binding), Is.True);
                Assert.That(binding.Kind, Is.EqualTo(PlatformNativeSurfaceKind.X11));
                Assert.That(binding.NativeDisplayOrInstance, Is.Not.Zero);
                Assert.That(binding.NativeWindowOrSurface, Is.Not.Zero);
                Assert.That(backend.DrainInputSamples().Samples, Is.Empty);
                backend.RequestInputStateReconciliation(window);
                var reconciliation = backend.DrainInputSamples().Samples;
                Assert.That(reconciliation, Has.Length.EqualTo(1));
                Assert.That(reconciliation[0].Kind, Is.EqualTo(PhysicalInputEventKind.StateReconciliation));
                Assert.That(reconciliation[0].ReconciliationState.IsDefault, Is.False);

                X11TestNative.SendKeyEvent(binding.NativeDisplayOrInstance, binding.NativeWindowOrSurface, 38, isDown: true, time: 100);
                X11TestNative.SendKeyEvent(binding.NativeDisplayOrInstance, binding.NativeWindowOrSurface, 38, isDown: false, time: 100);
                X11TestNative.SendKeyEvent(binding.NativeDisplayOrInstance, binding.NativeWindowOrSurface, 38, isDown: true, time: 100);
                X11TestNative.SendKeyEvent(binding.NativeDisplayOrInstance, binding.NativeWindowOrSurface, 38, isDown: false, time: 101);
                X11TestNative.SendButtonEvent(binding.NativeDisplayOrInstance, binding.NativeWindowOrSurface, 1, isDown: true);
                X11TestNative.SendButtonEvent(binding.NativeDisplayOrInstance, binding.NativeWindowOrSurface, 1, isDown: false);
                X11TestNative.SendMotionEvent(binding.NativeDisplayOrInstance, binding.NativeWindowOrSurface, x: 17, y: 29);
                backend.Pump();
                var inputSamples = backend.DrainInputSamples();
                Assert.That(inputSamples.DiscardedSampleCount, Is.Zero);
                Assert.That(inputSamples.Samples, Has.Length.EqualTo(6));
                Assert.That(inputSamples.Samples[0].Kind, Is.EqualTo(PhysicalInputEventKind.KeyDown));
                Assert.That(inputSamples.Samples[0].DeviceKind, Is.EqualTo(PlatformInputDeviceKind.Keyboard));
                Assert.That(inputSamples.Samples[0].NativeDeviceIdentity, Is.EqualTo((nint)(-1)));
                Assert.That(inputSamples.Samples[0].NativeUsagePage, Is.EqualTo((ushort)0x0007));
                Assert.That(inputSamples.Samples[0].NativeUsage, Is.EqualTo((ushort)0x0004));
                Assert.That(inputSamples.Samples[0].NativeScanCode, Is.EqualTo(38u));
                Assert.That(inputSamples.Samples[1].Kind, Is.EqualTo(PhysicalInputEventKind.KeyRepeat));
                Assert.That(inputSamples.Samples[2].Kind, Is.EqualTo(PhysicalInputEventKind.KeyUp));
                Assert.That(inputSamples.Samples[3].Kind, Is.EqualTo(PhysicalInputEventKind.PointerButtonDown));
                Assert.That(inputSamples.Samples[3].NativeDeviceIdentity, Is.EqualTo((nint)(-2)));
                Assert.That(inputSamples.Samples[3].MouseButton, Is.EqualTo(MouseButtonValue.Primary));
                Assert.That(inputSamples.Samples[4].Kind, Is.EqualTo(PhysicalInputEventKind.PointerButtonUp));
                Assert.That(inputSamples.Samples[5].Kind, Is.EqualTo(PhysicalInputEventKind.PointerAbsoluteMotion));
                Assert.That(inputSamples.Samples[5].PointerPosition?.X, Is.EqualTo(17.0f));
                Assert.That(inputSamples.Samples[5].PointerPosition?.Y, Is.EqualTo(29.0f));

                X11TestNative.SendDeleteWindow(binding.NativeDisplayOrInstance, binding.NativeWindowOrSurface);
                X11TestNative.SendDeleteWindow(binding.NativeDisplayOrInstance, binding.NativeWindowOrSurface);
                backend.Pump();
                var closeRequests = backend.DrainWindowCloseRequests();
                Assert.That(closeRequests, Has.Length.EqualTo(1));
                Assert.That(closeRequests[0], Is.EqualTo(window));
                backend.Pump();
                Assert.That(backend.DrainWindowCloseRequests(), Is.Empty);
            }
            finally
            {
                backend.CloseWindow(window);
            }

            Assert.That(backend.TryGetNativeWindowBinding(window, out _), Is.False);
        }
        finally
        {
            backend?.Dispose();
        }
    }

    [Test]
    public void X11OnlyReportsRandrMonitorSelectionWhenAvailable()
    {
        if (!OperatingSystem.IsLinux()
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            Assert.Ignore("The X11 monitor witness requires a Linux X11 display.");
            return;
        }

        IPlatformBackend? backend = null;

        try
        {
            try
            {
                backend = CreateX11Backend();
            }
            catch (DllNotFoundException exception)
            {
                Assert.Ignore($"The X11 client library is unavailable: {exception.Message}");
                return;
            }
            catch (PlatformNotSupportedException exception)
            {
                Assert.Ignore($"The X11 display is unavailable: {exception.Message}");
                return;
            }

            var nativeMonitors = backend.GetMonitorInfo();

            if (nativeMonitors.IsEmpty)
            {
                Assert.Ignore("The X11 server does not expose active RandR monitor records.");
                return;
            }

            var monitorHandle = new MonitorHandle(ownerId: 7, index: 0, generation: 1);
            backend.SetMonitorHandles([monitorHandle]);

            var window = new WindowHandle(ownerId: 7, index: 0, generation: 1);
            backend.CreateWindow(
                window,
                new WindowOptions("TerraFX X11 monitor test") {
                    Visible = false,
                    Monitor = monitorHandle,
                },
                nativeMonitors[0].NativeHandle);

            try
            {
                Assert.That(backend.GetNativeWindowMetrics(window).ClientPixelSize.Width, Is.EqualTo(1280));
                Assert.That(backend.GetNativeWindowMetrics(window).ClientPixelSize.Height, Is.EqualTo(720));
            }
            finally
            {
                backend.CloseWindow(window);
            }
        }
        finally
        {
            backend?.Dispose();
        }
    }

    private static unsafe partial class X11TestNative
    {
        private const int ButtonPressEventType = 4;
        private const nint ButtonPressMask = 4;
        private const int ButtonReleaseEventType = 5;
        private const nint ButtonReleaseMask = 8;
        private const int ClientMessageEventType = 33;
        private const int KeyPressEventType = 2;
        private const nint KeyPressMask = 1;
        private const int KeyReleaseEventType = 3;
        private const nint KeyReleaseMask = 2;
        private const int MotionNotifyEventType = 6;
        private const nint PointerMotionMask = 64;

        internal static void SendButtonEvent(nint display, nint nativeWindow, uint button, bool isDown)
        {
            var nativeEvent = default(NativeEvent);
            nativeEvent.Button.Type = isDown ? ButtonPressEventType : ButtonReleaseEventType;
            nativeEvent.Button.Display = display;
            nativeEvent.Button.Window = checked((nuint)nativeWindow);
            nativeEvent.Button.Time = 101;
            nativeEvent.Button.X = 11;
            nativeEvent.Button.Y = 13;
            nativeEvent.Button.Button = button;
            nativeEvent.Button.SameScreen = 1;
            var eventMask = isDown ? ButtonPressMask : ButtonReleaseMask;
            SendInputEvent(display, nativeWindow, eventMask, &nativeEvent);
        }

        internal static void SendDeleteWindow(nint display, nint nativeWindow)
        {
            var protocolsAtom = XInternAtom(display, "WM_PROTOCOLS", onlyIfExists: 0);
            var deleteWindowAtom = XInternAtom(display, "WM_DELETE_WINDOW", onlyIfExists: 0);
            var nativeEvent = default(NativeEvent);
            nativeEvent.ClientMessage.Type = ClientMessageEventType;
            nativeEvent.ClientMessage.Display = display;
            nativeEvent.ClientMessage.Window = checked((nuint)nativeWindow);
            nativeEvent.ClientMessage.MessageType = protocolsAtom;
            nativeEvent.ClientMessage.Format = 32;
            nativeEvent.ClientMessage.Data.Long0 = checked((nint)deleteWindowAtom);

            if ((protocolsAtom == 0)
                || (deleteWindowAtom == 0)
                || (XSendEvent(display, nativeEvent.ClientMessage.Window, propagate: 0, eventMask: 0, &nativeEvent) == 0))
            {
                throw new InvalidOperationException("The X11 test could not send a WM_DELETE_WINDOW client message.");
            }

            _ = XSync(display, discard: 0);
        }

        internal static void SendKeyEvent(nint display, nint nativeWindow, uint keycode, bool isDown, nuint time)
        {
            var nativeEvent = default(NativeEvent);
            nativeEvent.Key.Type = isDown ? KeyPressEventType : KeyReleaseEventType;
            nativeEvent.Key.Display = display;
            nativeEvent.Key.Window = checked((nuint)nativeWindow);
            nativeEvent.Key.Time = time;
            nativeEvent.Key.Keycode = keycode;
            nativeEvent.Key.SameScreen = 1;
            var eventMask = isDown ? KeyPressMask : KeyReleaseMask;
            SendInputEvent(display, nativeWindow, eventMask, &nativeEvent);
        }

        internal static void SendMotionEvent(nint display, nint nativeWindow, int x, int y)
        {
            var nativeEvent = default(NativeEvent);
            nativeEvent.Motion.Type = MotionNotifyEventType;
            nativeEvent.Motion.Display = display;
            nativeEvent.Motion.Window = checked((nuint)nativeWindow);
            nativeEvent.Motion.Time = 102;
            nativeEvent.Motion.X = x;
            nativeEvent.Motion.Y = y;
            nativeEvent.Motion.SameScreen = 1;
            SendInputEvent(display, nativeWindow, PointerMotionMask, &nativeEvent);
        }

        private static void SendInputEvent(nint display, nint nativeWindow, nint eventMask, NativeEvent* nativeEvent)
        {
            if (XSendEvent(display, checked((nuint)nativeWindow), propagate: 0, eventMask, nativeEvent) == 0)
            {
                throw new InvalidOperationException("The X11 test could not send a synthetic input event.");
            }

            _ = XSync(display, discard: 0);
        }

        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [LibraryImport("libX11.so.6", EntryPoint = "XInternAtom", StringMarshalling = StringMarshalling.Utf8)]
        private static partial nuint XInternAtom(nint display, string name, int onlyIfExists);

        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [LibraryImport("libX11.so.6", EntryPoint = "XSendEvent")]
        private static partial int XSendEvent(nint display, nuint window, int propagate, nint eventMask, NativeEvent* nativeEvent);

        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [LibraryImport("libX11.so.6", EntryPoint = "XSync")]
        private static partial int XSync(nint display, int discard);

        [StructLayout(LayoutKind.Sequential)]
        private struct ButtonEvent
        {
            internal int Type;
            internal nuint Serial;
            internal int SendEvent;
            internal nint Display;
            internal nuint Window;
            internal nuint Root;
            internal nuint Subwindow;
            internal nuint Time;
            internal int X;
            internal int Y;
            internal int XRoot;
            internal int YRoot;
            internal uint State;
            internal uint Button;
            internal int SameScreen;
        }

        [StructLayout(LayoutKind.Explicit, Size = 40)]
        private struct ClientMessageData
        {
            [FieldOffset(0)]
            internal nint Long0;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ClientMessageEvent
        {
            internal int Type;
            internal nuint Serial;
            internal int SendEvent;
            internal nint Display;
            internal nuint Window;
            internal nuint MessageType;
            internal int Format;
            internal ClientMessageData Data;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyEvent
        {
            internal int Type;
            internal nuint Serial;
            internal int SendEvent;
            internal nint Display;
            internal nuint Window;
            internal nuint Root;
            internal nuint Subwindow;
            internal nuint Time;
            internal int X;
            internal int Y;
            internal int XRoot;
            internal int YRoot;
            internal uint State;
            internal uint Keycode;
            internal int SameScreen;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MotionEvent
        {
            internal int Type;
            internal nuint Serial;
            internal int SendEvent;
            internal nint Display;
            internal nuint Window;
            internal nuint Root;
            internal nuint Subwindow;
            internal nuint Time;
            internal int X;
            internal int Y;
            internal int XRoot;
            internal int YRoot;
            internal uint State;
            internal byte IsHint;
            internal int SameScreen;
        }

        [StructLayout(LayoutKind.Explicit, Size = 192)]
        private struct NativeEvent
        {
            [FieldOffset(0)]
            internal int Type;

            [FieldOffset(0)]
            internal KeyEvent Key;

            [FieldOffset(0)]
            internal ButtonEvent Button;

            [FieldOffset(0)]
            internal MotionEvent Motion;

            [FieldOffset(0)]
            internal ClientMessageEvent ClientMessage;
        }
    }
}
