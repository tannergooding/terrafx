// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using TerraFX;
using TerraFX.Assets;

namespace TerraFX.Scenes;

internal static class ScenePrefabExpansionPlanner
{
    internal static async Task<SceneExpansionPlan> PrepareAsync(
        AssetStore assets,
        AssetId sceneId,
        SceneExpansionLimits limits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assets);

        if (!sceneId.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(sceneId));
        }

        var builder = new SceneExpansionBuilder(assets, limits, cancellationToken);
        using (builder)
        {
            return await builder.BuildAsync(sceneId).ConfigureAwait(false);
        }
    }

    private sealed class SceneExpansionBuilder : IDisposable
    {
        private readonly List<AssetId> _assetPath = [];
        private readonly AssetStore _assets;
        private int _buildChildPlacementCursor;
        private int _buildEntityCursor;
        private int _buildEntityMapCursor;
        private int _buildOccurrenceCursor;
        private int _buildRootCursor;
        private readonly CancellationToken _cancellationToken;
        private ImmutableArray<SceneExpandedEntity>.Builder _entities = ImmutableArray.CreateBuilder<SceneExpandedEntity>();
        private int _entityCount;
        private ImmutableArray<int>.Builder _entityIndicesByOccurrence = ImmutableArray.CreateBuilder<int>();
        private readonly SceneExpansionLimits _limits;
        private int _occurrenceCount;
        private Guid[] _occurrenceIdentities = [];
        private ImmutableArray<SceneExpandedOccurrence>.Builder _occurrences = ImmutableArray.CreateBuilder<SceneExpandedOccurrence>();
        private int _placementOccurrenceCount;
        private Dictionary<AssetId, AssetLease<ScenePrefabAsset>>? _prefabLeases = [];
        private int _rootCountCursor;
        private int[] _rootCounts = [];
        private int _rootEntityIndexCount;
        private ImmutableArray<int>.Builder _rootEntityIndicesByOccurrence = ImmutableArray.CreateBuilder<int>();
        private AssetLease<SceneAsset>? _sceneLease;
        private long _totalComponentRecordBytes;

        internal SceneExpansionBuilder(
            AssetStore assets,
            SceneExpansionLimits limits,
            CancellationToken cancellationToken)
        {
            _assets = assets;
            _limits = limits;
            _cancellationToken = cancellationToken;
        }

        private static long ArrayBytes(int count, int elementSize)
        {
            var payloadBytes = checked((long)count * elementSize);
            return checked(64L + ((payloadBytes + 7L) & ~7L));
        }

        private static int FindEntityIndex(ImmutableArray<SceneEntity> entities, Guid entityId)
        {
            var low = 0;
            var high = entities.Length - 1;

            while (low <= high)
            {
                var middle = low + ((high - low) / 2);
                var comparison = FoundationCatalogue.CompareRfc(entities[middle].AuthoredEntityId, entityId);

                if (comparison == 0)
                {
                    return middle;
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

            return -1;
        }

        private static int FindHierarchyNodeIndex(ImmutableArray<SceneHierarchyNode> nodes, Guid entityId)
        {
            var low = 0;
            var high = nodes.Length - 1;

            while (low <= high)
            {
                var middle = low + ((high - low) / 2);
                var comparison = FoundationCatalogue.CompareRfc(nodes[middle].EntityId, entityId);

                if (comparison == 0)
                {
                    return middle;
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

            return -1;
        }

        private static int FindPlacementIndex(ImmutableArray<ScenePrefabPlacement> placements, Guid placementId)
        {
            var low = 0;
            var high = placements.Length - 1;

            while (low <= high)
            {
                var middle = low + ((high - low) / 2);
                var comparison = FoundationCatalogue.CompareRfc(placements[middle].PlacementId, placementId);

                if (comparison == 0)
                {
                    return middle;
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

            return -1;
        }

        public void Dispose()
        {
            var sceneLease = _sceneLease;
            _sceneLease = null;
            sceneLease?.Dispose();

            var prefabLeases = _prefabLeases;
            _prefabLeases = null;

            if (prefabLeases is not null)
            {
                foreach (var lease in prefabLeases.Values)
                {
                    lease.Dispose();
                }
            }

            _entities = ImmutableArray.CreateBuilder<SceneExpandedEntity>();
            _occurrences = ImmutableArray.CreateBuilder<SceneExpandedOccurrence>();
            _entityIndicesByOccurrence = ImmutableArray.CreateBuilder<int>();
            _rootEntityIndicesByOccurrence = ImmutableArray.CreateBuilder<int>();
            _rootCounts = [];
            _occurrenceIdentities = [];
        }

        internal async Task<SceneExpansionPlan> BuildAsync(AssetId sceneId)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var sceneLease = await _assets.LoadAsync<SceneAsset>(sceneId, _cancellationToken).ConfigureAwait(false);
            _sceneLease = sceneLease;
            _assetPath.Add(sceneId);
            var sceneSource = new SceneExpansionSource(sceneLease.Value);
            var rootCount = await CountOccurrenceAsync(
                sceneSource,
                prefabDepth: 0,
                baseHierarchyDepth: 0).ConfigureAwait(false);

            if (_placementOccurrenceCount != (_occurrenceCount - 1))
            {
                throw new InvalidDataException("Prefab placement membership does not match the expanded occurrence count.");
            }

            var requiredBytes = GetRequiredByteReservation();

            if (requiredBytes > _limits.MaximumExpandedBytes)
            {
                throw new InvalidOperationException("The Scene expansion exceeds its selected byte budget.");
            }

            _rootCounts = new int[_occurrenceCount];
            _rootCountCursor = 0;
            var populatedRootCount = await FillOccurrenceRootCountsAsync(
                sceneSource,
                prefabDepth: 0,
                baseHierarchyDepth: 0).ConfigureAwait(false);

            if ((populatedRootCount != rootCount) || (_rootCountCursor != _occurrenceCount))
            {
                throw new InvalidOperationException("Scene expansion root-count preflight was inconsistent.");
            }

            _entities = ImmutableArray.CreateBuilder<SceneExpandedEntity>(_entityCount);
            _occurrences = ImmutableArray.CreateBuilder<SceneExpandedOccurrence>(_occurrenceCount);
            _entityIndicesByOccurrence = ImmutableArray.CreateBuilder<int>(_entityCount);
            _rootEntityIndicesByOccurrence = ImmutableArray.CreateBuilder<int>(_rootEntityIndexCount);
            var childOccurrenceIndicesByPlacement = ImmutableArray.CreateBuilder<int>(_placementOccurrenceCount);
            _occurrenceIdentities = new Guid[_occurrenceCount];

            for (var index = 0; index < _entityCount; index++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                _entityIndicesByOccurrence.Add(-1);
            }

            for (var index = 0; index < _placementOccurrenceCount; index++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                childOccurrenceIndicesByPlacement.Add(-1);
            }

            for (var index = 0; index < _rootEntityIndexCount; index++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                _rootEntityIndicesByOccurrence.Add(-1);
            }

            _buildEntityCursor = 0;
            _buildEntityMapCursor = 0;
            _buildChildPlacementCursor = 0;
            _buildOccurrenceCursor = 0;
            _buildRootCursor = 0;

            var rootOccurrenceIndex = await ExpandOccurrenceAsync(
                sceneSource,
                prefabDepth: 0,
                baseHierarchyDepth: 0,
                parentOccurrenceIndex: -1,
                placementId: Guid.Empty,
                parentEntityIndex: -1,
                firstChildOrdinal: 0,
                childOccurrenceIndicesByPlacement).ConfigureAwait(false);

            if ((rootOccurrenceIndex != 0)
                || (_buildEntityCursor != _entityCount)
                || (_buildEntityMapCursor != _entityCount)
                || (_buildChildPlacementCursor != _placementOccurrenceCount)
                || (_buildOccurrenceCursor != _occurrenceCount)
                || (_buildRootCursor != _rootEntityIndexCount))
            {
                throw new InvalidOperationException("The Scene expansion result did not match its bounded preflight.");
            }

            var entityAssignmentKeys = new Guid[_entityCount];

            for (var index = 0; index < _entityCount; index++)
            {
                entityAssignmentKeys[index] = _entities[index].ExpandedEntityKey;
            }

            SceneOccurrenceIdentity.SortExpandedEntityKeysForAssignment(entityAssignmentKeys);
            SceneOccurrenceIdentity.SortOccurrenceIdsAndValidateUnique(_occurrenceIdentities);

            var prefabLeases = _prefabLeases
                ?? throw new InvalidOperationException("The Scene expansion lost its Prefab lease set.");
            var plan = new SceneExpansionPlan(
                sceneLease,
                prefabLeases,
                _occurrences.MoveToImmutable(),
                _entities.MoveToImmutable(),
                _entityIndicesByOccurrence.MoveToImmutable(),
                childOccurrenceIndicesByPlacement.MoveToImmutable(),
                _rootEntityIndicesByOccurrence.MoveToImmutable(),
                entityAssignmentKeys,
                _occurrenceIdentities);
            _sceneLease = null;
            _prefabLeases = null;
            _entities = ImmutableArray.CreateBuilder<SceneExpandedEntity>();
            _occurrences = ImmutableArray.CreateBuilder<SceneExpandedOccurrence>();
            _entityIndicesByOccurrence = ImmutableArray.CreateBuilder<int>();
            _rootEntityIndicesByOccurrence = ImmutableArray.CreateBuilder<int>();
            _occurrenceIdentities = [];
            _rootCounts = [];
            return plan;
        }

        private async ValueTask CountEntitySubtreeAsync(
            SceneExpansionSource source,
            Guid entityId,
            int prefabDepth,
            int hierarchyDepth)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            if (hierarchyDepth > _limits.MaximumHierarchyDepth)
            {
                throw new InvalidDataException("The expanded Scene hierarchy exceeds its selected depth limit.");
            }

            var nodeIndex = FindHierarchyNodeIndex(source.Hierarchy.Nodes, entityId);

            if (nodeIndex < 0)
            {
                throw new InvalidDataException("The expanded Scene hierarchy references an unknown Entity.");
            }

            var node = source.Hierarchy.Nodes[nodeIndex];

            for (var childIndex = 0; childIndex < node.Children.Length; childIndex++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var child = node.Children[childIndex];

                if (child.Kind == SceneHierarchyEntryKind.Entity)
                {
                    await CountEntitySubtreeAsync(
                        source,
                        child.Id,
                        prefabDepth,
                        checked(hierarchyDepth + 1)).ConfigureAwait(false);
                }
                else
                {
                    _ = await CountPlacementAsync(
                        source,
                        child.Id,
                        prefabDepth,
                        hierarchyDepth).ConfigureAwait(false);
                }
            }
        }

        private async ValueTask<int> CountOccurrenceAsync(
            SceneExpansionSource source,
            int prefabDepth,
            int baseHierarchyDepth)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            if (_occurrenceCount >= _limits.MaximumExpandedOccurrences)
            {
                throw new InvalidDataException("The Scene expansion exceeds its occurrence limit.");
            }

            _occurrenceCount++;

            if (source.IsPrefab)
            {
                if (prefabDepth > _limits.MaximumPrefabDepth)
                {
                    throw new InvalidDataException("The Scene expansion exceeds its Prefab depth limit.");
                }

                if (source.Entities.Length > _limits.MaximumPrefabEntities)
                {
                    throw new InvalidDataException("A Prefab exceeds its selected per-asset Entity limit.");
                }
            }

            if (source.Entities.Length > (_limits.MaximumExpandedEntities - _entityCount))
            {
                throw new InvalidDataException("The Scene expansion exceeds its Entity limit.");
            }

            _entityCount += source.Entities.Length;

            if (source.Placements.Length > (_limits.MaximumExpandedOccurrences - 1 - _placementOccurrenceCount))
            {
                throw new InvalidDataException("The Scene expansion exceeds its occurrence limit.");
            }

            _placementOccurrenceCount += source.Placements.Length;

            for (var entityIndex = 0; entityIndex < source.Entities.Length; entityIndex++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var entity = source.Entities[entityIndex];

                for (var componentIndex = 0; componentIndex < entity.Components.Length; componentIndex++)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    _totalComponentRecordBytes = checked(
                        _totalComponentRecordBytes
                        + entity.Components[componentIndex].EncodedRecord.Length);
                }
            }

            var rootCount = 0;
            var hierarchy = source.Hierarchy;

            for (var rootIndex = 0; rootIndex < hierarchy.Roots.Length; rootIndex++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var root = hierarchy.Roots[rootIndex];

                if (root.Kind == SceneHierarchyEntryKind.Entity)
                {
                    rootCount++;
                    await CountEntitySubtreeAsync(
                        source,
                        root.Id,
                        prefabDepth,
                        checked(baseHierarchyDepth + 1)).ConfigureAwait(false);
                }
                else
                {
                    rootCount = checked(
                        rootCount
                        + await CountPlacementAsync(
                            source,
                            root.Id,
                            prefabDepth,
                            baseHierarchyDepth).ConfigureAwait(false));
                }
            }

            _rootEntityIndexCount = checked(_rootEntityIndexCount + rootCount);
            return rootCount;
        }

        private async ValueTask<int> CountPlacementAsync(
            SceneExpansionSource source,
            Guid placementId,
            int prefabDepth,
            int parentHierarchyDepth)
        {
            var placementIndex = FindPlacementIndex(source.Placements, placementId);

            if (placementIndex < 0)
            {
                throw new InvalidDataException("The Scene hierarchy references an unknown PrefabPlacement.");
            }

            var placement = source.Placements[placementIndex];
            var childPrefabDepth = checked(prefabDepth + 1);

            if (childPrefabDepth > _limits.MaximumPrefabDepth)
            {
                throw new InvalidDataException("The Scene expansion exceeds its Prefab depth limit.");
            }

            if (_assetPath.Contains(placement.PrefabAssetId))
            {
                throw new InvalidDataException("The selected Scene and Prefab graph contains a cycle.");
            }

            var childPrefab = await GetPrefabAsync(placement.PrefabAssetId).ConfigureAwait(false);
            _assetPath.Add(placement.PrefabAssetId);

            try
            {
                return await CountOccurrenceAsync(
                    new SceneExpansionSource(childPrefab),
                    childPrefabDepth,
                    parentHierarchyDepth).ConfigureAwait(false);
            }
            finally
            {
                _assetPath.RemoveAt(_assetPath.Count - 1);
            }
        }

        private async ValueTask<int> ExpandEntityAsync(
            SceneExpansionSource source,
            int occurrenceIndex,
            int sourceEntityIndex,
            int parentEntityIndex,
            int baseHierarchyDepth,
            int childOrdinal,
            int entityMapStart,
            int childPlacementMapStart,
            int prefabDepth,
            ImmutableArray<int>.Builder childOccurrenceIndicesByPlacement)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var sourceEntity = source.Entities[sourceEntityIndex];
            var hierarchyDepth = parentEntityIndex < 0
                ? checked(baseHierarchyDepth + 1)
                : checked(_entities[parentEntityIndex].HierarchyDepth + 1);

            if (hierarchyDepth > _limits.MaximumHierarchyDepth)
            {
                throw new InvalidDataException("The expanded Scene hierarchy exceeds its selected depth limit.");
            }

            var entityIndex = _buildEntityCursor++;
            var occurrenceId = _occurrenceIdentities[occurrenceIndex];
            var expandedEntityKey = SceneOccurrenceIdentity.CreateExpandedEntityKey(
                occurrenceId,
                sourceEntity.AuthoredEntityId);
            _entities.Add(
                new SceneExpandedEntity(
                    expandedEntityKey,
                    sourceEntity.AuthoredEntityId,
                    occurrenceIndex,
                    sourceEntityIndex,
                    parentEntityIndex,
                    childOrdinal,
                    hierarchyDepth));
            _entityIndicesByOccurrence[entityMapStart + sourceEntityIndex] = entityIndex;
            await ExpandEntityChildrenAsync(
                source,
                occurrenceIndex,
                sourceEntity.AuthoredEntityId,
                entityIndex,
                hierarchyDepth,
                entityMapStart,
                childPlacementMapStart,
                prefabDepth,
                childOccurrenceIndicesByPlacement).ConfigureAwait(false);
            return entityIndex;
        }

        private async ValueTask ExpandEntityChildrenAsync(
            SceneExpansionSource source,
            int occurrenceIndex,
            Guid entityId,
            int parentEntityIndex,
            int hierarchyDepth,
            int entityMapStart,
            int childPlacementMapStart,
            int prefabDepth,
            ImmutableArray<int>.Builder childOccurrenceIndicesByPlacement)
        {
            var nodeIndex = FindHierarchyNodeIndex(source.Hierarchy.Nodes, entityId);

            if (nodeIndex < 0)
            {
                throw new InvalidDataException("The expanded Scene hierarchy references an unknown Entity.");
            }

            var node = source.Hierarchy.Nodes[nodeIndex];
            var childOrdinal = 0;

            for (var childIndex = 0; childIndex < node.Children.Length; childIndex++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var child = node.Children[childIndex];

                if (child.Kind == SceneHierarchyEntryKind.Entity)
                {
                    var sourceEntityIndex = FindEntityIndex(source.Entities, child.Id);

                    if (sourceEntityIndex < 0)
                    {
                        throw new InvalidDataException("The expanded Scene hierarchy references an unknown Entity.");
                    }

                    _ = await ExpandEntityAsync(
                        source,
                        occurrenceIndex,
                        sourceEntityIndex,
                        parentEntityIndex,
                        hierarchyDepth,
                        childOrdinal,
                        entityMapStart,
                        childPlacementMapStart,
                        prefabDepth,
                        childOccurrenceIndicesByPlacement).ConfigureAwait(false);
                    childOrdinal++;
                }
                else
                {
                    var placementIndex = FindPlacementIndex(source.Placements, child.Id);

                    if (placementIndex < 0)
                    {
                        throw new InvalidDataException("The expanded Scene hierarchy references an unknown PrefabPlacement.");
                    }

                    var childOccurrenceIndex = await ExpandPlacementAsync(
                        source,
                        occurrenceIndex,
                        placementIndex,
                        parentEntityIndex,
                        hierarchyDepth,
                        childOrdinal,
                        entityMapStart,
                        childPlacementMapStart,
                        prefabDepth,
                        childOccurrenceIndicesByPlacement).ConfigureAwait(false);
                    childOrdinal = checked(childOrdinal + _occurrences[childOccurrenceIndex].RootEntityCount);
                }
            }
        }

        private async ValueTask<int> ExpandOccurrenceAsync(
            SceneExpansionSource source,
            int prefabDepth,
            int baseHierarchyDepth,
            int parentOccurrenceIndex,
            Guid placementId,
            int parentEntityIndex,
            int firstChildOrdinal,
            ImmutableArray<int>.Builder childOccurrenceIndicesByPlacement)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var occurrenceIndex = _buildOccurrenceCursor++;
            var rootCount = _rootCounts[occurrenceIndex];
            var rootStart = _buildRootCursor;
            _buildRootCursor = checked(_buildRootCursor + rootCount);
            var entityMapStart = _buildEntityMapCursor;
            _buildEntityMapCursor = checked(_buildEntityMapCursor + source.Entities.Length);
            var childPlacementMapStart = _buildChildPlacementCursor;
            _buildChildPlacementCursor = checked(_buildChildPlacementCursor + source.Placements.Length);
            var occurrenceId = parentOccurrenceIndex < 0
                ? SceneOccurrenceIdentity.CreateRootOccurrence(source.AssetId)
                : SceneOccurrenceIdentity.CreatePrefabOccurrence(
                    _occurrenceIdentities[parentOccurrenceIndex],
                    placementId,
                    source.AssetId);

            _occurrenceIdentities[occurrenceIndex] = occurrenceId;
            _occurrences.Add(default);

            var rootsWritten = 0;
            var hierarchy = source.Hierarchy;

            for (var rootIndex = 0; rootIndex < hierarchy.Roots.Length; rootIndex++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var root = hierarchy.Roots[rootIndex];

                if (root.Kind == SceneHierarchyEntryKind.Entity)
                {
                    var sourceEntityIndex = FindEntityIndex(source.Entities, root.Id);

                    if (sourceEntityIndex < 0)
                    {
                        throw new InvalidDataException("The expanded Scene hierarchy references an unknown Entity.");
                    }

                    var expandedEntityIndex = await ExpandEntityAsync(
                        source,
                        occurrenceIndex,
                        sourceEntityIndex,
                        parentEntityIndex,
                        baseHierarchyDepth,
                        checked(firstChildOrdinal + rootsWritten),
                        entityMapStart,
                        childPlacementMapStart,
                        prefabDepth,
                        childOccurrenceIndicesByPlacement).ConfigureAwait(false);
                    _rootEntityIndicesByOccurrence[rootStart + rootsWritten] = expandedEntityIndex;
                    rootsWritten++;
                }
                else
                {
                    var placementIndex = FindPlacementIndex(source.Placements, root.Id);

                    if (placementIndex < 0)
                    {
                        throw new InvalidDataException("The expanded Scene hierarchy references an unknown PrefabPlacement.");
                    }

                    var childOccurrenceIndex = await ExpandPlacementAsync(
                        source,
                        occurrenceIndex,
                        placementIndex,
                        parentEntityIndex,
                        baseHierarchyDepth,
                        checked(firstChildOrdinal + rootsWritten),
                        entityMapStart,
                        childPlacementMapStart,
                        prefabDepth,
                        childOccurrenceIndicesByPlacement).ConfigureAwait(false);
                    var childOccurrence = _occurrences[childOccurrenceIndex];

                    for (var childRootIndex = 0; childRootIndex < childOccurrence.RootEntityCount; childRootIndex++)
                    {
                        _rootEntityIndicesByOccurrence[rootStart + rootsWritten] =
                            _rootEntityIndicesByOccurrence[childOccurrence.RootEntityIndicesStart + childRootIndex];
                        rootsWritten++;
                    }
                }
            }

            if (rootsWritten != rootCount)
            {
                throw new InvalidOperationException("The expanded Scene root count does not match its preflight.");
            }

            _occurrences[occurrenceIndex] = new SceneExpandedOccurrence(
                source.AssetId,
                occurrenceId,
                parentOccurrenceIndex,
                placementId,
                prefabDepth,
                entityMapStart,
                source.Entities.Length,
                childPlacementMapStart,
                source.Placements.Length,
                rootStart,
                rootCount);
            return occurrenceIndex;
        }

        private async ValueTask<int> ExpandPlacementAsync(
            SceneExpansionSource source,
            int occurrenceIndex,
            int placementIndex,
            int parentEntityIndex,
            int parentHierarchyDepth,
            int childOrdinal,
            int entityMapStart,
            int childPlacementMapStart,
            int prefabDepth,
            ImmutableArray<int>.Builder childOccurrenceIndicesByPlacement)
        {
            var placement = source.Placements[placementIndex];
            var childPrefab = await GetPrefabAsync(placement.PrefabAssetId).ConfigureAwait(false);
            var childOccurrenceIndex = await ExpandOccurrenceAsync(
                new SceneExpansionSource(childPrefab),
                checked(prefabDepth + 1),
                parentHierarchyDepth,
                occurrenceIndex,
                placement.PlacementId,
                parentEntityIndex,
                childOrdinal,
                childOccurrenceIndicesByPlacement).ConfigureAwait(false);
            childOccurrenceIndicesByPlacement[childPlacementMapStart + placementIndex] = childOccurrenceIndex;
            return childOccurrenceIndex;
        }

        private async ValueTask FillEntitySubtreeRootCountsAsync(
            SceneExpansionSource source,
            Guid entityId,
            int prefabDepth,
            int hierarchyDepth)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var nodeIndex = FindHierarchyNodeIndex(source.Hierarchy.Nodes, entityId);

            if (nodeIndex < 0)
            {
                throw new InvalidDataException("The Scene hierarchy references an unknown Entity.");
            }

            var node = source.Hierarchy.Nodes[nodeIndex];

            for (var childIndex = 0; childIndex < node.Children.Length; childIndex++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var child = node.Children[childIndex];

                if (child.Kind == SceneHierarchyEntryKind.Entity)
                {
                    await FillEntitySubtreeRootCountsAsync(
                        source,
                        child.Id,
                        prefabDepth,
                        checked(hierarchyDepth + 1)).ConfigureAwait(false);
                }
                else
                {
                    _ = await FillPlacementRootCountsAsync(
                        source,
                        child.Id,
                        prefabDepth,
                        hierarchyDepth).ConfigureAwait(false);
                }
            }
        }

        private async ValueTask<int> FillOccurrenceRootCountsAsync(
            SceneExpansionSource source,
            int prefabDepth,
            int baseHierarchyDepth)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var occurrenceIndex = _rootCountCursor++;
            var rootCount = 0;
            var hierarchy = source.Hierarchy;

            for (var rootIndex = 0; rootIndex < hierarchy.Roots.Length; rootIndex++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var root = hierarchy.Roots[rootIndex];

                if (root.Kind == SceneHierarchyEntryKind.Entity)
                {
                    rootCount++;
                    await FillEntitySubtreeRootCountsAsync(
                        source,
                        root.Id,
                        prefabDepth,
                        checked(baseHierarchyDepth + 1)).ConfigureAwait(false);
                }
                else
                {
                    rootCount = checked(
                        rootCount
                        + await FillPlacementRootCountsAsync(
                            source,
                            root.Id,
                            prefabDepth,
                            baseHierarchyDepth).ConfigureAwait(false));
                }
            }

            _rootCounts[occurrenceIndex] = rootCount;
            return rootCount;
        }

        private async ValueTask<int> FillPlacementRootCountsAsync(
            SceneExpansionSource source,
            Guid placementId,
            int prefabDepth,
            int parentHierarchyDepth)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var placementIndex = FindPlacementIndex(source.Placements, placementId);

            if (placementIndex < 0)
            {
                throw new InvalidDataException("The Scene hierarchy references an unknown PrefabPlacement.");
            }

            var placement = source.Placements[placementIndex];
            var childPrefab = await GetPrefabAsync(placement.PrefabAssetId).ConfigureAwait(false);
            return await FillOccurrenceRootCountsAsync(
                new SceneExpansionSource(childPrefab),
                checked(prefabDepth + 1),
                parentHierarchyDepth).ConfigureAwait(false);
        }

        private async ValueTask<ScenePrefabAsset> GetPrefabAsync(AssetId assetId)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var prefabLeases = _prefabLeases
                ?? throw new ObjectDisposedException(nameof(SceneExpansionBuilder));

            if (prefabLeases.TryGetValue(assetId, out var existingLease))
            {
                return existingLease.Value;
            }

            var lease = await _assets.LoadAsync<ScenePrefabAsset>(assetId, _cancellationToken).ConfigureAwait(false);
            var value = lease.Value;
            var retained = false;

            try
            {
                prefabLeases.Add(assetId, lease);
                retained = true;
                return value;
            }
            finally
            {
                if (!retained)
                {
                    lease.Dispose();
                }
            }
        }

        private long GetRequiredByteReservation()
        {
            var prefabLeases = _prefabLeases
                ?? throw new InvalidOperationException("The Scene expansion lost its Prefab lease set.");
            var requiredBytes = _totalComponentRecordBytes;
            requiredBytes = checked(requiredBytes + ArrayBytes(_entityCount, Unsafe.SizeOf<SceneExpandedEntity>()));
            requiredBytes = checked(requiredBytes + ArrayBytes(_occurrenceCount, Unsafe.SizeOf<SceneExpandedOccurrence>()));
            requiredBytes = checked(requiredBytes + ArrayBytes(_entityCount, sizeof(int)));
            requiredBytes = checked(requiredBytes + ArrayBytes(_placementOccurrenceCount, sizeof(int)));
            requiredBytes = checked(requiredBytes + ArrayBytes(_rootEntityIndexCount, sizeof(int)));
            requiredBytes = checked(requiredBytes + ArrayBytes(_entityCount, Unsafe.SizeOf<Guid>()));
            requiredBytes = checked(requiredBytes + ArrayBytes(_occurrenceCount, Unsafe.SizeOf<Guid>()));
            requiredBytes = checked(requiredBytes + ArrayBytes(_occurrenceCount, sizeof(int)));
            requiredBytes = checked(requiredBytes + (checked((long)prefabLeases.Count) * 256L));
            return checked(requiredBytes + 2048L);
        }
    }
}
