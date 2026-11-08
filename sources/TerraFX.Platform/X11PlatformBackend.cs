// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using TerraFX;
using TerraFX.Input;
using TerraFX.Numerics;
using ClipboardTextResult = TerraFX.Platform.ClipboardStatus;

namespace TerraFX.Platform;

internal sealed unsafe partial class X11PlatformBackend : IPlatformBackend
{
    private const int ButtonPressEvent = 4;
    private const long ButtonPressMask = 1L << 2;
    private const int ButtonReleaseEvent = 5;
    private const long ButtonReleaseMask = 1L << 3;
    private const int ClientMessageEvent = 33;
    private const int DestroyNotifyEvent = 17;
    private const int EwmhAdd = 1;
    private const int EwmhRemove = 0;
    private const int EwmhSourceApplication = 1;
    private const long FocusChangeMask = 1L << 21;
    private const int FocusInEvent = 9;
    private const int FocusOutEvent = 10;
    private const int GenericEventType = 35;
    private const int KeyPressEvent = 2;
    private const long KeyPressMask = 1L << 0;
    private const int KeyReleaseEvent = 3;
    private const long KeyReleaseMask = 1L << 1;
    // Core Xlib events omit source-device IDs, so negative identities isolate their aggregate routes.
    private const nint KeyboardAggregateIdentity = -1;
    private const int MappingNotifyEvent = 34;
    // Bound the root-window resource property before parsing it on the Platform owner.
    private const int MaximumResourceManagerBytes = 64 * 1024;
    private const int MotionNotifyEvent = 6;
    private const nint PointerAggregateIdentity = -2;
    private const long PointerMotionMask = 1L << 6;
    private const long PropertyChangeMask = 1L << 22;
    private const int PropertyNotifyEvent = 28;
    private const int RandrNotifyEventOffset = 1;
    private const int RandrScreenChangeEventOffset = 0;
    private const long StructureNotifyMask = 1L << 17;
    private const long SubstructureNotifyMask = 1L << 19;
    private const long SubstructureRedirectMask = 1L << 20;
    private const int WindowedDpi = 96;

    private readonly nuint _atomAtom;
    private readonly ClipboardServiceStatus _clipboardStatus;
    private readonly List<WindowHandle> _closeRequests = [];
    private readonly nint _display;
    private float? _effectiveDpi;
    private readonly bool _ewmhFullscreenAvailable;
    private readonly LinuxEvdevGamepadCapture? _gamepadCapture;
    private readonly PlatformServiceStatus _gamepadStatus;
    private readonly PlatformInputSampleQueue _inputSamples;
    private bool _isDisposed;
    private readonly X11KeyboardKeymap? _keyboardKeymap;
    private ImmutableArray<MonitorHandle> _monitorHandles = [];
    private bool _monitorTopologyChanged;
    private readonly nuint _netWmStateAtom;
    private readonly nuint _netWmStateFullscreenAtom;
    private readonly uint _ownerId;
    private readonly int _ownerThreadId;
    private readonly int _randrEventBase;
    private readonly bool _randrMonitorInfoAvailable;
    private readonly nuint _resourceManagerAtom;
    private readonly nuint _rootWindow;
    private readonly nuint _wmDeleteWindowAtom;
    private readonly nuint _wmProtocolsAtom;
    private readonly Dictionary<WindowHandle, WindowState> _windowsByHandle = [];
    private readonly Dictionary<nuint, WindowState> _windowsByNativeId = [];
    private readonly bool _xinput2Available;
    private readonly int _xinput2Opcode;

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
            var atomAtom = X11Native.XInternAtom(display, "ATOM", onlyIfExists: 0);
            var netSupportedAtom = X11Native.XInternAtom(display, "_NET_SUPPORTED", onlyIfExists: 0);
            var netWmStateAtom = X11Native.XInternAtom(display, "_NET_WM_STATE", onlyIfExists: 0);
            var netWmStateFullscreenAtom = X11Native.XInternAtom(display, "_NET_WM_STATE_FULLSCREEN", onlyIfExists: 0);
            var randrEventBase = -1;
            var randrErrorBase = 0;
            var randrMajorVersion = 1;
            var randrMinorVersion = 2;
            var randrEventsAvailable = false;
            var randrMonitorInfoAvailable = false;
            var xinput2Opcode = 0;
            var xinput2EventBase = 0;
            var xinput2ErrorBase = 0;
            var xinput2Available = false;

            if ((rootWindow == 0)
                || (wmProtocolsAtom == 0)
                || (wmDeleteWindowAtom == 0)
                || (resourceManagerAtom == 0)
                || (atomAtom == 0)
                || (netSupportedAtom == 0)
                || (netWmStateAtom == 0)
                || (netWmStateFullscreenAtom == 0))
            {
                throw new InvalidOperationException("X11 did not provide the required default-screen, window-manager, or EWMH atoms.");
            }

            var ewmhFullscreenAvailable = IsEwmhFullscreenSupported(
                display,
                rootWindow,
                atomAtom,
                netSupportedAtom,
                netWmStateAtom,
                netWmStateFullscreenAtom);

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

            if (X11Native.XQueryExtension(
                    display,
                    "XInputExtension",
                    &xinput2Opcode,
                    &xinput2EventBase,
                    &xinput2ErrorBase) != 0)
            {
                var xinput2MajorVersion = 2;
                var xinput2MinorVersion = 0;

                try
                {
                    xinput2Available = (XInput2Native.XIQueryVersion(
                            display,
                            &xinput2MajorVersion,
                            &xinput2MinorVersion) == 0)
                        && (xinput2MajorVersion >= 2);
                }
                catch (DllNotFoundException)
                {
                    xinput2Available = false;
                }
                catch (EntryPointNotFoundException)
                {
                    xinput2Available = false;
                }
            }

