// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Input;

/// <summary>Identifies one binding schema in an input action map.</summary>
public enum BindingKind
{
    /// <summary>A single digital control.</summary>
    Button = 0,

    /// <summary>A chord of two through four digital controls.</summary>
    Chord = 1,

    /// <summary>A signed scalar derived from two keyboard controls.</summary>
    Axis1Keys = 2,

    /// <summary>A two-dimensional vector derived from four keyboard controls.</summary>
    Axis2Keys = 3,

    /// <summary>A scalar derived from one gamepad axis.</summary>
    GamepadAxis1 = 4,

    /// <summary>A vector derived from one gamepad stick.</summary>
    GamepadStick2 = 5,

    /// <summary>A nonnegative scalar derived from one gamepad trigger.</summary>
    GamepadTrigger = 6,

    /// <summary>A digital state derived from a thresholded gamepad source.</summary>
    AxisThreshold = 7,
}
