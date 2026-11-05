// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.Versioning;
using GameInputSharp.Abstractions;
using TerraFX;
using TerraFX.Input;
using TerraFX.Numerics;

namespace TerraFX.Platform;

[SupportedOSPlatform("windows")]
internal static class WindowsGamepadStateMapper
{
    private const string NativeClockDomain = "GameInput";
    private static readonly (GameInputGamepadButtons Source, GamepadButtonValue Target)[] s_buttonMappings = [
        (GameInputGamepadButtons.A, GamepadButtonValue.South),
        (GameInputGamepadButtons.B, GamepadButtonValue.East),
        (GameInputGamepadButtons.X, GamepadButtonValue.West),
        (GameInputGamepadButtons.Y, GamepadButtonValue.North),
        (GameInputGamepadButtons.DPadUp, GamepadButtonValue.DpadUp),
        (GameInputGamepadButtons.DPadDown, GamepadButtonValue.DpadDown),
        (GameInputGamepadButtons.DPadLeft, GamepadButtonValue.DpadLeft),
        (GameInputGamepadButtons.DPadRight, GamepadButtonValue.DpadRight),
        (GameInputGamepadButtons.Menu, GamepadButtonValue.Menu),
        (GameInputGamepadButtons.View, GamepadButtonValue.Start),
        (GameInputGamepadButtons.LeftShoulder, GamepadButtonValue.LeftBumper),
        (GameInputGamepadButtons.RightShoulder, GamepadButtonValue.RightBumper),
    ];
    private static readonly uint s_mappedButtonMask = GetMappedButtonMask();

    internal static long ConvertNativeTimestamp(ulong timestamp)
    {
        if (timestamp > long.MaxValue)
        {
            throw new InvalidDataException("A GameInput timestamp exceeds the supported signed range.");
        }

        return checked((long)timestamp);
    }

    internal static PlatformInputSample CreateLifecycleSample(
        PhysicalInputEventKind kind,
        nint nativeIdentity,
        long nativeTimestamp,
        long monotonicTimestamp)
        => new(
            kind,
            Window: null,
            nativeIdentity,
            PlatformInputDeviceKind.Gamepad,
            NativeTimestamp: nativeTimestamp,
            NativeClockDomain: NativeClockDomain,
            MonotonicTimestamp: monotonicTimestamp);

    internal static ImmutableArray<PlatformInputStateValue> CreateState(in PlatformGamepadState state)
    {
        Validate(state);
        var values = ImmutableArray.CreateBuilder<PlatformInputStateValue>(s_buttonMappings.Length + 8);

        foreach (var (source, target) in s_buttonMappings)
        {
            values.Add(new PlatformInputStateValue(
                PlatformInputStateValueKind.GamepadButton,
                GamepadButton: target,
                IsDown: (state.Buttons & (uint)source) != 0));
        }

        values.Add(new PlatformInputStateValue(
            PlatformInputStateValueKind.GamepadAxis,
            Axis: GamepadAxis.LeftX,
            AxisValue: state.LeftThumbstickX));
        values.Add(new PlatformInputStateValue(
            PlatformInputStateValueKind.GamepadAxis,
            Axis: GamepadAxis.LeftY,
            AxisValue: state.LeftThumbstickY));
        values.Add(new PlatformInputStateValue(
            PlatformInputStateValueKind.GamepadAxis,
            Axis: GamepadAxis.RightX,
            AxisValue: state.RightThumbstickX));
        values.Add(new PlatformInputStateValue(
            PlatformInputStateValueKind.GamepadAxis,
            Axis: GamepadAxis.RightY,
            AxisValue: state.RightThumbstickY));
        values.Add(new PlatformInputStateValue(
            PlatformInputStateValueKind.GamepadStick,
            Stick: GamepadStick.Left,
            StickValue: new Vector2(state.LeftThumbstickX, state.LeftThumbstickY)));
        values.Add(new PlatformInputStateValue(
            PlatformInputStateValueKind.GamepadStick,
            Stick: GamepadStick.Right,
            StickValue: new Vector2(state.RightThumbstickX, state.RightThumbstickY)));
        values.Add(new PlatformInputStateValue(
            PlatformInputStateValueKind.GamepadTrigger,
            Trigger: GamepadTrigger.Left,
            TriggerValue: state.LeftTrigger));
        values.Add(new PlatformInputStateValue(
            PlatformInputStateValueKind.GamepadTrigger,
            Trigger: GamepadTrigger.Right,
            TriggerValue: state.RightTrigger));
        return values.MoveToImmutable();
    }

