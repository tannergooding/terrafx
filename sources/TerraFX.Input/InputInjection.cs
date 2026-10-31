// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Numerics;

namespace TerraFX.Input;

/// <summary>Describes one normalized input value for explicit headless injection.</summary>
public sealed class InputInjection
{
    /// <summary>Initializes a keyboard-key transition.</summary>
    /// <param name="key">The physical or logical keyboard control.</param>
    /// <param name="keyEventKind">The transition kind.</param>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="key"/> is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="keyEventKind"/> is unknown.</exception>
    public InputInjection(KeyboardControl key, InputKeyEventKind keyEventKind)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (!InputActionMapValidator.IsValidKeyboardControl(key))
        {
            throw new ArgumentException("The keyboard control is invalid.", nameof(key));
        }

        if (!Enum.IsDefined(keyEventKind))
        {
            throw new ArgumentOutOfRangeException(nameof(keyEventKind));
        }

        Key = key;
        KeyEventKind = keyEventKind;
        Kind = InputInjectionKind.KeyboardKey;
    }

    /// <summary>Initializes a normalized gamepad-axis value.</summary>
    /// <param name="playerSlot">The explicitly targeted player slot.</param>
    /// <param name="axis">The normalized axis.</param>
    /// <param name="value">The finite axis value in [-1, 1].</param>
    /// <exception cref="ArgumentOutOfRangeException">The axis is unknown or the value is outside [-1, 1].</exception>
    public InputInjection(ushort playerSlot, GamepadAxis axis, float value)
    {
        if (!Enum.IsDefined(axis))
        {
            throw new ArgumentOutOfRangeException(nameof(axis));
        }

        if (!float.IsFinite(value) || (value < -1.0f) || (value > 1.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        Axis = axis;
        AxisValue = value;
        Kind = InputInjectionKind.GamepadAxis;
        PlayerSlot = playerSlot;
    }

    /// <summary>Initializes a normalized gamepad-button state.</summary>
    /// <param name="playerSlot">The explicitly targeted player slot.</param>
    /// <param name="button">The standardized gamepad button.</param>
    /// <param name="isDown"><see langword="true"/> when the button is down.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="button"/> is unknown.</exception>
    public InputInjection(ushort playerSlot, GamepadButtonValue button, bool isDown)
    {
        if (!Enum.IsDefined(button))
        {
            throw new ArgumentOutOfRangeException(nameof(button));
        }

        Button = button;
        IsDown = isDown;
        Kind = InputInjectionKind.GamepadButton;
        PlayerSlot = playerSlot;
    }

    /// <summary>Initializes a normalized gamepad-stick value.</summary>
    /// <param name="playerSlot">The explicitly targeted player slot.</param>
    /// <param name="stick">The normalized stick.</param>
    /// <param name="value">The finite normalized vector with length at most one.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="stick"/> or <paramref name="value"/> is invalid.</exception>
    public InputInjection(ushort playerSlot, GamepadStick stick, Vector2 value)
    {
        if (!Enum.IsDefined(stick))
        {
            throw new ArgumentOutOfRangeException(nameof(stick));
        }

        if (!value.IsFinite().All()
            || (value.X < -1.0f)
            || (value.X > 1.0f)
            || (value.Y < -1.0f)
            || (value.Y > 1.0f)
            || (Vector2.LengthSquared(value) > 1.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        Kind = InputInjectionKind.GamepadStick;
        PlayerSlot = playerSlot;
        Stick = stick;
        StickValue = value;
    }

    /// <summary>Initializes a normalized gamepad-trigger value.</summary>
    /// <param name="playerSlot">The explicitly targeted player slot.</param>
    /// <param name="trigger">The normalized trigger.</param>
    /// <param name="value">The finite trigger value in [0, 1].</param>
    /// <exception cref="ArgumentOutOfRangeException">The trigger is unknown or the value is outside [0, 1].</exception>
    public InputInjection(ushort playerSlot, GamepadTrigger trigger, float value)
    {
        if (!Enum.IsDefined(trigger))
        {
            throw new ArgumentOutOfRangeException(nameof(trigger));
        }

        if (!float.IsFinite(value) || (value < 0.0f) || (value > 1.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        Kind = InputInjectionKind.GamepadTrigger;
        PlayerSlot = playerSlot;
        Trigger = trigger;
        TriggerValue = value;
    }

    /// <summary>Gets the normalized gamepad axis, when this is an axis injection.</summary>
    public GamepadAxis? Axis { get; }

    /// <summary>Gets the normalized gamepad axis value, when this is an axis injection.</summary>
    public float? AxisValue { get; }

    /// <summary>Gets the standardized gamepad button, when this is a button injection.</summary>
    public GamepadButtonValue? Button { get; }

    /// <summary>Gets whether the gamepad button is down, when this is a button injection.</summary>
    public bool? IsDown { get; }

    /// <summary>Gets the keyboard control, when this is a keyboard injection.</summary>
    public KeyboardControl? Key { get; }

    /// <summary>Gets the keyboard transition, when this is a keyboard injection.</summary>
    public InputKeyEventKind? KeyEventKind { get; }

    /// <summary>Gets the injection kind.</summary>
    public InputInjectionKind Kind { get; }

    /// <summary>Gets the targeted player slot, when this is a gamepad injection.</summary>
    public ushort? PlayerSlot { get; }

    /// <summary>Gets the normalized gamepad stick, when this is a stick injection.</summary>
    public GamepadStick? Stick { get; }

    /// <summary>Gets the normalized gamepad stick value, when this is a stick injection.</summary>
    public Vector2? StickValue { get; }

    /// <summary>Gets the normalized gamepad trigger, when this is a trigger injection.</summary>
    public GamepadTrigger? Trigger { get; }

    /// <summary>Gets the normalized gamepad trigger value, when this is a trigger injection.</summary>
    public float? TriggerValue { get; }
}
