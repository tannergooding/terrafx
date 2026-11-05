// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;

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

    private static long ArrayCharge(int capacity, int stride)
    {
        var payloadBytes = checked((long)capacity * stride);
        var roundedBytes = checked((payloadBytes + 15L) & ~15L);
        return checked(64L + roundedBytes);
    }
}
