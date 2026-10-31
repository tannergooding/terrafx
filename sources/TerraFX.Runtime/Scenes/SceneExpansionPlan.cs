// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using TerraFX.Assets;

namespace TerraFX.Scenes;

internal sealed class SceneExpansionPlan : IDisposable
{
    private readonly ImmutableArray<int> _childOccurrenceIndicesByPlacement;
    private int _disposed;
    private readonly ImmutableArray<SceneExpandedEntity> _entities;
    private readonly Guid[] _entityAssignmentKeys;
    private readonly ImmutableArray<int> _entityIndicesByOccurrence;
    private readonly Guid[] _occurrenceIds;
    private readonly ImmutableArray<SceneExpandedOccurrence> _occurrences;
    private Dictionary<AssetId, AssetLease<ScenePrefabAsset>>? _prefabLeases;
    private readonly ImmutableArray<SceneExpandedReferenceBinding> _referenceBindings;
    private readonly ImmutableArray<int> _rootEntityIndicesByOccurrence;
    private readonly AssetId _sceneAssetId;
    private AssetLease<SceneAsset>? _sceneLease;

    internal SceneExpansionPlan(
        AssetLease<SceneAsset> sceneLease,
        Dictionary<AssetId, AssetLease<ScenePrefabAsset>> prefabLeases,
        ImmutableArray<SceneExpandedOccurrence> occurrences,
        ImmutableArray<SceneExpandedEntity> entities,
        ImmutableArray<int> entityIndicesByOccurrence,
        ImmutableArray<int> childOccurrenceIndicesByPlacement,
        ImmutableArray<int> rootEntityIndicesByOccurrence,
        ImmutableArray<SceneExpandedReferenceBinding> referenceBindings,
        Guid[] entityAssignmentKeys,
        Guid[] occurrenceIds)
    {
        ArgumentNullException.ThrowIfNull(sceneLease);
        ArgumentNullException.ThrowIfNull(prefabLeases);

        if (occurrences.IsDefaultOrEmpty
            || entities.IsDefault
            || entityIndicesByOccurrence.IsDefault
            || childOccurrenceIndicesByPlacement.IsDefault
            || rootEntityIndicesByOccurrence.IsDefault
            || referenceBindings.IsDefault)
        {
            throw new ArgumentException("The expansion plan arrays must be initialized.");
        }

        ArgumentNullException.ThrowIfNull(entityAssignmentKeys);
        ArgumentNullException.ThrowIfNull(occurrenceIds);

        _sceneAssetId = sceneLease.AssetId;
        _sceneLease = sceneLease;
        _prefabLeases = prefabLeases;
        _occurrences = occurrences;
        _entities = entities;
        _entityIndicesByOccurrence = entityIndicesByOccurrence;
        _childOccurrenceIndicesByPlacement = childOccurrenceIndicesByPlacement;
        _rootEntityIndicesByOccurrence = rootEntityIndicesByOccurrence;
        _referenceBindings = referenceBindings;
        _entityAssignmentKeys = entityAssignmentKeys;
        _occurrenceIds = occurrenceIds;
    }

    internal ImmutableArray<SceneExpandedEntity> Entities
    {
        get
        {
            ThrowIfDisposed();
            return _entities;
        }
    }

    internal ReadOnlySpan<Guid> EntityAssignmentKeys
    {
        get
        {
            ThrowIfDisposed();
            return _entityAssignmentKeys;
        }
    }

    internal ReadOnlySpan<Guid> OccurrenceIds
    {
        get
        {
            ThrowIfDisposed();
            return _occurrenceIds;
        }
    }

    internal ImmutableArray<SceneExpandedOccurrence> Occurrences
    {
        get
        {
            ThrowIfDisposed();
            return _occurrences;
        }
    }

    internal ImmutableArray<SceneExpandedReferenceBinding> ReferenceBindings
    {
        get
        {
            ThrowIfDisposed();
            return _referenceBindings;
        }
    }

    internal AssetId SceneAssetId => _sceneAssetId;

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
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var prefabLeases = Interlocked.Exchange(ref _prefabLeases, null);

        if (prefabLeases is not null)
        {
            foreach (var lease in prefabLeases.Values)
            {
                lease.Dispose();
            }
        }

