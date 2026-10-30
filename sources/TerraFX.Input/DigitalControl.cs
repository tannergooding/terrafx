// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Input;

/// <summary>Defines one digital control selected by its closed kind tag.</summary>
public sealed class DigitalControl
{
    /// <summary>Initializes a keyboard control.</summary>
    /// <param name="keyboard">The keyboard control.</param>
    /// <exception cref="ArgumentNullException"><paramref name="keyboard"/> is null.</exception>
    public DigitalControl(KeyboardControl keyboard)
    {
        ArgumentNullException.ThrowIfNull(keyboard);

        Kind = keyboard.Kind switch {
            KeyboardControlKind.KeyboardHid => DigitalControlKind.KeyboardHid,
            KeyboardControlKind.LogicalKey => DigitalControlKind.LogicalKey,
            _ => throw new ArgumentOutOfRangeException(nameof(keyboard)),
        };
        Keyboard = keyboard;
    }

    /// <summary>Initializes a pointer-button control.</summary>
    /// <param name="mouseButton">The pointer button.</param>
    public DigitalControl(MouseButtonValue mouseButton)
    {
        Kind = DigitalControlKind.MouseButton;
        MouseButton = mouseButton;
    }

    /// <summary>Initializes an assigned-player gamepad-button control.</summary>
    /// <param name="playerSlot">The player slot.</param>
    /// <param name="gamepadButton">The gamepad button.</param>
    public DigitalControl(ushort playerSlot, GamepadButtonValue gamepadButton)
    {
        Kind = DigitalControlKind.GamepadButton;
        PlayerSlot = playerSlot;
        GamepadButton = gamepadButton;
    }

    /// <summary>Gets the gamepad button, when this is a gamepad control.</summary>
    public GamepadButtonValue? GamepadButton { get; }

    /// <summary>Gets the keyboard control, when this is a keyboard control.</summary>
    public KeyboardControl? Keyboard { get; }

    /// <summary>Gets the closed control kind.</summary>
    public DigitalControlKind Kind { get; }

    /// <summary>Gets the pointer button, when this is a pointer control.</summary>
    public MouseButtonValue? MouseButton { get; }

    /// <summary>Gets the player slot, when this is a gamepad control.</summary>
    public ushort? PlayerSlot { get; }
}
