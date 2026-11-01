// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Input;

/// <summary>Identifies the normalized control represented by a physical input value.</summary>
public enum PhysicalInputValueKind
{
    /// <summary>Invalid value kind.</summary>
    None = 0,

    /// <summary>A digital control state.</summary>
    Digital = 1,

    /// <summary>A gamepad button state without recipient-specific player assignment.</summary>
    GamepadButton = 2,

    /// <summary>A scalar gamepad axis.</summary>
    GamepadAxis = 3,

    /// <summary>A two-dimensional gamepad stick.</summary>
    GamepadStick = 4,

    /// <summary>A gamepad trigger.</summary>
    GamepadTrigger = 5,
}
