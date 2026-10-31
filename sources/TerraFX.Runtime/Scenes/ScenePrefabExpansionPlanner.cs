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
        private int _externalSlotBindingCount;
        private long _externalSlotBindingResolutionBytes;
        private ImmutableArray<SceneExpandedExternalSlotBinding>.Builder _externalSlotBindings = ImmutableArray.CreateBuilder<SceneExpandedExternalSlotBinding>();
        private readonly SceneExpansionLimits _limits;
        private int _occurrenceCount;
        private Guid[] _occurrenceIdentities = [];
        private ImmutableArray<SceneExpandedOccurrence>.Builder _occurrences = ImmutableArray.CreateBuilder<SceneExpandedOccurrence>();
        private int _placementOccurrenceCount;
        private Dictionary<AssetId, AssetLease<ScenePrefabAsset>>? _prefabLeases = [];
        private List<SceneExpandedReferenceBinding> _referenceBindings = [];
        private long _overrideComponentArrayBytes;
        private long _overrideComponentRecordBytes;
        private long _overrideLayerBytes;
        private int _referenceBindingCount;
        private long _referenceBindingValidationBytes;
        private int _rootCountCursor;
        private int[] _rootCounts = [];
        private int _rootEntityIndexCount;
        private ImmutableArray<int>.Builder _rootEntityIndicesByOccurrence = ImmutableArray.CreateBuilder<int>();
        private long _rootPoseComponentArrayBytes;
        private long _rootPoseTargetScratchBytes;
        private long _rootPositionComponentBytes;
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

        private static ImmutableArray<SceneAuthoredComponentRecord> ApplyRootPoseComponents(
            ImmutableArray<SceneAuthoredComponentRecord> sourceComponents,
            SceneAuthoredComponentRecord localTransform,
            SceneAuthoredComponentRecord? rootPosition)
        {
            if (localTransform.Schema.SchemaId != SceneEntityReader.LocalTransformSchemaId
                || localTransform.Schema.Major != 1
                || localTransform.Schema.Minor != 0
                || localTransform.Schema.Scope != ContentSchemaScope.Content)
            {
                throw new InvalidDataException("A Prefab RootPose LocalTransform does not match the registered component schema.");
            }

            if (rootPosition is not null
                && ((rootPosition.Schema.SchemaId != SceneEntityReader.RootPositionSchemaId)
                    || (rootPosition.Schema.Major != 1)
                    || (rootPosition.Schema.Minor != 0)
                    || (rootPosition.Schema.Scope != ContentSchemaScope.Content)))
            {
                throw new InvalidDataException("A Prefab RootPose RootPosition does not match the registered component schema.");
            }

            var hasLocalTransform = false;
            var hasRootPosition = false;

            for (var index = 0; index < sourceComponents.Length; index++)
            {
                var schemaId = sourceComponents[index].Schema.SchemaId;

                if (schemaId == SceneEntityReader.LocalTransformSchemaId)
                {
                    if (hasLocalTransform)
                    {
                        throw new InvalidDataException("An expanded Entity contains more than one LocalTransform component.");
                    }

                    hasLocalTransform = true;
                }
                else if (schemaId == SceneEntityReader.RootPositionSchemaId)
                {
                    if (hasRootPosition)
                    {
                        throw new InvalidDataException("An expanded Entity contains more than one RootPosition component.");
                    }

                    hasRootPosition = true;
                }
            }

            if (!hasLocalTransform)
            {
                throw new InvalidDataException("A Prefab RootPose target has no LocalTransform component.");
            }

            var componentCount = checked(
                sourceComponents.Length
                + ((rootPosition is not null && !hasRootPosition) ? 1 : 0)
                - ((rootPosition is null && hasRootPosition) ? 1 : 0));
            var components = ImmutableArray.CreateBuilder<SceneAuthoredComponentRecord>(componentCount);
            var localTransformWritten = false;
            var rootPositionWritten = false;

            for (var index = 0; index < sourceComponents.Length; index++)
            {
                var component = sourceComponents[index];
                var schemaId = component.Schema.SchemaId;

                if ((rootPosition is not null)
                    && !rootPositionWritten
                    && (FoundationCatalogue.CompareRfc(rootPosition.Schema.SchemaId, schemaId) < 0))
                {
                    components.Add(rootPosition);
                    rootPositionWritten = true;
                }

                if (schemaId == SceneEntityReader.LocalTransformSchemaId)
                {
                    components.Add(localTransform);
                    localTransformWritten = true;
                }
                else if (schemaId == SceneEntityReader.RootPositionSchemaId)
                {
                    if (rootPosition is not null)
                    {
                        components.Add(rootPosition);
                        rootPositionWritten = true;
                    }
                }
                else
                {
                    components.Add(component);
                }
            }

            if (!localTransformWritten)
            {
                throw new InvalidDataException("A Prefab RootPose did not replace the target LocalTransform.");
            }

            if ((rootPosition is not null) && !rootPositionWritten)
            {
                components.Add(rootPosition);
            }

            return components.MoveToImmutable();
        }

        private static long ArrayBytes(int count, int elementSize)
        {
            var payloadBytes = checked((long)count * elementSize);
            return checked(64L + ((payloadBytes + 7L) & ~7L));
        }

        private static int CompareExpandedExternalSlotBindings(
            SceneExpandedExternalSlotBinding binding,
            int occurrenceIndex,
            Guid slotId)
        {
            var comparison = binding.OccurrenceIndex.CompareTo(occurrenceIndex);
            return (comparison != 0) ? comparison : FoundationCatalogue.CompareRfc(binding.SlotId, slotId);
        }

        private static int ComparePropertyPaths(
            ImmutableArray<ScenePropertyPathSegment> left,
            ImmutableArray<ScenePropertyPathSegment> right)
        {
            var sharedLength = Math.Min(left.Length, right.Length);

            for (var index = 0; index < sharedLength; index++)
            {
                var comparison = left[index].Kind.CompareTo(right[index].Kind);

                if (comparison == 0)
                {
                    comparison = left[index].Value.CompareTo(right[index].Value);
                }

                if (comparison != 0)
                {
                    return comparison;
                }
            }

            return left.Length.CompareTo(right.Length);
        }

        private static int CompareReferenceBindings(
            SceneExpandedReferenceBinding left,
            SceneExpandedReferenceBinding right)
        {
            var comparison = CompareReferenceKeys(left, right);
            return (comparison != 0) ? comparison : left.SourceScope.CompareTo(right.SourceScope);
        }

        private static int CompareReferenceKeys(
            SceneExpandedReferenceBinding left,
            SceneExpandedReferenceBinding right)
        {
            var comparison = left.ExpandedEntityIndex.CompareTo(right.ExpandedEntityIndex);

            if (comparison != 0)
            {
                return comparison;
            }

            comparison = CompareSchemaReferences(left.ComponentSchema, right.ComponentSchema);
            return (comparison != 0) ? comparison : ComparePropertyPaths(left.PropertyPath, right.PropertyPath);
        }

        private static int CompareReferenceLookupKey(
            SceneExpandedReferenceBinding binding,
            int sourceEntityIndex,
            ContentSchemaReference componentSchema,
            ImmutableArray<ScenePropertyPathSegment> propertyPath)
        {
            var comparison = binding.ExpandedEntityIndex.CompareTo(sourceEntityIndex);

            if (comparison != 0)
            {
                return comparison;
            }

            comparison = CompareSchemaReferences(binding.ComponentSchema, componentSchema);
            return (comparison != 0) ? comparison : ComparePropertyPaths(binding.PropertyPath, propertyPath);
        }

        private static int CompareResolvedOverrides(ResolvedOverride left, ResolvedOverride right)
        {
            var comparison = left.ExpandedEntityIndex.CompareTo(right.ExpandedEntityIndex);

            if (comparison != 0)
            {
                return comparison;
            }

            comparison = CompareSchemaReferences(left.Value.ComponentSchema, right.Value.ComponentSchema);
            return (comparison != 0)
                ? comparison
                : ComparePropertyPaths(left.Value.PropertyPath, right.Value.PropertyPath);
        }

        private static int CompareSchemaReferences(ContentSchemaReference left, ContentSchemaReference right)
        {
            var comparison = FoundationCatalogue.CompareRfc(left.SchemaId, right.SchemaId);

            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.Major.CompareTo(right.Major);

            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.Minor.CompareTo(right.Minor);
            return (comparison != 0) ? comparison : left.Scope.CompareTo(right.Scope);
        }

        private static bool ContainsExternalSlot(ImmutableArray<SceneExternalSlot> externalSlots, Guid slotId)
        {
            var low = 0;
            var high = externalSlots.Length - 1;

            while (low <= high)
            {
                var middle = low + ((high - low) / 2);
                var comparison = FoundationCatalogue.CompareRfc(externalSlots[middle].SlotId, slotId);

                if (comparison == 0)
                {
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

            return false;
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

        private static int FindEntityIndexForComponent(
            ImmutableArray<SceneAuthoredComponentRecord> components,
            ContentSchemaReference schema)
        {
            for (var componentIndex = 0; componentIndex < components.Length; componentIndex++)
            {
                if (components[componentIndex].Schema == schema)
                {
                    return componentIndex;
                }
            }

            return -1;
        }

        private static int FindExpandedExternalSlotBindingIndex(
            ImmutableArray<SceneExpandedExternalSlotBinding>.Builder bindings,
            int occurrenceIndex,
            Guid slotId)
        {
            var low = 0;
            var high = bindings.Count - 1;

            while (low <= high)
            {
                var middle = low + ((high - low) / 2);
                var comparison = CompareExpandedExternalSlotBindings(bindings[middle], occurrenceIndex, slotId);

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

        private static int FindReferenceBindingIndex(
            List<SceneExpandedReferenceBinding> bindings,
            int sourceEntityIndex,
            ContentSchemaReference componentSchema,
            ImmutableArray<ScenePropertyPathSegment> propertyPath)
        {
            var low = 0;
            var high = bindings.Count - 1;

            while (low <= high)
            {
                var middle = low + ((high - low) / 2);
                var comparison = CompareReferenceLookupKey(
                    bindings[middle],
                    sourceEntityIndex,
                    componentSchema,
                    propertyPath);

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

        private static bool IsPropertyPathPrefix(
            ImmutableArray<ScenePropertyPathSegment> prefix,
            ImmutableArray<ScenePropertyPathSegment> path)
        {
            if (prefix.Length >= path.Length)
            {
                return false;
            }

            for (var index = 0; index < prefix.Length; index++)
            {
                if (prefix[index] != path[index])
                {
                    return false;
                }
            }

            return true;
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
            _referenceBindings = [];
            _externalSlotBindings = ImmutableArray.CreateBuilder<SceneExpandedExternalSlotBinding>();
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
            await CountOverrideLayerAsync(
                sceneSource,
                sceneLease.Value.Overrides,
                allowInstanceAbsoluteSources: true).ConfigureAwait(false);

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
            _externalSlotBindings = ImmutableArray.CreateBuilder<SceneExpandedExternalSlotBinding>(_externalSlotBindingCount);
            _rootEntityIndicesByOccurrence = ImmutableArray.CreateBuilder<int>(_rootEntityIndexCount);
            var childOccurrenceIndicesByPlacement = ImmutableArray.CreateBuilder<int>(_placementOccurrenceCount);
            _ = _referenceBindings.EnsureCapacity(_referenceBindingCount);
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

            BuildReferenceBindings(childOccurrenceIndicesByPlacement);
            ApplyPlacementLayers(childOccurrenceIndicesByPlacement);
            var scene = _sceneLease
                ?? throw new InvalidOperationException("The Scene expansion lost its Scene lease.");
            ApplyOverrideLayer(
                scene.Value.Overrides,
                relativeAnchorOccurrenceIndex: 0,
                ownerOccurrenceIndex: 0,
                childOccurrenceIndicesByPlacement: childOccurrenceIndicesByPlacement,
                allowInstanceAbsoluteSources: true);
            BuildResolvedExternalSlotBindings(childOccurrenceIndicesByPlacement);
            ValidateExpandedReferenceBindings(childOccurrenceIndicesByPlacement);
            _ = _referenceBindings.RemoveAll(static binding => !binding.IsPresent);

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
                [.. _referenceBindings],
                _externalSlotBindings.MoveToImmutable(),
                entityAssignmentKeys,
                _occurrenceIdentities);
            _sceneLease = null;
            _prefabLeases = null;
            _entities = ImmutableArray.CreateBuilder<SceneExpandedEntity>();
            _occurrences = ImmutableArray.CreateBuilder<SceneExpandedOccurrence>();
            _entityIndicesByOccurrence = ImmutableArray.CreateBuilder<int>();
            _rootEntityIndicesByOccurrence = ImmutableArray.CreateBuilder<int>();
            _referenceBindings = [];
            _occurrenceIdentities = [];
            _rootCounts = [];
            return plan;
        }

        private void ApplyOverrideLayer(
            ImmutableArray<ScenePrefabOverride> overrides,
            int relativeAnchorOccurrenceIndex,
            int ownerOccurrenceIndex,
            ImmutableArray<int>.Builder childOccurrenceIndicesByPlacement,
            bool allowInstanceAbsoluteSources)
        {
            if (overrides.IsDefault)
            {
                throw new InvalidDataException("A Scene override layer is uninitialized.");
            }

            if (overrides.IsEmpty)
            {
                return;
            }

            var resolvedOverrides = new ResolvedOverride[overrides.Length];

            for (var overrideIndex = 0; overrideIndex < overrides.Length; overrideIndex++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var prefabOverride = overrides[overrideIndex];
                var expandedEntityIndex = ResolveEntity(
                    prefabOverride.Target,
                    relativeAnchorOccurrenceIndex,
                    childOccurrenceIndicesByPlacement,
                    allowInstanceAbsoluteSources);
                resolvedOverrides[overrideIndex] = new ResolvedOverride(expandedEntityIndex, prefabOverride);
            }

            Array.Sort(resolvedOverrides, CompareResolvedOverrides);

            for (var index = 1; index < resolvedOverrides.Length; index++)
            {
                var previous = resolvedOverrides[index - 1];
                var current = resolvedOverrides[index];

                if ((previous.ExpandedEntityIndex != current.ExpandedEntityIndex)
                    || (previous.Value.ComponentSchema != current.Value.ComponentSchema))
                {
                    continue;
                }

                var pathComparison = ComparePropertyPaths(previous.Value.PropertyPath, current.Value.PropertyPath);

                if ((pathComparison == 0)
                    || IsPropertyPathPrefix(previous.Value.PropertyPath, current.Value.PropertyPath))
                {
                    throw new InvalidDataException("A resolved Scene override layer contains duplicate or ancestor/descendant property writes.");
                }
            }

            var scene = _sceneLease
                ?? throw new InvalidOperationException("The Scene expansion lost its Scene lease.");
            var overrideOwnerSource = GetSource(ownerOccurrenceIndex);

            for (var index = 0; index < resolvedOverrides.Length; index++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var resolved = resolvedOverrides[index];
                var expandedEntity = _entities[resolved.ExpandedEntityIndex];
                var componentIndex = FindEntityIndexForComponent(expandedEntity.Components, resolved.Value.ComponentSchema);

                if (componentIndex < 0)
                {
                    throw new InvalidDataException("A Scene override names a Component that is absent from its selected Entity.");
                }

                var referenceBindingIndex = FindReferenceBindingIndex(
                    _referenceBindings,
                    resolved.ExpandedEntityIndex,
                    resolved.Value.ComponentSchema,
                    resolved.Value.PropertyPath);
                SceneExpandedReferenceBinding? updatedReferenceBinding = null;

                if (resolved.Value.ReferenceValue is not null)
                {
                    if (referenceBindingIndex < 0)
                    {
                        throw new InvalidDataException("An Entity-reference override has no effective source binding to replace.");
                    }

                    if ((resolved.Value.ReferenceValue.Kind == SceneReferenceTargetKind.ExternalSlot)
                        && !ContainsExternalSlot(
                            overrideOwnerSource.ExternalSlots,
                            resolved.Value.ReferenceValue.ExternalSlotId.GetValueOrDefault()))
                    {
                        throw new InvalidDataException("An Entity-reference override names an undeclared ExternalSlot.");
                    }

                    updatedReferenceBinding = _referenceBindings[referenceBindingIndex] with {
                        OwnerOccurrenceIndex = ownerOccurrenceIndex,
                        TargetAnchorOccurrenceIndex = expandedEntity.OccurrenceIndex,
                        Target = resolved.Value.ReferenceValue,
                        IsPresent = true,
                        ResolvedTargetExpandedEntityIndex = null,
                        ResolvedExternalSlotBindingIndex = null,
                    };
                }
                else if (resolved.Value.Operation is ScenePrefabOverrideOperation.Omit)
                {
                    if (referenceBindingIndex >= 0)
                    {
                        updatedReferenceBinding = _referenceBindings[referenceBindingIndex] with {
                            IsPresent = false,
                            ResolvedTargetExpandedEntityIndex = null,
                            ResolvedExternalSlotBindingIndex = null,
                        };
                    }
                }
                else if (referenceBindingIndex >= 0)
                {
                    throw new InvalidDataException("A non-reference Set override collides with an Entity-reference binding.");
                }

                var updatedComponent = ScenePrefabOverrideApplier.Apply(
                    expandedEntity.Components[componentIndex],
                    resolved.Value,
                    scene.Value.Schemas,
                    scene.Value.ReadLimits);
                var updatedComponents = expandedEntity.Components.SetItem(componentIndex, updatedComponent);
                _entities[resolved.ExpandedEntityIndex] = expandedEntity with { Components = updatedComponents };

                if (updatedReferenceBinding is SceneExpandedReferenceBinding referenceBinding)
                {
                    _referenceBindings[referenceBindingIndex] = referenceBinding;
                }
            }
        }

        private void ApplyPlacementLayers(
            ImmutableArray<int>.Builder childOccurrenceIndicesByPlacement)
        {
            for (var occurrenceIndex = _occurrences.Count - 1; occurrenceIndex > 0; occurrenceIndex--)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var occurrence = _occurrences[occurrenceIndex];

                if ((occurrence.ParentOccurrenceIndex < 0)
                    || (occurrence.ParentOccurrenceIndex >= occurrenceIndex))
                {
                    throw new InvalidOperationException("A Prefab occurrence has an invalid parent order.");
                }

                var parentSource = GetSource(occurrence.ParentOccurrenceIndex);
                var placementIndex = FindPlacementIndex(parentSource.Placements, occurrence.PlacementId);

                if (placementIndex < 0)
                {
                    throw new InvalidDataException("An expanded Prefab occurrence has no source placement.");
                }

                var placement = parentSource.Placements[placementIndex];
                ApplyRootPoses(placement, occurrenceIndex, childOccurrenceIndicesByPlacement);
                ApplyOverrideLayer(
                    placement.Overrides,
                    occurrenceIndex,
                    occurrence.ParentOccurrenceIndex,
                    childOccurrenceIndicesByPlacement,
                    allowInstanceAbsoluteSources: false);
            }
        }

        private void ApplyRootPoses(
            ScenePrefabPlacement placement,
            int childOccurrenceIndex,
            ImmutableArray<int>.Builder childOccurrenceIndicesByPlacement)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var occurrence = _occurrences[childOccurrenceIndex];
            var rootCount = occurrence.RootEntityCount;

            if (placement.RootPoses.Length != rootCount)
            {
                throw new InvalidDataException("A PrefabPlacement RootPoses table does not match its expanded root count.");
            }

            for (var rootIndex = 1; rootIndex < rootCount; rootIndex++)
            {
                if (_rootEntityIndicesByOccurrence[occurrence.RootEntityIndicesStart + rootIndex - 1]
                    >= _rootEntityIndicesByOccurrence[occurrence.RootEntityIndicesStart + rootIndex])
                {
                    throw new InvalidOperationException("Expanded root Entity indices are not in stable traversal order.");
                }
            }

            var coveredRoots = rootCount == 0 ? [] : new bool[rootCount];

            for (var rootPoseIndex = 0; rootPoseIndex < placement.RootPoses.Length; rootPoseIndex++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var rootPose = placement.RootPoses[rootPoseIndex];
                var expandedEntityIndex = ResolveEntity(
                    rootPose.Target,
                    childOccurrenceIndex,
                    childOccurrenceIndicesByPlacement,
                    allowInstanceAbsolute: false);
                var rootSlot = FindRootSlot(
                    occurrence.RootEntityIndicesStart,
                    rootCount,
                    expandedEntityIndex);

                if (rootSlot < 0)
                {
                    throw new InvalidDataException("A Prefab RootPose target is not a final expanded root.");
                }

                if (coveredRoots[rootSlot])
                {
                    throw new InvalidDataException("A PrefabPlacement RootPoses table targets one final root more than once.");
                }

                coveredRoots[rootSlot] = true;
                var rootPosition = placement.ParentEntityId is null
                    ? SceneEntityReader.CreateRootPositionRecord(
                        (rootPose.RootPositionPayload
                            ?? throw new InvalidDataException("A root PrefabPlacement RootPose has no RootPosition.")).AsSpan())
                    : null;
                var expandedEntity = _entities[expandedEntityIndex];
                var components = ApplyRootPoseComponents(
                    expandedEntity.Components,
                    rootPose.LocalTransform,
                    rootPosition);
                _entities[expandedEntityIndex] = expandedEntity with { Components = components };
            }
        }

        private void BuildReferenceBindings(
            ImmutableArray<int>.Builder childOccurrenceIndicesByPlacement)
        {
            for (var occurrenceIndex = 0; occurrenceIndex < _occurrences.Count; occurrenceIndex++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var source = GetSource(occurrenceIndex);

                for (var bindingIndex = 0; bindingIndex < source.References.Length; bindingIndex++)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    var binding = source.References[bindingIndex];
                    var sourceEntityIndex = ResolveEntity(
                        binding.Source,
                        occurrenceIndex,
                        childOccurrenceIndicesByPlacement,
                        allowInstanceAbsolute: !source.IsPrefab);
                    _referenceBindings.Add(
                        new SceneExpandedReferenceBinding(
                            sourceEntityIndex,
                            binding.Source.Scope,
                            binding.ComponentSchema,
                            binding.PropertyPath,
                            occurrenceIndex,
                            occurrenceIndex,
                            binding.Target,
                            IsPresent: true,
                            ResolvedTargetExpandedEntityIndex: null,
                            ResolvedExternalSlotBindingIndex: null));
                }
            }

            if (_referenceBindings.Count != _referenceBindingCount)
            {
                throw new InvalidOperationException("The expanded Entity-reference binding count did not match its preflight.");
            }

            _referenceBindings.Sort(CompareReferenceBindings);

            for (var index = 1; index < _referenceBindings.Count; index++)
            {
                if (CompareReferenceKeys(_referenceBindings[index - 1], _referenceBindings[index]) == 0)
                {
                    throw new InvalidDataException("Multiple Entity-reference bindings resolve to the same expanded source leaf.");
                }
            }
        }

        private void BuildResolvedExternalSlotBindings(
            ImmutableArray<int>.Builder childOccurrenceIndicesByPlacement)
        {
            var prefabLeases = _prefabLeases
                ?? throw new InvalidOperationException("The Scene expansion lost its Prefab lease set.");

            for (var occurrenceIndex = 0; occurrenceIndex < _occurrences.Count; occurrenceIndex++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var occurrence = _occurrences[occurrenceIndex];
                var source = GetSource(occurrenceIndex);

                if (occurrenceIndex == 0)
                {
                    for (var slotIndex = 0; slotIndex < source.ExternalSlots.Length; slotIndex++)
                    {
                        var slot = source.ExternalSlots[slotIndex];
                        _externalSlotBindings.Add(
                            new SceneExpandedExternalSlotBinding(
                                occurrenceIndex,
                                slot.SlotId,
                                TargetExpandedEntityIndex: null,
                                RootExternalSlotId: slot.SlotId));
                    }

                    continue;
                }

                if ((occurrence.ParentOccurrenceIndex < 0)
                    || (occurrence.ParentOccurrenceIndex >= occurrenceIndex)
                    || !prefabLeases.TryGetValue(occurrence.AssetId, out var childPrefabLease))
                {
                    throw new InvalidOperationException("An expanded Prefab occurrence has no parent or retained asset lease.");
                }

                var parentSource = GetSource(occurrence.ParentOccurrenceIndex);
                var placementIndex = FindPlacementIndex(parentSource.Placements, occurrence.PlacementId);

                if (placementIndex < 0)
                {
                    throw new InvalidDataException("An expanded Prefab occurrence has no source placement.");
                }

                var placement = parentSource.Placements[placementIndex];
                var resolvedBindings = ScenePrefabSlotBindingResolver.Resolve(
                    placement,
                    childPrefabLease.Value,
                    parentSource.ExternalSlots,
                    selector => {
                        var targetEntityIndex = ResolveEntity(
                            selector,
                            occurrence.ParentOccurrenceIndex,
                            childOccurrenceIndicesByPlacement,
                            allowInstanceAbsolute: !parentSource.IsPrefab);
                        return _entities[targetEntityIndex].Components;
                    },
                    allowInstanceAbsoluteInternalTargets: !parentSource.IsPrefab);

                for (var bindingIndex = 0; bindingIndex < resolvedBindings.Length; bindingIndex++)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    var binding = resolvedBindings[bindingIndex];
                    int? targetExpandedEntityIndex = null;
                    Guid? rootExternalSlotId = null;

                    if (binding.Target?.Kind is SceneReferenceTargetKind.Internal)
                    {
                        targetExpandedEntityIndex = ResolveEntity(
                            binding.Target.Entity
                                ?? throw new InvalidDataException("An Internal slot binding has no EntitySelector."),
                            occurrence.ParentOccurrenceIndex,
                            childOccurrenceIndicesByPlacement,
                            allowInstanceAbsolute: !parentSource.IsPrefab);
                    }
                    else if (binding.Target?.Kind is SceneReferenceTargetKind.ExternalSlot)
                    {
                        var forwardedSlotId = binding.Target.ExternalSlotId
                            ?? throw new InvalidDataException("An ExternalSlot binding has no ExternalSlotId.");
                        var forwardedBindingIndex = FindExpandedExternalSlotBindingIndex(
                            _externalSlotBindings,
                            occurrence.ParentOccurrenceIndex,
                            forwardedSlotId);

                        if (forwardedBindingIndex < 0)
                        {
                            throw new InvalidDataException("A placement forwards a child slot to an unresolved containing slot.");
                        }

                        var forwardedBinding = _externalSlotBindings[forwardedBindingIndex];
                        targetExpandedEntityIndex = forwardedBinding.TargetExpandedEntityIndex;
                        rootExternalSlotId = forwardedBinding.RootExternalSlotId;
                    }

                    _externalSlotBindings.Add(
                        new SceneExpandedExternalSlotBinding(
                            occurrenceIndex,
                            binding.SlotId,
                            targetExpandedEntityIndex,
                            rootExternalSlotId));
                }
            }

            if (_externalSlotBindings.Count != _externalSlotBindingCount)
            {
                throw new InvalidOperationException("Expanded ExternalSlot binding count did not match its preflight.");
            }
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

            var scene = _sceneLease
                ?? throw new InvalidOperationException("The Scene expansion lost its Scene lease.");

            if (!ReferenceEquals(source.Schemas, scene.Value.Schemas)
                || !ReferenceEquals(source.ReadLimits, scene.Value.ReadLimits))
            {
                throw new InvalidDataException("A Scene expansion cannot combine assets decoded from different package contexts.");
            }

            if (source.References.Length > (int.MaxValue - _referenceBindingCount))
            {
                throw new InvalidDataException("The expanded Entity-reference binding count exceeds the supported range.");
            }

            _referenceBindingCount += source.References.Length;

            if (source.ExternalSlots.Length > (int.MaxValue - _externalSlotBindingCount))
            {
                throw new InvalidDataException("The expanded ExternalSlot binding count exceeds the supported range.");
            }

            _externalSlotBindingCount += source.ExternalSlots.Length;
            _externalSlotBindingResolutionBytes = checked(
                _externalSlotBindingResolutionBytes
                + ArrayBytes(source.ExternalSlots.Length, IntPtr.Size)
                + (checked((long)source.ExternalSlots.Length) * 32L));

            for (var bindingIndex = 0; bindingIndex < source.References.Length; bindingIndex++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                _referenceBindingValidationBytes = checked(
                    _referenceBindingValidationBytes
                    + 64L
                    + checked(
                        (long)source.References[bindingIndex].PropertyPath.Length
                        * Unsafe.SizeOf<ScenePropertyPathSegment>()));
            }

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

        private async ValueTask CountOverrideLayerAsync(
            SceneExpansionSource source,
            ImmutableArray<ScenePrefabOverride> overrides,
            bool allowInstanceAbsoluteSources)
        {
            if (overrides.IsDefault)
            {
                throw new InvalidDataException("A Scene override layer is uninitialized.");
            }

            if (overrides.IsEmpty)
            {
                return;
            }

            _overrideLayerBytes = checked(
                _overrideLayerBytes
                + ArrayBytes(overrides.Length, Unsafe.SizeOf<ResolvedOverride>()));

            for (var overrideIndex = 0; overrideIndex < overrides.Length; overrideIndex++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var prefabOverride = overrides[overrideIndex];
                var sourceEntity = await GetSelectorTargetSourceEntityAsync(
                    source,
                    prefabOverride.Target,
                    allowInstanceAbsoluteSources,
                    requireFinalRoot: false).ConfigureAwait(false);
                var componentIndex = FindEntityIndexForComponent(sourceEntity.Components, prefabOverride.ComponentSchema);

                if (componentIndex < 0)
                {
                    throw new InvalidDataException("A Scene override names a Component that is absent from its selected Entity.");
                }

                var component = sourceEntity.Components[componentIndex];
                _overrideComponentArrayBytes = checked(
                    _overrideComponentArrayBytes
                    + ArrayBytes(checked(sourceEntity.Components.Length + 1), IntPtr.Size));
                _overrideComponentRecordBytes = checked(
                    _overrideComponentRecordBytes
                    + checked(2L * component.EncodedRecord.Length));
            }
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
                var rootCount = await CountOccurrenceAsync(
                    new SceneExpansionSource(childPrefab),
                    childPrefabDepth,
                    parentHierarchyDepth).ConfigureAwait(false);

                if (placement.RootPoses.Length != rootCount)
                {
                    throw new InvalidDataException("A PrefabPlacement RootPoses table must cover every final expanded root exactly once.");
                }

                if (rootCount > 0)
                {
                    _rootPoseTargetScratchBytes = checked(
                        _rootPoseTargetScratchBytes
                        + ArrayBytes(rootCount, sizeof(bool)));
                }

                for (var rootPoseIndex = 0; rootPoseIndex < placement.RootPoses.Length; rootPoseIndex++)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    var rootPose = placement.RootPoses[rootPoseIndex];

                    if (placement.ParentEntityId is null)
                    {
                        if ((rootPose.RootPositionPayload is null)
                            || (rootPose.LocalTransformPosition.X != 0.0f)
                            || (rootPose.LocalTransformPosition.Y != 0.0f)
                            || (rootPose.LocalTransformPosition.Z != 0.0f))
                        {
                            throw new InvalidDataException("A root PrefabPlacement requires a RootPosition and zero LocalTransform.Position for every RootPose.");
                        }

                        _rootPositionComponentBytes = checked(
                            _rootPositionComponentBytes
                            + SceneEntityReader.RootPositionRecordByteLength);
                    }
                    else if (rootPose.RootPositionPayload is not null)
                    {
                        throw new InvalidDataException("A child PrefabPlacement RootPose cannot contain a RootPosition.");
                    }

                    var sourceEntity = await GetSelectorTargetSourceEntityAsync(
                        new SceneExpansionSource(childPrefab),
                        rootPose.Target,
                        allowInstanceAbsoluteSources: false,
                        requireFinalRoot: true).ConfigureAwait(false);
                    _rootPoseComponentArrayBytes = checked(
                        _rootPoseComponentArrayBytes
                        + ArrayBytes(checked(sourceEntity.Components.Length + 1), IntPtr.Size));
                }

                await CountOverrideLayerAsync(
                    new SceneExpansionSource(childPrefab),
                    placement.Overrides,
                    allowInstanceAbsoluteSources: false).ConfigureAwait(false);
                return rootCount;
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
                    hierarchyDepth,
                    sourceEntity.Components));
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

        private int FindRootSlot(int rootStart, int rootCount, int expandedEntityIndex)
        {
            var low = 0;
            var high = rootCount - 1;

            while (low <= high)
            {
                var middle = low + ((high - low) / 2);
                var comparison = _rootEntityIndicesByOccurrence[rootStart + middle].CompareTo(expandedEntityIndex);

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
                var sceneLease = _sceneLease
                    ?? throw new InvalidOperationException("The Prefab was requested before the Scene lease was acquired.");

                if (!ReferenceEquals(value.Schemas, sceneLease.Value.Schemas)
                    || !ReferenceEquals(value.ReadLimits, sceneLease.Value.ReadLimits))
                {
                    throw new InvalidDataException("A Scene expansion cannot combine assets decoded from different package contexts.");
                }

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
            requiredBytes = checked(requiredBytes + _rootPositionComponentBytes);
            requiredBytes = checked(requiredBytes + _rootPoseComponentArrayBytes);
            requiredBytes = checked(requiredBytes + _rootPoseTargetScratchBytes);
            requiredBytes = checked(requiredBytes + _overrideComponentArrayBytes);
            requiredBytes = checked(requiredBytes + _overrideComponentRecordBytes);
            requiredBytes = checked(requiredBytes + _overrideLayerBytes);
            requiredBytes = checked(
                requiredBytes
                + ArrayBytes(
                    _externalSlotBindingCount,
                    Unsafe.SizeOf<SceneExpandedExternalSlotBinding>())
                + _externalSlotBindingResolutionBytes);
            requiredBytes = checked(
                requiredBytes
                + checked(2L * ArrayBytes(
                    _referenceBindingCount,
                    Unsafe.SizeOf<SceneExpandedReferenceBinding>())));
            requiredBytes = checked(requiredBytes + _referenceBindingValidationBytes);
            requiredBytes = checked(
                requiredBytes
                + ArrayBytes(_referenceBindingCount, IntPtr.Size)
                + _referenceBindingValidationBytes);

            if (_rootPositionComponentBytes > 0)
            {
                requiredBytes = checked(requiredBytes + SceneEntityReader.RootPositionRecordByteLength);
            }

            requiredBytes = checked(requiredBytes + ArrayBytes(_entityCount, Unsafe.SizeOf<Guid>()));
            requiredBytes = checked(requiredBytes + ArrayBytes(_occurrenceCount, Unsafe.SizeOf<Guid>()));
            requiredBytes = checked(requiredBytes + ArrayBytes(_occurrenceCount, sizeof(int)));
            requiredBytes = checked(requiredBytes + (checked((long)prefabLeases.Count) * 256L));
            return checked(requiredBytes + 2048L);
        }

        private async ValueTask<SceneEntity> GetSelectorTargetSourceEntityAsync(
            SceneExpansionSource source,
            SceneEntitySelector selector,
            bool allowInstanceAbsoluteSources,
            bool requireFinalRoot)
        {
            if ((selector.Scope is not SceneEntitySelectorScope.OccurrenceRelative
                    and not SceneEntitySelectorScope.InstanceAbsolute)
                || ((selector.Scope is SceneEntitySelectorScope.InstanceAbsolute)
                    && !allowInstanceAbsoluteSources)
                || selector.PlacementPath.IsDefault
                || (selector.PlacementPath.Length > 32)
                || (selector.AuthoredEntityId == Guid.Empty))
            {
                throw new InvalidDataException("A Scene EntitySelector is invalid at its selected owner boundary.");
            }

            for (var pathIndex = 0; pathIndex < selector.PlacementPath.Length; pathIndex++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var placementIndex = FindPlacementIndex(source.Placements, selector.PlacementPath[pathIndex]);

                if (placementIndex < 0)
                {
                    throw new InvalidDataException("A Prefab RootPose target placement path does not exist.");
                }

                var nestedPlacement = source.Placements[placementIndex];

                if (requireFinalRoot && (nestedPlacement.ParentEntityId is not null))
                {
                    throw new InvalidDataException("A RootPose target path must follow root placements to a final expanded root.");
                }

                source = new SceneExpansionSource(await GetPrefabAsync(nestedPlacement.PrefabAssetId).ConfigureAwait(false));
            }

            var sourceEntityIndex = FindEntityIndex(source.Entities, selector.AuthoredEntityId);

            if (sourceEntityIndex < 0)
            {
                throw new InvalidDataException("A Prefab RootPose target Entity does not exist in its selected occurrence.");
            }

            var hierarchyNodeIndex = FindHierarchyNodeIndex(source.Hierarchy.Nodes, selector.AuthoredEntityId);

            if ((hierarchyNodeIndex < 0)
                || (requireFinalRoot && (source.Hierarchy.Nodes[hierarchyNodeIndex].ParentEntityId is not null)))
            {
                throw new InvalidDataException("The selected Entity is not a final expanded root where one is required.");
            }

            return source.Entities[sourceEntityIndex];
        }

        private SceneExpansionSource GetSource(int occurrenceIndex)
        {
            var assetId = _occurrences[occurrenceIndex].AssetId;
            var sceneLease = _sceneLease
                ?? throw new ObjectDisposedException(nameof(SceneExpansionBuilder));

            if (assetId == sceneLease.AssetId)
            {
                return new SceneExpansionSource(sceneLease.Value);
            }

            var prefabLeases = _prefabLeases
                ?? throw new ObjectDisposedException(nameof(SceneExpansionBuilder));

            if (prefabLeases.TryGetValue(assetId, out var prefabLease))
            {
                return new SceneExpansionSource(prefabLease.Value);
            }

            throw new InvalidOperationException("The Scene expansion does not retain the selected Prefab asset lease.");
        }

        private int ResolveEntity(
            SceneEntitySelector selector,
            int relativeAnchorOccurrenceIndex,
            ImmutableArray<int>.Builder childOccurrenceIndicesByPlacement,
            bool allowInstanceAbsolute)
        {
            if (selector.PlacementPath.IsDefault
                || (selector.PlacementPath.Length > 32)
                || (selector.AuthoredEntityId == Guid.Empty))
            {
                throw new InvalidDataException("A Scene EntitySelector is invalid.");
            }

            var occurrenceIndex = selector.Scope switch {
                SceneEntitySelectorScope.OccurrenceRelative => relativeAnchorOccurrenceIndex,
                SceneEntitySelectorScope.InstanceAbsolute when allowInstanceAbsolute => 0,
                _ => throw new InvalidDataException("The Scene EntitySelector scope is not allowed at this owner boundary."),
            };

            for (var pathIndex = 0; pathIndex < selector.PlacementPath.Length; pathIndex++)
            {
                var occurrence = _occurrences[occurrenceIndex];
                var source = GetSource(occurrenceIndex);
                var placementIndex = FindPlacementIndex(source.Placements, selector.PlacementPath[pathIndex]);

                if (placementIndex < 0)
                {
                    throw new InvalidDataException("A Prefab RootPose EntitySelector placement path does not exist.");
                }

                occurrenceIndex = childOccurrenceIndicesByPlacement[
                    occurrence.ChildPlacementMapStart + placementIndex];

                if (occurrenceIndex < 0)
                {
                    throw new InvalidDataException("A Prefab RootPose EntitySelector placement was not expanded.");
                }
            }

            var targetSource = GetSource(occurrenceIndex);
            var sourceEntityIndex = FindEntityIndex(targetSource.Entities, selector.AuthoredEntityId);

            if (sourceEntityIndex < 0)
            {
                throw new InvalidDataException("A Prefab RootPose EntitySelector Entity does not exist.");
            }

            var targetOccurrence = _occurrences[occurrenceIndex];
            var expandedEntityIndex = _entityIndicesByOccurrence[
                targetOccurrence.EntityMapStart + sourceEntityIndex];

            if (expandedEntityIndex < 0)
            {
                throw new InvalidDataException("A Prefab RootPose EntitySelector Entity was not expanded.");
            }

            return expandedEntityIndex;
        }

        private void ValidateExpandedReferenceBindings(
            ImmutableArray<int>.Builder childOccurrenceIndicesByPlacement)
        {
            var sceneLease = _sceneLease
                ?? throw new InvalidOperationException("The Scene expansion lost its Scene lease.");
            var schemas = sceneLease.Value.Schemas;
            var readLimits = sceneLease.Value.ReadLimits;
            var referenceIndex = 0;

            for (var bindingIndex = 0; bindingIndex < _referenceBindings.Count; bindingIndex++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var binding = _referenceBindings[bindingIndex];

                if (!binding.IsPresent)
                {
                    continue;
                }

                var ownerSource = GetSource(binding.OwnerOccurrenceIndex);
                int? resolvedTargetExpandedEntityIndex = null;
                int? resolvedExternalSlotBindingIndex = null;

                if (binding.Target.Kind is SceneReferenceTargetKind.Internal)
                {
                    resolvedTargetExpandedEntityIndex = ResolveEntity(
                        binding.Target.Entity
                            ?? throw new InvalidDataException("An Internal Entity-reference target has no EntitySelector."),
                        binding.TargetAnchorOccurrenceIndex,
                        childOccurrenceIndicesByPlacement,
                        allowInstanceAbsolute: !ownerSource.IsPrefab);
                }
                else if (binding.Target.Kind is SceneReferenceTargetKind.ExternalSlot)
                {
                    var externalSlotId = binding.Target.ExternalSlotId.GetValueOrDefault();

                    if (!ContainsExternalSlot(
                        ownerSource.ExternalSlots,
                        externalSlotId))
                    {
                        throw new InvalidDataException("An Entity-reference binding names an undeclared ExternalSlot.");
                    }

                    resolvedExternalSlotBindingIndex = FindExpandedExternalSlotBindingIndex(
                        _externalSlotBindings,
                        binding.OwnerOccurrenceIndex,
                        externalSlotId);

                    if (resolvedExternalSlotBindingIndex is not int externalSlotBindingIndex)
                    {
                        throw new InvalidDataException("An Entity-reference binding names an unresolved ExternalSlot.");
                    }

                    resolvedTargetExpandedEntityIndex =
                        _externalSlotBindings[externalSlotBindingIndex].TargetExpandedEntityIndex;
                }
                else
                {
                    throw new InvalidDataException("An Entity-reference binding has an unknown target kind.");
                }

                _referenceBindings[bindingIndex] = binding with {
                    ResolvedTargetExpandedEntityIndex = resolvedTargetExpandedEntityIndex,
                    ResolvedExternalSlotBindingIndex = resolvedExternalSlotBindingIndex,
                };
            }

            for (var entityIndex = 0; entityIndex < _entities.Count; entityIndex++)
            {
                var entity = _entities[entityIndex];
                var localTransformSchema = new ContentSchemaReference(
                    SceneEntityReader.LocalTransformSchemaId,
                    1,
                    0,
                    ContentSchemaScope.Content);
                var rootPositionSchema = new ContentSchemaReference(
                    SceneEntityReader.RootPositionSchemaId,
                    1,
                    0,
                    ContentSchemaScope.Content);
                var localTransformIndex = FindEntityIndexForComponent(entity.Components, localTransformSchema);

                if (localTransformIndex < 0)
                {
                    throw new InvalidDataException("An expanded Entity has no LocalTransform component.");
                }

                var localTransformPosition = SceneEntityReader.ReadLocalTransformPosition(
                    entity.Components[localTransformIndex],
                    readLimits);
                var rootPositionIndex = FindEntityIndexForComponent(entity.Components, rootPositionSchema);
                var isRoot = entity.ParentEntityIndex < 0;

                if (isRoot != (rootPositionIndex >= 0))
                {
                    throw new InvalidDataException(
                        isRoot
                            ? "An expanded root Entity requires a RootPosition component."
                            : "An expanded child Entity cannot contain a RootPosition component.");
                }

                if (rootPositionIndex >= 0)
                {
                    SceneEntityReader.ValidateRootPositionComponentRecord(
                        entity.Components[rootPositionIndex],
                        readLimits);
                }

                if (isRoot
                    && ((localTransformPosition.X != 0.0f)
                        || (localTransformPosition.Y != 0.0f)
                        || (localTransformPosition.Z != 0.0f)))
                {
                    throw new InvalidDataException("An expanded root Entity must have zero LocalTransform.Position.");
                }

                while (referenceIndex < _referenceBindings.Count
                    && (_referenceBindings[referenceIndex].ExpandedEntityIndex < entityIndex))
                {
                    if (_referenceBindings[referenceIndex].IsPresent)
                    {
                        throw new InvalidDataException("An Entity-reference binding names an absent expanded Entity.");
                    }

                    referenceIndex++;
                }

                for (var componentIndex = 0; componentIndex < entity.Components.Length; componentIndex++)
                {
                    var component = entity.Components[componentIndex];

                    while (referenceIndex < _referenceBindings.Count
                        && (_referenceBindings[referenceIndex].ExpandedEntityIndex == entityIndex)
                        && (CompareSchemaReferences(
                            _referenceBindings[referenceIndex].ComponentSchema,
                            component.Schema) < 0))
                    {
                        if (_referenceBindings[referenceIndex].IsPresent)
                        {
                            throw new InvalidDataException("An Entity-reference binding names a missing Component.");
                        }

                        referenceIndex++;
                    }

                    var bindingStart = referenceIndex;

                    while (referenceIndex < _referenceBindings.Count
                        && (_referenceBindings[referenceIndex].ExpandedEntityIndex == entityIndex)
                        && (_referenceBindings[referenceIndex].ComponentSchema == component.Schema))
                    {
                        referenceIndex++;
                    }

                    if (!schemas.TryGetSchema(component.Schema, out var metadata))
                    {
                        throw new NotSupportedException($"The expanded Component schema {component.Schema} is not registered.");
                    }

                    if (metadata.Role is not ContentSchemaRole.Component and not ContentSchemaRole.EntityTag)
                    {
                        throw new InvalidDataException("The expanded Entity contains a non-component schema.");
                    }

                    if (metadata.Role is ContentSchemaRole.EntityTag)
                    {
                        for (var bindingIndex = bindingStart; bindingIndex < referenceIndex; bindingIndex++)
                        {
                            if (_referenceBindings[bindingIndex].IsPresent)
                            {
                                throw new InvalidDataException("An Entity-reference binding cannot target an EntityTag.");
                            }
                        }

                        continue;
                    }

                    var activeBindingCount = 0;

                    for (var bindingIndex = bindingStart; bindingIndex < referenceIndex; bindingIndex++)
                    {
                        if (_referenceBindings[bindingIndex].IsPresent)
                        {
                            activeBindingCount++;
                        }
                    }

                    var localBindings = new SceneEntityReferenceBinding[activeBindingCount];
                    var localBindingIndex = 0;

                    for (var bindingIndex = bindingStart; bindingIndex < referenceIndex; bindingIndex++)
                    {
                        var expandedBinding = _referenceBindings[bindingIndex];

                        if (expandedBinding.IsPresent)
                        {
                            localBindings[localBindingIndex++] = new SceneEntityReferenceBinding(
                                new SceneEntitySelector(
                                    expandedBinding.SourceScope,
                                    [],
                                    entity.AuthoredEntityId),
                                expandedBinding.ComponentSchema,
                                expandedBinding.PropertyPath,
                                expandedBinding.Target);
                        }
                    }

                    Array.Sort(
                        localBindings,
                        static (left, right) => {
                            var comparison = left.Source.Scope.CompareTo(right.Source.Scope);

                            if (comparison != 0)
                            {
                                return comparison;
                            }

                            return ComparePropertyPaths(left.PropertyPath, right.PropertyPath);
                        });
                    _ = SceneEntityReferenceBindingValidator.ValidateEncodedComponentRecord(
                        component,
                        entity.AuthoredEntityId,
                        metadata,
                        [.. localBindings],
                        schemas,
                        readLimits,
                        allowInstanceAbsoluteSources: true);
                }

                while (referenceIndex < _referenceBindings.Count
                    && (_referenceBindings[referenceIndex].ExpandedEntityIndex == entityIndex))
                {
                    if (_referenceBindings[referenceIndex].IsPresent)
                    {
                        throw new InvalidDataException("An Entity-reference binding names a Component that is absent from its Entity.");
                    }

                    referenceIndex++;
                }
            }

            while (referenceIndex < _referenceBindings.Count)
            {
                if (_referenceBindings[referenceIndex].IsPresent)
                {
                    throw new InvalidDataException("An Entity-reference binding names an absent expanded Entity.");
                }

                referenceIndex++;
            }
        }

        private readonly record struct ResolvedOverride(
            int ExpandedEntityIndex,
            ScenePrefabOverride Value);
    }
}
