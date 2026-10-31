// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Entities;

namespace TerraFX.Scenes;

internal readonly struct SceneExpansionLimits
{
    internal SceneExpansionLimits(
        int maximumExpandedEntities,
        int maximumPrefabEntities,
        int maximumExpandedOccurrences,
        int maximumPrefabDepth,
        int maximumHierarchyDepth,
        long maximumExpandedBytes,
        Func<long, WorldSceneStorageReservation?>? reserveStorage = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumExpandedEntities);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumExpandedEntities, 1_048_576);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPrefabEntities);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumPrefabEntities, 65_536);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumExpandedOccurrences);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumExpandedOccurrences, 1_048_576);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPrefabDepth);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumPrefabDepth, 32);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumHierarchyDepth);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumHierarchyDepth, 256);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumExpandedBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            maximumExpandedBytes,
            WorldSchedulingLimits.Default.MaximumOwnedStateBytes);

        MaximumExpandedEntities = maximumExpandedEntities;
        MaximumPrefabEntities = maximumPrefabEntities;
        MaximumExpandedOccurrences = maximumExpandedOccurrences;
        MaximumPrefabDepth = maximumPrefabDepth;
        MaximumHierarchyDepth = maximumHierarchyDepth;
        MaximumExpandedBytes = maximumExpandedBytes;
        ReserveStorage = reserveStorage;
    }

    internal static SceneExpansionLimits Default => new(1_048_576, 65_536, 1_048_576, 32, 256, WorldSchedulingLimits.Default.MaximumOwnedStateBytes);

    internal long MaximumExpandedBytes { get; }

    internal int MaximumExpandedEntities { get; }

    internal int MaximumExpandedOccurrences { get; }

    internal int MaximumHierarchyDepth { get; }

    internal int MaximumPrefabDepth { get; }

    internal int MaximumPrefabEntities { get; }

    internal Func<long, WorldSceneStorageReservation?>? ReserveStorage { get; }
}
