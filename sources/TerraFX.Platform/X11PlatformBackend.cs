// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using TerraFX;
using ClipboardTextResult = TerraFX.Platform.ClipboardStatus;

namespace TerraFX.Platform;

internal sealed unsafe partial class X11PlatformBackend : IPlatformBackend
{
    private const int ClientMessageEvent = 33;
    private const int DestroyNotifyEvent = 17;
    private const long StructureNotifyMask = 1L << 17;
    private const int WindowedDpi = 96;

    private readonly Dictionary<nuint, WindowState> _windowsByNativeId = [];
    private readonly Dictionary<WindowHandle, WindowState> _windowsByHandle = [];
    private readonly List<WindowHandle> _closeRequests = [];
    private readonly ClipboardServiceStatus _clipboardStatus;
    private readonly nint _display;
    private readonly uint _ownerId;
    private readonly int _ownerThreadId;
    private readonly nuint _rootWindow;
    private readonly int _screen;
    private readonly nuint _wmDeleteWindowAtom;
    private readonly nuint _wmProtocolsAtom;
    private ImmutableArray<MonitorHandle> _monitorHandles = [];
    private bool _isDisposed;

    internal X11PlatformBackend(uint ownerId, ClipboardServicePolicy clipboardPolicy)
    {
        ArgumentOutOfRangeException.ThrowIfZero(ownerId);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((int)clipboardPolicy, (int)ClipboardServicePolicy.Required);
        ArgumentOutOfRangeException.ThrowIfNegative((int)clipboardPolicy);

        if (clipboardPolicy is ClipboardServicePolicy.Required)
        {
            throw new NotSupportedException("The X11 backend does not provide clipboard transfers yet.");
        }

        var display = X11Native.XOpenDisplay(null);

        if (display == 0)
        {
            throw new PlatformNotSupportedException("The X11 display connection could not be opened.");
        }

        try
        {
            var screen = X11Native.XDefaultScreen(display);
            var rootWindow = X11Native.XRootWindow(display, screen);
            var wmProtocolsAtom = X11Native.XInternAtom(display, "WM_PROTOCOLS", onlyIfExists: 0);
            var wmDeleteWindowAtom = X11Native.XInternAtom(display, "WM_DELETE_WINDOW", onlyIfExists: 0);

            if ((rootWindow == 0)
                || (wmProtocolsAtom == 0)
                || (wmDeleteWindowAtom == 0))
            {
                throw new InvalidOperationException("X11 did not provide the required default-screen or window-manager atoms.");
            }

            _display = display;
            _screen = screen;
            _rootWindow = rootWindow;
            _wmProtocolsAtom = wmProtocolsAtom;
            _wmDeleteWindowAtom = wmDeleteWindowAtom;
        }
        catch
        {
            _ = X11Native.XCloseDisplay(display);
            throw;
        }

        _ownerId = ownerId;
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _clipboardStatus = (clipboardPolicy is ClipboardServicePolicy.Optional)
            ? ClipboardServiceStatus.Unsupported
            : ClipboardServiceStatus.NotRequested;
    }

    public PlatformServiceStatus AudioStatus
    {
        get
        {
            EnsureOwnerThread();
            return PlatformServiceStatus.NotRequested;
        }
    }

    public PlatformBackend Backend
    {
        get
        {
            EnsureOwnerThread();
            return PlatformBackend.X11Only;
        }
    }

    public ClipboardServiceStatus ClipboardStatus
    {
        get
        {
            EnsureOwnerThread();
            return _clipboardStatus;
        }
    }

    public PlatformServiceStatus DisplayStatus
    {
        get
        {
            EnsureOwnerThread();
            return PlatformServiceStatus.Available;
        }
    }

    public PlatformServiceStatus GamepadStatus
    {
        get
        {
            EnsureOwnerThread();
            return PlatformServiceStatus.Unsupported;
        }
    }

    public uint OwnerId => _ownerId;

    public PlatformServiceStatus PhysicalInputStatus
    {
        get
        {
            EnsureOwnerThread();
            return PlatformServiceStatus.Unsupported;
        }
    }

