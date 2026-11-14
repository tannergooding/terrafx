// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal sealed class WorldRestoreEcsPlan
{
    private readonly WorldRestoreEcsArchetypePlan?[] _archetypes;
    private readonly int[] _entityArchetypeIndices;

    private WorldRestoreEcsPlan(
        int entityCount,
        WorldRestoreEcsArchetypePlan?[] archetypes,
        int archetypeCount,
        int[] entityArchetypeIndices)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entityCount);
        ArgumentNullException.ThrowIfNull(archetypes);
        ArgumentOutOfRangeException.ThrowIfNegative(archetypeCount);
        ArgumentNullException.ThrowIfNull(entityArchetypeIndices);
        var hasEntities = entityCount != 0;
        var hasArchetypes = archetypeCount != 0;

        if ((archetypeCount > archetypes.Length)
            || (entityArchetypeIndices.Length != entityCount)
            || (hasEntities != hasArchetypes))
        {
            throw new ArgumentException("The restored ECS archetype and Entity maps have inconsistent extents.");
        }

        for (var archetypeIndex = 0; archetypeIndex < archetypeCount; archetypeIndex++)
        {
            ArgumentNullException.ThrowIfNull(archetypes[archetypeIndex]);
        }

        var totalArchetypeEntities = 0;

        for (var entityIndex = 0; entityIndex < entityArchetypeIndices.Length; entityIndex++)
        {
            var archetypeIndex = entityArchetypeIndices[entityIndex];

            if ((uint)archetypeIndex >= (uint)archetypeCount)
            {
                throw new ArgumentOutOfRangeException(nameof(entityArchetypeIndices));
            }
        }

        for (var archetypeIndex = 0; archetypeIndex < archetypeCount; archetypeIndex++)
        {
            var archetype = archetypes[archetypeIndex]!;

            if (archetype.EntityCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(archetypes));
            }

            totalArchetypeEntities = checked(totalArchetypeEntities + archetype.EntityCount);
        }

        if (totalArchetypeEntities != entityCount)
        {
            throw new ArgumentException("Restored ECS archetype counts do not match the Entity row map.", nameof(archetypes));
        }

        EntityCount = entityCount;
        ArchetypeCount = archetypeCount;
        _archetypes = archetypes;
        _entityArchetypeIndices = entityArchetypeIndices;
    }

    internal int ArchetypeCount { get; }

    internal int EntityCount { get; }

    internal bool HasArchetypes
    {
        get
        {
            for (var archetypeIndex = 0; archetypeIndex < ArchetypeCount; archetypeIndex++)
            {
                var archetype = _archetypes[archetypeIndex]!;

                if (!archetype.Components.IsEmpty || !archetype.Tags.IsEmpty)
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal long OwnedByteReservation
    {
        get
        {
            var bytes = checked(
                64L
                + ArrayBytes(_archetypes.Length, IntPtr.Size)
                + ArrayBytes(_entityArchetypeIndices.Length, sizeof(int)));

            for (var archetypeIndex = 0; archetypeIndex < ArchetypeCount; archetypeIndex++)
            {
                bytes = checked(bytes + _archetypes[archetypeIndex]!.OwnedByteReservation);
            }

            return bytes;
        }
    }

    internal static WorldRestoreEcsPlan Create(
        int entityCount,
        WorldRestoreEcsArchetypePlan?[] archetypes,
        int archetypeCount,
        int[] entityArchetypeIndices)
        => new(entityCount, archetypes, archetypeCount, entityArchetypeIndices);

    internal static WorldRestoreEcsPlan CreateEmpty(int entityCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entityCount);

        if (entityCount == 0)
        {
            return new WorldRestoreEcsPlan(0, [], 0, []);
        }

        var archetypes = new WorldRestoreEcsArchetypePlan?[] {
            WorldRestoreEcsArchetypePlan.CreateEmpty(entityCount),
        };
        return new WorldRestoreEcsPlan(entityCount, archetypes, 1, new int[entityCount]);
    }

    internal static int GetArchetypeHashTableLength(int archetypeCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(archetypeCapacity);
        var requiredCapacity = checked(Math.Max(1, archetypeCapacity) * 2);
        var capacity = 1;

        while (capacity < requiredCapacity)
        {
            capacity = checked(capacity * 2);
        }

        return capacity;
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

    internal WorldRestoreEcsArchetypePlan GetArchetype(int archetypeIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(archetypeIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(archetypeIndex, ArchetypeCount);
        return _archetypes[archetypeIndex]
            ?? throw new InvalidOperationException("The restore ECS plan omitted an archetype.");
    }

    internal int GetEntityArchetypeIndex(int entityIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entityIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(entityIndex, EntityCount);
        return _entityArchetypeIndices[entityIndex];
    }
}
