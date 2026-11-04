// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

internal readonly struct WorldOwnerControlQueueSnapshot
{
    internal WorldOwnerControlQueueSnapshot(
        int pendingCount,
        int pendingLimit,
        long nextOwnerRequestSequence,
        long acceptedOwnerRequests,
        long nextControlSequence)
    {
        PendingCount = pendingCount;
        PendingLimit = pendingLimit;
        NextOwnerRequestSequence = nextOwnerRequestSequence;
        AcceptedOwnerRequests = acceptedOwnerRequests;
        NextControlSequence = nextControlSequence;
    }

    internal long AcceptedOwnerRequests { get; }

    internal long NextControlSequence { get; }

    internal long NextOwnerRequestSequence { get; }

    internal int PendingCount { get; }

    internal int PendingLimit { get; }
}
