// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Hosting;
using TerraFX.Input;
using TerraFX.Platform;

namespace TerraFX.Runtime.Tests.Hosting;

internal static partial class WaylandClipboardTestSupport
{
    private const int SignalStop = 19;

    internal static PhysicalInputEvent CaptureWaylandPointerGesture(
        EngineHost host,
        WindowHandle window,
        nint x11Display)
    {
        InputRouter? router = null;
        InputRecorder? recorder = null;
        host.InvokeAsync(context => {
            router = context.CreateInputRouter(InputRecipientKind.HostLiveUI, window);
            _ = router.ServiceInputControlBoundary();
            recorder = router.StartRecording();
        }).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();

        SendPointerClick(x11Display, 320, 180);
        Thread.Sleep(150);

        InputRecording? recording = null;
        host.InvokeAsync(context => {
            var activeRouter = router
                ?? throw new InvalidOperationException("The Wayland input route was not created.");
            _ = activeRouter.ServiceInputControlBoundary();
            recording = recorder
                ?.Complete()
                ?? throw new InvalidOperationException("The Wayland input route omitted its recorder.");
            _ = context.DisposeInputRouter(activeRouter);
        }).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();

        return recording?.Entries
                .Where(static entry => entry.Kind == InputRecordingEntryKind.PhysicalInput)
                .Select(static entry => entry.PhysicalEvent)
                .FirstOrDefault(inputEvent =>
                    (inputEvent is not null)
                    && (inputEvent.Kind == PhysicalInputEventKind.PointerButtonDown)
                    && (inputEvent.NativeInputSerial is not null)
                    && (inputEvent.Window == window))
            ?? throw new InvalidOperationException("The private Wayland compositor did not deliver a serial-bearing pointer event to the host window.");
    }

    internal static PhysicalInputEvent CaptureWaylandPointerGesture(
        PlatformHostOwner platformOwner,
        WindowHandle window,
        nint x11Display)
    {
        SendPointerClick(x11Display, 320, 180);
        Thread.Sleep(150);
        var stopwatch = Stopwatch.StartNew();
        var capturedEventCount = 0;
        var pointerButtonDownCount = 0;
        var matchingWindowButtonDownCount = 0;

        while (stopwatch.Elapsed < TimeSpan.FromSeconds(5))
        {
            var capture = platformOwner.Pump();
            capturedEventCount += capture.Events.Length;

            foreach (var inputEvent in capture.Events)
            {
                if (inputEvent.Kind is not PhysicalInputEventKind.PointerButtonDown)
                {
                    continue;
                }

                pointerButtonDownCount++;

                if (inputEvent.Window == window)
                {
                    matchingWindowButtonDownCount++;

                    if (inputEvent.NativeInputSerial is not null)
                    {
                        return inputEvent;
                    }
                }
            }

            Thread.Sleep(10);
        }

        throw new TimeoutException(
            $"The private Wayland compositor delivered {capturedEventCount} captured event(s) and "
            + $"{pointerButtonDownCount} pointer-button event(s), "
            + $"including {matchingWindowButtonDownCount} for the target window, but none carried a serial.");
    }

    internal static void CloseDisplay(nint display)
    {
        _ = XCloseDisplay(display);
    }

    internal static IDisposable MapWindowSurface(PlatformHostOwner platformOwner, WindowHandle window)
    {
        ArgumentNullException.ThrowIfNull(platformOwner);
        var binding = platformOwner.GetNativeWindowBinding(window);

        if (binding.Kind is not PlatformNativeSurfaceKind.Wayland)
        {
            throw new InvalidOperationException("The private clipboard witness requires a Wayland surface.");
        }

        return new WaylandClipboardTestSurfaceBuffer(binding.NativeDisplayOrInstance, binding.NativeWindowOrSurface, 640, 360);
    }

    internal static nint OpenDisplay()
        => XOpenDisplay(Environment.GetEnvironmentVariable("DISPLAY"));

