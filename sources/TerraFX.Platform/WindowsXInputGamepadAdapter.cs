// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using TerraFX.Input;
using TerraFX.Numerics;

namespace TerraFX.Platform;

[SupportedOSPlatform("windows10.0.22000")]
internal sealed partial class WindowsXInputGamepadAdapter
{
    private const uint DeviceNotConnected = 1167;
    private const int GamepadCount = 4;
    private static readonly ButtonMapping[] s_buttonMappings = [
        new(0x1000, GamepadButtonValue.South),
        new(0x2000, GamepadButtonValue.East),
        new(0x4000, GamepadButtonValue.West),
        new(0x8000, GamepadButtonValue.North),
        new(0x0001, GamepadButtonValue.DpadUp),
        new(0x0002, GamepadButtonValue.DpadDown),
        new(0x0004, GamepadButtonValue.DpadLeft),
        new(0x0008, GamepadButtonValue.DpadRight),
        new(0x0020, GamepadButtonValue.Menu),
        new(0x0010, GamepadButtonValue.Start),
        new(0x0100, GamepadButtonValue.LeftBumper),
        new(0x0200, GamepadButtonValue.RightBumper),
    ];

    private readonly Action<PlatformInputSample> _capture;
    private readonly XInputGetState _getState;
    private readonly int _ownerThreadId;
    private readonly SlotState[] _slots = new SlotState[GamepadCount];
    private readonly TimeProvider _timeProvider;