    private static int GetWindowExtent(float logicalExtent, string parameterName)
    {
        var extent = Math.Round(logicalExtent, MidpointRounding.ToEven);

        if (!double.IsFinite(extent) || (extent < 1.0) || (extent > int.MaxValue))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return checked((int)extent);
    }

    private static void SetWindowSizeHints(nint display, nuint window, WindowOptions options, int width, int height)
    {
        var minimumWidth = GetWindowExtent(options.MinimumClientSize.X, nameof(options.MinimumClientSize));
        var minimumHeight = GetWindowExtent(options.MinimumClientSize.Y, nameof(options.MinimumClientSize));
        var hints = new X11Native.SizeHints {
            Flags = X11Native.PMinSize,
            MinWidth = minimumWidth,
            MinHeight = minimumHeight,
        };

        if (!options.Resizable)
        {
            hints.Flags |= X11Native.PMaxSize;
            hints.MaxWidth = width;
            hints.MaxHeight = height;
        }

        X11Native.XSetWMNormalHints(display, window, &hints);
    }

    public void CloseWindow(WindowHandle window)
    {
        EnsureActiveOwnerThread();

        if (!_windowsByHandle.Remove(window, out var state))
        {
            throw new InvalidOperationException("The X11 window handle is stale or no longer active.");
        }

        _ = _windowsByNativeId.Remove(state.NativeWindow);

        if (!state.NativeDestroyed)
        {
            _ = X11Native.XDestroyWindow(_display, state.NativeWindow);
            _ = X11Native.XSync(_display, discard: 0);
        }

        _ = _closeRequests.Remove(window);
    }

