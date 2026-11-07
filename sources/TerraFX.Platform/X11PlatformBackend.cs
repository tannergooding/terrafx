// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using TerraFX;
using ClipboardTextResult = TerraFX.Platform.ClipboardStatus;

namespace TerraFX.Platform;

internal sealed unsafe partial class X11PlatformBackend : IPlatformBackend
{
    private const int ClientMessageEvent = 33;
    private const int DestroyNotifyEvent = 17;
    // Bound the root-window resource property before parsing it on the Platform owner.
    private const int MaximumResourceManagerBytes = 64 * 1024;
    private const long PropertyChangeMask = 1L << 22;
    private const int PropertyNotifyEvent = 28;
    private const int RandrNotifyEventOffset = 1;
    private const int RandrScreenChangeEventOffset = 0;
    private const long StructureNotifyMask = 1L << 17;
    private const int WindowedDpi = 96;

    private readonly Dictionary<nuint, WindowState> _windowsByNativeId = [];
    private readonly Dictionary<WindowHandle, WindowState> _windowsByHandle = [];
    private readonly List<WindowHandle> _closeRequests = [];
    private readonly ClipboardServiceStatus _clipboardStatus;
    private readonly nint _display;
    private float? _effectiveDpi;
    private readonly uint _ownerId;
    private readonly int _ownerThreadId;
    private readonly int _randrEventBase;
    private readonly bool _randrMonitorInfoAvailable;
    private readonly nuint _resourceManagerAtom;
    private readonly nuint _rootWindow;
    private readonly nuint _wmDeleteWindowAtom;
    private readonly nuint _wmProtocolsAtom;
    private ImmutableArray<MonitorHandle> _monitorHandles = [];
    private bool _isDisposed;
    private bool _monitorTopologyChanged;

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
            var resourceManagerAtom = X11Native.XInternAtom(display, "RESOURCE_MANAGER", onlyIfExists: 0);
            var randrEventBase = -1;
            var randrErrorBase = 0;
            var randrMajorVersion = 1;
            var randrMinorVersion = 2;
            var randrEventsAvailable = false;
            var randrMonitorInfoAvailable = false;

            if ((rootWindow == 0)
                || (wmProtocolsAtom == 0)
                || (wmDeleteWindowAtom == 0)
                || (resourceManagerAtom == 0))
            {
                throw new InvalidOperationException("X11 did not provide the required default-screen, window-manager, or resource-manager atoms.");
            }

            if (X11Native.XSelectInput(display, rootWindow, checked((nint)PropertyChangeMask)) == 0)
            {
                throw new InvalidOperationException("X11 rejected resource-manager property notifications on its root window.");
            }

            if ((X11Native.XRRQueryExtension(display, &randrEventBase, &randrErrorBase) != 0)
                && (X11Native.XRRQueryVersion(display, &randrMajorVersion, &randrMinorVersion) != 0))
            {
                var randrEventMask = X11Native.RandrCrtcChangeNotifyMask
                    | X11Native.RandrOutputChangeNotifyMask
                    | X11Native.RandrOutputPropertyNotifyMask
                    | X11Native.RandrScreenChangeNotifyMask;

                if ((randrMajorVersion > 1) || ((randrMajorVersion == 1) && (randrMinorVersion >= 4)))
                {
                    randrEventMask |= X11Native.RandrProviderChangeNotifyMask
                        | X11Native.RandrProviderPropertyNotifyMask
                        | X11Native.RandrResourceChangeNotifyMask;
                }

                if ((randrMajorVersion > 1) || ((randrMajorVersion == 1) && (randrMinorVersion >= 6)))
                {
                    randrEventMask |= X11Native.RandrLeaseNotifyMask;
                }

                X11Native.XRRSelectInput(display, rootWindow, randrEventMask);
                randrEventsAvailable = true;
                randrMonitorInfoAvailable = (randrMajorVersion > 1)
                    || ((randrMajorVersion == 1) && (randrMinorVersion >= 5));
            }

            _display = display;
            _rootWindow = rootWindow;
            _randrEventBase = randrEventsAvailable ? randrEventBase : -1;
            _randrMonitorInfoAvailable = randrMonitorInfoAvailable;
            _resourceManagerAtom = resourceManagerAtom;
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
        RefreshDpiSettings();
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

