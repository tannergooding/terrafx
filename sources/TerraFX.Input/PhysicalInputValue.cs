// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Numerics;

namespace TerraFX.Input;

/// <summary>Stores one normalized control value carried by a physical event or reconciliation.</summary>
public readonly record struct PhysicalInputValue
{
    internal PhysicalInputValue(DigitalControl digitalControl, bool isDown)
    {
        ArgumentNullException.ThrowIfNull(digitalControl);

        var isValid = digitalControl.Kind switch {
            DigitalControlKind.KeyboardHid or DigitalControlKind.LogicalKey =>
                digitalControl.Keyboard is not null && InputActionMapValidator.IsValidKeyboardControl(digitalControl.Keyboard),
            DigitalControlKind.MouseButton => digitalControl.MouseButton is { } mouseButton && Enum.IsDefined(mouseButton),
            _ => false,
        };

        if (!isValid)
        {
            throw new ArgumentException("The digital control is invalid.", nameof(digitalControl));
        }

        Kind = PhysicalInputValueKind.Digital;
        DigitalControl = digitalControl;
        IsDown = isDown;
    }

    internal PhysicalInputValue(GamepadAxis axis, float axisValue)
    {
        if (!Enum.IsDefined(axis))
        {
            throw new ArgumentOutOfRangeException(nameof(axis));
        }

        if (!float.IsFinite(axisValue) || (axisValue < -1.0f) || (axisValue > 1.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(axisValue));
        }

        Kind = PhysicalInputValueKind.GamepadAxis;
        Axis = axis;
        AxisValue = axisValue;
    }

    internal PhysicalInputValue(GamepadButtonValue gamepadButton, bool isDown)
    {
        if (!Enum.IsDefined(gamepadButton))
        {
            throw new ArgumentOutOfRangeException(nameof(gamepadButton));
        }

        Kind = PhysicalInputValueKind.GamepadButton;
        GamepadButton = gamepadButton;
        IsDown = isDown;
    }

    internal PhysicalInputValue(GamepadStick stick, Vector2 stickValue)
    {
        if (!Enum.IsDefined(stick))
        {
            throw new ArgumentOutOfRangeException(nameof(stick));
        }

        if (!stickValue.IsFinite().All()
            || (stickValue.X < -1.0f)
            || (stickValue.X > 1.0f)
            || (stickValue.Y < -1.0f)
            || (stickValue.Y > 1.0f)
            || (Vector2.LengthSquared(stickValue) > 1.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(stickValue));
        }

        Kind = PhysicalInputValueKind.GamepadStick;
        Stick = stick;
        StickValue = stickValue;
    }

    internal PhysicalInputValue(GamepadTrigger trigger, float triggerValue)
    {
        if (!Enum.IsDefined(trigger))
        {
            throw new ArgumentOutOfRangeException(nameof(trigger));
        }

        if (!float.IsFinite(triggerValue) || (triggerValue < 0.0f) || (triggerValue > 1.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(triggerValue));
        }

        Kind = PhysicalInputValueKind.GamepadTrigger;
        Trigger = trigger;
        TriggerValue = triggerValue;
    }

    /// <summary>Gets the gamepad axis, when this is an axis value.</summary>
    public GamepadAxis? Axis { get; }

    /// <summary>Gets the normalized gamepad axis value.</summary>
    public float? AxisValue { get; }

    /// <summary>Gets the digital control, when this is a digital value.</summary>
    public DigitalControl? DigitalControl { get; }

    /// <summary>Gets the gamepad button, when this is a gamepad button value.</summary>
    public GamepadButtonValue? GamepadButton { get; }

    /// <summary>Gets whether the digital control is down.</summary>
    public bool? IsDown { get; }

    /// <summary>Gets the normalized value kind.</summary>
    public PhysicalInputValueKind Kind { get; }

    /// <summary>Gets the gamepad stick, when this is a stick value.</summary>
    public GamepadStick? Stick { get; }

    /// <summary>Gets the normalized gamepad stick value.</summary>
    public Vector2? StickValue { get; }

    /// <summary>Gets the gamepad trigger, when this is a trigger value.</summary>
    public GamepadTrigger? Trigger { get; }

    /// <summary>Gets the normalized gamepad trigger value.</summary>
    public float? TriggerValue { get; }

    /// <inheritdoc />
    public bool Equals(PhysicalInputValue other)
        => (Kind == other.Kind)
            && AreDigitalControlsEqual(DigitalControl, other.DigitalControl)
            && (IsDown == other.IsDown)
            && (Axis == other.Axis)
            && (AxisValue == other.AxisValue)
            && (Stick == other.Stick)
            && (StickValue == other.StickValue)
            && (Trigger == other.Trigger)
            && (TriggerValue == other.TriggerValue);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(GetDigitalControlHashCode(DigitalControl));
        hash.Add(IsDown);
        hash.Add(Axis);
        hash.Add(AxisValue);
        hash.Add(Stick);
        hash.Add(StickValue);
        hash.Add(Trigger);
        hash.Add(TriggerValue);
        return hash.ToHashCode();
    }

    private static bool AreDigitalControlsEqual(DigitalControl? left, DigitalControl? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if ((left is null) || (right is null) || (left.Kind != right.Kind))
        {
            return false;
        }

        return left.Kind switch {
            DigitalControlKind.KeyboardHid =>
                (left.Keyboard?.UsagePage == right.Keyboard?.UsagePage)
                && (left.Keyboard?.Usage == right.Keyboard?.Usage),
            DigitalControlKind.LogicalKey =>
                StringComparer.Ordinal.Equals(left.Keyboard?.LogicalKeyName, right.Keyboard?.LogicalKeyName),
            DigitalControlKind.MouseButton => left.MouseButton == right.MouseButton,
            DigitalControlKind.GamepadButton =>
                (left.PlayerSlot == right.PlayerSlot) && (left.GamepadButton == right.GamepadButton),
            _ => false,
        };
    }

    private static int GetDigitalControlHashCode(DigitalControl? control)
    {
        if (control is null)
        {
            return 0;
        }

        return control.Kind switch {
            DigitalControlKind.KeyboardHid => HashCode.Combine(control.Kind, control.Keyboard?.UsagePage, control.Keyboard?.Usage),
            DigitalControlKind.LogicalKey => HashCode.Combine(
                control.Kind,
                (control.Keyboard?.LogicalKeyName is { } name) ? StringComparer.Ordinal.GetHashCode(name) : 0),
            DigitalControlKind.MouseButton => HashCode.Combine(control.Kind, control.MouseButton),
            DigitalControlKind.GamepadButton => HashCode.Combine(control.Kind, control.PlayerSlot, control.GamepadButton),
            _ => 0,
        };
    }
}