    public void CreateWindow(WindowHandle window, WindowOptions options, nint nativeMonitorHandle)
    {
        EnsureActiveOwnerThread();
        ArgumentNullException.ThrowIfNull(options);

        if (!window.IsValid || (window.OwnerId != _ownerId))
        {
            throw new ArgumentException("The window handle belongs to another X11 owner.", nameof(window));
        }

        if (_windowsByHandle.ContainsKey(window))
        {
            throw new InvalidOperationException("The X11 owner already contains this window handle.");
        }

        if (options.Mode is not WindowMode.Windowed)
        {
            throw new NotSupportedException("The X11 backend currently admits only windowed windows.");
        }

        if ((options.Monitor is not null) != (nativeMonitorHandle != 0)
            || ((nativeMonitorHandle != 0) && (unchecked((nuint)nativeMonitorHandle) != _rootWindow)))
        {
            throw new NotSupportedException("The X11 backend only supports default-screen window placement.");
        }

        var width = GetWindowExtent(options.ClientSize.X, nameof(options.ClientSize));
        var height = GetWindowExtent(options.ClientSize.Y, nameof(options.ClientSize));
        var nativeWindow = X11Native.XCreateSimpleWindow(
            _display,
            _rootWindow,
            0,
            0,
            checked((uint)width),
            checked((uint)height),
            borderWidth: 0,
            border: 0,
            background: 0);

        if (nativeWindow == 0)
        {
            throw new InvalidOperationException("X11 did not create a native window.");
        }

        var state = new WindowState(window, nativeWindow);

        try
        {
            if (X11Native.XSelectInput(_display, nativeWindow, checked((nint)StructureNotifyMask)) == 0)
            {
                throw new InvalidOperationException("X11 rejected structure notifications for the new window.");
            }

            SetWindowTitle(nativeWindow, options.Title);
            SetWindowSizeHints(_display, nativeWindow, options, width, height);
            var deleteWindowAtom = _wmDeleteWindowAtom;

            if (X11Native.XSetWMProtocols(_display, nativeWindow, &deleteWindowAtom, 1) == 0)
            {
                throw new InvalidOperationException("The X11 window manager rejected the close protocol.");
            }

            _windowsByHandle.Add(window, state);
            _windowsByNativeId.Add(nativeWindow, state);

            if (options.Visible)
            {
                _ = X11Native.XMapWindow(_display, nativeWindow);
            }

            _ = X11Native.XSync(_display, discard: 0);
        }
        catch (Exception exception)
        {
            _ = _windowsByHandle.Remove(window);
            _ = _windowsByNativeId.Remove(nativeWindow);
            _ = X11Native.XDestroyWindow(_display, nativeWindow);
            _ = X11Native.XSync(_display, discard: 0);
            throw new InvalidOperationException("X11 window creation failed and the partially created window was destroyed.", exception);
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        EnsureOwnerThread();

        foreach (var state in _windowsByHandle.Values)
        {
            if (!state.NativeDestroyed)
            {
                _ = X11Native.XDestroyWindow(_display, state.NativeWindow);
            }
        }

        _windowsByNativeId.Clear();
        _windowsByHandle.Clear();
        _closeRequests.Clear();
        _ = X11Native.XSync(_display, discard: 0);
        _ = X11Native.XCloseDisplay(_display);
        _isDisposed = true;
    }

    public PlatformInputSampleBatch DrainInputSamples()
    {
        EnsureActiveOwnerThread();
        return new PlatformInputSampleBatch([], 0);
    }

    public ImmutableArray<WindowHandle> DrainWindowCloseRequests()
    {
        EnsureActiveOwnerThread();
        var closeRequests = ImmutableArray.CreateRange(_closeRequests);
        _closeRequests.Clear();
        return closeRequests;
    }

    public ImmutableArray<MonitorHandle> GetMonitorHandles()
    {
        EnsureActiveOwnerThread();
        return _monitorHandles;
    }

    public ImmutableArray<PlatformMonitorInfo> GetMonitorInfo()
    {
        EnsureActiveOwnerThread();
        return [new PlatformMonitorInfo($"X11.Screen.{_screen}", checked((nint)_rootWindow))];
    }

    public PlatformNativeWindowMetrics GetNativeWindowMetrics(WindowHandle window)
    {
        EnsureActiveOwnerThread();
        var state = GetWindowState(window);
        var root = (nuint)0;
        var x = 0;
        var y = 0;
        var width = 0u;
        var height = 0u;
        var borderWidth = 0u;
        var depth = 0u;

        if (X11Native.XGetGeometry(
                _display,
                state.NativeWindow,
                &root,
                &x,
                &y,
                &width,
                &height,
                &borderWidth,
                &depth) == 0)
        {
            throw new InvalidOperationException("X11 could not query the native window extent.");
        }

        var pixelSize = new PixelSize(checked((int)width), checked((int)height));
        var logicalSize = new TerraFX.Numerics.Vector2(width, height);
        return new PlatformNativeWindowMetrics(
            logicalSize,
            pixelSize,
            new TerraFX.Numerics.Vector2(1.0f, 1.0f),
            WindowedDpi,
            DpiSource.Unknown,
            DpiConfidence.Assumed96Dpi);
    }

    public void Pump()
    {
        EnsureActiveOwnerThread();

        while (X11Native.XPending(_display) > 0)
        {
            var nativeEvent = default(X11Native.NativeEvent);

            if (X11Native.XNextEvent(_display, &nativeEvent) != 0)
            {
                throw new InvalidOperationException("X11 failed while draining its owner event queue.");
            }

            if (nativeEvent.Type == ClientMessageEvent)
            {
                var clientMessage = nativeEvent.ClientMessage;

                if ((clientMessage.MessageType == _wmProtocolsAtom)
                    && (unchecked((nuint)clientMessage.Data.Long0) == _wmDeleteWindowAtom)
                    && _windowsByNativeId.TryGetValue(clientMessage.Window, out var state))
                {
                    QueueCloseRequest(state);
                }
            }
            else if ((nativeEvent.Type == DestroyNotifyEvent)
                && _windowsByNativeId.TryGetValue(nativeEvent.Destroy.Window, out var destroyedState))
            {
                destroyedState.NativeDestroyed = true;
                QueueCloseRequest(destroyedState);
            }
        }
    }

    public ClipboardReadResponse ReadClipboardText(ClipboardReadRequest request)
    {
        EnsureActiveOwnerThread();
        ArgumentNullException.ThrowIfNull(request);
        return new ClipboardReadResponse(
            ClipboardTextResult.Unsupported,
            text: null,
            ClipboardTextService.CreateDiagnostic(
                "ClipboardUnsupported",
                request.Window,
                "ClipboardText.ReadTextAsync",
                nativeResult: null,
                "The X11 backend does not provide clipboard transfers yet."));
    }

    public void RequestGamepadStateReconciliation(nint nativeDeviceIdentity)
    {
        EnsureActiveOwnerThread();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero((long)nativeDeviceIdentity);
        throw new NotSupportedException("The X11 backend does not capture physical gamepads yet.");
    }

    public void RequestInputStateReconciliation(WindowHandle? window)
    {
        EnsureActiveOwnerThread();

        if (window is { } windowHandle)
        {
            _ = GetWindowState(windowHandle);
            throw new NotSupportedException("The X11 backend does not capture physical keyboard or pointer input yet.");
        }
    }

    public void SetMonitorHandles(ImmutableArray<MonitorHandle> monitorHandles)
    {
        EnsureActiveOwnerThread();

        if (monitorHandles.IsDefault || (monitorHandles.Length > 1))
        {
            throw new ArgumentException("The X11 backend exposes at most one default-screen monitor handle.", nameof(monitorHandles));
        }

        _monitorHandles = [.. monitorHandles];
    }

    public bool TakeMonitorTopologyChanged()
    {
        EnsureActiveOwnerThread();
        return false;
    }

    public bool TryGetNativeWindowBinding(WindowHandle window, out PlatformNativeWindowBinding binding)
    {
        EnsureActiveOwnerThread();

        if (window.IsValid
            && (window.OwnerId == _ownerId)
            && _windowsByHandle.TryGetValue(window, out var state)
            && !state.NativeDestroyed)
        {
            binding = new PlatformNativeWindowBinding(
                window,
                PlatformNativeSurfaceKind.X11,
                _display,
                checked((nint)state.NativeWindow));
            return true;
        }

        binding = default;
        return false;
    }

    public ClipboardWriteResponse WriteClipboardText(ClipboardWriteRequest request)
    {
        EnsureActiveOwnerThread();
        ArgumentNullException.ThrowIfNull(request);
        return new ClipboardWriteResponse(
            ClipboardTextResult.Unsupported,
            ClipboardTextService.CreateDiagnostic(
                "ClipboardUnsupported",
                request.Window,
                "ClipboardText.WriteTextAsync",
                nativeResult: null,
                "The X11 backend does not provide clipboard transfers yet."));
    }

    private void EnsureActiveOwnerThread()
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }

