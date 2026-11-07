// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using TerraFX.Input;

namespace TerraFX.Platform;

internal sealed class LinuxEvdevGamepadButtonState
{
    private const ushort AbsHat0X = 0x0010;
    private const ushort AbsHat0Y = 0x0011;
    private const ushort BtnDpadDown = 0x0221;
    private const ushort BtnDpadLeft = 0x0222;
    private const ushort BtnDpadRight = 0x0223;
    private const ushort BtnDpadUp = 0x0220;
    private const ushort BtnEast = 0x0131;
    private const ushort BtnMode = 0x013C;
    private const ushort BtnNorth = 0x0133;
    private const ushort BtnSelect = 0x013A;
    private const ushort BtnSouth = 0x0130;
    private const ushort BtnStart = 0x013B;
    private const ushort BtnTl = 0x0136;
    private const ushort BtnTr = 0x0137;
    private const ushort BtnWest = 0x0134;

    private sbyte _hat0X;
    private sbyte _hat0Y;
    private readonly HashSet<ushort> _pressedButtonCodes = [];

    private static bool TryMapButtonCode(ushort code, out GamepadButtonValue button)
    {
        button = code switch {
            BtnSouth => GamepadButtonValue.South,
            BtnEast => GamepadButtonValue.East,
            BtnWest => GamepadButtonValue.West,
            BtnNorth => GamepadButtonValue.North,
            BtnDpadUp => GamepadButtonValue.DpadUp,
            BtnDpadDown => GamepadButtonValue.DpadDown,
            BtnDpadLeft => GamepadButtonValue.DpadLeft,
            BtnDpadRight => GamepadButtonValue.DpadRight,
            BtnSelect => GamepadButtonValue.Menu,
            BtnStart => GamepadButtonValue.Start,
            BtnMode => GamepadButtonValue.System,
            BtnTl => GamepadButtonValue.LeftBumper,
            BtnTr => GamepadButtonValue.RightBumper,
            _ => default,
        };
        return code is BtnSouth
            or BtnEast
            or BtnWest
            or BtnNorth
            or BtnDpadUp
            or BtnDpadDown
            or BtnDpadLeft
            or BtnDpadRight
            or BtnSelect
            or BtnStart
            or BtnMode
            or BtnTl
            or BtnTr;
    }

    internal ImmutableArray<GamepadButtonValue> GetPressedButtons()
    {
        var pressedButtons = ImmutableArray.CreateBuilder<GamepadButtonValue>();

        foreach (var button in Enum.GetValues<GamepadButtonValue>())
        {
            if (IsButtonDown(button))
            {
                pressedButtons.Add(button);
            }
        }

        return pressedButtons.ToImmutable();
    }

    internal ImmutableArray<ButtonTransition> Reset()
    {
        var pressedButtons = GetPressedButtons();
        _pressedButtonCodes.Clear();
        _hat0X = 0;
        _hat0Y = 0;

        var transitions = ImmutableArray.CreateBuilder<ButtonTransition>(pressedButtons.Length);

        foreach (var button in pressedButtons)
        {
            transitions.Add(new ButtonTransition(button, false));
        }

        return transitions.MoveToImmutable();
    }

    internal bool TryUpdateButton(
        ushort code,
        bool isDown,
        out ButtonTransition transition)
    {
        transition = default;

        if (!TryMapButtonCode(code, out var button))
        {
            return false;
        }

        var wasDown = IsButtonDown(button);

        if (isDown)
        {
            _ = _pressedButtonCodes.Add(code);
        }
        else
        {
            _ = _pressedButtonCodes.Remove(code);
        }

        var isNowDown = IsButtonDown(button);

        if (wasDown != isNowDown)
        {
            transition = new ButtonTransition(button, isNowDown);
            return true;
        }

        return false;
    }

    internal bool TryUpdateHat(
        ushort code,
        int value,
        out ImmutableArray<ButtonTransition> transitions)
    {
        transitions = [];

        if (code is not (AbsHat0X or AbsHat0Y))
        {
            return false;
        }

        if (value is < -1 or > 1)
        {
            throw new InvalidDataException("An evdev hat sample is outside its normalized -1..1 range.");
        }

        var upWasDown = IsButtonDown(GamepadButtonValue.DpadUp);
        var downWasDown = IsButtonDown(GamepadButtonValue.DpadDown);
        var leftWasDown = IsButtonDown(GamepadButtonValue.DpadLeft);
        var rightWasDown = IsButtonDown(GamepadButtonValue.DpadRight);

        if (code == AbsHat0X)
        {
            _hat0X = checked((sbyte)value);
        }
        else
        {
            _hat0Y = checked((sbyte)value);
        }

        var transitionBuilder = ImmutableArray.CreateBuilder<ButtonTransition>(4);
        AddTransition(GamepadButtonValue.DpadUp, upWasDown, transitionBuilder);
        AddTransition(GamepadButtonValue.DpadDown, downWasDown, transitionBuilder);
        AddTransition(GamepadButtonValue.DpadLeft, leftWasDown, transitionBuilder);
        AddTransition(GamepadButtonValue.DpadRight, rightWasDown, transitionBuilder);
        transitions = transitionBuilder.ToImmutable();
        return !transitions.IsEmpty;
    }

    private void AddTransition(
        GamepadButtonValue button,
        bool wasDown,
        ImmutableArray<ButtonTransition>.Builder transitions)
    {
        var isNowDown = IsButtonDown(button);

        if (wasDown != isNowDown)
        {
            transitions.Add(new ButtonTransition(button, isNowDown));
        }
    }

    private bool IsButtonDown(GamepadButtonValue button)
    {
        var hatIsDown = button switch {
            GamepadButtonValue.DpadUp => _hat0Y < 0,
            GamepadButtonValue.DpadDown => _hat0Y > 0,
            GamepadButtonValue.DpadLeft => _hat0X < 0,
            GamepadButtonValue.DpadRight => _hat0X > 0,
            _ => false,
        };

        if (hatIsDown)
        {
            return true;
        }

        foreach (var code in _pressedButtonCodes)
        {
            if (TryMapButtonCode(code, out var mappedButton) && (mappedButton == button))
            {
                return true;
            }
        }

        return false;
    }

    internal readonly record struct ButtonTransition(GamepadButtonValue Button, bool IsDown);
}
