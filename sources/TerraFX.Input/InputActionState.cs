// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Input;

/// <summary>Stores one immutable action result for a recipient snapshot.</summary>
public sealed class InputActionState
{
    internal InputActionState(
        Guid mapId,
        Guid actionId,
        long mapRevision,
        InputActionKind kind,
        bool isDownAtEnd,
        bool pressedThisTick,
        bool releasedThisTick,
        bool valueChangedThisTick,
        float axisX,
        float axisY,
        ImmutableArray<InputActionTransition> transitions)
    {
        ActionId = actionId;
        AxisX = axisX;
        AxisY = axisY;
        IsDownAtEnd = isDownAtEnd;
        Kind = kind;
        MapId = mapId;
        MapRevision = mapRevision;
        PressedThisTick = pressedThisTick;
        ReleasedThisTick = releasedThisTick;
        Transitions = transitions;
        ValueChangedThisTick = valueChangedThisTick;
    }

    /// <summary>Gets the action identifier.</summary>
    public Guid ActionId { get; }

    /// <summary>Gets the effective axis X component.</summary>
    public float AxisX { get; }

    /// <summary>Gets the effective axis Y component.</summary>
    public float AxisY { get; }

    /// <summary>Gets a value indicating whether the action is down after the snapshot's input.</summary>
    public bool IsDownAtEnd { get; }

    /// <summary>Gets the action kind.</summary>
    public InputActionKind Kind { get; }

    /// <summary>Gets the action map identifier.</summary>
    public Guid MapId { get; }

    /// <summary>Gets the action map revision that produced this state.</summary>
    public long MapRevision { get; }

    /// <summary>Gets whether a press occurred during this tick.</summary>
    public bool PressedThisTick { get; }

    /// <summary>Gets whether a release occurred during this tick.</summary>
    public bool ReleasedThisTick { get; }

    /// <summary>Gets the ordered transitions for this action in the snapshot interval.</summary>
    public ImmutableArray<InputActionTransition> Transitions { get; }

    /// <summary>Gets whether the effective axis value changed during this tick.</summary>
    public bool ValueChangedThisTick { get; }
}