    private void EnsureOwnerThread()
    {
        if (_ownerThreadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("The X11 platform owner is restricted to its issuing thread.");
        }
    }

    private WindowState GetWindowState(WindowHandle window)
    {
        if (!window.IsValid || (window.OwnerId != _ownerId))
        {
            throw new ArgumentException("The window handle belongs to another X11 owner.", nameof(window));
        }

        if (!_windowsByHandle.TryGetValue(window, out var state) || state.NativeDestroyed)
        {
            throw new InvalidOperationException("The X11 window handle is stale or no longer active.");
        }

        return state;
    }

    private void QueueCloseRequest(WindowState state)
    {
        if (!state.CloseRequested)
        {
            state.CloseRequested = true;
            _closeRequests.Add(state.Window);
        }
    }

    private void SetWindowTitle(nuint window, string title)
    {
        _ = X11Native.XStoreName(_display, window, title);
        X11Native.Xutf8SetWMProperties(
            _display,
            window,
            title,
            title,
            argv: 0,
            argc: 0,
            normalHints: 0,
            wmHints: 0,
            classHints: 0);
    }

    private static unsafe partial class X11Native
    {
        internal const nint PMaxSize = 0x0020;
        internal const nint PMinSize = 0x0010;

        [LibraryImport("libX11.so.6", EntryPoint = "XCloseDisplay")]
        internal static partial int XCloseDisplay(nint display);

        [LibraryImport("libX11.so.6", EntryPoint = "XCreateSimpleWindow")]
        internal static partial nuint XCreateSimpleWindow(
            nint display,
            nuint parent,
            int x,
            int y,
            uint width,
            uint height,
            uint borderWidth,
            nuint border,
            nuint background);

        [LibraryImport("libX11.so.6", EntryPoint = "XDefaultScreen")]
        internal static partial int XDefaultScreen(nint display);

        [LibraryImport("libX11.so.6", EntryPoint = "XDestroyWindow")]
        internal static partial int XDestroyWindow(nint display, nuint window);

