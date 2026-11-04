// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using TerraFX;

namespace TerraFX.Entities;

/// <summary>Reports a frame request refused before admission because the World's owner-control queue is full.</summary>
[SuppressMessage(
    "Design",
    "CA1032:Implement standard exception constructors",
    Justification = "This exception is issued only by an exact hosted World request and requires its immutable diagnostic context.")]
public sealed class WorldOwnerQueueFullException : InvalidOperationException
{
    internal WorldOwnerQueueFullException(
        Diagnostic diagnostic,
        Guid worldGuid,
        int dimension,
        int pendingCount,
        int pendingLimit,
        long nextOwnerRequestSequence,
        long acceptedOwnerRequests,
        long nextControlSequence)
        : base("The World owner-control queue is full.")
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        ArgumentOutOfRangeException.ThrowIfEqual(worldGuid, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimension);
        ArgumentOutOfRangeException.ThrowIfNegative(pendingCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pendingLimit);
        ArgumentOutOfRangeException.ThrowIfLessThan(nextOwnerRequestSequence, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(acceptedOwnerRequests);
        ArgumentOutOfRangeException.ThrowIfLessThan(nextControlSequence, 1);

        Diagnostic = diagnostic;
        WorldGuid = worldGuid;
        Dimension = dimension;
        PendingCount = pendingCount;
        PendingLimit = pendingLimit;
        NextOwnerRequestSequence = nextOwnerRequestSequence;
        AcceptedOwnerRequests = acceptedOwnerRequests;
        NextControlSequence = nextControlSequence;
    }

    /// <summary>Gets the number of owner requests admitted before this refusal.</summary>
    public long AcceptedOwnerRequests { get; }

    /// <summary>Gets the stable diagnostic describing queue-capacity refusal.</summary>
    public Diagnostic Diagnostic { get; }

    /// <summary>Gets the requested frame dimension (2 or 3).</summary>
    public int Dimension { get; }

    /// <summary>Gets the next World control sequence.</summary>
    public long NextControlSequence { get; }

    /// <summary>Gets the next World owner-request sequence.</summary>
    public long NextOwnerRequestSequence { get; }

    /// <summary>Gets the number of requests pending at refusal.</summary>
    public int PendingCount { get; }

    /// <summary>Gets the configured pending-request limit.</summary>
    public int PendingLimit { get; }

    /// <summary>Gets the stable identity of the targeted World.</summary>
    public Guid WorldGuid { get; }
}
