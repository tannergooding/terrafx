// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using TerraFX;
using TerraFX.Numerics;

namespace TerraFX.Platform;

internal sealed unsafe partial class WaylandPlatformBackend
{
    private static int GetPixelExtent(float logicalExtent, float scale, string parameterName)
    {
        var pixelExtent = Math.Round((double)logicalExtent * scale, MidpointRounding.ToEven);

        if (pixelExtent is < 1.0 or > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(parameterName, "The Wayland client extent is outside the native pixel range.");
        }

        return checked((int)pixelExtent);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Exceptions must not cross the Wayland unmanaged callback boundary.")]
    private static void OnSurfaceEnter(nint data, nint surface, nint output)
    {
        if (GCHandle.FromIntPtr(data).Target is not WaylandPlatformBackend backend)
        {
            return;
        }

        try
        {
            backend.HandleSurfaceEnter(surface, output);
        }
        catch (Exception exception)
        {
            backend._callbackFailure ??= exception;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Exceptions must not cross the Wayland unmanaged callback boundary.")]
    private static void OnSurfaceLeave(nint data, nint surface, nint output)
    {
        if (GCHandle.FromIntPtr(data).Target is not WaylandPlatformBackend backend)
        {
            return;
        }

        try
        {
            backend.HandleSurfaceLeave(surface, output);
        }
        catch (Exception exception)
        {
            backend._callbackFailure ??= exception;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Exceptions must not cross the Wayland unmanaged callback boundary.")]
    private static void OnToplevelClose(nint data, nint toplevel)
    {
        if (GCHandle.FromIntPtr(data).Target is not WaylandPlatformBackend backend)
        {
            return;
        }

        try
        {
            backend.HandleToplevelClose(toplevel);
        }
        catch (Exception exception)
        {
            backend._callbackFailure ??= exception;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Exceptions must not cross the Wayland unmanaged callback boundary.")]
    private static void OnToplevelConfigure(nint data, nint toplevel, int width, int height, nint states)
    {
        if (GCHandle.FromIntPtr(data).Target is not WaylandPlatformBackend backend)
        {
            return;
        }

        try
        {
            backend.HandleToplevelConfigure(toplevel, width, height, states);
        }
        catch (Exception exception)
        {
            backend._callbackFailure ??= exception;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Exceptions must not cross the Wayland unmanaged callback boundary.")]
    private static void OnXdgSurfaceConfigure(nint data, nint xdgSurface, uint serial)
    {
        if (GCHandle.FromIntPtr(data).Target is not WaylandPlatformBackend backend)
        {
            return;
        }

        try
        {
            backend.HandleXdgSurfaceConfigure(xdgSurface, serial);
        }
        catch (Exception exception)
        {
            backend._callbackFailure ??= exception;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Exceptions must not cross the Wayland unmanaged callback boundary.")]
    private static void OnXdgWmBasePing(nint data, nint xdgWmBase, uint serial)
    {
        if (GCHandle.FromIntPtr(data).Target is not WaylandPlatformBackend backend)
        {
            return;
        }

        try
        {
            backend.HandleXdgWmBasePing(xdgWmBase, serial);
        }
        catch (Exception exception)
        {
            backend._callbackFailure ??= exception;
        }
    }

    public void CloseWindow(WindowHandle window)
    {
        EnsureActiveOwnerThread();
        CancelClipboardReads(window, TerraFX.Platform.ClipboardStatus.Canceled);
        DestroyWindowState(GetWindowState(window), sendDestroyRequests: true);
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "A partially created Wayland toplevel must be fully retired if setup fails.")]
    public void CreateWindow(WindowHandle window, WindowOptions options, nint nativeMonitorHandle)
    {
        EnsureActiveOwnerThread();
        ArgumentNullException.ThrowIfNull(options);

        if (!window.IsValid || (window.OwnerId != _ownerId))
        {
            throw new ArgumentException("The window handle belongs to another Wayland owner.", nameof(window));
        }

        if (_windowsByHandle.ContainsKey(window))
        {
            throw new InvalidOperationException("The Wayland owner already contains this window handle.");
        }

        if (_windowsByHandle.Count >= MaximumWindowCount)
        {
            throw new InvalidOperationException($"A platform host supports at most {MaximumWindowCount} windows.");
        }

        if (options.Mode is WindowMode.ExclusiveFullscreen)
        {
            throw new NotSupportedException("The Wayland backend does not support exclusive fullscreen.");
        }

        if ((options.Monitor is not null) != (nativeMonitorHandle != 0))
        {
            throw new ArgumentException("The native monitor binding does not match the requested monitor option.", nameof(nativeMonitorHandle));
        }

        if ((nativeMonitorHandle != 0)
            && (options.Mode is not WindowMode.BorderlessFullscreen))
        {
            throw new NotSupportedException("Wayland can select a target output only for fullscreen toplevels.");
        }

        if ((nativeMonitorHandle != 0) && !_outputsByProxy.ContainsKey(nativeMonitorHandle))
        {
            throw new PlatformMonitorUnavailableException("The requested Wayland output is no longer available.");
        }

        nint surface = 0;
        nint xdgSurface = 0;
        nint toplevel = 0;
        WaylandWindowState? state = null;

        try
        {
            var surfaceArguments = stackalloc WaylandArgument[1];
            surfaceArguments[0] = default;
            surface = Connection.CreateProxy(
                _compositor,
                opcode: 0,
                ProtocolMetadata.WlSurfaceInterface,
                Connection.GetProxyVersion(_compositor),
                surfaceArguments);

            if (surface == 0)
            {
                throw new IOException("The Wayland compositor could not create a wl_surface.");
            }

            var xdgSurfaceArguments = stackalloc WaylandArgument[2];
            xdgSurfaceArguments[0] = default;
            xdgSurfaceArguments[1].Pointer = surface;
            xdgSurface = Connection.CreateProxy(
                _xdgWmBase,
                opcode: 2,
                ProtocolMetadata.XdgSurfaceInterface,
                Connection.GetProxyVersion(_xdgWmBase),
                xdgSurfaceArguments);

            if (xdgSurface == 0)
            {
                throw new IOException("The Wayland compositor could not create an xdg_surface.");
            }

            var toplevelArguments = stackalloc WaylandArgument[1];
            toplevelArguments[0] = default;
            toplevel = Connection.CreateProxy(
                xdgSurface,
                opcode: 1,
                ProtocolMetadata.XdgToplevelInterface,
                Connection.GetProxyVersion(xdgSurface),
                toplevelArguments);

            if (toplevel == 0)
            {
                throw new IOException("The Wayland compositor could not create an xdg_toplevel.");
            }

            state = new WaylandWindowState(
                window,
                options.ClientSize,
                options.Mode,
                nativeMonitorHandle,
                surface,
                xdgSurface,
                toplevel);
            _windowsByHandle.Add(window, state);
            _windowsBySurface.Add(surface, state);
            _windowsByXdgSurface.Add(xdgSurface, state);
            _windowsByToplevel.Add(toplevel, state);

            if (Connection.AddListener(surface, _surfaceListener, GCHandle.ToIntPtr(_callbackHandle)) < 0)
            {
                throw new IOException("The Wayland wl_surface listener could not be installed.");
            }

            if (Connection.AddListener(xdgSurface, _xdgSurfaceListener, GCHandle.ToIntPtr(_callbackHandle)) < 0)
            {
                throw new IOException("The Wayland xdg_surface listener could not be installed.");
            }

            if (Connection.AddListener(toplevel, _xdgToplevelListener, GCHandle.ToIntPtr(_callbackHandle)) < 0)
            {
                throw new IOException("The Wayland xdg_toplevel listener could not be installed.");
            }

            SetTitle(state, options.Title);
            SetApplicationId(state);

            var minimumSizeArguments = stackalloc WaylandArgument[2];
            minimumSizeArguments[0].Unsigned = checked((uint)GetPixelExtent(
                options.MinimumClientSize.X,
                1.0f,
                nameof(options.MinimumClientSize)));
            minimumSizeArguments[1].Unsigned = checked((uint)GetPixelExtent(
                options.MinimumClientSize.Y,
                1.0f,
                nameof(options.MinimumClientSize)));
            Connection.SendRequest(toplevel, opcode: 8, minimumSizeArguments);

            if (!options.Resizable)
            {
                var maximumSizeArguments = stackalloc WaylandArgument[2];
                maximumSizeArguments[0].Unsigned = checked((uint)GetPixelExtent(
                    options.ClientSize.X,
                    1.0f,
                    nameof(options.ClientSize)));
                maximumSizeArguments[1].Unsigned = checked((uint)GetPixelExtent(
                    options.ClientSize.Y,
                    1.0f,
                    nameof(options.ClientSize)));
                Connection.SendRequest(toplevel, opcode: 7, maximumSizeArguments);
            }

            if (options.Mode is WindowMode.BorderlessFullscreen)
            {
                SetFullscreen(state, requestedFullscreen: true);
            }

            Connection.SendRequest(surface, opcode: 6);
            Connection.Roundtrip();
            ThrowIfCallbackFailed();

            if (!state.IsConfigured)
            {
                throw new InvalidOperationException("The Wayland compositor did not configure the new xdg_surface.");
            }
        }
        catch (Exception exception)
        {
            try
            {
                if (state is not null)
                {
                    DestroyWindowState(state, sendDestroyRequests: true);
                }
                else
                {
                    DestroyUntrackedProxy(toplevel);
                    DestroyUntrackedProxy(xdgSurface);
                    DestroyUntrackedProxy(surface);
                }
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException("Wayland window creation and rollback both failed.", exception, cleanupException);
            }

            throw;
        }
    }

    public ImmutableArray<WindowHandle> DrainWindowCloseRequests()
    {
        EnsureActiveOwnerThread();
        var closeRequests = ImmutableArray.CreateRange(_closeRequests);
        _closeRequests.Clear();
        return closeRequests;
    }

    public PlatformNativeWindowMetrics GetNativeWindowMetrics(WindowHandle window)
    {
        EnsureActiveOwnerThread();
        var state = GetWindowState(window);
        var scale = GetContentScale(state);
        var reportedScale = state.EnteredOutputs.Count != 0;
        float? effectiveDpi = reportedScale ? (96.0f * scale) : null;
        var contentScale = new Vector2(scale, scale);
        var pixelSize = new PixelSize(
            GetPixelExtent(state.LogicalClientSize.X, scale, nameof(state.LogicalClientSize)),
            GetPixelExtent(state.LogicalClientSize.Y, scale, nameof(state.LogicalClientSize)));
        var logicalSize = new Vector2(
            pixelSize.Width / scale,
            pixelSize.Height / scale);

        return new PlatformNativeWindowMetrics(
            logicalSize,
            pixelSize,
            contentScale,
            effectiveDpi,
            reportedScale ? DpiSource.WaylandIntegerBufferScale : DpiSource.Unknown,
            reportedScale ? DpiConfidence.Reported : DpiConfidence.Assumed96Dpi);
    }

    public PlatformNativeWindowModeState GetNativeWindowModeState(WindowHandle window)
    {
        EnsureActiveOwnerThread();
        var state = GetWindowState(window);
        return new PlatformNativeWindowModeState(state.RequestedMode, state.ObservedMode);
    }

    public WindowCommandStatus SetWindowClientSize(WindowHandle window, Vector2 logicalClientSize)
    {
        EnsureActiveOwnerThread();
        _ = WindowOptions.ValidateClientSize(logicalClientSize, nameof(logicalClientSize));
        return _windowsByHandle.ContainsKey(window)
            ? WindowCommandStatus.Unsupported
            : WindowCommandStatus.Disposed;
    }

    public WindowCommandStatus SetWindowMode(WindowHandle window, WindowMode mode)
    {
        EnsureActiveOwnerThread();

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (!_windowsByHandle.TryGetValue(window, out var state))
        {
            return WindowCommandStatus.Disposed;
        }

        if (mode is WindowMode.ExclusiveFullscreen)
        {
            return WindowCommandStatus.Unsupported;
        }

        SetFullscreen(state, mode is WindowMode.BorderlessFullscreen);
        Connection.SendRequest(state.Surface, opcode: 6);
        state.RequestedMode = mode;
        return WindowCommandStatus.Applied;
    }

    public WindowCommandStatus SetWindowTitle(WindowHandle window, string title)
    {
        EnsureActiveOwnerThread();
        ArgumentNullException.ThrowIfNull(title);

        if (title.AsSpan().Contains('\0'))
        {
            throw new ArgumentException("A window title cannot contain an embedded null character.", nameof(title));
        }

        if (!_windowsByHandle.TryGetValue(window, out var state))
        {
            return WindowCommandStatus.Disposed;
        }

        SetTitle(state, title);
        return WindowCommandStatus.Applied;
    }

    public bool TryGetNativeWindowBinding(WindowHandle window, out PlatformNativeWindowBinding binding)
    {
        EnsureActiveOwnerThread();

        if (_windowsByHandle.TryGetValue(window, out var state))
        {
            binding = new PlatformNativeWindowBinding(
                window,
                PlatformNativeSurfaceKind.Wayland,
                Connection.Display,
                state.Surface);
            return true;
        }

        binding = default;
        return false;
    }

    private void DestroyUntrackedProxy(nint proxy)
    {
        if (proxy != 0)
        {
            Connection.DestroyProxy(proxy);
        }
    }

    private void DestroyWindowState(WaylandWindowState state, bool sendDestroyRequests)
    {
        RetireTextInputWindow(state, sendDestroyRequests);
        _ = _windowsByHandle.Remove(state.Window);
        _ = _windowsBySurface.Remove(state.Surface);
        _ = _windowsByXdgSurface.Remove(state.XdgSurface);
        _ = _windowsByToplevel.Remove(state.Toplevel);
        state.EnteredOutputs.Clear();
        var failures = new List<Exception>();

        RetireWindowProxy(state.Toplevel, sendDestroyRequests, failures);
        RetireWindowProxy(state.XdgSurface, sendDestroyRequests, failures);
        RetireWindowProxy(state.Surface, sendDestroyRequests, failures);

        if (failures.Count > 0)
        {
            throw new AggregateException("Wayland window retirement failed.", failures);
        }
    }

    private float GetContentScale(WaylandWindowState window)
    {
        var scale = 1;

        foreach (var outputProxy in window.EnteredOutputs)
        {
            if (_outputsByProxy.TryGetValue(outputProxy, out var output))
            {
                scale = Math.Max(scale, output.Scale);
            }
        }

        return scale;
    }

    private WaylandWindowState GetWindowState(WindowHandle window)
        => _windowsByHandle.TryGetValue(window, out var state)
            ? state
            : throw new InvalidOperationException("The Wayland window handle is stale or no longer active.");

    private void HandleSurfaceEnter(nint surface, nint output)
    {
        if (_windowsBySurface.TryGetValue(surface, out var window)
            && _outputsByProxy.ContainsKey(output))
        {
            _ = window.EnteredOutputs.Add(output);
        }
    }

    private void HandleSurfaceLeave(nint surface, nint output)
    {
        if (_windowsBySurface.TryGetValue(surface, out var window))
        {
            _ = window.EnteredOutputs.Remove(output);
        }
    }

    private void HandleToplevelClose(nint toplevel)
    {
        if (_windowsByToplevel.TryGetValue(toplevel, out var window)
            && !_closeRequests.Contains(window.Window))
        {
            _closeRequests.Add(window.Window);
        }
    }

    private void HandleToplevelConfigure(nint toplevel, int width, int height, nint statesPointer)
    {
        if (!_windowsByToplevel.TryGetValue(toplevel, out var window))
        {
            return;
        }

        if ((width < 0) || (height < 0))
        {
            throw new InvalidOperationException("The Wayland compositor configured a negative toplevel size.");
        }

        var fullscreen = false;

        if (statesPointer != 0)
        {
            var states = (NativeWaylandArray*)statesPointer;

            if ((states->Size > 4096) || ((states->Size % sizeof(uint)) != 0) || ((states->Size != 0) && (states->Data == 0)))
            {
                throw new InvalidOperationException("The Wayland compositor configured an invalid xdg_toplevel state array.");
            }

            var stateCount = checked((int)(states->Size / sizeof(uint)));
            var stateValues = new ReadOnlySpan<uint>((void*)states->Data, stateCount);

            foreach (var state in stateValues)
            {
                if (state is XdgToplevelStateFullscreen)
                {
                    fullscreen = true;
                }
            }
        }

        window.PendingConfiguredWidth = width;
        window.PendingConfiguredHeight = height;
        window.PendingFullscreen = fullscreen;
    }

    private void HandleXdgSurfaceConfigure(nint xdgSurface, uint serial)
    {
        if (!_windowsByXdgSurface.TryGetValue(xdgSurface, out var window))
        {
            return;
        }

        var arguments = stackalloc WaylandArgument[1];
        arguments[0].Unsigned = serial;
        Connection.SendRequest(xdgSurface, opcode: 4, arguments);

        var clientWidth = (window.PendingConfiguredWidth > 0)
            ? window.PendingConfiguredWidth
            : window.LogicalClientSize.X;
        var clientHeight = (window.PendingConfiguredHeight > 0)
            ? window.PendingConfiguredHeight
            : window.LogicalClientSize.Y;
        window.LogicalClientSize = new Vector2(clientWidth, clientHeight);
        window.IsFullscreen = window.PendingFullscreen;
        window.ObservedMode = window.IsFullscreen ? WindowMode.BorderlessFullscreen : WindowMode.Windowed;
        window.IsConfigured = true;
    }

    private void HandleXdgWmBasePing(nint xdgWmBase, uint serial)
    {
        if (xdgWmBase != _xdgWmBase)
        {
            return;
        }

        var arguments = stackalloc WaylandArgument[1];
        arguments[0].Unsigned = serial;
        Connection.SendRequest(xdgWmBase, opcode: 3, arguments);
    }

    private void InitializeWindowListeners()
    {
        _surfaceListener = AllocateListenerTable(
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnSurfaceEnter,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnSurfaceLeave);
        _xdgSurfaceListener = AllocateListenerTable(
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, uint, void>)&OnXdgSurfaceConfigure);
        _xdgToplevelListener = AllocateListenerTable(
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, int, int, nint, void>)&OnToplevelConfigure,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnToplevelClose);
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Retirement must attempt local proxy destruction after a protocol destroy request fails.")]
    private void RetireWindowProxy(nint proxy, bool sendDestroyRequest, List<Exception> failures)
    {
        if (proxy == 0)
        {
            return;
        }

        if (sendDestroyRequest)
        {
            try
            {
                Connection.SendRequest(proxy, opcode: 0);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        try
        {
            Connection.DestroyProxy(proxy);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private void SetApplicationId(WaylandWindowState state)
    {
        var applicationId = Marshal.StringToCoTaskMemUTF8("TerraFX");

        try
        {
            var arguments = stackalloc WaylandArgument[1];
            arguments[0].Pointer = applicationId;
            Connection.SendRequest(state.Toplevel, opcode: 3, arguments);
        }
        finally
        {
            Marshal.FreeCoTaskMem(applicationId);
        }
    }

    private void SetFullscreen(WaylandWindowState state, bool requestedFullscreen)
    {
        if (requestedFullscreen)
        {
            var arguments = stackalloc WaylandArgument[1];
            arguments[0].Pointer = state.NativeOutput;
            Connection.SendRequest(state.Toplevel, opcode: 11, arguments);
        }
        else
        {
            Connection.SendRequest(state.Toplevel, opcode: 12);
        }
    }

    private void SetTitle(WaylandWindowState state, string title)
    {
        var titleUtf8 = Marshal.StringToCoTaskMemUTF8(title);

        try
        {
            var arguments = stackalloc WaylandArgument[1];
            arguments[0].Pointer = titleUtf8;
            Connection.SendRequest(state.Toplevel, opcode: 2, arguments);
        }
        finally
        {
            Marshal.FreeCoTaskMem(titleUtf8);
        }
    }
}
