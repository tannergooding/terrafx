// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace TerraFX.Entities;

internal sealed class WorldRestoreIdentityState
{
    private WorldRestoreIdentityState(
        Guid worldGuid,
        Guid replayLineageId,
        Guid activeBranchId,
        Guid issuanceNamespace,
        long nextAssignmentOrdinal,
        long nextTick,
        StableIdentityLedger identityLedger,
        EntitySlotTable entitySlots,
        World.WorldIdentityBranchRecord[] branches,
        int activeBranchIndex,
        uint[] locatorsByOrdinal,
        (uint Locator, uint Ordinal)[] entitySlotsByLocator,
        (uint Locator, uint Ordinal)[] sceneInstancesByLocator)
    {
        WorldGuid = worldGuid;
        ReplayLineageId = replayLineageId;
        ActiveBranchId = activeBranchId;
        IssuanceNamespace = issuanceNamespace;
        NextAssignmentOrdinal = nextAssignmentOrdinal;
        NextTick = nextTick;
        IdentityLedger = identityLedger;
        EntitySlots = entitySlots;
        Branches = branches;
        ActiveBranchIndex = activeBranchIndex;
        LocatorsByOrdinal = locatorsByOrdinal;
        EntitySlotsByLocator = entitySlotsByLocator;
        SceneInstancesByLocator = sceneInstancesByLocator;
    }

    internal Guid ActiveBranchId { get; }

    internal int ActiveBranchIndex { get; }

    internal World.WorldIdentityBranchRecord[] Branches { get; }

    internal EntitySlotTable EntitySlots { get; }

    internal (uint Locator, uint Ordinal)[] EntitySlotsByLocator { get; }

    internal StableIdentityLedger IdentityLedger { get; }

    internal Guid IssuanceNamespace { get; }

    internal uint[] LocatorsByOrdinal { get; }

    internal long NextAssignmentOrdinal { get; }

    internal long NextTick { get; }

    internal Guid ReplayLineageId { get; }

    internal (uint Locator, uint Ordinal)[] SceneInstancesByLocator { get; }

    internal Guid WorldGuid { get; }

    internal static WorldRestoreIdentityState CreateBranch(
        ValidatedWorldIdentityHistory history,
        Guid identityBranchId,
        scoped ReadOnlySpan<byte> sourceCheckpointDigest,
        long nextTick,
        bool isStrictLocal,
        WorldPersistenceConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(configuration);

        if (sourceCheckpointDigest.Length != 32)
        {
            throw new ArgumentException("A restore branch requires the exact 32-byte source checkpoint digest.", nameof(sourceCheckpointDigest));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(nextTick, 1);

        if (isStrictLocal)
        {
            throw new NotSupportedException("StrictLocal restore requires exact slot, chunk, and free-list reconstruction.");
        }

        var targetBranchId = history.ActiveBranchId;

        if (identityBranchId == Guid.Empty)
        {
            throw new ArgumentException("Branch restore requires a nonempty identity branch.", nameof(identityBranchId));
        }

        var activeBranchIndex = -1;

        for (var index = 0; index < history.Branches.Length; index++)
        {
            if (history.Branches[index].BranchId == history.ActiveBranchId)
            {
                activeBranchIndex = index;
                break;
            }
        }

        if (activeBranchIndex < 0)
        {
            throw new InvalidOperationException("The validated identity history lost its active branch.");
        }

        if (history.Branches.Any(branch => branch.BranchId == identityBranchId))
        {
            throw new ArgumentException("The selected identity branch is already present in the source history.", nameof(identityBranchId));
        }

        if (history.Branches.Length >= configuration.MaximumIdentityBranches)
        {
            throw new InvalidOperationException("The target World identity branch limit would be exceeded.");
        }

        targetBranchId = identityBranchId;
        activeBranchIndex = history.Branches.Length;
        var branches = new World.WorldIdentityBranchRecord[history.Branches.Length + 1];

        for (var index = 0; index < history.Branches.Length; index++)
        {
            var branch = history.Branches[index];

            branches[index] = new World.WorldIdentityBranchRecord(
                branch.BranchId,
                branch.IssuanceNamespace,
                branch.CreationLineageId,
                branch.ParentBranchId,
                branch.CreatedAtNextTick,
                branch.FirstAssignmentOrdinal,
                index,
                branch.SourceCheckpointDigest,
                branch.ForkSourceRecord);
        }

        branches[^1] = new World.WorldIdentityBranchRecord(
            targetBranchId,
            targetBranchId,
            history.ReplayLineageId,
            history.ActiveBranchId,
            createdAtNextTick: nextTick,
            checked((uint)history.NextAssignmentOrdinal),
            activeBranchIndex,
            sourceCheckpointDigest.ToArray());

        var locatorsByOrdinal = new uint[history.AssignmentsByOrdinal.Length];
        var entitySlotsByLocator = new List<(uint Locator, uint Ordinal)>(history.LiveEntityCount);
        var sceneInstancesByLocator = new List<(uint Locator, uint Ordinal)>(history.LiveSceneInstanceCount);
        uint nextEntitySlot = 1;
        uint nextSceneInstanceSlot = 1;

        for (var index = 0; index < history.AssignmentsByOrdinal.Length; index++)
        {
            var assignment = history.AssignmentsByOrdinal[index];

            if (!assignment.IsLive)
            {
                continue;
            }

            if (assignment.Kind is (byte)IdentityKind.Entity)
            {
                locatorsByOrdinal[index] = nextEntitySlot;
                entitySlotsByLocator.Add((nextEntitySlot, assignment.AssignmentOrdinal));
                nextEntitySlot = checked(nextEntitySlot + 1);
            }
            else
            {
                locatorsByOrdinal[index] = nextSceneInstanceSlot;
                sceneInstancesByLocator.Add((nextSceneInstanceSlot, assignment.AssignmentOrdinal));
                nextSceneInstanceSlot = checked(nextSceneInstanceSlot + 1);
            }
        }

        var identityLedger = StableIdentityLedger.CreateRestored(
            configuration.SchedulingLimits.MaximumOwnedStateBytes,
            configuration.MaximumIdentityAssignments,
            configuration.MaximumEntitySlots,
            configuration.MaximumActiveSceneInstances,
            branches.Length,
            history.AssignmentsByOrdinal.AsSpan(),
            locatorsByOrdinal,
            [.. entitySlotsByLocator],
            [.. sceneInstancesByLocator]);
        var entitySlots = EntitySlotTable.CreateStandardRestore(
            configuration.MaximumEntitySlots,
            history.LiveEntityCount);

        return new WorldRestoreIdentityState(
            history.WorldGuid,
            history.ReplayLineageId,
            targetBranchId,
            targetBranchId,
            history.NextAssignmentOrdinal,
            nextTick,
            identityLedger,
            entitySlots,
            branches,
            activeBranchIndex,
            locatorsByOrdinal,
            [.. entitySlotsByLocator],
            [.. sceneInstancesByLocator]);
    }
}