            _display = display;
            _rootWindow = rootWindow;
            _atomAtom = atomAtom;
            _ewmhFullscreenAvailable = ewmhFullscreenAvailable;
            _netWmStateAtom = netWmStateAtom;
            _netWmStateFullscreenAtom = netWmStateFullscreenAtom;
            _randrEventBase = randrEventsAvailable ? randrEventBase : -1;
            _randrMonitorInfoAvailable = randrMonitorInfoAvailable;
            _resourceManagerAtom = resourceManagerAtom;
            _wmProtocolsAtom = wmProtocolsAtom;
            _wmDeleteWindowAtom = wmDeleteWindowAtom;
            _xinput2Available = xinput2Available;
            _xinput2Opcode = xinput2Available ? xinput2Opcode : 0;
        }
        catch
        {
            _ = X11Native.XCloseDisplay(display);
            throw;
        }

        _ownerId = ownerId;
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _inputSamples = new PlatformInputSampleQueue(_ownerThreadId);
        _keyboardKeymap = X11KeyboardKeymap.TryCreate(display);
        _clipboardStatus = (clipboardPolicy is ClipboardServicePolicy.Optional)
            ? ClipboardServiceStatus.Unsupported
            : ClipboardServiceStatus.NotRequested;

        try
        {
            try
            {
                _gamepadCapture = new LinuxEvdevGamepadCapture(_inputSamples, TimeProvider.System);
                _gamepadStatus = _gamepadCapture.GamepadStatus;
            }
            catch (DllNotFoundException)
            {
                _gamepadStatus = PlatformServiceStatus.Unsupported;
            }
            catch (EntryPointNotFoundException)
            {
                _gamepadStatus = PlatformServiceStatus.Unsupported;
            }

            RefreshDpiSettings();
        }
        catch
        {
            _gamepadCapture?.Dispose();
            _keyboardKeymap?.Dispose();
            _ = X11Native.XCloseDisplay(_display);
            throw;
        }
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
            return _gamepadCapture?.GamepadStatus ?? _gamepadStatus;
        }
    }

    public uint OwnerId => _ownerId;

    public PlatformServiceStatus PhysicalInputStatus
    {
        get
        {
            EnsureOwnerThread();
            return PlatformServiceStatus.Available;
        }
    }

    internal bool HasKeyboardKeymap => _keyboardKeymap is not null;

    internal bool XInput2Available => _xinput2Available;

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

    private static int GetWindowExtent(float logicalExtent, float scale, string parameterName)
    {
        var extent = Math.Round((double)logicalExtent * scale, MidpointRounding.ToEven);

        if (!double.IsFinite(extent) || (extent < 1.0) || (extent > int.MaxValue))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return checked((int)extent);
    }

    private static bool IsEwmhFullscreenSupported(
        nint display,
        nuint rootWindow,
        nuint atomAtom,
        nuint netSupportedAtom,
        nuint netWmStateAtom,
        nuint netWmStateFullscreenAtom)
    {
        var supportedAtoms = ReadAtomProperty(display, rootWindow, atomAtom, netSupportedAtom);
        var supportsWmState = false;
        var supportsFullscreen = false;

        foreach (var atom in supportedAtoms)
        {
            supportsWmState |= atom == netWmStateAtom;
            supportsFullscreen |= atom == netWmStateFullscreenAtom;
        }

        return supportsWmState && supportsFullscreen;
    }

    private static ImmutableArray<nuint> ReadAtomProperty(
        nint display,
        nuint window,
        nuint atomAtom,
        nuint property)
    {
        const int maximumAtomCount = 4096;
        var actualType = (nuint)0;
        var actualFormat = 0;
        var itemCount = (nuint)0;
        var bytesAfter = (nuint)0;
        nint propertyValue = 0;
        var status = X11Native.XGetWindowProperty(
            display,
            window,
            property,
            longOffset: 0,
            longLength: maximumAtomCount,
            delete: 0,
            requestedType: atomAtom,
            &actualType,
            &actualFormat,
            &itemCount,
            &bytesAfter,
            &propertyValue);

        try
        {
            if (status != 0)
            {
                throw new InvalidOperationException("X11 could not read an EWMH atom property.");
            }

            if ((actualType == 0) && (itemCount == 0) && (bytesAfter == 0) && (propertyValue == 0))
            {
                return [];
            }

            if ((actualType != atomAtom)
                || (actualFormat != 32)
                || (itemCount > maximumAtomCount)
                || (bytesAfter != 0)
                || ((itemCount != 0) && (propertyValue == 0)))
            {
                throw new InvalidOperationException("X11 returned an unsupported or unbounded EWMH atom property.");
            }

            var atoms = (nuint*)propertyValue;
            var result = ImmutableArray.CreateBuilder<nuint>(checked((int)itemCount));

            for (var index = 0; index < checked((int)itemCount); index++)
            {
                result.Add(atoms[index]);
            }

            return result.MoveToImmutable();
        }
        finally
        {
            if (propertyValue != 0)
            {
                _ = X11Native.XFree(propertyValue);
            }
        }
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

    private static void SetWindowSizeHints(
        nint display,
        nuint window,
        bool resizable,
        Vector2 minimumClientSize,
        int width,
        int height,
        float scale)
    {
        var minimumWidth = GetWindowExtent(minimumClientSize.X, scale, nameof(minimumClientSize));
        var minimumHeight = GetWindowExtent(minimumClientSize.Y, scale, nameof(minimumClientSize));
        var hints = new X11Native.SizeHints {
            Flags = X11Native.PMinSize,
            MinWidth = minimumWidth,
            MinHeight = minimumHeight,
        };

        if (!resizable)
        {
            hints.Flags |= X11Native.PMaxSize;
            hints.MaxWidth = width;
            hints.MaxHeight = height;
        }

        X11Native.XSetWMNormalHints(display, window, &hints);
    }

    private static bool TryMapMouseButton(uint button, out MouseButtonValue mouseButton)
    {
        mouseButton = button switch {
            1 => MouseButtonValue.Primary,
            2 => MouseButtonValue.Middle,
            3 => MouseButtonValue.Secondary,
            8 => MouseButtonValue.Auxiliary1,
            9 => MouseButtonValue.Auxiliary2,
            10 => MouseButtonValue.Auxiliary3,
            11 => MouseButtonValue.Auxiliary4,
            12 => MouseButtonValue.Auxiliary5,
            _ => default,
        };
        return button is (>= 1 and <= 3) or (>= 8 and <= 12);
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

        var scale = (_effectiveDpi is float dpi) ? dpi / WindowedDpi : 1.0f;
        var width = GetWindowExtent(options.ClientSize.X, scale, nameof(options.ClientSize));
        var height = GetWindowExtent(options.ClientSize.Y, scale, nameof(options.ClientSize));
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

        var state = new WindowState(window, nativeWindow, options);

        try
        {
            var eventMask = StructureNotifyMask
                | KeyPressMask
                | KeyReleaseMask
                | ButtonPressMask
                | ButtonReleaseMask
                | PointerMotionMask
                | FocusChangeMask;

            if (X11Native.XSelectInput(_display, nativeWindow, checked((nint)eventMask)) == 0)
            {
                throw new InvalidOperationException("X11 rejected structure or physical-input notifications for the new window.");
            }

            if (_xinput2Available)
            {
                var inputMask = stackalloc byte[1];
                inputMask[0] = (1 << XInput2Native.KeyPress)
                    | (1 << XInput2Native.KeyRelease)
                    | (1 << XInput2Native.ButtonPress)
                    | (1 << XInput2Native.ButtonRelease)
                    | (1 << XInput2Native.Motion);
                var xiEventMask = new XInput2Native.XIEventMask {
                    DeviceId = XInput2Native.AllMasterDevices,
                    MaskLength = 1,
                    Mask = inputMask,
                };

                if (XInput2Native.XISelectEvents(_display, nativeWindow, &xiEventMask, 1) != 0)
                {
                    throw new InvalidOperationException("XInput2 rejected keyboard and pointer selections for the new window.");
                }
            }

            SetWindowTitle(nativeWindow, options.Title);
            SetWindowSizeHints(
                _display,
                nativeWindow,
                options.Resizable,
                options.MinimumClientSize,
                width,
                height,
                scale);
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
        _gamepadCapture?.Dispose();
        _keyboardKeymap?.Dispose();
        _ = X11Native.XCloseDisplay(_display);
        _isDisposed = true;
    }

    public PlatformInputSampleBatch DrainInputSamples()
    {
        EnsureActiveOwnerThread();
        return _inputSamples.Drain();
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

    public PlatformNativeWindowModeState GetNativeWindowModeState(WindowHandle window)
    {
        EnsureActiveOwnerThread();
        var state = GetWindowState(window);
        WindowMode? observedMode = !_ewmhFullscreenAvailable
            ? (state.RequestedMode is WindowMode.Windowed ? WindowMode.Windowed : null)
            : IsWindowFullscreen(state.NativeWindow)
                ? WindowMode.BorderlessFullscreen
                : WindowMode.Windowed;
        return new PlatformNativeWindowModeState(state.RequestedMode, observedMode);
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

            if (nativeEvent.Type == MappingNotifyEvent)
            {
                if (X11Native.XRefreshKeyboardMapping(&nativeEvent.Mapping) == 0)
                {
                    throw new InvalidOperationException("X11 failed to refresh its keyboard mapping.");
                }

                if ((_keyboardKeymap is X11KeyboardKeymap keymap) && !keymap.Refresh())
                {
                    throw new InvalidOperationException("XKB could not refresh the core keyboard keymap.");
                }
            }

            else if ((nativeEvent.Type == PropertyNotifyEvent)
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
            else if ((nativeEvent.Type == KeyPressEvent)
                && (!_xinput2Available || (nativeEvent.Key.SendEvent != 0))
                && _windowsByNativeId.TryGetValue(nativeEvent.Key.Window, out var keyDownState))
            {
                CaptureKey(
                    keyDownState,
                    nativeEvent.Key.Keycode,
                    nativeEvent.Key.Time,
                    KeyboardAggregateIdentity,
                    isDown: true,
                    isRepeat: false);
            }
            else if ((nativeEvent.Type == KeyReleaseEvent)
                && (!_xinput2Available || (nativeEvent.Key.SendEvent != 0))
                && _windowsByNativeId.TryGetValue(nativeEvent.Key.Window, out var keyUpState))
            {
                if (!IsAutoRepeatRelease(nativeEvent.Key))
                {
                    CaptureKey(
                        keyUpState,
                        nativeEvent.Key.Keycode,
                        nativeEvent.Key.Time,
                        KeyboardAggregateIdentity,
                        isDown: false,
                        isRepeat: false);
                }
            }
            else if ((nativeEvent.Type == ButtonPressEvent)
                && (!_xinput2Available || (nativeEvent.Button.SendEvent != 0))
                && _windowsByNativeId.TryGetValue(nativeEvent.Button.Window, out var buttonDownState))
            {
                CaptureButton(
                    buttonDownState,
                    nativeEvent.Button.Button,
                    nativeEvent.Button.X,
                    nativeEvent.Button.Y,
                    nativeEvent.Button.Time,
                    PointerAggregateIdentity,
                    isDown: true);
            }
            else if ((nativeEvent.Type == ButtonReleaseEvent)
                && (!_xinput2Available || (nativeEvent.Button.SendEvent != 0))
                && _windowsByNativeId.TryGetValue(nativeEvent.Button.Window, out var buttonUpState))
            {
                CaptureButton(
                    buttonUpState,
                    nativeEvent.Button.Button,
                    nativeEvent.Button.X,
                    nativeEvent.Button.Y,
                    nativeEvent.Button.Time,
                    PointerAggregateIdentity,
                    isDown: false);
            }
            else if ((nativeEvent.Type == MotionNotifyEvent)
                && (!_xinput2Available || (nativeEvent.Motion.SendEvent != 0))
                && _windowsByNativeId.TryGetValue(nativeEvent.Motion.Window, out var motionState))
            {
                CaptureMotion(
                    motionState,
                    nativeEvent.Motion.X,
                    nativeEvent.Motion.Y,
                    nativeEvent.Motion.Time,
                    PointerAggregateIdentity);
            }
            else if ((nativeEvent.Type == FocusInEvent)
                && _windowsByNativeId.TryGetValue(nativeEvent.Focus.Window, out var focusInState))
            {
                CaptureFocus(focusInState, PhysicalInputEventKind.FocusGained);
            }
            else if ((nativeEvent.Type == FocusOutEvent)
                && _windowsByNativeId.TryGetValue(nativeEvent.Focus.Window, out var focusOutState))
            {
                CaptureFocus(focusOutState, PhysicalInputEventKind.FocusLost);
            }
            else if (nativeEvent.Type == GenericEventType)
            {
                CaptureXInput2Event(&nativeEvent.Cookie);
            }
        }

        _gamepadCapture?.Pump();
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

        if (_gamepadCapture is not LinuxEvdevGamepadCapture capture)
        {
            throw new NotSupportedException("The X11 backend does not provide Linux evdev gamepad capture.");
        }

        capture.RequestDeviceReconciliation(nativeDeviceIdentity);
    }

    public void RequestInputStateReconciliation(WindowHandle? window)
    {
        EnsureActiveOwnerThread();

        if (window is { } windowHandle)
        {
            var state = GetWindowState(windowHandle);
            QueueInputStateReconciliation(state);
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

    public WindowCommandStatus SetWindowClientSize(WindowHandle window, Vector2 logicalClientSize)
    {
        EnsureActiveOwnerThread();
        logicalClientSize = WindowOptions.ValidateClientSize(logicalClientSize, nameof(logicalClientSize));

        if (!_windowsByHandle.TryGetValue(window, out var state) || state.NativeDestroyed)
        {
            return WindowCommandStatus.Disposed;
        }

        var scale = (_effectiveDpi is float dpi) ? dpi / WindowedDpi : 1.0f;
        var width = GetWindowExtent(logicalClientSize.X, scale, nameof(logicalClientSize));
        var height = GetWindowExtent(logicalClientSize.Y, scale, nameof(logicalClientSize));
        SetWindowSizeHints(
            _display,
            state.NativeWindow,
            state.Resizable,
            state.MinimumClientSize,
            width,
            height,
            scale);

        if (X11Native.XResizeWindow(_display, state.NativeWindow, checked((uint)width), checked((uint)height)) == 0)
        {
            throw new InvalidOperationException("X11 rejected the client-size request.");
        }

        _ = X11Native.XSync(_display, discard: 0);
        return WindowCommandStatus.Applied;
    }

    public WindowCommandStatus SetWindowMode(WindowHandle window, WindowMode mode)
    {
        EnsureActiveOwnerThread();

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (!_windowsByHandle.TryGetValue(window, out var state) || state.NativeDestroyed)
        {
            return WindowCommandStatus.Disposed;
        }

        if (mode is WindowMode.ExclusiveFullscreen)
        {
            return WindowCommandStatus.Unsupported;
        }

        if (mode is WindowMode.BorderlessFullscreen && !_ewmhFullscreenAvailable)
        {
            return WindowCommandStatus.Unsupported;
        }

        var observedMode = !_ewmhFullscreenAvailable
            ? WindowMode.Windowed
            : IsWindowFullscreen(state.NativeWindow)
                ? WindowMode.BorderlessFullscreen
                : WindowMode.Windowed;

        if (observedMode == mode)
        {
            state.RequestedMode = mode;
            return WindowCommandStatus.Applied;
        }

        SendEwmhFullscreenRequest(state.NativeWindow, mode is WindowMode.BorderlessFullscreen);
        state.RequestedMode = mode;
        return WindowCommandStatus.Applied;
    }

    public WindowCommandStatus SetWindowTitle(WindowHandle window, string title)
    {
        EnsureActiveOwnerThread();

        if (!_windowsByHandle.TryGetValue(window, out var state) || state.NativeDestroyed)
        {
            return WindowCommandStatus.Disposed;
        }

        SetWindowTitle(state.NativeWindow, title);
        _ = X11Native.XSync(_display, discard: 0);
        return WindowCommandStatus.Applied;
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

    private void CaptureButton(
        WindowState state,
        uint button,
        float x,
        float y,
        nuint nativeTime,
        nint nativeDeviceIdentity,
        bool isDown)
    {
        var position = new Vector2(x, y);
        var contentScale = GetPointerContentScale();
        var timestamp = TimeProvider.System.GetTimestamp();

        switch (button)
        {
            case 4 when isDown:
            {
                _ = _inputSamples.TryEnqueue(new PlatformInputSample(
                    PhysicalInputEventKind.VerticalScroll,
                    state.Window,
                    NativeDeviceIdentity: nativeDeviceIdentity,
                    PlatformInputDeviceKind.Pointer,
                    PointerPosition: position,
                    PointerContentScale: contentScale,
                    ScrollValue: 1.0f,
                    ScrollUnit: PointerScrollUnit.Detents,
                    NativeTimestamp: checked((long)nativeTime),
                    NativeClockDomain: "X11ServerTime32Milliseconds",
                    MonotonicTimestamp: timestamp));
                return;
            }

            case 5 when isDown:
            {
                _ = _inputSamples.TryEnqueue(new PlatformInputSample(
                    PhysicalInputEventKind.VerticalScroll,
                    state.Window,
                    NativeDeviceIdentity: nativeDeviceIdentity,
                    PlatformInputDeviceKind.Pointer,
                    PointerPosition: position,
                    PointerContentScale: contentScale,
                    ScrollValue: -1.0f,
                    ScrollUnit: PointerScrollUnit.Detents,
                    NativeTimestamp: checked((long)nativeTime),
                    NativeClockDomain: "X11ServerTime32Milliseconds",
                    MonotonicTimestamp: timestamp));
                return;
            }

            case 6 when isDown:
            {
                _ = _inputSamples.TryEnqueue(new PlatformInputSample(
                    PhysicalInputEventKind.HorizontalScroll,
                    state.Window,
                    NativeDeviceIdentity: nativeDeviceIdentity,
                    PlatformInputDeviceKind.Pointer,
                    PointerPosition: position,
                    PointerContentScale: contentScale,
                    ScrollValue: -1.0f,
                    ScrollUnit: PointerScrollUnit.Detents,
                    NativeTimestamp: checked((long)nativeTime),
                    NativeClockDomain: "X11ServerTime32Milliseconds",
                    MonotonicTimestamp: timestamp));
                return;
            }

            case 7 when isDown:
            {
                _ = _inputSamples.TryEnqueue(new PlatformInputSample(
                    PhysicalInputEventKind.HorizontalScroll,
                    state.Window,
                    NativeDeviceIdentity: nativeDeviceIdentity,
                    PlatformInputDeviceKind.Pointer,
                    PointerPosition: position,
                    PointerContentScale: contentScale,
                    ScrollValue: 1.0f,
                    ScrollUnit: PointerScrollUnit.Detents,
                    NativeTimestamp: checked((long)nativeTime),
                    NativeClockDomain: "X11ServerTime32Milliseconds",
                    MonotonicTimestamp: timestamp));
                return;
            }
        }

        if (!TryMapMouseButton(button, out var mouseButton))
        {
            return;
        }

        _ = _inputSamples.TryEnqueue(new PlatformInputSample(
            isDown ? PhysicalInputEventKind.PointerButtonDown : PhysicalInputEventKind.PointerButtonUp,
            state.Window,
            NativeDeviceIdentity: nativeDeviceIdentity,
            PlatformInputDeviceKind.Pointer,
            MouseButton: mouseButton,
            IsDown: isDown,
            PointerPosition: position,
            PointerContentScale: contentScale,
            NativeTimestamp: checked((long)nativeTime),
            NativeClockDomain: "X11ServerTime32Milliseconds",
            MonotonicTimestamp: timestamp));
    }

    private void CaptureFocus(WindowState state, PhysicalInputEventKind kind)
    {
        state.IsFocused = kind == PhysicalInputEventKind.FocusGained;

        if (!state.IsFocused)
        {
            state.KeysDown.Clear();
        }

        _ = _inputSamples.TryEnqueue(new PlatformInputSample(
            kind,
            state.Window,
            NativeDeviceIdentity: 0,
            PlatformInputDeviceKind.Unknown,
            MonotonicTimestamp: TimeProvider.System.GetTimestamp()));

        if (state.IsFocused)
        {
            QueueInputStateReconciliation(state);
        }
    }

    private void CaptureKey(
        WindowState state,
        uint xKeycode,
        nuint nativeTime,
        nint nativeDeviceIdentity,
        bool isDown,
        bool isRepeat)
    {
        var (kind, nativeUsagePage, nativeUsage, evdevKeycode) = CaptureKeyState(
            state,
            nativeDeviceIdentity,
            xKeycode,
            isDown,
            isRepeat);
        _ = _inputSamples.TryEnqueue(new PlatformInputSample(
            kind,
            state.Window,
            NativeDeviceIdentity: nativeDeviceIdentity,
            PlatformInputDeviceKind.Keyboard,
            NativeUsagePage: nativeUsagePage,
            NativeUsage: nativeUsage,
            NativeScanCode: evdevKeycode,
            IsDown: isDown,
            NativeTimestamp: checked((long)nativeTime),
            NativeClockDomain: "X11ServerTime32Milliseconds",
            MonotonicTimestamp: TimeProvider.System.GetTimestamp()));
    }

    private (PhysicalInputEventKind Kind, ushort? NativeUsagePage, ushort? NativeUsage, uint NativeScanCode) CaptureKeyState(
        WindowState state,
        nint nativeDeviceIdentity,
        uint xKeycode,
        bool isDown,
        bool isRepeat)
    {
        var key = (nativeDeviceIdentity, xKeycode);
        var isKnownKeyDown = state.KeysDown.Contains(key);
        var kind = isDown
            ? ((isRepeat || isKnownKeyDown) ? PhysicalInputEventKind.KeyRepeat : PhysicalInputEventKind.KeyDown)
            : PhysicalInputEventKind.KeyUp;

        if (isDown)
        {
            _ = state.KeysDown.Add(key);
        }
        else
        {
            _ = state.KeysDown.Remove(key);
        }

        var mapped = TryMapKeyboardKeycode(xKeycode, out var nativeScanCode, out var hidUsage);
        return (
            kind,
            mapped ? (ushort)0x0007 : null,
            mapped ? hidUsage : null,
            nativeScanCode);
    }

    private void CaptureMotion(
        WindowState state,
        float x,
        float y,
        nuint nativeTime,
        nint nativeDeviceIdentity)
        => _ = _inputSamples.TryEnqueue(new PlatformInputSample(
            PhysicalInputEventKind.PointerAbsoluteMotion,
            state.Window,
            NativeDeviceIdentity: nativeDeviceIdentity,
            PlatformInputDeviceKind.Pointer,
            PointerPosition: new Vector2(x, y),
            PointerContentScale: GetPointerContentScale(),
            NativeTimestamp: checked((long)nativeTime),
            NativeClockDomain: "X11ServerTime32Milliseconds",
            MonotonicTimestamp: TimeProvider.System.GetTimestamp()));

    private void CaptureXInput2Event(X11Native.GenericEventCookie* cookie)
    {
        if (!_xinput2Available || (cookie->Extension != _xinput2Opcode))
        {
            return;
        }

        if (X11Native.XGetEventData(_display, cookie) == 0)
        {
            throw new InvalidOperationException("X11 failed to retrieve XInput2 event data.");
        }

        try
        {
            if (cookie->Data == 0)
            {
                throw new InvalidOperationException("XInput2 returned an event without event data.");
            }

            var inputEvent = (XInput2Native.XIDeviceEvent*)cookie->Data;

            if (!_windowsByNativeId.TryGetValue(inputEvent->Event, out var state))
            {
                return;
            }

            if (inputEvent->SourceId <= 0)
            {
                throw new InvalidOperationException("XInput2 returned a pointer or keyboard event without a source device.");
            }

            var nativeDeviceIdentity = checked((nint)inputEvent->SourceId);

            switch (cookie->Evtype)
            {
                case XInput2Native.KeyPress:
                {
                    CaptureKey(
                        state,
                        checked((uint)inputEvent->Detail),
                        inputEvent->Time,
                        nativeDeviceIdentity,
                        isDown: true,
                        isRepeat: (inputEvent->Flags & XInput2Native.KeyRepeat) != 0);
                    break;
                }

                case XInput2Native.KeyRelease:
                {
                    CaptureKey(
                        state,
                        checked((uint)inputEvent->Detail),
                        inputEvent->Time,
                        nativeDeviceIdentity,
                        isDown: false,
                        isRepeat: false);
                    break;
                }

                case XInput2Native.ButtonPress:
                {
                    CaptureButton(
                        state,
                        checked((uint)inputEvent->Detail),
                        checked((float)inputEvent->EventX),
                        checked((float)inputEvent->EventY),
                        inputEvent->Time,
                        nativeDeviceIdentity,
                        isDown: true);
                    break;
                }

                case XInput2Native.ButtonRelease:
                {
                    CaptureButton(
                        state,
                        checked((uint)inputEvent->Detail),
                        checked((float)inputEvent->EventX),
                        checked((float)inputEvent->EventY),
                        inputEvent->Time,
                        nativeDeviceIdentity,
                        isDown: false);
                    break;
                }

                case XInput2Native.Motion:
                {
                    CaptureMotion(
                        state,
                        checked((float)inputEvent->EventX),
                        checked((float)inputEvent->EventY),
                        inputEvent->Time,
                        nativeDeviceIdentity);
                    break;
                }
            }
        }
        finally
        {
            X11Native.XFreeEventData(_display, cookie);
        }
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

    private Vector2 GetPointerContentScale()
    {
        var scale = (_effectiveDpi is float dpi) ? dpi / WindowedDpi : 1.0f;
        return new Vector2(scale, scale);
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

    private bool IsAutoRepeatRelease(X11Native.KeyEvent keyEvent)
    {
        if (X11Native.XPending(_display) == 0)
        {
            return false;
        }

        var nextEvent = default(X11Native.NativeEvent);

        if (X11Native.XPeekEvent(_display, &nextEvent) == 0)
        {
            throw new InvalidOperationException("X11 failed while inspecting the next keyboard event.");
        }

        return (nextEvent.Type == KeyPressEvent)
            && (nextEvent.Key.Window == keyEvent.Window)
            && (nextEvent.Key.Keycode == keyEvent.Keycode)
            && (nextEvent.Key.Time == keyEvent.Time);
    }

    private bool IsWindowFullscreen(nuint window)
    {
        var stateAtoms = ReadAtomProperty(window, _netWmStateAtom);

        foreach (var atom in stateAtoms)
        {
            if (atom == _netWmStateFullscreenAtom)
            {
                return true;
            }
        }

        return false;
    }

    private void QueueCloseRequest(WindowState state)
    {
        if (!state.CloseRequested)
        {
            state.CloseRequested = true;
            _closeRequests.Add(state.Window);
        }
    }

    private void QueueInputStateReconciliation(WindowState state)
    {
        var reconciliationState = ImmutableArray.CreateBuilder<PlatformInputStateValue>();
        var keymap = stackalloc byte[32];

        if ((_keyboardKeymap is X11KeyboardKeymap selectedKeymap) && !selectedKeymap.Refresh())
        {
            throw new InvalidOperationException("XKB could not refresh the core keyboard keymap.");
        }

        if (X11Native.XQueryKeymap(_display, keymap) == 0)
        {
            throw new InvalidOperationException("X11 failed to query the current keyboard state.");
        }

        for (uint keycode = 0; keycode < 256; keycode++)
        {
            if ((keymap[keycode >> 3] & (1 << checked((int)(keycode & 7)))) == 0
                || !TryMapKeyboardKeycode(keycode, out _, out var usage))
            {
                continue;
            }

            reconciliationState.Add(new PlatformInputStateValue(
                PlatformInputStateValueKind.Digital,
                IsDown: true,
                DigitalControl: new DigitalControl(new KeyboardControl(0x0007, usage))));
        }

        nuint root = 0;
        nuint child = 0;
        var rootX = 0;
        var rootY = 0;
        var windowX = 0;
        var windowY = 0;
        uint pointerMask = 0;

        if (X11Native.XQueryPointer(
                _display,
                _rootWindow,
                &root,
                &child,
                &rootX,
                &rootY,
                &windowX,
                &windowY,
                &pointerMask) == 0)
        {
            throw new InvalidOperationException("X11 failed to query the current pointer state.");
        }

        for (uint button = 1; button <= 5; button++)
        {
            if (((pointerMask & (1u << checked((int)(button + 7)))) != 0)
                && TryMapMouseButton(button, out var mouseButton))
            {
                reconciliationState.Add(new PlatformInputStateValue(
                    PlatformInputStateValueKind.Digital,
                    IsDown: true,
                    DigitalControl: new DigitalControl(mouseButton)));
            }
        }

        _ = _inputSamples.TryEnqueue(new PlatformInputSample(
            PhysicalInputEventKind.StateReconciliation,
            state.Window,
            NativeDeviceIdentity: 0,
            PlatformInputDeviceKind.Unknown,
            MonotonicTimestamp: TimeProvider.System.GetTimestamp(),
            ReconciliationState: reconciliationState.ToImmutable()));
    }

    private ImmutableArray<nuint> ReadAtomProperty(nuint window, nuint property)
        => ReadAtomProperty(_display, window, _atomAtom, property);

    private void RefreshDpiSettings()
    {
        EnsureActiveOwnerThread();
        _effectiveDpi = ReadXftDpiSettings(_display, _rootWindow, _resourceManagerAtom);
    }

    private void SendEwmhFullscreenRequest(nuint window, bool fullscreen)
    {
        var nativeEvent = default(X11Native.NativeEvent);
        nativeEvent.ClientMessage = new X11Native.ClientMessageEvent {
            Type = ClientMessageEvent,
            Display = _display,
            Window = window,
            MessageType = _netWmStateAtom,
            Format = 32,
            Data = new X11Native.ClientMessageData {
                Long0 = fullscreen ? EwmhAdd : EwmhRemove,
                Long1 = checked((nint)_netWmStateFullscreenAtom),
                Long2 = 0,
                Long3 = EwmhSourceApplication,
                Long4 = 0,
            },
        };

        if (X11Native.XSendEvent(
                _display,
                _rootWindow,
                propagate: 0,
                SubstructureNotifyMask | SubstructureRedirectMask,
                &nativeEvent) == 0)
        {
            throw new InvalidOperationException("X11 could not deliver the EWMH fullscreen request.");
        }

        _ = X11Native.XFlush(_display);
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

    private bool TryMapKeyboardKeycode(uint xKeycode, out uint nativeScanCode, out ushort hidUsage)
    {
        if (_keyboardKeymap is X11KeyboardKeymap keymap)
        {
            nativeScanCode = xKeycode;
            return keymap.TryGetUsage(xKeycode, out hidUsage);
        }

        return X11KeyboardKeyMapper.TryMap(xKeycode, out nativeScanCode, out hidUsage);
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

        [LibraryImport("libX11.so.6", EntryPoint = "XFreeEventData")]
        internal static partial void XFreeEventData(nint display, GenericEventCookie* cookie);

        [LibraryImport("libX11.so.6", EntryPoint = "XGetAtomName")]
        internal static partial nint XGetAtomName(nint display, nuint atom);

        [LibraryImport("libX11.so.6", EntryPoint = "XGetEventData")]
        internal static partial int XGetEventData(nint display, GenericEventCookie* cookie);

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

        [LibraryImport("libX11.so.6", EntryPoint = "XPeekEvent")]
        internal static partial int XPeekEvent(nint display, NativeEvent* nativeEvent);

        [LibraryImport("libX11.so.6", EntryPoint = "XPending")]
        internal static partial int XPending(nint display);

        [LibraryImport("libX11.so.6", EntryPoint = "XQueryExtension", StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int XQueryExtension(
            nint display,
            string name,
            int* majorOpcodeReturn,
            int* firstEventReturn,
            int* firstErrorReturn);

        [LibraryImport("libX11.so.6", EntryPoint = "XQueryKeymap")]
        internal static partial int XQueryKeymap(nint display, byte* keysReturn);

        [LibraryImport("libX11.so.6", EntryPoint = "XQueryPointer")]
        internal static partial int XQueryPointer(
            nint display,
            nuint window,
            nuint* rootReturn,
            nuint* childReturn,
            int* rootXReturn,
            int* rootYReturn,
            int* windowXReturn,
            int* windowYReturn,
            uint* maskReturn);

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

        [LibraryImport("libX11.so.6", EntryPoint = "XRefreshKeyboardMapping")]
        internal static partial int XRefreshKeyboardMapping(MappingEvent* mappingEvent);

        [LibraryImport("libX11.so.6", EntryPoint = "XResizeWindow")]
        internal static partial int XResizeWindow(nint display, nuint window, uint width, uint height);

        [LibraryImport("libX11.so.6", EntryPoint = "XRootWindow")]
        internal static partial nuint XRootWindow(nint display, int screen);

        [LibraryImport("libX11.so.6", EntryPoint = "XSelectInput")]
        internal static partial int XSelectInput(nint display, nuint window, nint eventMask);

        [LibraryImport("libX11.so.6", EntryPoint = "XSendEvent")]
        internal static partial int XSendEvent(
            nint display,
            nuint window,
            int propagate,
            long eventMask,
            NativeEvent* eventSend);

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

        [StructLayout(LayoutKind.Sequential)]
        internal struct ButtonEvent
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
        internal struct ClientMessageData
        {
            [FieldOffset(0)]
            internal nint Long0;

            [FieldOffset(8)]
            internal nint Long1;

            [FieldOffset(16)]
            internal nint Long2;

            [FieldOffset(24)]
            internal nint Long3;

            [FieldOffset(32)]
            internal nint Long4;
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

        [StructLayout(LayoutKind.Sequential)]
        internal struct FocusChangeEvent
        {
            internal int Type;
            internal nuint Serial;
            internal int SendEvent;
            internal nint Display;
            internal nuint Window;
            internal int Mode;
            internal int Detail;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct GenericEventCookie
        {
            internal int Type;
            internal nuint Serial;
            internal int SendEvent;
            internal nint Display;
            internal int Extension;
            internal int Evtype;
            internal uint Cookie;
            internal nint Data;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct KeyEvent
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
        internal struct MappingEvent
        {
            internal int Type;
            internal nuint Serial;
            internal int SendEvent;
            internal nint Display;
            internal nuint Window;
            internal int Request;
            internal int FirstKeyCode;
            internal int Count;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MotionEvent
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
        internal struct NativeEvent
        {
            [FieldOffset(0)]
            internal int Type;

            [FieldOffset(0)]
            internal KeyEvent Key;

            [FieldOffset(0)]
            internal MappingEvent Mapping;

            [FieldOffset(0)]
            internal ButtonEvent Button;

            [FieldOffset(0)]
            internal MotionEvent Motion;

            [FieldOffset(0)]
            internal FocusChangeEvent Focus;

            [FieldOffset(0)]
            internal GenericEventCookie Cookie;

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

    private static unsafe partial class XInput2Native
    {
        internal const int AllMasterDevices = 1;
        internal const int ButtonPress = 4;
        internal const int ButtonRelease = 5;
        internal const int KeyPress = 2;
        internal const int KeyRelease = 3;
        internal const int KeyRepeat = 1 << 16;
        internal const int Motion = 6;

        [LibraryImport("libXi.so.6", EntryPoint = "XIQueryVersion")]
        internal static partial int XIQueryVersion(nint display, int* majorVersionInOut, int* minorVersionInOut);

        [LibraryImport("libXi.so.6", EntryPoint = "XISelectEvents")]
        internal static partial int XISelectEvents(
            nint display,
            nuint window,
            XIEventMask* eventMasks,
            int numMasks);

        [StructLayout(LayoutKind.Sequential)]
        internal struct XIDeviceEvent
        {
            internal int Type;
            internal nuint Serial;
            internal int SendEvent;
            internal nint Display;
            internal int Extension;
            internal int Evtype;
            internal nuint Time;
            internal int DeviceId;
            internal int SourceId;
            internal int Detail;
            internal nuint Root;
            internal nuint Event;
            internal nuint Child;
            internal double RootX;
            internal double RootY;
            internal double EventX;
            internal double EventY;
            internal int Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct XIEventMask
        {
            internal int DeviceId;
            internal int MaskLength;
            internal byte* Mask;
        }
    }

    private sealed class WindowState
    {
        internal bool CloseRequested;
        internal bool IsFocused;
        internal readonly HashSet<(nint Device, uint Keycode)> KeysDown = [];
        internal Vector2 MinimumClientSize;
        internal bool NativeDestroyed;
        internal WindowMode RequestedMode;
        internal bool Resizable;

        internal WindowState(WindowHandle window, nuint nativeWindow, WindowOptions options)
        {
            Window = window;
            NativeWindow = nativeWindow;
            MinimumClientSize = options.MinimumClientSize;
            RequestedMode = options.Mode;
            Resizable = options.Resizable;
        }

        internal nuint NativeWindow { get; }

        internal WindowHandle Window { get; }
    }
}
