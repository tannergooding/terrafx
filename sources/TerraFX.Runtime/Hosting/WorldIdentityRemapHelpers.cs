// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;
using TerraFX.Entities;

namespace TerraFX.Hosting;

internal static class WorldIdentityRemapHelpers
{
    internal static WorldIdentityRemapEntry[] CreateMappings(
        scoped ReadOnlySpan<Guid> sourceIds,
        scoped ReadOnlySpan<Guid> targetIds)
    {
        var mappings = new WorldIdentityRemapEntry[sourceIds.Length];

        for (var index = 0; index < mappings.Length; index++)
        {
            mappings[index] = new WorldIdentityRemapEntry(sourceIds[index], targetIds[index]);
        }

        return mappings;
    }

    internal static int MeasureHistoryMappingCount(
        scoped ReadOnlySpan<WorldPersistenceIdentityScratchEntry> sourceHistory,
        byte identityKind)
    {
        ValidateIdentityKind(identityKind);
        ValidateHistoryOrder(sourceHistory, nameof(sourceHistory));
        var mappingCount = 0;

        foreach (var entry in sourceHistory)
        {
            if (entry.IsLive && (entry.Kind == identityKind))
            {
                mappingCount = checked(mappingCount + 1);
            }
        }

        return mappingCount;
    }

    internal static long MeasureRetainedBytes(int entityCount, int sceneInstanceCount)
    {
        var targetIdentityCount = checked(entityCount + sceneInstanceCount);
        return checked(
            ArrayCharge(entityCount, Unsafe.SizeOf<WorldIdentityRemapEntry>())
            + ArrayCharge(sceneInstanceCount, Unsafe.SizeOf<WorldIdentityRemapEntry>())
            + ArrayCharge(targetIdentityCount, Unsafe.SizeOf<Guid>())
            + ArrayCharge(32, sizeof(byte))
            + ArrayCharge(32, sizeof(byte)));
    }

