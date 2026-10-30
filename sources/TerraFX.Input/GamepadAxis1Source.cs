// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Input;

/// <summary>Defines the source and processors for a gamepad scalar axis.</summary>
public sealed class GamepadAxis1Source
{
    /// <summary>Initializes a gamepad scalar-axis source.</summary>
    /// <param name="playerSlot">The assigned player slot.</param>
    /// <param name="axis">The normalized axis.</param>
    /// <param name="invert">Whether to invert after deadzone processing.</param>
    /// <param name="deadzone">The scalar deadzone.</param>
    public GamepadAxis1Source(ushort playerSlot, GamepadAxis axis, bool invert = false, float deadzone = 0.15f)
    {
        PlayerSlot = playerSlot;
        Axis = axis;
        Invert = invert;
        Deadzone = deadzone;
    }

    /// <summary>Gets the normalized axis.</summary>
    public GamepadAxis Axis { get; }

    /// <summary>Gets the scalar deadzone.</summary>
    public float Deadzone { get; }

    /// <summary>Gets whether to invert after deadzone processing.</summary>
    public bool Invert { get; }

    /// <summary>Gets the assigned player slot.</summary>
    public ushort PlayerSlot { get; }
}
