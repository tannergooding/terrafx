// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Input;

/// <summary>Identifies one kind of normalized headless input injection.</summary>
public enum InputInjectionKind
{
    /// <summary>A keyboard control transition.</summary>
    KeyboardKey = 0,

    /// <summary>A gamepad button state.</summary>
    GamepadButton = 1,

    /// <summary>A normalized gamepad axis value.</summary>
    GamepadAxis = 2,

    /// <summary>A normalized two-dimensional gamepad stick value.</summary>
    GamepadStick = 3,

    /// <summary>A normalized gamepad trigger value.</summary>
    GamepadTrigger = 4,
}
