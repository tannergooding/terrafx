// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Input;

/// <summary>Defines the source and processor for one gamepad trigger.</summary>
public sealed class GamepadTriggerSource
{
    /// <summary>Initializes a gamepad trigger source.</summary>
    /// <param name="playerSlot">The assigned player slot.</param>
    /// <param name="trigger">The normalized trigger.</param>
    /// <param name="deadzone">The trigger deadzone.</param>
    public GamepadTriggerSource(ushort playerSlot, GamepadTrigger trigger, float deadzone = 0.05f)
    {
        PlayerSlot = playerSlot;
        Trigger = trigger;
        Deadzone = deadzone;
    }

    /// <summary>Gets the trigger deadzone.</summary>
    public float Deadzone { get; }

    /// <summary>Gets the assigned player slot.</summary>
    public ushort PlayerSlot { get; }

    /// <summary>Gets the normalized trigger.</summary>
    public GamepadTrigger Trigger { get; }
}
