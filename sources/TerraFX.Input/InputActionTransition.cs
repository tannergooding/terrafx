// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Input;

/// <summary>Describes one ordered derived action transition.</summary>
public sealed class InputActionTransition
{
    internal InputActionTransition(
        Guid mapId,
        Guid actionId,
        InputTransitionKind kind,
        InputCancellationReason? cancellationReason,
        long hostSequence,
        long? assignedTick,
        float axisX,
        float axisY)
    {
        ActionId = actionId;
        AssignedTick = assignedTick;
        AxisX = axisX;
        AxisY = axisY;
        CancellationReason = cancellationReason;
        HostSequence = hostSequence;
        Kind = kind;
        MapId = mapId;
    }

    /// <summary>Gets the action identifier.</summary>
    public Guid ActionId { get; }

    /// <summary>Gets the recipient-assigned simulation tick, when present.</summary>
    public long? AssignedTick { get; }

    /// <summary>Gets the X component associated with the transition.</summary>
    public float AxisX { get; }

    /// <summary>Gets the Y component associated with the transition.</summary>
    public float AxisY { get; }

    /// <summary>Gets the cancellation reason for a canceled transition.</summary>
    public InputCancellationReason? CancellationReason { get; }

    /// <summary>Gets the process-wide capture/control sequence that caused the transition.</summary>
    public long HostSequence { get; }

    /// <summary>Gets the transition kind.</summary>
    public InputTransitionKind Kind { get; }

    /// <summary>Gets the action map identifier.</summary>
    public Guid MapId { get; }
}
