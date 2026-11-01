// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Assets;
using TerraFX.Entities;

namespace TerraFX.Scenes;

internal sealed class SceneExpansionPlan : IDisposable
{
    private readonly ImmutableArray<int> _childOccurrenceIndicesByPlacement;
    private int _disposed;
    private readonly ImmutableArray<SceneExpandedEntity> _entities;
    private readonly Guid[] _entityAssignmentKeys;
    private readonly int[] _entityIndicesByAssignmentOrdinal;
    private readonly ImmutableArray<int> _entityIndicesByOccurrence;
    private readonly int[] _entityParentIndices;
    private readonly ImmutableArray<SceneExpandedExternalSlotBinding> _externalSlotBindings;
    private readonly Guid[] _occurrenceIds;
    private readonly ImmutableArray<SceneExpandedOccurrence> _occurrences;
    private Dictionary<AssetId, AssetLease<ScenePrefabAsset>>? _prefabLeases;
    private IDisposable? _preparedAssets;
    private readonly ImmutableArray<SceneExpandedReferenceBinding> _referenceBindings;
    private readonly ImmutableArray<int> _rootEntityIndicesByOccurrence;
    private readonly AssetId _sceneAssetId;
    private AssetLease<SceneAsset>? _sceneLease;
    private WorldSceneStorageReservation? _storageReservation;

    internal SceneExpansionPlan(
        AssetLease<SceneAsset> sceneLease,
        Dictionary<AssetId, AssetLease<ScenePrefabAsset>> prefabLeases,
        ImmutableArray<SceneExpandedOccurrence> occurrences,
        ImmutableArray<SceneExpandedEntity> entities,
        ImmutableArray<int> entityIndicesByOccurrence,
        ImmutableArray<int> childOccurrenceIndicesByPlacement,
        ImmutableArray<int> rootEntityIndicesByOccurrence,
        ImmutableArray<SceneExpandedReferenceBinding> referenceBindings,
        ImmutableArray<SceneExpandedExternalSlotBinding> externalSlotBindings,
        Guid[] entityAssignmentKeys,
        int[] entityIndicesByAssignmentOrdinal,
        int[] entityParentIndices,
        Guid[] occurrenceIds,
        long requiredByteReservation,
        WorldSceneStorageReservation? storageReservation)
    {
        ArgumentNullException.ThrowIfNull(sceneLease);
        ArgumentNullException.ThrowIfNull(prefabLeases);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requiredByteReservation);

        if (storageReservation is not null && (storageReservation.Bytes != requiredByteReservation))
        {
            throw new ArgumentException("The retained Scene byte reservation does not match the preflight estimate.", nameof(storageReservation));
        }

        if (occurrences.IsDefaultOrEmpty
            || entities.IsDefault
            || entityIndicesByOccurrence.IsDefault
            || childOccurrenceIndicesByPlacement.IsDefault
            || rootEntityIndicesByOccurrence.IsDefault
            || referenceBindings.IsDefault
            || externalSlotBindings.IsDefault)
        {
            throw new ArgumentException("The expansion plan arrays must be initialized.");
        }

        ArgumentNullException.ThrowIfNull(entityAssignmentKeys);
        ArgumentNullException.ThrowIfNull(entityIndicesByAssignmentOrdinal);
        ArgumentNullException.ThrowIfNull(entityParentIndices);
        ArgumentNullException.ThrowIfNull(occurrenceIds);

        if ((entityAssignmentKeys.Length != entities.Length)
            || (entityIndicesByAssignmentOrdinal.Length != entities.Length)
            || (entityParentIndices.Length != entities.Length))
        {
            throw new ArgumentException("The Scene Entity assignment and parent maps must cover every expanded Entity.");
        }

        for (var index = 0; index < entities.Length; index++)
        {
            var parentIndex = entityParentIndices[index];

            if ((parentIndex < -1) || (parentIndex >= index))
            {
                throw new ArgumentException("The Scene Entity parent map must refer only to an earlier expanded Entity or the root.", nameof(entityParentIndices));
            }

            var expectedHierarchyDepth = (parentIndex < 0)
                ? 1
                : checked(entities[parentIndex].HierarchyDepth + 1);

            if (entities[index].HierarchyDepth != expectedHierarchyDepth)
            {
                throw new ArgumentException("The Scene Entity parent map does not match the expanded hierarchy depths.", nameof(entityParentIndices));
            }
        }

        _sceneAssetId = sceneLease.AssetId;
        _sceneLease = sceneLease;
        _prefabLeases = prefabLeases;
        _occurrences = occurrences;
        _entities = entities;
        _entityIndicesByOccurrence = entityIndicesByOccurrence;
        _childOccurrenceIndicesByPlacement = childOccurrenceIndicesByPlacement;
        _rootEntityIndicesByOccurrence = rootEntityIndicesByOccurrence;
        _referenceBindings = referenceBindings;
        _externalSlotBindings = externalSlotBindings;
        _entityAssignmentKeys = entityAssignmentKeys;
        _entityIndicesByAssignmentOrdinal = entityIndicesByAssignmentOrdinal;
        _entityParentIndices = entityParentIndices;
        _occurrenceIds = occurrenceIds;
        RequiredByteReservation = requiredByteReservation;
        _storageReservation = storageReservation;
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

