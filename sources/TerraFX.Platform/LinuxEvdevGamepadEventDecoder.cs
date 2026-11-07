// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX;
using TerraFX.Input;
using TerraFX.Numerics;

namespace TerraFX.Platform;

internal static class LinuxEvdevGamepadEventDecoder
{
    internal const ushort AbsoluteAxisEventType = 0x0003;
    internal const ushort KeyEventType = 0x0001;
    internal const ushort SynchronizationDropped = 0x0003;
    internal const ushort SynchronizationEventType = 0x0000;
    internal const ushort SynchronizationReport = 0x0000;

    internal static ImmutableArray<PlatformInputSample> Decode(
        LinuxEvdevGamepadState state,
        nint nativeDeviceIdentity,
        in LinuxEvdevInputEvent inputEvent,
        bool monotonicClockRequested,
        long monotonicTimestamp)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero((long)nativeDeviceIdentity);
        ArgumentOutOfRangeException.ThrowIfNegative(monotonicTimestamp);

        if (inputEvent.Type is not (KeyEventType or AbsoluteAxisEventType))
        {
            return [];
        }

        if (!state.TryProcessEvent(inputEvent.Type, inputEvent.Code, inputEvent.Value, out var stateChanges))
        {
            return [];
        }

        var hasNativeTimestamp = inputEvent.TryGetNativeTimestamp(out var nativeTimestamp);
        var nativeClockDomain = !hasNativeTimestamp
            ? null
            : monotonicClockRequested
                ? "CLOCK_MONOTONIC"
                : "evdev-default";
        var diagnostics = (monotonicClockRequested && hasNativeTimestamp)
            ? PhysicalInputEventDiagnostic.None
            : PhysicalInputEventDiagnostic.TimestampUncertain;
        var samples = ImmutableArray.CreateBuilder<PlatformInputSample>(stateChanges.Length);

        foreach (var stateChange in stateChanges)
        {
            samples.Add(CreateSample(
                nativeDeviceIdentity,
                stateChange,
                hasNativeTimestamp ? nativeTimestamp : null,
                nativeClockDomain,
                diagnostics,
                monotonicTimestamp));
        }

        return samples.MoveToImmutable();
    }

    private static PlatformInputSample CreateSample(
        nint nativeDeviceIdentity,
        PlatformInputStateValue state,
        long? nativeTimestamp,
        string? nativeClockDomain,
        PhysicalInputEventDiagnostic diagnostics,
        long monotonicTimestamp)
    {
        switch (state.Kind)
        {
            case PlatformInputStateValueKind.GamepadButton:
            {
                if ((state.GamepadButton is not { } button) || (state.IsDown is not { } isDown))
                {
                    throw new InvalidOperationException("The evdev gamepad state returned an incomplete button value.");
                }

                return new PlatformInputSample(
                    isDown ? PhysicalInputEventKind.GamepadButtonDown : PhysicalInputEventKind.GamepadButtonUp,
                    Window: null,
                    nativeDeviceIdentity,
                    PlatformInputDeviceKind.Gamepad,
                    GamepadButton: button,
                    IsDown: isDown,
                    NativeTimestamp: nativeTimestamp,
                    NativeClockDomain: nativeClockDomain,
                    Diagnostics: diagnostics,
                    MonotonicTimestamp: monotonicTimestamp);
            }

            case PlatformInputStateValueKind.GamepadAxis:
            {
                if ((state.Axis is not { } axis) || (state.AxisValue is not { } value))
                {
                    throw new InvalidOperationException("The evdev gamepad state returned an incomplete axis value.");
                }

                return new PlatformInputSample(
                    PhysicalInputEventKind.GamepadAxis,
                    Window: null,
                    nativeDeviceIdentity,
                    PlatformInputDeviceKind.Gamepad,
                    Axis: axis,
                    AxisValue: value,
                    NativeTimestamp: nativeTimestamp,
                    NativeClockDomain: nativeClockDomain,
                    Diagnostics: diagnostics,
                    MonotonicTimestamp: monotonicTimestamp);
            }

            case PlatformInputStateValueKind.GamepadStick:
            {
                if ((state.Stick is not { } stick) || (state.StickValue is not { } value))
                {
                    throw new InvalidOperationException("The evdev gamepad state returned an incomplete stick value.");
                }

                return new PlatformInputSample(
                    PhysicalInputEventKind.GamepadStick,
                    Window: null,
                    nativeDeviceIdentity,
                    PlatformInputDeviceKind.Gamepad,
                    Stick: stick,
                    StickValue: value,
                    NativeTimestamp: nativeTimestamp,
                    NativeClockDomain: nativeClockDomain,
                    Diagnostics: diagnostics,
                    MonotonicTimestamp: monotonicTimestamp);
            }

            case PlatformInputStateValueKind.GamepadTrigger:
            {
                if ((state.Trigger is not { } trigger) || (state.TriggerValue is not { } value))
                {
                    throw new InvalidOperationException("The evdev gamepad state returned an incomplete trigger value.");
                }

                return new PlatformInputSample(
                    PhysicalInputEventKind.GamepadTrigger,
                    Window: null,
                    nativeDeviceIdentity,
                    PlatformInputDeviceKind.Gamepad,
                    Trigger: trigger,
                    TriggerValue: value,
                    NativeTimestamp: nativeTimestamp,
                    NativeClockDomain: nativeClockDomain,
                    Diagnostics: diagnostics,
                    MonotonicTimestamp: monotonicTimestamp);
            }

            default:
            {
                throw new InvalidOperationException("The evdev gamepad state returned an unsupported value kind.");
            }
        }
    }
}
