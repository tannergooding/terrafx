// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.IO;
using TerraFX.Input;

namespace TerraFX.Platform;

internal sealed class LinuxEvdevGamepadState
{
    private const ushort AbsHat0X = 0x0010;
    private const ushort AbsHat0Y = 0x0011;
    private const ushort EvAbs = 0x0003;
    private const ushort EvKey = 0x0001;

    private readonly LinuxEvdevGamepadAxisState _axes;
    private readonly LinuxEvdevGamepadButtonState _buttons = new();
    private bool _isRetired;

    internal LinuxEvdevGamepadState(
        ReadOnlySpan<(ushort Code, LinuxEvdevAxisInfo Info, int InitialValue)> axisDescriptors)
    {
        _axes = new LinuxEvdevGamepadAxisState(axisDescriptors);
    }

    internal ImmutableArray<PlatformInputStateValue> GetCurrentState()
    {
        var pressedButtons = _buttons.GetPressedButtons();
        var buttonValues = Enum.GetValues<GamepadButtonValue>();
        var state = ImmutableArray.CreateBuilder<PlatformInputStateValue>(buttonValues.Length + 8);

        foreach (var button in buttonValues)
        {
            state.Add(new PlatformInputStateValue(
                PlatformInputStateValueKind.GamepadButton,
                GamepadButton: button,
                IsDown: pressedButtons.Contains(button)));
        }

        state.AddRange(_axes.GetCurrentState());
        return state.MoveToImmutable();
    }

    internal ImmutableArray<PlatformInputStateValue> Reset()
    {
        if (_isRetired)
        {
            return [];
        }

        _isRetired = true;
        var buttonTransitions = _buttons.Reset();
        var axisChanges = _axes.Reset();
        var changes = ImmutableArray.CreateBuilder<PlatformInputStateValue>(
            buttonTransitions.Length + axisChanges.Length);

        foreach (var transition in buttonTransitions)
        {
            changes.Add(new PlatformInputStateValue(
                PlatformInputStateValueKind.GamepadButton,
                GamepadButton: transition.Button,
                IsDown: false));
        }

        changes.AddRange(axisChanges);
        return changes.MoveToImmutable();
    }

    internal bool TryProcessEvent(
        ushort type,
        ushort code,
        int value,
        out ImmutableArray<PlatformInputStateValue> changes)
    {
        changes = [];

        if (_isRetired)
        {
            return false;
        }

        if (type == EvKey)
        {
            if (value == 2)
            {
                return false;
            }

            if (value is not (0 or 1))
            {
                throw new InvalidDataException("An evdev gamepad key event has an invalid value.");
            }

            if (!_buttons.TryUpdateButton(code, value == 1, out var transition))
            {
                return false;
            }

            changes = [
                new PlatformInputStateValue(
                    PlatformInputStateValueKind.GamepadButton,
                    GamepadButton: transition.Button,
                    IsDown: transition.IsDown),
            ];
            return true;
        }

        if (type != EvAbs)
        {
            return false;
        }

        if (code is AbsHat0X or AbsHat0Y)
        {
            if (!_buttons.TryUpdateHat(code, value, out var transitions))
            {
                return false;
            }

            var buttonChanges = ImmutableArray.CreateBuilder<PlatformInputStateValue>(transitions.Length);

            foreach (var transition in transitions)
            {
                buttonChanges.Add(new PlatformInputStateValue(
                    PlatformInputStateValueKind.GamepadButton,
                    GamepadButton: transition.Button,
                    IsDown: transition.IsDown));
            }

            changes = buttonChanges.MoveToImmutable();
            return true;
        }

        return _axes.TryUpdate(code, value, out changes);
    }
}
