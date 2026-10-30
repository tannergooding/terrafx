// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Input;

/// <summary>Defines one tagged binding and its kind-specific input fields.</summary>
public sealed class BindingDefinition
{
    /// <summary>Initializes the common binding fields.</summary>
    /// <param name="bindingId">The stable binding identifier.</param>
    /// <param name="actionId">The referenced action identifier.</param>
    /// <param name="contextMask">The nonzero set of eligible context bits.</param>
    /// <param name="kind">The binding source schema.</param>
    public BindingDefinition(Guid bindingId, Guid actionId, ulong contextMask, BindingKind kind)
    {
        BindingId = bindingId;
        ActionId = actionId;
        ContextMask = contextMask;
        Kind = kind;
        Controls = [];
    }

    /// <summary>Gets the action identifier.</summary>
    public Guid ActionId { get; }

    /// <summary>Gets the scalar source for a gamepad-axis binding.</summary>
    public GamepadAxis1Source? Axis1Source { get; init; }

    /// <summary>Gets the binding identifier.</summary>
    public Guid BindingId { get; }

    /// <summary>Gets the chord completion window in milliseconds.</summary>
    public int? CompletionWindowMilliseconds { get; init; }

    /// <summary>Gets the nonzero set of eligible context bits.</summary>
    public ulong ContextMask { get; }

    /// <summary>Gets the button control for a Button binding.</summary>
    public DigitalControl? Control { get; init; }

    /// <summary>Gets the ordered controls for a Chord binding.</summary>
    public ImmutableArray<DigitalControl> Controls { get; init; }

    /// <summary>Gets the down keyboard control for an Axis2Keys binding.</summary>
    public KeyboardControl? Down { get; init; }

    /// <summary>Gets whether this binding is enabled.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Gets the binding behavior flags.</summary>
    public InputBindingBehavior Flags { get; init; } = InputBindingBehavior.None;

    /// <summary>Gets the binding source kind.</summary>
    public BindingKind Kind { get; }

    /// <summary>Gets the left keyboard control for an Axis2Keys binding.</summary>
    public KeyboardControl? Left { get; init; }

    /// <summary>Gets the negative keyboard control for an Axis1Keys binding.</summary>
    public KeyboardControl? Negative { get; init; }

    /// <summary>Gets the positive keyboard control for an Axis1Keys binding.</summary>
    public KeyboardControl? Positive { get; init; }

    /// <summary>Gets the press threshold for an AxisThreshold binding.</summary>
    public float? PressThreshold { get; init; }

    /// <summary>Gets the binding priority.</summary>
    public int Priority { get; init; }

    /// <summary>Gets the release threshold for an AxisThreshold binding.</summary>
    public float? ReleaseThreshold { get; init; }

    /// <summary>Gets the right keyboard control for an Axis2Keys binding.</summary>
    public KeyboardControl? Right { get; init; }

    /// <summary>Gets the gamepad stick source for a GamepadStick2 binding.</summary>
    public GamepadStick2Source? Stick2Source { get; init; }

    /// <summary>Gets the action-threshold source, when this is an axis-threshold binding.</summary>
    public GamepadAxis1Source? ThresholdAxisSource { get; init; }

    /// <summary>Gets the direction for an axis-threshold binding.</summary>
    public AxisThresholdDirection? ThresholdDirection { get; init; }

    /// <summary>Gets the trigger-threshold source, when this is an axis-threshold binding.</summary>
    public GamepadTriggerSource? ThresholdTriggerSource { get; init; }

    /// <summary>Gets the gamepad trigger source for a GamepadTrigger binding.</summary>
    public GamepadTriggerSource? TriggerSource { get; init; }

    /// <summary>Gets the up keyboard control for an Axis2Keys binding.</summary>
    public KeyboardControl? Up { get; init; }
}