        Interlocked.Exchange(ref _sceneLease, null)?.Dispose();
    }

    internal ReadOnlySpan<int> GetRootEntityIndices(int occurrenceIndex)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(occurrenceIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(occurrenceIndex, _occurrences.Length);
        var occurrence = _occurrences[occurrenceIndex];
        return _rootEntityIndicesByOccurrence.AsSpan().Slice(
            occurrence.RootEntityIndicesStart,
            occurrence.RootEntityCount);
    }

    internal SceneExpansionSource GetSource(int occurrenceIndex)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(occurrenceIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(occurrenceIndex, _occurrences.Length);
        return GetAssetSource(_occurrences[occurrenceIndex].AssetId);
    }

    internal SceneEntity GetSourceEntity(int expandedEntityIndex)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(expandedEntityIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(expandedEntityIndex, _entities.Length);
        var entity = _entities[expandedEntityIndex];
        var occurrence = _occurrences[entity.OccurrenceIndex];
        var source = GetAssetSource(occurrence.AssetId);
        return source.Entities[entity.SourceEntityIndex];
    }

    internal int ResolveEntity(SceneEntitySelector selector, int relativeAnchorOccurrenceIndex)
    {
        ThrowIfDisposed();

        if (selector.PlacementPath.IsDefault
            || (selector.PlacementPath.Length > 32)
            || (selector.AuthoredEntityId == Guid.Empty)
            || (selector.Scope is not SceneEntitySelectorScope.OccurrenceRelative
                and not SceneEntitySelectorScope.InstanceAbsolute))
        {
            throw new InvalidDataException("The Scene EntitySelector is invalid.");
        }

        var occurrenceIndex = selector.Scope == SceneEntitySelectorScope.InstanceAbsolute
            ? 0
            : relativeAnchorOccurrenceIndex;

        if ((occurrenceIndex < 0) || (occurrenceIndex >= _occurrences.Length))
        {
            throw new InvalidDataException("The Scene EntitySelector anchor occurrence does not exist.");
        }

        for (var pathIndex = 0; pathIndex < selector.PlacementPath.Length; pathIndex++)
        {
            var source = GetSource(occurrenceIndex);
            var placementIndex = FindPlacementIndex(source.Placements, selector.PlacementPath[pathIndex]);

            if (placementIndex < 0)
            {
                throw new InvalidDataException("The Scene EntitySelector placement path does not exist.");
            }

            var occurrence = _occurrences[occurrenceIndex];
            var childOccurrenceIndex = _childOccurrenceIndicesByPlacement[
                occurrence.ChildPlacementMapStart + placementIndex];

            if (childOccurrenceIndex < 0)
            {
                throw new InvalidDataException("The Scene EntitySelector placement was not expanded.");
            }

            occurrenceIndex = childOccurrenceIndex;
        }

        var targetSource = GetSource(occurrenceIndex);
        var sourceEntityIndex = FindEntityIndex(targetSource.Entities, selector.AuthoredEntityId);

        if (sourceEntityIndex < 0)
        {
            throw new InvalidDataException("The Scene EntitySelector does not name an entity in its occurrence.");
        }

        var targetOccurrence = _occurrences[occurrenceIndex];
        var expandedEntityIndex = _entityIndicesByOccurrence[
            targetOccurrence.EntityMapStart + sourceEntityIndex];

        if (expandedEntityIndex < 0)
        {
            throw new InvalidDataException("The Scene EntitySelector entity was not expanded.");
        }

        return expandedEntityIndex;
    }

    private SceneExpansionSource GetAssetSource(AssetId assetId)
    {
        ThrowIfDisposed();
        var sceneLease = _sceneLease
            ?? throw new ObjectDisposedException(nameof(SceneExpansionPlan));

        if (assetId == sceneLease.AssetId)
        {
            return new SceneExpansionSource(sceneLease.Value);
        }

        var prefabLeases = _prefabLeases
            ?? throw new ObjectDisposedException(nameof(SceneExpansionPlan));

        if (prefabLeases.TryGetValue(assetId, out var prefabLease))
        {
            return new SceneExpansionSource(prefabLease.Value);
        }

        throw new InvalidOperationException("The expansion plan does not retain the selected Prefab asset lease.");
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
}
