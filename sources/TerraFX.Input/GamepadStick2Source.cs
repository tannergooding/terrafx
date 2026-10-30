// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Input;

/// <summary>Defines the source and processors for a gamepad stick.</summary>
public sealed class GamepadStick2Source
{
    /// <summary>Initializes a gamepad stick source.</summary>
    /// <param name="playerSlot">The assigned player slot.</param>
    /// <param name="stick">The normalized stick.</param>
    /// <param name="invertY">Whether to invert Y after radial deadzone processing.</param>
    /// <param name="radialDeadzone">The radial deadzone.</param>
    public GamepadStick2Source(ushort playerSlot, GamepadStick stick, bool invertY = false, float radialDeadzone = 0.15f)
    {
        PlayerSlot = playerSlot;
        Stick = stick;
        InvertY = invertY;
        RadialDeadzone = radialDeadzone;
    }

    /// <summary>Gets whether to invert Y after radial deadzone processing.</summary>
    public bool InvertY { get; }

    /// <summary>Gets the assigned player slot.</summary>
    public ushort PlayerSlot { get; }

    /// <summary>Gets the radial deadzone.</summary>
    public float RadialDeadzone { get; }

    /// <summary>Gets the normalized stick.</summary>
    public GamepadStick Stick { get; }
}
