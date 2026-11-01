// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using TerraFX;
using TerraFX.Numerics;

namespace TerraFX.Platform;

internal sealed unsafe partial class WindowsPlatformBackend
{
    private const int CreateWindowDefaultPosition = int.MinValue;
    private const uint MonitorDefaultToPrimary = 0x00000001;
    private const uint WindowClassHorizontalRedraw = 0x0002;
    private const uint WindowClassVerticalRedraw = 0x0001;
    private const uint WindowMessageClose = 0x0010;
    private const uint WindowMessageDisplayChange = 0x007E;
    private const uint WindowMessageDpiChanged = 0x02E0;
    private const uint WindowMessageGetMinMaxInfo = 0x0024;
    private const uint WindowMessageNcDestroy = 0x0082;
    private const uint WindowPositionNoActivate = 0x0010;
    private const uint WindowPositionNoZOrder = 0x0004;
    private const uint WindowResizableStyleMask = 0x00050000;
    private const int WindowShowNormal = 1;
    private const string WindowStatePropertyName = "TerraFX.Platform.WindowState";
    private const uint WindowStyleOverlapped = 0x00CF0000;
    private const uint WindowStylePopup = 0x80000000;
    private readonly string _windowClassName;
    private bool _windowClassRegistered;
    private nint _windowInstance;
    private readonly WindowState?[] _windows;