    internal ReadOnlySpan<int> EntityIndicesByAssignmentOrdinal
    {
        get
        {
            ThrowIfDisposed();
            return _entityIndicesByAssignmentOrdinal;
        }
    }

    internal ReadOnlyMemory<int> EntityParentIndices
    {
        get
        {
            ThrowIfDisposed();
            return _entityParentIndices;
        }
    }

    internal ImmutableArray<SceneExpandedExternalSlotBinding> ExternalSlotBindings
    {
        get
        {
            ThrowIfDisposed();
            return _externalSlotBindings;
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

    internal long RequiredByteReservation { get; }

    internal ImmutableArray<SceneExternalSlot> RootExternalSlots
    {
        get
        {
            ThrowIfDisposed();
            return (_sceneLease ?? throw new ObjectDisposedException(nameof(SceneExpansionPlan))).Value.ExternalSlots;
        }
    }

    internal AssetId SceneAssetId => _sceneAssetId;

    internal CompiledContentSchemaRegistry Schemas
    {
        get
        {
            ThrowIfDisposed();
            return (_sceneLease ?? throw new ObjectDisposedException(nameof(SceneExpansionPlan))).Value.Schemas;
        }
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

        try
        {
            Interlocked.Exchange(ref _preparedAssets, null)?.Dispose();
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
        finally
        {
            Interlocked.Exchange(ref _storageReservation, null)?.Dispose();
        }
    }

    internal void AttachPreparedAssets(IDisposable preparedAssets)
    {
        ArgumentNullException.ThrowIfNull(preparedAssets);
        var transferred = false;

        try
        {
            ThrowIfDisposed();

            if (Interlocked.CompareExchange(ref _preparedAssets, preparedAssets, null) is not null)
            {
                throw new InvalidOperationException("The Scene expansion plan already owns prepared asset resources.");
            }

            transferred = true;
        }
        finally
        {
            if (!transferred)
            {
                preparedAssets.Dispose();
            }
        }
    }

    internal int GetEntityAssignmentOrdinal(int expandedEntityIndex)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(expandedEntityIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(expandedEntityIndex, _entities.Length);
        var expandedEntityKey = _entities[expandedEntityIndex].ExpandedEntityKey;
        var low = 0;
        var high = _entityAssignmentKeys.Length - 1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = FoundationCatalogue.CompareRfc(_entityAssignmentKeys[middle], expandedEntityKey);

            if (comparison == 0)
            {
                if (_entityIndicesByAssignmentOrdinal[middle] != expandedEntityIndex)
                {
                    throw new InvalidOperationException("The Scene expanded-Entity assignment map is inconsistent.");
                }

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

        throw new InvalidOperationException("The Scene expanded Entity is missing from the assignment map.");
    }

    internal ImmutableArray<Guid> GetOccurrencePlacementPath(int occurrenceIndex)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(occurrenceIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(occurrenceIndex, _occurrences.Length);
        var pathLength = 0;
        var currentIndex = occurrenceIndex;

        while (_occurrences[currentIndex].ParentOccurrenceIndex >= 0)
        {
            pathLength++;
            currentIndex = _occurrences[currentIndex].ParentOccurrenceIndex;
        }

        if (pathLength > 32)
        {
            throw new InvalidOperationException("A retained Scene occurrence path exceeds the reviewed placement-depth bound.");
        }

        var placementPath = new Guid[pathLength];
        currentIndex = occurrenceIndex;

        for (var index = pathLength - 1; index >= 0; index--)
        {
            var occurrence = _occurrences[currentIndex];
            placementPath[index] = occurrence.PlacementId;
            currentIndex = occurrence.ParentOccurrenceIndex;
        }

        return [.. placementPath];
    }

    internal TResource? GetPreparedAssets<TResource>()
        where TResource : class
    {
        ThrowIfDisposed();
        return Volatile.Read(ref _preparedAssets) as TResource;
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

    internal Task<AssetLease<TAsset>> LoadFromPackageAsync<TAsset>(
        AssetId assetId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var sceneLease = Volatile.Read(ref _sceneLease)
            ?? throw new ObjectDisposedException(nameof(SceneExpansionPlan));
        return sceneLease.LoadFromPackageAsync<TAsset>(assetId, cancellationToken);
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

    internal bool TryResolveRootEntity(Guid authoredEntityId, out int expandedEntityIndex)
    {
        ThrowIfDisposed();

        if (authoredEntityId == Guid.Empty)
        {
            throw new ArgumentException("A nonempty authored Entity identity is required.", nameof(authoredEntityId));
        }

        var source = GetSource(0);
        var sourceEntityIndex = FindEntityIndex(source.Entities, authoredEntityId);

        if (sourceEntityIndex < 0)
        {
            expandedEntityIndex = -1;
            return false;
        }

        var rootOccurrence = _occurrences[0];
        expandedEntityIndex = _entityIndicesByOccurrence[rootOccurrence.EntityMapStart + sourceEntityIndex];
        return expandedEntityIndex >= 0;
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