    internal static PlatformInputSample CreateStateSample(
        nint nativeIdentity,
        PlatformInputStateValue state,
        long nativeTimestamp,
        long monotonicTimestamp)
    {
        switch (state.Kind)
        {
            case PlatformInputStateValueKind.GamepadButton:
            {
                if ((state.GamepadButton is not { } button) || (state.IsDown is not { } isDown))
                {
                    throw new InvalidOperationException("The GameInput button mapper returned an incomplete state.");
                }

                return new PlatformInputSample(
                    isDown ? PhysicalInputEventKind.GamepadButtonDown : PhysicalInputEventKind.GamepadButtonUp,
                    Window: null,
                    nativeIdentity,
                    PlatformInputDeviceKind.Gamepad,
                    GamepadButton: button,
                    IsDown: isDown,
                    NativeTimestamp: nativeTimestamp,
                    NativeClockDomain: NativeClockDomain,
                    MonotonicTimestamp: monotonicTimestamp);
            }

            case PlatformInputStateValueKind.GamepadAxis:
            {
                if ((state.Axis is not { } axis) || (state.AxisValue is not { } axisValue))
                {
                    throw new InvalidOperationException("The GameInput axis mapper returned an incomplete state.");
                }

                return new PlatformInputSample(
                    PhysicalInputEventKind.GamepadAxis,
                    Window: null,
                    nativeIdentity,
                    PlatformInputDeviceKind.Gamepad,
                    Axis: axis,
                    AxisValue: axisValue,
                    NativeTimestamp: nativeTimestamp,
                    NativeClockDomain: NativeClockDomain,
                    MonotonicTimestamp: monotonicTimestamp);
            }

            case PlatformInputStateValueKind.GamepadStick:
            {
                if ((state.Stick is not { } stick) || (state.StickValue is not { } stickValue))
                {
                    throw new InvalidOperationException("The GameInput stick mapper returned an incomplete state.");
                }

                return new PlatformInputSample(
                    PhysicalInputEventKind.GamepadStick,
                    Window: null,
                    nativeIdentity,
                    PlatformInputDeviceKind.Gamepad,
                    Stick: stick,
                    StickValue: stickValue,
                    NativeTimestamp: nativeTimestamp,
                    NativeClockDomain: NativeClockDomain,
                    MonotonicTimestamp: monotonicTimestamp);
            }

            case PlatformInputStateValueKind.GamepadTrigger:
            {
                if ((state.Trigger is not { } trigger) || (state.TriggerValue is not { } triggerValue))
                {
                    throw new InvalidOperationException("The GameInput trigger mapper returned an incomplete state.");
                }

                return new PlatformInputSample(
                    PhysicalInputEventKind.GamepadTrigger,
                    Window: null,
                    nativeIdentity,
                    PlatformInputDeviceKind.Gamepad,
                    Trigger: trigger,
                    TriggerValue: triggerValue,
                    NativeTimestamp: nativeTimestamp,
                    NativeClockDomain: NativeClockDomain,
                    MonotonicTimestamp: monotonicTimestamp);
            }

            default:
            {
                throw new InvalidOperationException("The GameInput mapper returned an unknown state kind.");
            }
        }
    }

    internal static PlatformGamepadState FromGameInput(in GamepadState state)
        => new(
            state.Buttons,
            state.LeftTrigger,
            state.RightTrigger,
            state.LeftThumbstickX,
            state.LeftThumbstickY,
            state.RightThumbstickX,
            state.RightThumbstickY);

    internal static ImmutableArray<PlatformInputStateValue> GetChanges(
        in PlatformGamepadState previous,
        in PlatformGamepadState current)
    {
        var previousState = CreateState(previous);
        var currentState = CreateState(current);
        var changes = ImmutableArray.CreateBuilder<PlatformInputStateValue>(currentState.Length);

        for (var index = 0; index < currentState.Length; index++)
        {
            if (previousState[index] != currentState[index])
            {
                changes.Add(currentState[index]);
            }
        }

        return changes.ToImmutable();
    }

    internal static uint GetUnmappedButtonMask(in PlatformGamepadState state)
        => state.Buttons & ~s_mappedButtonMask;

    private static uint GetMappedButtonMask()
    {
        var mask = 0u;

        foreach (var (source, _) in s_buttonMappings)
        {
            mask |= (uint)source;
        }

        return mask;
    }

    private static bool IsNormalizedAxis(float value)
        => float.IsFinite(value) && (value >= -1.0f) && (value <= 1.0f);

    private static bool IsNormalizedTrigger(float value)
        => float.IsFinite(value) && (value >= 0.0f) && (value <= 1.0f);

    private static void Validate(in PlatformGamepadState state)
    {
        if (!IsNormalizedAxis(state.LeftThumbstickX)
            || !IsNormalizedAxis(state.LeftThumbstickY)
            || !IsNormalizedAxis(state.RightThumbstickX)
            || !IsNormalizedAxis(state.RightThumbstickY)
            || !IsNormalizedTrigger(state.LeftTrigger)
            || !IsNormalizedTrigger(state.RightTrigger))
        {
            throw new InvalidDataException("GameInput returned a non-finite or out-of-range standard gamepad value.");
        }
    }
}
