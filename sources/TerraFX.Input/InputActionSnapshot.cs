// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Collections.Immutable;

namespace TerraFX.Input;

/// <summary>Stores an immutable action-state snapshot for one input recipient.</summary>
public sealed class InputActionSnapshot
{
    internal InputActionSnapshot(
        InputRecipientKind recipientKind,
        uint recipientId,
        long? assignedTick,
        ImmutableArray<InputActionState> actions)
    {
        Actions = actions;
        AssignedTick = assignedTick;
        RecipientId = recipientId;
        RecipientKind = recipientKind;
    }

    /// <summary>Gets the ordered action states.</summary>
    public ImmutableArray<InputActionState> Actions { get; }

    /// <summary>Gets the simulation tick assigned to this snapshot, or null for host-clock state.</summary>
    public long? AssignedTick { get; }

    /// <summary>Gets the recipient identity.</summary>
    public uint RecipientId { get; }

    /// <summary>Gets the recipient clock kind.</summary>
    public InputRecipientKind RecipientKind { get; }
}