    internal static float? ParseXftDpiSettings(string resourceManager)
    {
        ArgumentNullException.ThrowIfNull(resourceManager);
        resourceManager = resourceManager.TrimEnd('\0');
        float? effectiveDpi = null;

        foreach (var line in resourceManager.Split('\n'))
        {
            var entry = line.AsSpan().Trim();

            if (entry.IsEmpty || (entry[0] == '!'))
            {
                continue;
            }

            var separatorIndex = entry.IndexOfAny(':', '=');

            if ((separatorIndex < 0)
                || !entry[..separatorIndex].Trim().Equals("Xft.dpi".AsSpan(), StringComparison.Ordinal))
            {
                continue;
            }

            var value = entry[(separatorIndex + 1)..].Trim();

            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var dpi)
                || !float.IsFinite(dpi)
                || (dpi <= 0.0f))
            {
                throw new InvalidOperationException("The X11 Xft.dpi resource is not a finite positive DPI value.");
            }

            var scale = dpi / WindowedDpi;

            if (!float.IsFinite(scale) || (scale <= 0.0f))
            {
                throw new InvalidOperationException("The X11 Xft.dpi resource produces an invalid content scale.");
            }

            effectiveDpi = dpi;
        }

        return effectiveDpi;
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

    private static float? ReadXftDpiSettings(nint display, nuint rootWindow, nuint resourceManagerAtom)
    {
        var actualType = (nuint)0;
        var actualFormat = 0;
        var itemCount = (nuint)0;
        var bytesAfter = (nuint)0;
        nint propertyValue = 0;
        var status = X11Native.XGetWindowProperty(
            display,
            rootWindow,
            resourceManagerAtom,
            longOffset: 0,
            longLength: checked((nint)(MaximumResourceManagerBytes / sizeof(int))),
            delete: 0,
            requestedType: 0,
            &actualType,
            &actualFormat,
            &itemCount,
            &bytesAfter,
            &propertyValue);

        try
        {
            if (status != 0)
            {
                throw new InvalidOperationException("X11 could not read the root resource-manager property.");
            }

            if ((actualType == 0) && (itemCount == 0) && (bytesAfter == 0) && (propertyValue == 0))
            {
                return null;
            }

            if ((actualType == 0)
                || (actualFormat != 8)
                || (bytesAfter != 0)
                || (itemCount > checked((nuint)MaximumResourceManagerBytes))
                || ((itemCount != 0) && (propertyValue == 0)))
            {
                throw new InvalidOperationException("The X11 resource-manager property has an unsupported or unbounded representation.");
            }

            if (itemCount == 0)
            {
                return null;
            }

            var resourceBytes = new ReadOnlySpan<byte>((void*)propertyValue, checked((int)itemCount));
            var resourceManager = Encoding.ASCII.GetString(resourceBytes);
            return ParseXftDpiSettings(resourceManager);
        }
        finally
        {
            if (propertyValue != 0)
            {
                _ = X11Native.XFree(propertyValue);
            }
        }
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

        if ((options.Monitor is not null) != (nativeMonitorHandle != 0))
        {
            throw new ArgumentException("The native monitor binding does not match the requested monitor option.", nameof(nativeMonitorHandle));
        }

        var width = GetWindowExtent(options.ClientSize.X, nameof(options.ClientSize));
        var height = GetWindowExtent(options.ClientSize.Y, nameof(options.ClientSize));
        var x = 0;
        var y = 0;

        if (nativeMonitorHandle != 0)
        {
            if (!TryGetRandrMonitor(nativeMonitorHandle, out var monitor))
            {
                throw new InvalidOperationException("The requested X11 monitor is no longer available.");
            }

            x = checked(monitor.X + ((monitor.Width - width) / 2));
            y = checked(monitor.Y + ((monitor.Height - height) / 2));
        }

        var nativeWindow = X11Native.XCreateSimpleWindow(
            _display,
            _rootWindow,
            x,
            y,
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

        if (!_randrMonitorInfoAvailable)
        {
            return [];
        }

        var monitorCount = 0;
        var nativeMonitors = X11Native.XRRGetMonitors(_display, _rootWindow, getActive: 1, &monitorCount);

        if (monitorCount < 0)
        {
            if (nativeMonitors != 0)
            {
                X11Native.XRRFreeMonitors(nativeMonitors);
            }

            throw new InvalidOperationException("X11 RandR returned a negative monitor count.");
        }

        if (nativeMonitors == 0)
        {
            if (monitorCount != 0)
            {
                throw new InvalidOperationException("X11 RandR returned a monitor count without monitor data.");
            }

            return [];
        }

        try
        {
            var monitorInfos = ImmutableArray.CreateBuilder<PlatformMonitorInfo>(monitorCount);
            var monitors = (X11Native.RandrMonitorInfo*)nativeMonitors;

            for (var index = 0; index < monitorCount; index++)
            {
                var monitor = monitors[index];

                if ((monitor.Name == 0) || (monitor.Width <= 0) || (monitor.Height <= 0))
                {
                    throw new InvalidOperationException("X11 RandR returned an invalid active-monitor record.");
                }

                var atomName = X11Native.XGetAtomName(_display, monitor.Name);

                if (atomName == 0)
                {
                    throw new InvalidOperationException("X11 could not resolve an active monitor's RandR name.");
                }

                string deviceName;

                try
                {
                    deviceName = Marshal.PtrToStringUTF8(atomName)
                        ?? throw new InvalidOperationException("X11 returned a null RandR monitor name.");
                }
                finally
                {
                    _ = X11Native.XFree(atomName);
                }

                monitorInfos.Add(new PlatformMonitorInfo(deviceName, checked((nint)monitor.Name)));
            }

            return monitorInfos.MoveToImmutable();
        }
        finally
        {
            X11Native.XRRFreeMonitors(nativeMonitors);
        }
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
        var effectiveDpi = _effectiveDpi;
        var scale = (effectiveDpi is float dpi) ? dpi / WindowedDpi : 1.0f;
        var contentScale = new TerraFX.Numerics.Vector2(scale, scale);
        var logicalSize = new TerraFX.Numerics.Vector2(width / scale, height / scale);
        return new PlatformNativeWindowMetrics(
            logicalSize,
            pixelSize,
            contentScale,
            effectiveDpi,
            effectiveDpi.HasValue ? DpiSource.X11Settings : DpiSource.Unknown,
            effectiveDpi.HasValue ? DpiConfidence.Reported : DpiConfidence.Assumed96Dpi);
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

            if ((nativeEvent.Type == PropertyNotifyEvent)
                && (nativeEvent.Property.Window == _rootWindow)
                && (nativeEvent.Property.Atom == _resourceManagerAtom))
            {
                RefreshDpiSettings();
            }
            else if ((_randrEventBase >= 0)
                && ((nativeEvent.Type == (_randrEventBase + RandrScreenChangeEventOffset))
                    || (nativeEvent.Type == (_randrEventBase + RandrNotifyEventOffset))))
            {
                if (nativeEvent.Type == (_randrEventBase + RandrScreenChangeEventOffset)
                    && (X11Native.XRRUpdateConfiguration(&nativeEvent) == 0))
                {
                    throw new InvalidOperationException("X11 RandR could not apply a screen-configuration event.");
                }

                _monitorTopologyChanged = true;
            }
            else if (nativeEvent.Type == ClientMessageEvent)
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

        if (monitorHandles.IsDefault)
        {
            throw new ArgumentException("The monitor handle array must be initialized.", nameof(monitorHandles));
        }

        _monitorHandles = [.. monitorHandles];
    }

    public bool TakeMonitorTopologyChanged()
    {
        EnsureActiveOwnerThread();
        var monitorTopologyChanged = _monitorTopologyChanged;
        _monitorTopologyChanged = false;
        return monitorTopologyChanged;
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

    private void RefreshDpiSettings()
    {
        EnsureActiveOwnerThread();
        _effectiveDpi = ReadXftDpiSettings(_display, _rootWindow, _resourceManagerAtom);
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

    private bool TryGetRandrMonitor(nint nativeMonitorHandle, out X11Native.RandrMonitorInfo monitor)
    {
        monitor = default;

        if (!_randrMonitorInfoAvailable || (nativeMonitorHandle <= 0))
        {
            return false;
        }

        var monitorCount = 0;
        var nativeMonitors = X11Native.XRRGetMonitors(_display, _rootWindow, getActive: 1, &monitorCount);

        if (monitorCount < 0)
        {
            if (nativeMonitors != 0)
            {
                X11Native.XRRFreeMonitors(nativeMonitors);
            }

            throw new InvalidOperationException("X11 RandR returned a negative monitor count.");
        }

        if (nativeMonitors == 0)
        {
            if (monitorCount != 0)
            {
                throw new InvalidOperationException("X11 RandR returned a monitor count without monitor data.");
            }

            return false;
        }

        try
        {
            var monitors = (X11Native.RandrMonitorInfo*)nativeMonitors;
            var requestedName = checked((nuint)nativeMonitorHandle);

            for (var index = 0; index < monitorCount; index++)
            {
                var candidate = monitors[index];

                if (candidate.Name == requestedName)
                {
                    if ((candidate.Width <= 0) || (candidate.Height <= 0))
                    {
                        throw new InvalidOperationException("X11 RandR returned an invalid active-monitor extent.");
                    }

                    monitor = candidate;
                    return true;
                }
            }

            return false;
        }
        finally
        {
            X11Native.XRRFreeMonitors(nativeMonitors);
        }
    }

    private static unsafe partial class X11Native
    {
        internal const nint PMaxSize = 0x0020;
        internal const nint PMinSize = 0x0010;
        internal const int RandrCrtcChangeNotifyMask = 1 << 1;
        internal const int RandrLeaseNotifyMask = 1 << 7;
        internal const int RandrOutputChangeNotifyMask = 1 << 2;
        internal const int RandrOutputPropertyNotifyMask = 1 << 3;
        internal const int RandrProviderChangeNotifyMask = 1 << 4;
        internal const int RandrProviderPropertyNotifyMask = 1 << 5;
        internal const int RandrResourceChangeNotifyMask = 1 << 6;
        internal const int RandrScreenChangeNotifyMask = 1 << 0;

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

        [LibraryImport("libX11.so.6", EntryPoint = "XFree")]
        internal static partial int XFree(nint data);

        [LibraryImport("libX11.so.6", EntryPoint = "XGetAtomName")]
        internal static partial nint XGetAtomName(nint display, nuint atom);

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

        [LibraryImport("libX11.so.6", EntryPoint = "XGetWindowProperty")]
        internal static partial int XGetWindowProperty(
            nint display,
            nuint window,
            nuint property,
            nint longOffset,
            nint longLength,
            int delete,
            nuint requestedType,
            nuint* actualType,
            int* actualFormat,
            nuint* itemCount,
            nuint* bytesAfter,
            nint* propertyValue);

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

        [LibraryImport("libXrandr.so.2", EntryPoint = "XRRFreeMonitors")]
        internal static partial void XRRFreeMonitors(nint monitors);

        [LibraryImport("libXrandr.so.2", EntryPoint = "XRRGetMonitors")]
        internal static partial nint XRRGetMonitors(nint display, nuint window, int getActive, int* monitorCount);

        [LibraryImport("libXrandr.so.2", EntryPoint = "XRRQueryExtension")]
        internal static partial int XRRQueryExtension(nint display, int* eventBase, int* errorBase);

        [LibraryImport("libXrandr.so.2", EntryPoint = "XRRQueryVersion")]
        internal static partial int XRRQueryVersion(nint display, int* majorVersion, int* minorVersion);

        [LibraryImport("libXrandr.so.2", EntryPoint = "XRRSelectInput")]
        internal static partial void XRRSelectInput(nint display, nuint window, int mask);

        [LibraryImport("libXrandr.so.2", EntryPoint = "XRRUpdateConfiguration")]
        internal static partial int XRRUpdateConfiguration(NativeEvent* nativeEvent);

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

            [FieldOffset(0)]
            internal PropertyEvent Property;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PropertyEvent
        {
            internal int Type;
            internal nuint Serial;
            internal int SendEvent;
            internal nint Display;
            internal nuint Window;
            internal nuint Atom;
            internal nuint Time;
            internal int State;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RandrMonitorInfo
        {
            internal nuint Name;
            internal int Primary;
            internal int Automatic;
            internal int OutputCount;
            internal int X;
            internal int Y;
            internal int Width;
            internal int Height;
            internal int WidthMillimeters;
            internal int HeightMillimeters;
            internal nint Outputs;
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
