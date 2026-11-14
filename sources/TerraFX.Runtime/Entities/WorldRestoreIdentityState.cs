// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;

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
        WorldPersistenceIdentityHistoryReader.ValidatedLineageMembership lineageMembership,
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
        LineageMembership = lineageMembership;
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

    internal WorldPersistenceIdentityHistoryReader.ValidatedLineageMembership LineageMembership { get; }

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

        if (identityBranchId == Guid.Empty)
        {
            throw new ArgumentException("Branch restore requires a nonempty identity branch.", nameof(identityBranchId));
        }

        var activeBranchIndex = -1;

        for (var index = 0; index < history.Branches.Length; index++)
        {
            if (history.Branches[index].BranchId == identityBranchId)
            {
                throw new ArgumentException("The selected identity branch is already present in the source history.", nameof(identityBranchId));
            }

            if (history.Branches[index].BranchId == history.ActiveBranchId)
            {
                activeBranchIndex = index;
            }
        }

        if (activeBranchIndex < 0)
        {
            throw new InvalidOperationException("The validated identity history lost its active branch.");
        }

        if (history.Branches.Length >= configuration.MaximumIdentityBranches)
        {
            throw new InvalidOperationException("The target World identity branch limit would be exceeded.");
        }

        var targetBranchId = identityBranchId;
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
        var entitySlotsByLocator = new (uint Locator, uint Ordinal)[history.LiveEntityCount];
        var sceneInstancesByLocator = new (uint Locator, uint Ordinal)[history.LiveSceneInstanceCount];
        var entitySlotCount = 0;
        var sceneInstanceSlotCount = 0;
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
                entitySlotsByLocator[entitySlotCount++] = (nextEntitySlot, assignment.AssignmentOrdinal);
                nextEntitySlot = checked(nextEntitySlot + 1);
            }
            else
            {
                locatorsByOrdinal[index] = nextSceneInstanceSlot;
                sceneInstancesByLocator[sceneInstanceSlotCount++] = (nextSceneInstanceSlot, assignment.AssignmentOrdinal);
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
            entitySlotsByLocator,
            sceneInstancesByLocator);
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
            history.LineageMembership,
            identityLedger,
            entitySlots,
            branches,
            activeBranchIndex,
            locatorsByOrdinal,
            entitySlotsByLocator,
            sceneInstancesByLocator);
    }

    internal static long MeasureBranchOwnedByteReservation(
        ValidatedWorldIdentityHistory history,
        Guid identityBranchId,
        scoped ReadOnlySpan<byte> sourceCheckpointDigest,
        WorldPersistenceConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentOutOfRangeException.ThrowIfEqual(identityBranchId, Guid.Empty);

        if (sourceCheckpointDigest.Length != 32)
        {
            throw new ArgumentException("A restore branch requires the exact 32-byte source checkpoint digest.", nameof(sourceCheckpointDigest));
        }

        var branchCount = checked(history.Branches.Length + 1);
        var bytes = checked(
            256L
            + StableIdentityLedger.GetRestoredByteReservation(
                history.AssignmentsByOrdinal.Length,
                history.LiveEntityCount,
                history.LiveSceneInstanceCount)
            + EntitySlotTable.GetStandardRestoreByteReservation(history.LiveEntityCount)
            + ArrayBytes(branchCount, Unsafe.SizeOf<World.WorldIdentityBranchRecord>())
            + ArrayBytes(history.AssignmentsByOrdinal.Length, sizeof(uint))
            + ArrayBytes(history.LiveEntityCount, Unsafe.SizeOf<(uint Locator, uint Ordinal)>())
            + ArrayBytes(history.LiveSceneInstanceCount, Unsafe.SizeOf<(uint Locator, uint Ordinal)>())
            + ArrayBytes(sourceCheckpointDigest.Length, sizeof(byte)));

        foreach (var branch in history.Branches)
        {
            if (!branch.SourceCheckpointDigest.IsEmpty)
            {
                bytes = checked(bytes + ArrayBytes(branch.SourceCheckpointDigest.Length, sizeof(byte)));
            }

            if (!branch.ForkSourceRecord.IsEmpty)
            {
                bytes = checked(bytes + ArrayBytes(branch.ForkSourceRecord.Length, sizeof(byte)));
            }
        }

        return bytes;
    }

    private static long ArrayBytes(long count, int elementSize)
    {
        if (count == 0)
        {
            return 0;
        }

        var payloadBytes = checked(count * elementSize);
        return checked(64L + ((payloadBytes + 15L) & ~15L));
    }
}