        [LibraryImport("libX11.so.6", EntryPoint = "XFlush")]
        internal static partial int XFlush(nint display);

        [LibraryImport("libX11.so.6", EntryPoint = "XGetGeometry")]
        internal static partial int XGetGeometry(
            nint display,
            nuint drawable,
            nuint* root,
            int* x,
            int* y,
            uint* width,
            uint* height,
            uint* borderWidth,
            uint* depth);

        [LibraryImport("libX11.so.6", EntryPoint = "XInternAtom", StringMarshalling = StringMarshalling.Utf8)]
        internal static partial nuint XInternAtom(nint display, string name, int onlyIfExists);

        [LibraryImport("libX11.so.6", EntryPoint = "XMapWindow")]
        internal static partial int XMapWindow(nint display, nuint window);

        [LibraryImport("libX11.so.6", EntryPoint = "XNextEvent")]
        internal static partial int XNextEvent(nint display, NativeEvent* nativeEvent);

        [LibraryImport("libX11.so.6", EntryPoint = "XOpenDisplay", StringMarshalling = StringMarshalling.Utf8)]
        internal static partial nint XOpenDisplay(string? displayName);

        [LibraryImport("libX11.so.6", EntryPoint = "XPending")]
        internal static partial int XPending(nint display);

        [LibraryImport("libX11.so.6", EntryPoint = "XRootWindow")]
        internal static partial nuint XRootWindow(nint display, int screen);

        [LibraryImport("libX11.so.6", EntryPoint = "XSelectInput")]
        internal static partial int XSelectInput(nint display, nuint window, nint eventMask);

        [LibraryImport("libX11.so.6", EntryPoint = "XSetWMNormalHints")]
        internal static partial void XSetWMNormalHints(nint display, nuint window, SizeHints* hints);

        [LibraryImport("libX11.so.6", EntryPoint = "XSetWMProtocols")]
        internal static partial int XSetWMProtocols(nint display, nuint window, nuint* protocols, int count);

        [LibraryImport("libX11.so.6", EntryPoint = "XStoreName", StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int XStoreName(nint display, nuint window, string windowName);

        [LibraryImport("libX11.so.6", EntryPoint = "XSync")]
        internal static partial int XSync(nint display, int discard);

        [LibraryImport("libX11.so.6", EntryPoint = "Xutf8SetWMProperties", StringMarshalling = StringMarshalling.Utf8)]
        internal static partial void Xutf8SetWMProperties(
            nint display,
            nuint window,
            string windowName,
            string iconName,
            nint argv,
            int argc,
            nint normalHints,
            nint wmHints,
            nint classHints);

        [StructLayout(LayoutKind.Explicit, Size = 40)]
        internal struct ClientMessageData
        {
            [FieldOffset(0)]
            internal nint Long0;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ClientMessageEvent
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
        internal struct DestroyWindowEvent
        {
            internal int Type;
            internal nuint Serial;
            internal int SendEvent;
            internal nint Display;
            internal nuint Event;
            internal nuint Window;
        }

        [StructLayout(LayoutKind.Explicit, Size = 192)]
        internal struct NativeEvent
        {
            [FieldOffset(0)]
            internal int Type;

            [FieldOffset(0)]
            internal ClientMessageEvent ClientMessage;

            [FieldOffset(0)]
            internal DestroyWindowEvent Destroy;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct SizeHints
        {
            internal nint Flags;
            internal int X;
            internal int Y;
            internal int Width;
            internal int Height;
            internal int MinWidth;
            internal int MinHeight;
            internal int MaxWidth;
            internal int MaxHeight;
            internal int WidthIncrement;
            internal int HeightIncrement;
            internal int MinAspectX;
            internal int MinAspectY;
            internal int MaxAspectX;
            internal int MaxAspectY;
            internal int BaseWidth;
            internal int BaseHeight;
            internal int WindowGravity;
        }
    }

    private sealed class WindowState
    {
        internal bool CloseRequested;
        internal bool NativeDestroyed;

        internal WindowState(WindowHandle window, nuint nativeWindow)
        {
            Window = window;
            NativeWindow = nativeWindow;
        }

        internal nuint NativeWindow { get; }

        internal WindowHandle Window { get; }
    }
}
