// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

internal sealed class WorldPersistenceCursorContents
{
    internal WorldPersistenceCursorContents(
        long nextControlSequence,
        long? nextOwnerRequestSequence,
        long? nextBarrierEpoch,
        long? acceptedOwnerRequests,
        int pendingCommandCount,
        int pendingOwnerRequestCount,
        int pendingWorldEventCount)
    {
        NextControlSequence = nextControlSequence;
        NextOwnerRequestSequence = nextOwnerRequestSequence;
        NextBarrierEpoch = nextBarrierEpoch;
        AcceptedOwnerRequests = acceptedOwnerRequests;
        PendingCommandCount = pendingCommandCount;
        PendingOwnerRequestCount = pendingOwnerRequestCount;
        PendingWorldEventCount = pendingWorldEventCount;
    }

    internal long? AcceptedOwnerRequests { get; }

    internal long? NextBarrierEpoch { get; }

    internal long NextControlSequence { get; }

    internal long? NextOwnerRequestSequence { get; }

    internal int PendingCommandCount { get; }

    internal int PendingOwnerRequestCount { get; }

    internal int PendingWorldEventCount { get; }
}