    internal WindowsXInputGamepadAdapter(
        Action<PlatformInputSample> capture,
        TimeProvider timeProvider,
        XInputGetState? getState = null)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _capture = capture;
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _timeProvider = timeProvider;
        _getState = getState ?? QueryNativeState;
    }

    private static nint GetNativeDeviceIdentity(int slot)
        => checked((nint)(int.MinValue + slot));

    [LibraryImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static partial uint GetStateNative(uint userIndex, out NativeState state);

    private static float NormalizeAxis(short value)
        => (value < 0) ? (value / 32768.0f) : (value / 32767.0f);

    private static float NormalizeTrigger(byte value)
        => value / 255.0f;

    private static uint QueryNativeState(uint userIndex, out NativeState state)
        => GetStateNative(userIndex, out state);

    internal void Pump()
    {
        EnsureOwnerThread();
        var timestamp = _timeProvider.GetTimestamp();

        for (var slot = 0; slot < GamepadCount; slot++)
        {
            var result = _getState(checked((uint)slot), out var state);

            if (result == 0)
            {
                ProcessConnectedState(slot, state, timestamp);
            }
            else if (result == DeviceNotConnected)
            {
                ProcessDisconnectedState(slot, timestamp);
            }
            else
            {
                throw new Win32Exception(checked((int)result), "XInputGetState failed while polling a gamepad.");
            }
        }
    }

    private void CaptureAxis(int slot, GamepadAxis axis, short previous, short current, long timestamp)
    {
        if (previous != current)
        {
            _capture(new PlatformInputSample(
                PhysicalInputEventKind.GamepadAxis,
                Window: null,
                GetNativeDeviceIdentity(slot),
                PlatformInputDeviceKind.Gamepad,
                Axis: axis,
                AxisValue: NormalizeAxis(current),
                MonotonicTimestamp: timestamp));
        }
    }

    private void CaptureAxisChanges(int slot, in GamepadSnapshot previous, in GamepadSnapshot current, long timestamp)
    {
        CaptureAxis(slot, GamepadAxis.LeftX, previous.LeftThumbX, current.LeftThumbX, timestamp);
        CaptureAxis(slot, GamepadAxis.LeftY, previous.LeftThumbY, current.LeftThumbY, timestamp);
        CaptureAxis(slot, GamepadAxis.RightX, previous.RightThumbX, current.RightThumbX, timestamp);
        CaptureAxis(slot, GamepadAxis.RightY, previous.RightThumbY, current.RightThumbY, timestamp);
    }

    private void CaptureButtonChanges(int slot, ushort previous, ushort current, long timestamp)
    {
        var nativeDevice = GetNativeDeviceIdentity(slot);

        foreach (var mapping in s_buttonMappings)
        {
            var wasDown = (previous & mapping.Mask) != 0;
            var isDown = (current & mapping.Mask) != 0;

            if (wasDown != isDown)
            {
                _capture(new PlatformInputSample(
                    isDown ? PhysicalInputEventKind.GamepadButtonDown : PhysicalInputEventKind.GamepadButtonUp,
                    Window: null,
                    nativeDevice,
                    PlatformInputDeviceKind.Gamepad,
                    GamepadButton: mapping.Button,
                    IsDown: isDown,
                    MonotonicTimestamp: timestamp));
            }
        }
    }

    private void CaptureStick(
        int slot,
        GamepadStick stick,
        short previousX,
        short previousY,
        short currentX,
        short currentY,
        long timestamp)
    {
        if ((previousX != currentX) || (previousY != currentY))
        {
            _capture(new PlatformInputSample(
                PhysicalInputEventKind.GamepadStick,
                Window: null,
                GetNativeDeviceIdentity(slot),
                PlatformInputDeviceKind.Gamepad,
                Stick: stick,
                StickValue: new Vector2(NormalizeAxis(currentX), NormalizeAxis(currentY)),
                MonotonicTimestamp: timestamp));
        }
    }

    private void CaptureStickChanges(int slot, in GamepadSnapshot previous, in GamepadSnapshot current, long timestamp)
    {
        CaptureStick(
            slot,
            GamepadStick.Left,
            previous.LeftThumbX,
            previous.LeftThumbY,
            current.LeftThumbX,
            current.LeftThumbY,
            timestamp);
        CaptureStick(
            slot,
            GamepadStick.Right,
            previous.RightThumbX,
            previous.RightThumbY,
            current.RightThumbX,
            current.RightThumbY,
            timestamp);
    }

    private void CaptureTrigger(int slot, GamepadTrigger trigger, byte previous, byte current, long timestamp)
    {
        if (previous != current)
        {
            _capture(new PlatformInputSample(
                PhysicalInputEventKind.GamepadTrigger,
                Window: null,
                GetNativeDeviceIdentity(slot),
                PlatformInputDeviceKind.Gamepad,
                Trigger: trigger,
                TriggerValue: NormalizeTrigger(current),
                MonotonicTimestamp: timestamp));
        }
    }

    private void CaptureTriggerChanges(int slot, in GamepadSnapshot previous, in GamepadSnapshot current, long timestamp)
    {
        CaptureTrigger(slot, GamepadTrigger.Left, previous.LeftTrigger, current.LeftTrigger, timestamp);
        CaptureTrigger(slot, GamepadTrigger.Right, previous.RightTrigger, current.RightTrigger, timestamp);
    }

    private void EnsureOwnerThread()
    {
        if (_ownerThreadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("The Windows XInput adapter is restricted to its platform-owner thread.");
        }
    }

    private void ProcessConnectedState(int slot, in NativeState nativeState, long timestamp)
    {
        ref var slotState = ref _slots[slot];
        var current = GamepadSnapshot.FromNative(nativeState.Gamepad);

        if (!slotState.Connected)
        {
            _capture(new PlatformInputSample(
                PhysicalInputEventKind.DeviceAdded,
                Window: null,
                GetNativeDeviceIdentity(slot),
                PlatformInputDeviceKind.Gamepad,
                MonotonicTimestamp: timestamp));
        }

        var previous = slotState.Connected ? slotState.Snapshot : default;
        CaptureButtonChanges(slot, previous.Buttons, current.Buttons, timestamp);
        CaptureAxisChanges(slot, previous, current, timestamp);
        CaptureStickChanges(slot, previous, current, timestamp);
        CaptureTriggerChanges(slot, previous, current, timestamp);
        slotState = new SlotState(true, current);
    }

    private void ProcessDisconnectedState(int slot, long timestamp)
    {
        ref var slotState = ref _slots[slot];

        if (slotState.Connected)
        {
            _capture(new PlatformInputSample(
                PhysicalInputEventKind.DeviceRemoved,
                Window: null,
                GetNativeDeviceIdentity(slot),
                PlatformInputDeviceKind.Gamepad,
                MonotonicTimestamp: timestamp));
            slotState = default;
        }
    }

    internal struct NativeGamepad
    {
        internal ushort Buttons;
        internal byte LeftTrigger;
        internal byte RightTrigger;
        internal short ThumbLX;
        internal short ThumbLY;
        internal short ThumbRX;
        internal short ThumbRY;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct NativeState
    {
        [FieldOffset(4)]
        internal NativeGamepad Gamepad;

        [FieldOffset(0)]
        internal uint PacketNumber;
    }

    internal delegate uint XInputGetState(uint userIndex, out NativeState state);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct ButtonMapping
    {
        internal ButtonMapping(ushort mask, GamepadButtonValue button)
        {
            Mask = mask;
            Button = button;
        }

        internal GamepadButtonValue Button { get; }

        internal ushort Mask { get; }
    }

    private readonly struct GamepadSnapshot
    {
        private GamepadSnapshot(ushort buttons, byte leftTrigger, byte rightTrigger, short leftThumbX, short leftThumbY, short rightThumbX, short rightThumbY)
        {
            Buttons = buttons;
            LeftTrigger = leftTrigger;
            RightTrigger = rightTrigger;
            LeftThumbX = leftThumbX;
            LeftThumbY = leftThumbY;
            RightThumbX = rightThumbX;
            RightThumbY = rightThumbY;
        }

        internal ushort Buttons { get; }

        internal short LeftThumbX { get; }

        internal short LeftThumbY { get; }

        internal byte LeftTrigger { get; }

        internal short RightThumbX { get; }

        internal short RightThumbY { get; }

        internal byte RightTrigger { get; }

        internal static GamepadSnapshot FromNative(in NativeGamepad native)
            => new(
                native.Buttons,
                native.LeftTrigger,
                native.RightTrigger,
                native.ThumbLX,
                native.ThumbLY,
                native.ThumbRX,
                native.ThumbRY);
    }

    private struct SlotState
    {
        internal bool Connected;

        internal GamepadSnapshot Snapshot;

        internal SlotState(bool connected, GamepadSnapshot snapshot)
        {
            Connected = connected;
            Snapshot = snapshot;
        }
    }
}