    internal static bool TryMap(
        scoped ReadOnlySpan<WorldIdentityRemapEntry> entries,
        Guid sourceId,
        out Guid targetId)
    {
        var low = 0;
        var high = entries.Length - 1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var entry = entries[middle];
            var comparison = entry.SourceId.CompareTo(sourceId);

            if (comparison == 0)
            {
                targetId = entry.TargetId;
                return true;
            }

            if (comparison < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        targetId = Guid.Empty;
        return false;
    }

    internal static void ValidateDisjointSourceIds(
        scoped ReadOnlySpan<Guid> entityIds,
        scoped ReadOnlySpan<Guid> sceneInstanceIds)
    {
        var entityIndex = 0;
        var sceneIndex = 0;

        while ((entityIndex < entityIds.Length) && (sceneIndex < sceneInstanceIds.Length))
        {
            var comparison = entityIds[entityIndex].CompareTo(sceneInstanceIds[sceneIndex]);

            if (comparison == 0)
            {
                throw new ArgumentException("Source Entity and SceneInstance remap identities must be unique.");
            }

            if (comparison < 0)
            {
                entityIndex++;
            }
            else
            {
                sceneIndex++;
            }
        }
    }

    internal static void ValidateDisjointSourceIds(
        scoped ReadOnlySpan<WorldIdentityRemapEntry> entityMappings,
        scoped ReadOnlySpan<WorldIdentityRemapEntry> sceneMappings)
    {
        var entityIndex = 0;
        var sceneIndex = 0;

        while ((entityIndex < entityMappings.Length) && (sceneIndex < sceneMappings.Length))
        {
            var comparison = entityMappings[entityIndex].SourceId.CompareTo(sceneMappings[sceneIndex].SourceId);

            if (comparison == 0)
            {
                throw new ArgumentException("Source Entity and SceneInstance remap identities must be unique.");
            }

            if (comparison < 0)
            {
                entityIndex++;
            }
            else
            {
                sceneIndex++;
            }
        }
    }

    internal static void ValidateMappings(
        scoped ReadOnlySpan<Guid> sourceIds,
        scoped ReadOnlySpan<Guid> targetIds,
        string parameterName)
    {
        if (sourceIds.Length != targetIds.Length)
        {
            throw new ArgumentException("Source and target remap vectors must have equal lengths.", parameterName);
        }

        for (var index = 0; index < sourceIds.Length; index++)
        {
            if ((sourceIds[index] == Guid.Empty)
                || (targetIds[index] == Guid.Empty)
                || ((index > 0) && (sourceIds[index - 1].CompareTo(sourceIds[index]) >= 0)))
            {
                throw new ArgumentException("Remap source IDs must be nonempty, sorted, and unique; target IDs must be nonempty.", parameterName);
            }
        }
    }

    internal static void WriteHistoryMappings(
        scoped ReadOnlySpan<WorldPersistenceIdentityScratchEntry> sourceHistory,
        scoped ReadOnlySpan<WorldPersistenceIdentityScratchEntry> targetHistory,
        Guid sourceWorldGuid,
        Guid targetWorldGuid,
        byte identityKind,
        scoped Span<WorldIdentityRemapEntry> destination)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sourceWorldGuid, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(targetWorldGuid, Guid.Empty);
        ValidateIdentityKind(identityKind);
        ValidateHistoryOrder(sourceHistory, nameof(sourceHistory));
        ValidateHistoryOrder(targetHistory, nameof(targetHistory));

        if (sourceHistory.Length != targetHistory.Length)
        {
            throw new ArgumentException("Source and target identity histories must preserve the complete assignment ordinal set.");
        }

        var requiredCount = MeasureHistoryMappingCount(sourceHistory, identityKind);

        if (destination.Length != requiredCount)
        {
            throw new ArgumentException("The identity remap destination must exactly match the live source identity count.", nameof(destination));
        }

        var written = 0;

        foreach (var source in sourceHistory)
        {
            var targetIdentity = (sourceWorldGuid == targetWorldGuid)
                ? source.StableId
                : StableIdentityLedger.CreateAutomaticIdentity(
                    targetWorldGuid,
                    (IdentityKind)source.Kind,
                    source.AssignmentOrdinal);
            var targetIndex = WorldPersistenceIdentityScratchEntry.FindIndex(targetHistory, targetIdentity);

            if ((targetIndex < 0)
                || (targetHistory[targetIndex].AssignmentOrdinal != source.AssignmentOrdinal)
                || (targetHistory[targetIndex].Kind != source.Kind)
                || (targetHistory[targetIndex].IsLive != source.IsLive))
            {
                throw new ArgumentException("The target identity history does not preserve the validated source assignment transformation.", nameof(targetHistory));
            }

            if (source.IsLive && (source.Kind == identityKind))
            {
                destination[written++] = new WorldIdentityRemapEntry(source.StableId, targetIdentity);
            }
        }

        destination.Sort();

        for (var index = 1; index < destination.Length; index++)
        {
            if (destination[index - 1].SourceId == destination[index].SourceId)
            {
                throw new ArgumentException("The source identity history contains a duplicate live identity.", nameof(sourceHistory));
            }
        }
    }

    private static long ArrayCharge(int capacity, int stride)
    {
        var payloadBytes = checked((long)capacity * stride);
        var roundedBytes = checked((payloadBytes + 15L) & ~15L);
        return checked(64L + roundedBytes);
    }

    private static void ValidateHistoryOrder(
        scoped ReadOnlySpan<WorldPersistenceIdentityScratchEntry> history,
        string parameterName)
    {
        for (var index = 0; index < history.Length; index++)
        {
            var entry = history[index];

            if ((entry.AssignmentOrdinal == 0)
                || (entry.AssignmentOrdinal > (uint)history.Length)
                || (entry.StableId == Guid.Empty)
                || (entry.Kind is not 1 and not 2))
            {
                throw new ArgumentException("The identity history contains an invalid assignment row.", parameterName);
            }

            if ((index > 0) && (history[index - 1].StableId.CompareTo(entry.StableId) >= 0))
            {
                throw new ArgumentException("Identity history must be strictly sorted by stable identity.", parameterName);
            }
        }
    }

    private static void ValidateIdentityKind(byte identityKind)
    {
        if (identityKind is not 1 and not 2)
        {
            throw new ArgumentOutOfRangeException(nameof(identityKind));
        }
    }
}
