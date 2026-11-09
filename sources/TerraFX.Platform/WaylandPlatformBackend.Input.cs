// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using ClipboardTextResult = TerraFX.Platform.ClipboardStatus;
using TerraFX;
using TerraFX.Input;
using TerraFX.Numerics;

namespace TerraFX.Platform;

internal sealed unsafe partial class WaylandPlatformBackend
{
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Exceptions must not cross the Wayland unmanaged callback boundary.")]
    private static void OnKeyboardEnter(nint data, nint keyboard, uint serial, nint surface, nint keys)
    {
        if (GCHandle.FromIntPtr(data).Target is not WaylandPlatformBackend backend)
        {
            return;
        }

        try
        {
            backend.HandleKeyboardEnter(keyboard, surface, keys);
        }
        catch (Exception exception)
        {
            backend._callbackFailure ??= exception;
        }

        _ = serial;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Exceptions must not cross the Wayland unmanaged callback boundary.")]
    private static void OnKeyboardKey(nint data, nint keyboard, uint serial, uint time, uint key, uint state)
    {
        if (GCHandle.FromIntPtr(data).Target is not WaylandPlatformBackend backend)
        {
            return;
        }

        try
        {
            backend.HandleKeyboardKey(keyboard, time, key, state);
        }
        catch (Exception exception)
        {
            backend._callbackFailure ??= exception;
        }

        _ = serial;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Exceptions must not cross the Wayland unmanaged callback boundary.")]
    private static void OnKeyboardKeymap(nint data, nint keyboard, uint format, int fileDescriptor, uint size)
    {
        if (GCHandle.FromIntPtr(data).Target is not WaylandPlatformBackend backend)
        {
            return;
        }

        try
        {
            backend.HandleKeyboardKeymap(keyboard, format, fileDescriptor, size);
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
    private static void OnKeyboardLeave(nint data, nint keyboard, uint serial, nint surface)
    {
        if (GCHandle.FromIntPtr(data).Target is not WaylandPlatformBackend backend)
        {
            return;
        }

        try
        {
            backend.HandleKeyboardLeave(keyboard, surface);
        }
        catch (Exception exception)
        {
            backend._callbackFailure ??= exception;
        }

        _ = serial;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Exceptions must not cross the Wayland unmanaged callback boundary.")]
    private static void OnKeyboardModifiers(
        nint data,
        nint keyboard,
        uint serial,
        uint depressed,
        uint latched,
        uint locked,
        uint group)
    {
        _ = data;
        _ = keyboard;
        _ = serial;
        _ = depressed;
        _ = latched;
        _ = locked;
        _ = group;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Exceptions must not cross the Wayland unmanaged callback boundary.")]
    private static void OnKeyboardRepeatInfo(nint data, nint keyboard, int rate, int delay)
    {
        if (GCHandle.FromIntPtr(data).Target is not WaylandPlatformBackend backend)
        {
            return;
        }

        try
        {
            backend.HandleKeyboardRepeatInfo(keyboard, rate, delay);
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
    private static void OnPointerAxis(nint data, nint pointer, uint time, uint axis, int value)
    {
        if (GCHandle.FromIntPtr(data).Target is not WaylandPlatformBackend backend)
        {
            return;
        }

        try
        {
            backend.HandlePointerAxis(pointer, time, axis, value);
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
    private static void OnPointerButton(
        nint data,
        nint pointer,
        uint serial,
        uint time,
        uint button,
        uint state)
    {
        if (GCHandle.FromIntPtr(data).Target is not WaylandPlatformBackend backend)
        {
            return;
        }

        try
        {
            backend.HandlePointerButton(pointer, time, button, state);
        }
        catch (Exception exception)
        {
            backend._callbackFailure ??= exception;
        }

        _ = serial;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Exceptions must not cross the Wayland unmanaged callback boundary.")]
    private static void OnPointerEnter(nint data, nint pointer, uint serial, nint surface, int x, int y)
    {
        if (GCHandle.FromIntPtr(data).Target is not WaylandPlatformBackend backend)
        {
            return;
        }

        try
        {
            backend.HandlePointerEnter(pointer, surface, x, y);
        }
        catch (Exception exception)
        {
            backend._callbackFailure ??= exception;
        }

        _ = serial;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Exceptions must not cross the Wayland unmanaged callback boundary.")]
    private static void OnPointerLeave(nint data, nint pointer, uint serial, nint surface)
    {
        if (GCHandle.FromIntPtr(data).Target is not WaylandPlatformBackend backend)
        {
            return;
        }

        try
        {
            backend.HandlePointerLeave(pointer, surface);
        }
        catch (Exception exception)
        {
            backend._callbackFailure ??= exception;
        }

        _ = serial;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Exceptions must not cross the Wayland unmanaged callback boundary.")]
    private static void OnPointerMotion(nint data, nint pointer, uint time, int x, int y)
    {
        if (GCHandle.FromIntPtr(data).Target is not WaylandPlatformBackend backend)
        {
            return;
        }

        try
        {
            backend.HandlePointerMotion(pointer, time, x, y);
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
    private static void OnSeatCapabilities(nint data, nint seat, uint capabilities)
    {
        if (GCHandle.FromIntPtr(data).Target is not WaylandPlatformBackend backend)
        {
            return;
        }

        try
        {
            backend.HandleSeatCapabilities(seat, capabilities);
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
    private static void OnSeatName(nint data, nint seat, nint name)
    {
        _ = data;
        _ = seat;
        _ = name;
    }

    private static bool TryMapMouseButton(uint button, out MouseButtonValue mouseButton)
    {
        mouseButton = button switch {
            0x110 => MouseButtonValue.Primary,
            0x111 => MouseButtonValue.Secondary,
            0x112 => MouseButtonValue.Middle,
            0x113 => MouseButtonValue.Auxiliary1,
            0x114 => MouseButtonValue.Auxiliary2,
            0x115 => MouseButtonValue.Auxiliary3,
            0x116 => MouseButtonValue.Auxiliary4,
            0x117 => MouseButtonValue.Auxiliary5,
            _ => (MouseButtonValue)int.MaxValue,
        };
        return Enum.IsDefined(mouseButton);
    }

    public PlatformInputSampleBatch DrainInputSamples()
    {
        EnsureActiveOwnerThread();
        return _inputSamples.Drain();
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
                "The Wayland backend does not provide clipboard transfers yet."));
    }

    public void RequestGamepadStateReconciliation(nint nativeDeviceIdentity)
    {
        EnsureActiveOwnerThread();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero((long)nativeDeviceIdentity);

        if (_gamepadCapture is not LinuxEvdevGamepadCapture capture)
        {
            throw new NotSupportedException("The Wayland backend does not provide Linux evdev gamepad capture.");
        }

        capture.RequestDeviceReconciliation(nativeDeviceIdentity);
    }

    public void RequestInputStateReconciliation(WindowHandle? window)
    {
        EnsureActiveOwnerThread();

        if (window is { } windowHandle)
        {
            QueueWindowStateReconciliation(GetWindowState(windowHandle));
        }
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
                "The Wayland backend does not provide clipboard transfers yet."));
    }

    private void BindKeyboard()
    {
        var version = Math.Min(checked((uint)_seatVersion), 4u);
        var arguments = stackalloc WaylandArgument[1];
        arguments[0] = default;
        var keyboard = Connection.CreateProxy(
            _seat,
            opcode: 1,
            ProtocolMetadata.WlKeyboardInterface,
            version,
            arguments);

        if (keyboard == 0)
        {
            throw new IOException("The Wayland seat could not create a wl_keyboard.");
        }

        if (Connection.AddListener(keyboard, _keyboardListener, GCHandle.ToIntPtr(_callbackHandle)) < 0)
        {
            Connection.DestroyProxy(keyboard);
            throw new IOException("The Wayland wl_keyboard listener could not be installed.");
        }

        _keyboard = keyboard;
    }

    private void BindPointer()
    {
        var version = Math.Min(checked((uint)_seatVersion), 4u);
        var arguments = stackalloc WaylandArgument[1];
        arguments[0] = default;
        var pointer = Connection.CreateProxy(
            _seat,
            opcode: 0,
            ProtocolMetadata.WlPointerInterface,
            version,
            arguments);

        if (pointer == 0)
        {
            throw new IOException("The Wayland seat could not create a wl_pointer.");
        }

        if (Connection.AddListener(pointer, _pointerListener, GCHandle.ToIntPtr(_callbackHandle)) < 0)
        {
            Connection.DestroyProxy(pointer);
            throw new IOException("The Wayland wl_pointer listener could not be installed.");
        }

        _pointer = pointer;
    }

    private void BindSeat(WaylandGlobal global)
    {
        var version = Math.Min(global.Version, 5u);
        var seat = Connection.BindGlobal(global, ProtocolMetadata.WlSeatInterface, version);

        if (Connection.AddListener(seat, _seatListener, GCHandle.ToIntPtr(_callbackHandle)) < 0)
        {
            Connection.DestroyProxy(seat);
            throw new IOException("The Wayland wl_seat listener could not be installed.");
        }

        _seat = seat;
        _seatGlobalName = global.Name;
        _seatVersion = checked((int)version);
    }

    private void HandleKeyboardEnter(nint keyboard, nint surface, nint keys)
    {
        if ((keyboard != _keyboard)
            || !_windowsBySurface.TryGetValue(surface, out var window))
        {
            return;
        }

        var nativeKeys = (NativeWaylandArray*)keys;

        if ((nativeKeys == null)
            || (nativeKeys->Size > 4096)
            || ((nativeKeys->Size % sizeof(uint)) != 0)
            || ((nativeKeys->Size != 0) && (nativeKeys->Data == 0)))
        {
            throw new InvalidOperationException("The Wayland compositor returned an invalid keyboard-enter key array.");
        }

        var previousWindow = _keyboardFocusedWindow;

        if ((previousWindow is not null) && (previousWindow != window))
        {
            QueueFocusEvent(previousWindow, PhysicalInputEventKind.FocusLost);
        }

        _keyboardFocusedWindow = window;
        _keyboardKeysDown.Clear();
        var keyCount = checked((int)(nativeKeys->Size / sizeof(uint)));
        var keyValues = new ReadOnlySpan<uint>((void*)nativeKeys->Data, keyCount);

        foreach (var key in keyValues)
        {
            _ = _keyboardKeysDown.Add(key);
        }

        if ((previousWindow is not null) && (previousWindow != window))
        {
            QueueWindowStateReconciliation(previousWindow);
        }

        RescheduleKeyboardRepeats(_timeProvider.GetTimestamp());
        QueueFocusEvent(window, PhysicalInputEventKind.FocusGained);
        QueueWindowStateReconciliation(window);
    }

    private void HandleKeyboardKey(nint keyboard, uint time, uint key, uint state)
    {
        if (keyboard != _keyboard)
        {
            return;
        }

        if (state > 1)
        {
            throw new InvalidOperationException("The Wayland compositor returned an invalid keyboard key state.");
        }

        if (_keyboardFocusedWindow is not WaylandWindowState window)
        {
            return;
        }

        var timestamp = _timeProvider.GetTimestamp();
        var isDown = state == 1;
        PhysicalInputEventKind kind;

        if (isDown)
        {
            var wasDown = !_keyboardKeysDown.Add(key);
            kind = wasDown ? PhysicalInputEventKind.KeyRepeat : PhysicalInputEventKind.KeyDown;

            if (!wasDown)
            {
                var repeatable = (_keyboardKeymap is WaylandKeyboardKeymap keymap)
                    && keymap.CanRepeat(key);
                _keyboardRepeatSchedule.StartKey(key, timestamp, repeatable);
            }
        }
        else
        {
            _ = _keyboardKeysDown.Remove(key);
            _keyboardRepeatSchedule.StopKey(key);
            kind = PhysicalInputEventKind.KeyUp;
        }

        QueueKeyboardSample(window, key, kind, isDown, timestamp, time);
    }

    private void HandleKeyboardKeymap(nint keyboard, uint format, int fileDescriptor, uint size)
    {
        if (keyboard != _keyboard)
        {
            using var keymapFile = new SafeFileHandle((nint)fileDescriptor, ownsHandle: true);
            return;
        }

        var keymap = WaylandKeyboardKeymap.TryCreate(format, fileDescriptor, size);
        _keyboardKeymap?.Dispose();
        _keyboardKeymap = keymap;
        RescheduleKeyboardRepeats(_timeProvider.GetTimestamp());

        if (_keyboardFocusedWindow is WaylandWindowState window)
        {
            QueueWindowStateReconciliation(window);
        }
    }

    private void HandleKeyboardLeave(nint keyboard, nint surface)
    {
        if ((keyboard == _keyboard)
            && (_keyboardFocusedWindow is WaylandWindowState window)
            && (window.Surface == surface))
        {
            _keyboardFocusedWindow = null;
            _keyboardKeysDown.Clear();
            _keyboardRepeatSchedule.Reset();
            QueueFocusEvent(window, PhysicalInputEventKind.FocusLost);
            QueueWindowStateReconciliation(window);
        }
    }

    private void HandleKeyboardRepeatInfo(nint keyboard, int rate, int delay)
    {
        if (keyboard != _keyboard)
        {
            return;
        }

        _keyboardRepeatSchedule.Configure(rate, delay);
        RescheduleKeyboardRepeats(_timeProvider.GetTimestamp());
    }

    private void HandlePointerAxis(nint pointer, uint time, uint axis, int value)
    {
        if ((pointer != _pointer)
            || (_pointerFocusedWindow is not WaylandWindowState window)
            || (axis > 1))
        {
            return;
        }

        var scrollValue = value / 256.0f;
        var scale = GetContentScale(window);

        if (scrollValue == 0.0f)
        {
            return;
        }

        _ = _inputSamples.TryEnqueue(new PlatformInputSample(
            (axis == 0) ? PhysicalInputEventKind.VerticalScroll : PhysicalInputEventKind.HorizontalScroll,
            window.Window,
            NativeDeviceIdentity: _pointer,
            PlatformInputDeviceKind.Pointer,
            PointerPosition: window.PointerPosition,
            PointerContentScale: new Vector2(scale, scale),
            ScrollValue: scrollValue,
            ScrollUnit: PointerScrollUnit.SurfaceScale,
            NativeTimestamp: checked((long)time),
            NativeClockDomain: "WaylandMonotonicMilliseconds",
            MonotonicTimestamp: _timeProvider.GetTimestamp()));
    }

    private void HandlePointerButton(nint pointer, uint time, uint button, uint state)
    {
        if (pointer != _pointer)
        {
            return;
        }

        if (state > 1)
        {
            throw new InvalidOperationException("The Wayland compositor returned an invalid pointer-button state.");
        }

        if ((_pointerFocusedWindow is not WaylandWindowState window)
            || !TryMapMouseButton(button, out var mouseButton))
        {
            return;
        }

        var isDown = state == 1;

        if (isDown)
        {
            _ = _pointerButtonsDown.Add(button);
        }
        else
        {
            _ = _pointerButtonsDown.Remove(button);
        }

        var scale = GetContentScale(window);
        _ = _inputSamples.TryEnqueue(new PlatformInputSample(
            isDown ? PhysicalInputEventKind.PointerButtonDown : PhysicalInputEventKind.PointerButtonUp,
            window.Window,
            NativeDeviceIdentity: _pointer,
            PlatformInputDeviceKind.Pointer,
            MouseButton: mouseButton,
            IsDown: isDown,
            PointerPosition: window.PointerPosition,
            PointerContentScale: new Vector2(scale, scale),
            NativeTimestamp: checked((long)time),
            NativeClockDomain: "WaylandMonotonicMilliseconds",
            MonotonicTimestamp: _timeProvider.GetTimestamp()));
    }

    private void HandlePointerEnter(nint pointer, nint surface, int x, int y)
    {
        if ((pointer != _pointer)
            || !_windowsBySurface.TryGetValue(surface, out var window))
        {
            return;
        }

        if ((_pointerFocusedWindow is { } previousWindow) && (previousWindow != window))
        {
            QueueWindowPointerEvent(previousWindow, PhysicalInputEventKind.WindowLeft);
            _pointerFocusedWindow = null;
            _pointerButtonsDown.Clear();
            QueueWindowStateReconciliation(previousWindow);
        }

        _pointerFocusedWindow = window;
        QueueWindowPointerEvent(window, PhysicalInputEventKind.WindowEntered);
        QueuePointerPosition(window, x, y, nativeTime: null);
        QueueWindowStateReconciliation(window);
    }

    private void HandlePointerLeave(nint pointer, nint surface)
    {
        if ((pointer == _pointer)
            && (_pointerFocusedWindow is WaylandWindowState window)
            && (window.Surface == surface))
        {
            _pointerFocusedWindow = null;
            _pointerButtonsDown.Clear();
            QueueWindowPointerEvent(window, PhysicalInputEventKind.WindowLeft);
            QueueWindowStateReconciliation(window);
        }
    }

    private void HandlePointerMotion(nint pointer, uint time, int x, int y)
    {
        if ((pointer == _pointer) && (_pointerFocusedWindow is WaylandWindowState window))
        {
            QueuePointerPosition(window, x, y, time);
        }
    }

    private void HandleSeatCapabilities(nint seat, uint capabilities)
    {
        if (seat != _seat)
        {
            return;
        }

        var failures = new List<Exception>();

        if ((capabilities & 2u) != 0)
        {
            if (_keyboard == 0)
            {
                BindKeyboard();
            }
        }
        else
        {
            RetireKeyboard(failures, notifyFocus: true);
        }

        if ((capabilities & 1u) != 0)
        {
            if (_pointer == 0)
            {
                BindPointer();
            }
        }
        else
        {
            RetirePointer(failures, notifyLeave: true);
        }

        if (failures.Count > 0)
        {
            throw new AggregateException("Wayland input-device retirement failed.", failures);
        }
    }

    private void InitializeInputListeners()
    {
        _seatListener = AllocateListenerTable(
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, uint, void>)&OnSeatCapabilities,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnSeatName);
        _keyboardListener = AllocateListenerTable(
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, uint, int, uint, void>)&OnKeyboardKeymap,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, uint, nint, nint, void>)&OnKeyboardEnter,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, uint, nint, void>)&OnKeyboardLeave,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, uint, uint, uint, uint, void>)&OnKeyboardKey,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, uint, uint, uint, uint, uint, void>)&OnKeyboardModifiers,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, int, int, void>)&OnKeyboardRepeatInfo);
        _pointerListener = AllocateListenerTable(
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, uint, nint, int, int, void>)&OnPointerEnter,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, uint, nint, void>)&OnPointerLeave,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, uint, int, int, void>)&OnPointerMotion,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, uint, uint, uint, uint, void>)&OnPointerButton,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, uint, uint, int, void>)&OnPointerAxis);
    }

    private void PumpKeyboardRepeats()
    {
        if (_keyboardFocusedWindow is not WaylandWindowState window)
        {
            return;
        }

        var timestamp = _timeProvider.GetTimestamp();

        foreach (var key in _keyboardRepeatSchedule.TakeDueKeys(timestamp, PlatformInputSampleQueue.DefaultCapacity))
        {
            QueueKeyboardSample(
                window,
                key,
                PhysicalInputEventKind.KeyRepeat,
                isDown: true,
                timestamp,
                nativeTime: null);
        }
    }

    private void QueueFocusEvent(WaylandWindowState window, PhysicalInputEventKind kind)
        => _ = _inputSamples.TryEnqueue(new PlatformInputSample(
            kind,
            window.Window,
            NativeDeviceIdentity: 0,
            PlatformInputDeviceKind.Unknown,
            MonotonicTimestamp: _timeProvider.GetTimestamp()));

    private void QueueKeyboardSample(
        WaylandWindowState window,
        uint key,
        PhysicalInputEventKind kind,
        bool isDown,
        long timestamp,
        uint? nativeTime)
    {
        ushort hidUsage = 0;
        var hasUsage = (_keyboardKeymap is WaylandKeyboardKeymap keymap)
            && keymap.TryGetUsage(key, out hidUsage);
        _ = _inputSamples.TryEnqueue(new PlatformInputSample(
            kind,
            window.Window,
            NativeDeviceIdentity: _keyboard,
            PlatformInputDeviceKind.Keyboard,
            NativeUsagePage: hasUsage ? (ushort)0x0007 : null,
            NativeUsage: hasUsage ? hidUsage : null,
            NativeScanCode: key,
            IsDown: isDown,
            NativeTimestamp: nativeTime.HasValue ? checked((long)nativeTime.Value) : null,
            NativeClockDomain: nativeTime.HasValue ? "WaylandMonotonicMilliseconds" : null,
            MonotonicTimestamp: timestamp));
    }

    private void QueuePointerPosition(WaylandWindowState window, int x, int y, uint? nativeTime)
    {
        var position = new Vector2(x / 256.0f, y / 256.0f);
        window.PointerPosition = position;
        var scale = GetContentScale(window);
        _ = _inputSamples.TryEnqueue(new PlatformInputSample(
            PhysicalInputEventKind.PointerAbsoluteMotion,
            window.Window,
            NativeDeviceIdentity: _pointer,
            PlatformInputDeviceKind.Pointer,
            PointerPosition: position,
            PointerContentScale: new Vector2(scale, scale),
            NativeTimestamp: nativeTime.HasValue ? checked((long)nativeTime.Value) : null,
            NativeClockDomain: nativeTime.HasValue ? "WaylandMonotonicMilliseconds" : null,
            MonotonicTimestamp: _timeProvider.GetTimestamp()));
    }

    private void QueueWindowPointerEvent(WaylandWindowState window, PhysicalInputEventKind kind)
        => _ = _inputSamples.TryEnqueue(new PlatformInputSample(
            kind,
            window.Window,
            NativeDeviceIdentity: 0,
            PlatformInputDeviceKind.Unknown,
            MonotonicTimestamp: _timeProvider.GetTimestamp()));

    private void QueueWindowStateReconciliation(WaylandWindowState window)
    {
        var keyboardFocus = _keyboardFocusedWindow;
        var keyboardKeymap = _keyboardKeymap;
        var isKeyboardFocused = keyboardFocus == window;
        var reconciliationState = ImmutableArray.CreateBuilder<PlatformInputStateValue>();

        if (isKeyboardFocused && (keyboardKeymap is not null))
        {
            foreach (var key in _keyboardKeysDown)
            {
                if (keyboardKeymap.TryGetUsage(key, out var usage))
                {
                    reconciliationState.Add(new PlatformInputStateValue(
                        PlatformInputStateValueKind.Digital,
                        IsDown: true,
                        DigitalControl: new DigitalControl(new KeyboardControl(0x0007, usage))));
                }
            }
        }

        if (_pointerFocusedWindow == window)
        {
            foreach (var button in _pointerButtonsDown)
            {
                if (TryMapMouseButton(button, out var mouseButton))
                {
                    reconciliationState.Add(new PlatformInputStateValue(
                        PlatformInputStateValueKind.Digital,
                        IsDown: true,
                        DigitalControl: new DigitalControl(mouseButton)));
                }
            }
        }

        _ = _inputSamples.TryEnqueue(new PlatformInputSample(
            PhysicalInputEventKind.StateReconciliation,
            window.Window,
            NativeDeviceIdentity: 0,
            PlatformInputDeviceKind.Unknown,
            MonotonicTimestamp: _timeProvider.GetTimestamp(),
            ReconciliationState: reconciliationState.ToImmutable()));
    }

    private void RefreshSeatBinding()
    {
        var globals = Connection.GetGlobals("wl_seat");
        var currentGlobal = globals.IsEmpty
            ? (WaylandGlobal?)null
            : globals[0];

        foreach (var global in globals)
        {
            if (global.Name == _seatGlobalName)
            {
                currentGlobal = global;
                break;
            }
        }

        if ((_seat != 0) && ((currentGlobal is null) || (currentGlobal.Value.Name != _seatGlobalName)))
        {
            var failures = new List<Exception>();
            RetireSeat(failures, notifyFocus: true);

            if (failures.Count > 0)
            {
                throw new AggregateException("The Wayland seat could not be retired.", failures);
            }
        }

        if ((_seat == 0) && (currentGlobal is WaylandGlobal globalToBind))
        {
            BindSeat(globalToBind);
        }
    }

    private void RescheduleKeyboardRepeats(long timestamp)
    {
        _keyboardRepeatSchedule.Reset();

        if (_keyboardKeymap is not WaylandKeyboardKeymap keymap)
        {
            return;
        }

        foreach (var key in _keyboardKeysDown)
        {
            _keyboardRepeatSchedule.StartKey(key, timestamp, keymap.CanRepeat(key));
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Keyboard proxy retirement attempts both protocol release and local destruction.")]
    private void RetireKeyboard(List<Exception> failures, bool notifyFocus)
    {
        var focusedWindow = _keyboardFocusedWindow;

        if (notifyFocus && (focusedWindow is WaylandWindowState window))
        {
            QueueFocusEvent(window, PhysicalInputEventKind.FocusLost);
        }

        _keyboardFocusedWindow = null;
        _keyboardKeysDown.Clear();
        _keyboardRepeatSchedule.Reset();
        _keyboardKeymap?.Dispose();
        _keyboardKeymap = null;

        if (focusedWindow is not null)
        {
            QueueWindowStateReconciliation(focusedWindow);
        }

        var keyboard = _keyboard;
        _keyboard = 0;

        if (keyboard == 0)
        {
            return;
        }

        if (Connection.GetProxyVersion(keyboard) >= 3)
        {
            try
            {
                Connection.SendRequest(keyboard, opcode: 0);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        try
        {
            Connection.DestroyProxy(keyboard);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Pointer proxy retirement attempts both protocol release and local destruction.")]
    private void RetirePointer(List<Exception> failures, bool notifyLeave)
    {
        var focusedWindow = _pointerFocusedWindow;

        if (notifyLeave && (focusedWindow is WaylandWindowState window))
        {
            QueueWindowPointerEvent(window, PhysicalInputEventKind.WindowLeft);
        }

        _pointerFocusedWindow = null;
        _pointerButtonsDown.Clear();

        if (focusedWindow is not null)
        {
            QueueWindowStateReconciliation(focusedWindow);
        }

        var pointer = _pointer;
        _pointer = 0;

        if (pointer == 0)
        {
            return;
        }

        if (Connection.GetProxyVersion(pointer) >= 3)
        {
            try
            {
                Connection.SendRequest(pointer, opcode: 0);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        try
        {
            Connection.DestroyProxy(pointer);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Seat proxy retirement attempts release and local destruction after child cleanup.")]
    private void RetireSeat(List<Exception> failures, bool notifyFocus)
    {
        RetireKeyboard(failures, notifyFocus);
        RetirePointer(failures, notifyLeave: notifyFocus);
        var seat = _seat;
        _seat = 0;
        _seatGlobalName = 0;
        _seatVersion = 0;

        if (seat == 0)
        {
            return;
        }

        if (Connection.GetProxyVersion(seat) >= 5)
        {
            try
            {
                Connection.SendRequest(seat, opcode: 3);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        try
        {
            Connection.DestroyProxy(seat);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }
}
