// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal sealed class WorldPersistenceConfiguration
{
    internal WorldPersistenceConfiguration(
        int maximumLiveEntities,
        int maximumEntitySlots,
        int maximumArchetypes,
        int maximumDeferredCommands,
        int maximumComponentTypes,
        int maximumPendingOwnerRequests,
        int maximumRecordedControls,
        int maximumIdentityAssignments,
        WorldSchedulingLimits schedulingLimits,
        int maximumIdentityBranches,
        int maximumEntityReferencePositions,
        int maximumActiveSceneInstances,
        int maximumSceneOccurrences,
        long maximumSnapshotBytes,
        long maximumPersistenceAdmissionBytes)
    {
        ArgumentNullException.ThrowIfNull(schedulingLimits);
        MaximumLiveEntities = maximumLiveEntities;
        MaximumEntitySlots = maximumEntitySlots;
        MaximumArchetypes = maximumArchetypes;
        MaximumDeferredCommands = maximumDeferredCommands;
        MaximumComponentTypes = maximumComponentTypes;
        MaximumPendingOwnerRequests = maximumPendingOwnerRequests;
        MaximumRecordedControls = maximumRecordedControls;
        MaximumIdentityAssignments = maximumIdentityAssignments;
        SchedulingLimits = schedulingLimits;
        MaximumIdentityBranches = maximumIdentityBranches;
        MaximumEntityReferencePositions = maximumEntityReferencePositions;
        MaximumActiveSceneInstances = maximumActiveSceneInstances;
        MaximumSceneOccurrences = maximumSceneOccurrences;
        MaximumSnapshotBytes = maximumSnapshotBytes;
        MaximumPersistenceAdmissionBytes = maximumPersistenceAdmissionBytes;
    }

    internal int MaximumActiveSceneInstances { get; }

    internal int MaximumArchetypes { get; }

    internal int MaximumComponentTypes { get; }

    internal int MaximumDeferredCommands { get; }

    internal int MaximumEntityReferencePositions { get; }

    internal int MaximumEntitySlots { get; }

    internal int MaximumIdentityAssignments { get; }

    internal int MaximumIdentityBranches { get; }

    internal int MaximumLiveEntities { get; }

    internal int MaximumPendingOwnerRequests { get; }

    internal long MaximumPersistenceAdmissionBytes { get; }

    internal int MaximumRecordedControls { get; }

    internal int MaximumSceneOccurrences { get; }

    internal long MaximumSnapshotBytes { get; }

    internal WorldSchedulingLimits SchedulingLimits { get; }
}