    internal static string RunWaylandClipboardClient(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("wl-paste") {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The private Wayland clipboard reader did not start.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(5_000))
        {
            process.Kill(entireProcessTree: true);
            _ = process.WaitForExit(5_000);
            throw new TimeoutException("The private Wayland clipboard reader did not complete its transfer.");
        }

        var output = standardOutput.GetAwaiter().GetResult();
        var error = standardError.GetAwaiter().GetResult();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"The private Wayland clipboard reader failed: {error}");
        }

        return output;
    }

    internal static string RunWaylandClipboardClientOnOwner(
        PlatformHostOwner platformOwner,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("wl-paste") {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The private Wayland clipboard reader did not start.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        var stopwatch = Stopwatch.StartNew();

        while (!process.HasExited && (stopwatch.Elapsed < TimeSpan.FromSeconds(5)))
        {
            _ = platformOwner.Pump();
            Thread.Sleep(1);
        }

        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            _ = process.WaitForExit(5_000);
            throw new TimeoutException("The private Wayland clipboard reader did not complete its transfer.");
        }

        var output = standardOutput.GetAwaiter().GetResult();
        var error = standardError.GetAwaiter().GetResult();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"The private Wayland clipboard reader failed: {error}");
        }

        return output;
    }

    internal static void SendPointerClick(nint display, int x, int y)
    {
        if (display == 0)
        {
            throw new InvalidOperationException("The private Xvfb display could not be opened.");
        }

        if ((XTestFakeMotionEvent(display, screenNumber: 0, x: 0, y: 0, delay: 0) == 0)
            || (XTestFakeMotionEvent(display, screenNumber: 0, x: x, y: y, delay: 0) == 0))
        {
            throw new InvalidOperationException("The private Xvfb XTest extension rejected the pointer motion.");
        }

        _ = XSync(display, discard: 0);
        Thread.Sleep(50);

        if ((XTestFakeButtonEvent(display, button: 1, isPress: 1, delay: 0) == 0)
            || (XTestFakeButtonEvent(display, button: 1, isPress: 0, delay: 0) == 0))
        {
            throw new InvalidOperationException("The private Xvfb XTest extension rejected the pointer button.");
        }

        _ = XSync(display, discard: 0);
    }

    internal static Process StartWaylandClipboardSource(string text, bool pasteOnce = true)
    {
        var startInfo = new ProcessStartInfo("wl-copy") {
            CreateNoWindow = true,
            UseShellExecute = false,
        };

        if (pasteOnce)
        {
            startInfo.ArgumentList.Add("--paste-once");
        }

        startInfo.ArgumentList.Add("--foreground");
        startInfo.ArgumentList.Add("--type");
        startInfo.ArgumentList.Add("text/plain");
        startInfo.ArgumentList.Add(text);
        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("The private Wayland clipboard source did not start.");
    }

    internal static void SuspendWaylandClipboardSource(Process clipboardSource)
    {
        ArgumentNullException.ThrowIfNull(clipboardSource);

        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("The private Wayland clipboard source can only be suspended on Linux.");
        }

        if (clipboardSource.HasExited)
        {
            throw new InvalidOperationException("The private Wayland clipboard source exited before it could be suspended.");
        }

        if (Kill(clipboardSource.Id, SignalStop) < 0)
        {
            throw new IOException($"The private Wayland clipboard source could not be suspended; native error {Marshal.GetLastPInvokeError()}.");
        }
    }

    internal static TResponse WaitForClipboardResponse<TResponse>(
        PlatformHostOwner platformOwner,
        Task<TResponse> responseTask,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(platformOwner);
        ArgumentNullException.ThrowIfNull(responseTask);

        var stopwatch = Stopwatch.StartNew();

        while (!responseTask.IsCompleted && (stopwatch.Elapsed < timeout))
        {
            _ = platformOwner.Pump();
            Thread.Sleep(1);
        }

        if (!responseTask.IsCompleted)
        {
            throw new TimeoutException("The private Wayland clipboard request did not complete before its bound.");
        }

        return responseTask.GetAwaiter().GetResult();
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static partial int Kill(int processId, int signal);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libX11.so.6", EntryPoint = "XCloseDisplay")]
    private static partial int XCloseDisplay(nint display);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libX11.so.6", EntryPoint = "XOpenDisplay", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint XOpenDisplay(string? displayName);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libX11.so.6", EntryPoint = "XSync")]
    private static partial int XSync(nint display, int discard);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libXtst.so.6", EntryPoint = "XTestFakeButtonEvent")]
    private static partial int XTestFakeButtonEvent(nint display, uint button, int isPress, nuint delay);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libXtst.so.6", EntryPoint = "XTestFakeMotionEvent")]
    private static partial int XTestFakeMotionEvent(nint display, int screenNumber, int x, int y, nuint delay);
}
