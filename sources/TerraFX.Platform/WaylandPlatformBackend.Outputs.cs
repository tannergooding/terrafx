// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TerraFX.Platform;

internal sealed unsafe partial class WaylandPlatformBackend
{
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Exceptions must not cross the Wayland unmanaged callback boundary.")]
    private static void OnOutputDone(nint data, nint output)
    {
        if (GCHandle.FromIntPtr(data).Target is not WaylandPlatformBackend backend)
        {
            return;
        }

        try
        {
            backend.HandleOutputDone(output);
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
    private static void OnOutputGeometry(
        nint data,
        nint output,
        int x,
        int y,
        int physicalWidth,
        int physicalHeight,
        int subpixel,
        nint make,
        nint model,
        int transform)
    {
        if (GCHandle.FromIntPtr(data).Target is not WaylandPlatformBackend backend)
        {
            return;
        }

        try
        {
            backend.HandleOutputGeometry(output, x, y, physicalWidth, physicalHeight, make, model);
        }
        catch (Exception exception)
        {
            backend._callbackFailure ??= exception;
        }

        _ = subpixel;
        _ = transform;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Exceptions must not cross the Wayland unmanaged callback boundary.")]
    private static void OnOutputMode(nint data, nint output, uint flags, int width, int height, int refreshRate)
    {
        if (GCHandle.FromIntPtr(data).Target is not WaylandPlatformBackend backend)
        {
            return;
        }

        try
        {
            backend.HandleOutputMode(output, flags, width, height, refreshRate);
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
    private static void OnOutputScale(nint data, nint output, int scale)
    {
        if (GCHandle.FromIntPtr(data).Target is not WaylandPlatformBackend backend)
        {
            return;
        }

        try
        {
            backend.HandleOutputScale(output, scale);
        }
        catch (Exception exception)
        {
            backend._callbackFailure ??= exception;
        }
    }

    public ImmutableArray<MonitorHandle> GetMonitorHandles()
    {
        EnsureActiveOwnerThread();
        return _monitorHandles;
    }

    public ImmutableArray<PlatformMonitorInfo> GetMonitorInfo()
    {
        EnsureActiveOwnerThread();
        var monitors = ImmutableArray.CreateBuilder<PlatformMonitorInfo>(_outputsByGlobalName.Count);

        foreach (var output in _outputsByGlobalName.Values)
        {
            monitors.Add(new PlatformMonitorInfo(output.DeviceName, output.NativeProxy));
        }

        return monitors.MoveToImmutable();
    }

    public void Pump()
    {
        EnsureActiveOwnerThread();
        Connection.Pump();
        RefreshOutputBindings();
        RefreshClipboardManager();
        RefreshSeatBinding();
        _gamepadCapture?.Pump();
        ThrowIfCallbackFailed();
        Connection.Pump();
        ExpireClipboardSelectionIfUnfocused();
        PumpClipboardTransfers();
        ThrowIfCallbackFailed();
        PumpKeyboardRepeats();
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

    private void BindOutput(WaylandGlobal global)
    {
        var version = Math.Min(global.Version, MaximumWlOutputVersion);
        var nativeOutput = Connection.BindGlobal(global, ProtocolMetadata.WlOutputInterface, version);
        var output = new WaylandOutputState(global, nativeOutput, version);
        _outputsByGlobalName.Add(global.Name, output);
        _outputsByProxy.Add(nativeOutput, output);

        if (Connection.AddListener(nativeOutput, _outputListener, GCHandle.ToIntPtr(_callbackHandle)) < 0)
        {
            RemoveOutput(output);
            throw new IOException("The Wayland wl_output listener could not be installed.");
        }

        _monitorTopologyChanged = true;
    }

    private void HandleOutputDone(nint nativeOutput)
    {
        if (!_outputsByProxy.ContainsKey(nativeOutput))
        {
            return;
        }

        _monitorTopologyChanged = true;
    }

    private void HandleOutputGeometry(
        nint nativeOutput,
        int x,
        int y,
        int physicalWidth,
        int physicalHeight,
        nint make,
        nint model)
    {
        if (!_outputsByProxy.TryGetValue(nativeOutput, out var output))
        {
            return;
        }

        output.X = x;
        output.Y = y;
        output.PhysicalWidth = physicalWidth;
        output.PhysicalHeight = physicalHeight;
        output.Make = Marshal.PtrToStringUTF8(make)
            ?? throw new InvalidOperationException("The Wayland compositor returned an output without a make string.");
        output.Model = Marshal.PtrToStringUTF8(model)
            ?? throw new InvalidOperationException("The Wayland compositor returned an output without a model string.");
        _monitorTopologyChanged = true;
    }

    private void HandleOutputMode(nint nativeOutput, uint flags, int width, int height, int refreshRate)
    {
        if (!_outputsByProxy.TryGetValue(nativeOutput, out var output))
        {
            return;
        }

        if ((width <= 0) || (height <= 0) || (refreshRate < 0))
        {
            throw new InvalidOperationException("The Wayland compositor returned an invalid output mode.");
        }

        if ((flags & 1u) != 0)
        {
            output.HasCurrentMode = true;
            output.CurrentWidth = width;
            output.CurrentHeight = height;
            output.CurrentRefreshRate = refreshRate;
        }

        _monitorTopologyChanged = true;
    }

    private void HandleOutputScale(nint nativeOutput, int scale)
    {
        if (scale < 1)
        {
            throw new InvalidOperationException("The Wayland compositor advertised an invalid integer output scale.");
        }

        if (_outputsByProxy.TryGetValue(nativeOutput, out var output) && (output.Scale != scale))
        {
            output.Scale = scale;
        }
    }

    private void InitializeOutputListeners()
    {
        _outputListener = AllocateListenerTable(
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, int, int, int, int, int, nint, nint, int, void>)&OnOutputGeometry,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, uint, int, int, int, void>)&OnOutputMode,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnOutputDone,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, int, void>)&OnOutputScale);
    }

    private void RefreshOutputBindings()
    {
        var currentGlobals = Connection.GetGlobals("wl_output");
        var currentGlobalNames = new HashSet<uint>();

        foreach (var global in currentGlobals)
        {
            _ = currentGlobalNames.Add(global.Name);

            if (!_outputsByGlobalName.ContainsKey(global.Name))
            {
                BindOutput(global);
            }
        }

        var removedOutputs = new List<WaylandOutputState>();

        foreach (var output in _outputsByGlobalName.Values)
        {
            if (!currentGlobalNames.Contains(output.Global.Name))
            {
                removedOutputs.Add(output);
            }
        }

        foreach (var output in removedOutputs)
        {
            RemoveOutput(output);
        }
    }

    private void RemoveOutput(WaylandOutputState output)
    {
        _ = _outputsByGlobalName.Remove(output.Global.Name);
        _ = _outputsByProxy.Remove(output.NativeProxy);

        foreach (var window in _windowsByHandle.Values)
        {
            _ = window.EnteredOutputs.Remove(output.NativeProxy);

            if (window.NativeOutput == output.NativeProxy)
            {
                window.NativeOutput = 0;
            }
        }

        if (output.Version >= 3)
        {
            Connection.SendRequest(output.NativeProxy, opcode: 0);
        }

        Connection.DestroyProxy(output.NativeProxy);
        _monitorTopologyChanged = true;
    }
}
