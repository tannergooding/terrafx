// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Input;

/// <summary>Identifies a digital control representation.</summary>
public enum DigitalControlKind
{
    /// <summary>A physical USB HID usage.</summary>
    KeyboardHid = 0,

    /// <summary>A layout-dependent logical key name.</summary>
    LogicalKey = 1,

    /// <summary>A pointer button.</summary>
    MouseButton = 2,

    /// <summary>A gamepad button assigned to a player slot.</summary>
    GamepadButton = 3,
}
