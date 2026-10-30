// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Input;

/// <summary>Identifies a standardized gamepad button.</summary>
public enum GamepadButtonValue
{
    /// <summary>The south face button.</summary>
    South = 0,

    /// <summary>The east face button.</summary>
    East = 1,

    /// <summary>The west face button.</summary>
    West = 2,

    /// <summary>The north face button.</summary>
    North = 3,

    /// <summary>The up direction on the directional pad.</summary>
    DpadUp = 4,

    /// <summary>The down direction on the directional pad.</summary>
    DpadDown = 5,

    /// <summary>The left direction on the directional pad.</summary>
    DpadLeft = 6,

    /// <summary>The right direction on the directional pad.</summary>
    DpadRight = 7,

    /// <summary>The menu button.</summary>
    Menu = 8,

    /// <summary>The start button.</summary>
    Start = 9,

    /// <summary>The system button.</summary>
    System = 10,

    /// <summary>The left bumper.</summary>
    LeftBumper = 11,

    /// <summary>The right bumper.</summary>
    RightBumper = 12,
}