    [LibraryImport("user32.dll", EntryPoint = "AdjustWindowRectExForDpi", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustWindowRectExForDpiNative(
        NativeRect* rect,
        uint style,
        [MarshalAs(UnmanagedType.Bool)] bool hasMenu,
        uint extendedStyle,
        uint dpi);

    private static void ConfigureWindowBounds(WindowState state, nint monitor)
    {
        var dpi = GetDpiForWindowNative(state.NativeWindow);

        if (dpi == 0)
        {
            throw new InvalidOperationException("Windows did not report DPI for the created window.");
        }

        if (state.Mode == WindowMode.BorderlessFullscreen)
        {
            if (monitor == 0)
            {
                monitor = MonitorFromPointNative(default, MonitorDefaultToPrimary);
            }

            if (!TryGetNativeMonitorInfo(monitor, out var monitorInfo))
            {
                throw new InvalidOperationException("Windows could not resolve the monitor for borderless fullscreen.");
            }

            SetWindowBounds(
                state.NativeWindow,
                monitorInfo.Monitor.Left,
                monitorInfo.Monitor.Top,
                monitorInfo.Monitor.Right - monitorInfo.Monitor.Left,
                monitorInfo.Monitor.Bottom - monitorInfo.Monitor.Top);
            return;
        }

        var rect = default(NativeRect);

        if (!GetWindowRectNative(state.NativeWindow, &rect))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Windows could not read the created window extent.");
        }

        var x = rect.Left;
        var y = rect.Top;
        var width = GetScaledClientExtent(state.ClientWidth, dpi);
        var height = GetScaledClientExtent(state.ClientHeight, dpi);
        var frame = new NativeRect {
            Right = width,
            Bottom = height,
        };

        if (!AdjustWindowRectExForDpiNative(&frame, state.Style, hasMenu: false, state.ExtendedStyle, dpi))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Windows could not calculate the requested client extent.");
        }

        width = frame.Right - frame.Left;
        height = frame.Bottom - frame.Top;

        if (monitor != 0 && TryGetNativeMonitorInfo(monitor, out var requestedMonitor))
        {
            x = requestedMonitor.Work.Left + ((requestedMonitor.Work.Right - requestedMonitor.Work.Left - width) / 2);
            y = requestedMonitor.Work.Top + ((requestedMonitor.Work.Bottom - requestedMonitor.Work.Top - height) / 2);
        }

        SetWindowBounds(state.NativeWindow, x, y, width, height);
    }

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint CreateWindowExNative(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static partial nint DefWindowProcNative(nint window, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "DestroyWindow", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindowNative(nint window);

    [LibraryImport("user32.dll", EntryPoint = "GetDpiForWindow", SetLastError = true)]
    private static partial uint GetDpiForWindowNative(nint window);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint GetModuleHandleNative(string? moduleName);

    [LibraryImport("user32.dll", EntryPoint = "GetPropW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetPropertyNative(nint window, string name);

    private static int GetScaledClientExtent(float logicalExtent, uint dpi)
    {
        var scaledExtent = (double)logicalExtent * dpi / 96.0;

        if (!double.IsFinite(scaledExtent) || (scaledExtent > int.MaxValue))
        {
            throw new ArgumentOutOfRangeException(nameof(logicalExtent));
        }

        return Math.Max(1, checked((int)Math.Round(scaledExtent, MidpointRounding.ToEven)));
    }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowRect", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRectNative(nint window, NativeRect* rect);

    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW")]
    private static partial nint LoadCursorNative(nint instance, nint cursorName);

    [LibraryImport("user32.dll", EntryPoint = "MonitorFromPoint")]
    private static partial nint MonitorFromPointNative(NativePoint point, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    private static partial ushort RegisterClassExNative(NativeWindowClassEx* windowClass);

    [LibraryImport("user32.dll", EntryPoint = "RemovePropW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint RemovePropertyNative(nint window, string name);

    [LibraryImport("user32.dll", EntryPoint = "SetPropW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetPropertyNative(nint window, string name, nint data);

    private static void SetWindowBounds(nint window, int x, int y, int width, int height)
    {
        if (!SetWindowPositionNative(
            window,
            insertAfter: 0,
            x,
            y,
            width,
            height,
            WindowPositionNoActivate | WindowPositionNoZOrder))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Windows could not apply the requested window bounds.");
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "SetWindowPos", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPositionNative(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [LibraryImport("user32.dll", EntryPoint = "ShowWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindowNative(nint window, int command);

    private static bool TryGetNativeMonitorInfo(nint monitor, out MonitorInfoEx monitorInfo)
    {
        if (monitor == 0)
        {
            monitorInfo = default;
            return false;
        }

        var candidate = default(MonitorInfoEx);
        candidate.Size = checked((uint)sizeof(MonitorInfoEx));
        var result = GetMonitorInfoNative(monitor, &candidate);
        monitorInfo = candidate;
        return result;
    }

    [LibraryImport("user32.dll", EntryPoint = "UnregisterClassW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterClassNative(string className, nint instance);

    [LibraryImport("user32.dll", EntryPoint = "UpdateWindow", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateWindowNative(nint window);

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "The unmanaged window callback records faults and returns to Win32 before the owner reports them.")]
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam)
    {
        WindowState? state = null;

        try
        {
            var property = GetPropertyNative(window, WindowStatePropertyName);

            if ((property != 0) && (GCHandle.FromIntPtr(property).Target is WindowState windowState))
            {
                state = windowState;
            }

            if (state is null)
            {
                return DefWindowProcNative(window, message, wParam, lParam);
            }

            switch (message)
            {
                case WindowMessageClose:
                {
                    _ = Interlocked.Exchange(ref state.CloseRequested, 1);
                    return 0;
                }

                case WindowMessageDisplayChange:
                {
                    state.Owner.MarkMonitorTopologyChanged();
                    break;
                }

                case WindowMessageDpiChanged:
                {
                    state.Owner.QueueDpiBounds(state, lParam);
                    return 0;
                }

                case WindowMessageGetMinMaxInfo:
                {
                    state.Owner.ApplyMinimumTrackSize(state, window, lParam);
                    return 0;
                }

                case WindowMessageNcDestroy:
                {
                    state.NativeDestroyed = true;
                    _ = RemovePropertyNative(window, WindowStatePropertyName);
                    state.PropertyAttached = false;
                    break;
                }
            }

            return DefWindowProcNative(window, message, wParam, lParam);
        }
        catch (Exception exception)
        {
            state?.CaptureCallbackFailure(exception);
            return 0;
        }
    }

    public void CloseWindow(WindowHandle window)
    {
        EnsureOwnerThread();
        var state = FindWindowState(window)
            ?? throw new InvalidOperationException("The window handle is stale or no longer active.");
        state.IsClosing = true;

        if (!DestroyWindowNative(state.NativeWindow))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "The Windows Platform owner could not destroy a window.");
        }

        ForgetWindowState(state);
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Partially created HWNDs must be destroyed or retained by the backend owner before the creation fault escapes.")]
    public void CreateWindow(WindowHandle window, WindowOptions options, nint nativeMonitorHandle)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(options);

        if (!window.IsValid || (window.OwnerId != _ownerId))
        {
            throw new ArgumentException("The window handle belongs to another platform owner.", nameof(window));
        }

        if (FindWindowState(window) is not null)
        {
            throw new InvalidOperationException("The Windows Platform owner already contains this window handle.");
        }

        if (!Enum.IsDefined(options.Mode))
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        if (options.Mode == WindowMode.ExclusiveFullscreen)
        {
            throw new NotSupportedException("Exclusive fullscreen is not implemented by the Windows Platform backend.");
        }

        if ((options.Monitor is not null) != (nativeMonitorHandle != 0))
        {
            throw new ArgumentException("The native monitor binding does not match the requested monitor option.", nameof(nativeMonitorHandle));
        }

        if ((nativeMonitorHandle != 0) && !TryGetNativeMonitorInfo(nativeMonitorHandle, out _))
        {
            throw new InvalidOperationException("The requested monitor is no longer available.");
        }

        EnsureWindowClassRegistered();
        var slot = FindFreeWindowSlot();

        if (slot < 0)
        {
            throw new InvalidOperationException($"A Windows Platform owner supports at most {PlatformHost.MaximumWindowCount} windows.");
        }

        var style = options.Mode == WindowMode.BorderlessFullscreen
            ? WindowStylePopup
            : options.Resizable
                ? WindowStyleOverlapped
                : WindowStyleOverlapped & ~WindowResizableStyleMask;
        var state = new WindowState(this, window, options, style, extendedStyle: 0, slot);
        state.Root = GCHandle.Alloc(state);
        _windows[slot] = state;

        try
        {
            CreateNativeWindow(state, nativeMonitorHandle);
        }
        catch (Exception exception)
        {
            if ((state.NativeWindow != 0) && !DestroyWindowNative(state.NativeWindow))
            {
                throw new AggregateException(
                    exception,
                    new Win32Exception(Marshal.GetLastPInvokeError(), "The partially created Windows window could not be retired."));
            }

            ForgetWindowState(state);
            throw;
        }
    }

    public ImmutableArray<WindowHandle> DrainWindowCloseRequests()
    {
        EnsureOwnerThread();
        var closeRequests = ImmutableArray.CreateBuilder<WindowHandle>();

        foreach (var state in _windows)
        {
            if ((state is not null) && (Interlocked.Exchange(ref state.CloseRequested, 0) != 0))
            {
                closeRequests.Add(state.Window);
            }
        }

        return closeRequests.ToImmutable();
    }

    public bool TryGetNativeWindowBinding(WindowHandle window, out PlatformNativeWindowBinding binding)
    {
        EnsureOwnerThread();
        var state = FindWindowState(window);

        if ((state is null) || state.IsClosing || state.NativeDestroyed || (state.NativeWindow == 0))
        {
            binding = default;
            return false;
        }

        binding = new PlatformNativeWindowBinding(window, PlatformNativeSurfaceKind.Win32, _windowInstance, state.NativeWindow);
        return true;
    }

    private void ApplyDeferredWindowUpdates()
    {
        foreach (var state in _windows)
        {
            if ((state is not null) && (Interlocked.Exchange(ref state.PendingDpiBoundsAvailable, 0) != 0))
            {
                var bounds = state.PendingDpiBounds;
                SetWindowBounds(
                    state.NativeWindow,
                    bounds.Left,
                    bounds.Top,
                    bounds.Right - bounds.Left,
                    bounds.Bottom - bounds.Top);
            }
        }
    }

    private void ApplyMinimumTrackSize(WindowState state, nint window, nint minimumMaximumInfo)
    {
        if (!state.Resizable || (state.Mode != WindowMode.Windowed) || (minimumMaximumInfo == 0))
        {
            return;
        }

        var dpi = GetDpiForWindowNative(window);

        if (dpi == 0)
        {
            throw new InvalidOperationException("Windows did not report DPI for the live window.");
        }

        var rect = new NativeRect {
            Right = GetScaledClientExtent(state.MinimumClientWidth, dpi),
            Bottom = GetScaledClientExtent(state.MinimumClientHeight, dpi),
        };

        if (!AdjustWindowRectExForDpiNative(&rect, state.Style, hasMenu: false, state.ExtendedStyle, dpi))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Windows could not calculate the minimum window tracking size.");
        }

        var minimum = (NativeMinMaxInfo*)minimumMaximumInfo;
        minimum->MinimumTrackSize.X = Math.Max(minimum->MinimumTrackSize.X, rect.Right - rect.Left);
        minimum->MinimumTrackSize.Y = Math.Max(minimum->MinimumTrackSize.Y, rect.Bottom - rect.Top);
    }

    private void CreateNativeWindow(WindowState state, nint monitor)
    {
        var initialDpi = 96u;
        var initialRect = new NativeRect {
            Right = GetScaledClientExtent(state.ClientWidth, initialDpi),
            Bottom = GetScaledClientExtent(state.ClientHeight, initialDpi),
        };

        if (!AdjustWindowRectExForDpiNative(&initialRect, state.Style, hasMenu: false, state.ExtendedStyle, initialDpi))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Windows could not calculate the initial window extent.");
        }

        var x = CreateWindowDefaultPosition;
        var y = CreateWindowDefaultPosition;

        if ((monitor != 0) && (state.Mode == WindowMode.Windowed))
        {
            if (!TryGetNativeMonitorInfo(monitor, out var monitorInfo))
            {
                throw new InvalidOperationException("The requested monitor is no longer available.");
            }

            var width = initialRect.Right - initialRect.Left;
            var height = initialRect.Bottom - initialRect.Top;
            x = monitorInfo.Work.Left + ((monitorInfo.Work.Right - monitorInfo.Work.Left - width) / 2);
            y = monitorInfo.Work.Top + ((monitorInfo.Work.Bottom - monitorInfo.Work.Top - height) / 2);
        }
        else if (state.Mode == WindowMode.BorderlessFullscreen)
        {
            if (monitor == 0)
            {
                monitor = MonitorFromPointNative(default, MonitorDefaultToPrimary);
            }

            if (!TryGetNativeMonitorInfo(monitor, out var monitorInfo))
            {
                throw new InvalidOperationException("Windows could not resolve the monitor for borderless fullscreen.");
            }

            x = monitorInfo.Monitor.Left;
            y = monitorInfo.Monitor.Top;
            initialRect.Left = monitorInfo.Monitor.Left;
            initialRect.Top = monitorInfo.Monitor.Top;
            initialRect.Right = monitorInfo.Monitor.Right;
            initialRect.Bottom = monitorInfo.Monitor.Bottom;
        }

        var window = CreateWindowExNative(
            extendedStyle: 0,
            _windowClassName,
            state.Title,
            state.Style,
            x,
            y,
            initialRect.Right - initialRect.Left,
            initialRect.Bottom - initialRect.Top,
            parent: 0,
            menu: 0,
            _windowInstance,
            parameter: 0);

        if (window == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Windows could not create the native window.");
        }

        state.NativeWindow = window;

        if (!SetPropertyNative(window, WindowStatePropertyName, GCHandle.ToIntPtr(state.Root)))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Windows could not bind the native window to its owner state.");
        }

        state.PropertyAttached = true;
        ConfigureWindowBounds(state, monitor);

        if (state.Visible)
        {
            _ = ShowWindowNative(window, WindowShowNormal);
            _ = UpdateWindowNative(window);
        }
    }

    private void DisposeWindows()
    {
        foreach (var state in _windows)
        {
            if (state is not null)
            {
                CloseWindow(state.Window);
            }
        }

        if (_windowClassRegistered)
        {
            if (!UnregisterClassNative(_windowClassName, _windowInstance))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Windows could not unregister the owner window class.");
            }

            _windowClassRegistered = false;
        }
    }

    private void EnsureWindowClassRegistered()
    {
        if (_windowClassRegistered)
        {
            return;
        }

        _windowInstance = GetModuleHandleNative(null);

        if (_windowInstance == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Windows could not resolve the host module handle.");
        }

        var windowClass = new NativeWindowClassEx {
            Size = checked((uint)sizeof(NativeWindowClassEx)),
            Style = WindowClassHorizontalRedraw | WindowClassVerticalRedraw,
            WindowProcedure = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint>)&WindowProcedure,
            Instance = _windowInstance,
            Cursor = LoadCursorNative(0, new nint(32512)),
        };

        fixed (char* className = _windowClassName)
        {
            windowClass.ClassName = (nint)className;

            if (RegisterClassExNative(&windowClass) == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Windows could not register the owner window class.");
            }
        }

        _windowClassRegistered = true;
    }

    private int FindFreeWindowSlot()
    {
        for (var index = 0; index < _windows.Length; index++)
        {
            if (_windows[index] is null)
            {
                return index;
            }
        }

        return -1;
    }

    private WindowState? FindWindowState(WindowHandle window)
    {
        if (!window.IsValid || (window.OwnerId != _ownerId))
        {
            return null;
        }

        foreach (var state in _windows)
        {
            if ((state is not null) && (state.Window == window))
            {
                return state;
            }
        }

        return null;
    }

    private void ForgetWindowState(WindowState state)
    {
        if (state.PropertyAttached && (state.NativeWindow != 0))
        {
            _ = RemovePropertyNative(state.NativeWindow, WindowStatePropertyName);
            state.PropertyAttached = false;
        }

        _windows[state.Slot] = null;
        state.ReleaseRoot();
    }

    private void MarkMonitorTopologyChanged()
        => _ = Interlocked.Exchange(ref _monitorTopologyChanged, 1);

    private void QueueDpiBounds(WindowState state, nint suggestedRect)
    {
        if (suggestedRect == 0)
        {
            return;
        }

        state.PendingDpiBounds = *(NativeRect*)suggestedRect;
        _ = Interlocked.Exchange(ref state.PendingDpiBoundsAvailable, 1);
    }

    private void ThrowCallbackFailures()
    {
        foreach (var state in _windows)
        {
            if (state?.TakeCallbackFailure() is Exception callbackFailure)
            {
                ExceptionDispatchInfo.Capture(callbackFailure).Throw();
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMinMaxInfo
    {
        internal NativePoint Reserved;
        internal NativePoint MaximumSize;
        internal NativePoint MaximumPosition;
        internal NativePoint MinimumTrackSize;
        internal NativePoint MaximumTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeWindowClassEx
    {
        internal uint Size;
        internal uint Style;
        internal nint WindowProcedure;
        internal int ClassExtraBytes;
        internal int WindowExtraBytes;
        internal nint Instance;
        internal nint Icon;
        internal nint Cursor;
        internal nint Background;
        internal nint MenuName;
        internal nint ClassName;
        internal nint SmallIcon;
    }

    private sealed class WindowState
    {
        internal float ClientHeight;
        internal float ClientWidth;
        internal int CloseRequested;
        internal uint ExtendedStyle;
        internal bool IsClosing;
        internal float MinimumClientHeight;
        internal float MinimumClientWidth;
        internal WindowMode Mode;
        internal bool NativeDestroyed;
        internal nint NativeWindow;
        internal WindowsPlatformBackend Owner;
        internal NativeRect PendingDpiBounds;
        internal int PendingDpiBoundsAvailable;
        internal bool PropertyAttached;
        internal bool Resizable;
        internal GCHandle Root;
        internal int Slot;
        internal uint Style;
        internal string Title;
        internal bool Visible;
        internal WindowHandle Window;
        private Exception? _callbackFailure;

        internal WindowState(
            WindowsPlatformBackend owner,
            WindowHandle window,
            WindowOptions options,
            uint style,
            uint extendedStyle,
            int slot)
        {
            Owner = owner;
            Window = window;
            ClientHeight = options.ClientSize.Y;
            ClientWidth = options.ClientSize.X;
            ExtendedStyle = extendedStyle;
            MinimumClientHeight = options.MinimumClientSize.Y;
            MinimumClientWidth = options.MinimumClientSize.X;
            Mode = options.Mode;
            Resizable = options.Resizable;
            Slot = slot;
            Style = style;
            Title = options.Title;
            Visible = options.Visible;
        }

        internal void CaptureCallbackFailure(Exception exception)
            => _ = Interlocked.CompareExchange(ref _callbackFailure, exception, null);

        internal void ReleaseRoot()
        {
            if (Root.IsAllocated)
            {
                Root.Free();
            }
        }

        internal Exception? TakeCallbackFailure()
            => Interlocked.Exchange(ref _callbackFailure, null);
    }
}
