// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Input;

/// <summary>Reports the ordering and outcome of one recipient-scoped input-control operation.</summary>
public sealed class InputRoutingReceipt
{
    internal InputRoutingReceipt(
        InputRoutingStatus status,
        uint recipientId,
        long? hostSequence,
        long? assignedTick,
        ValidationIssue? rejection,
        bool late = false)
    {
        AssignedTick = assignedTick;
        HostSequence = hostSequence;
        Late = late;
        RecipientId = recipientId;
        Rejection = rejection;
        Status = status;
    }

    /// <summary>Gets the simulation tick, when the operation has a timeline assignment.</summary>
    public long? AssignedTick { get; }

    /// <summary>Gets the process-wide host sequence assigned to an admitted operation.</summary>
    public long? HostSequence { get; }

    /// <summary>Gets whether the recipient mapped this captured event to its next open tick because its eligible tick had already passed.</summary>
    public bool Late { get; }

    /// <summary>Gets the recipient that owned the operation.</summary>
    public uint RecipientId { get; }

    /// <summary>Gets the stable rejection reason, when the operation was not applied.</summary>
    public ValidationIssue? Rejection { get; }

    /// <summary>Gets the operation outcome.</summary>
    public InputRoutingStatus Status { get; }
}
