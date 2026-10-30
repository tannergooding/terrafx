// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading;

namespace TerraFX.Entities;

public sealed partial class World
{
    /// <summary>Adds one registered component initialized through its default provider.</summary>
    /// <typeparam name="T">The unmanaged component type.</typeparam>
    /// <param name="entity">The live Entity to update.</param>
    /// <exception cref="ArgumentException"><paramref name="entity"/> is default, stale, or belongs to another World.</exception>
    /// <exception cref="InvalidOperationException">The World is not idle on its owner thread, T is unregistered, or the Entity already has T.</exception>
    public void Add<T>(Entity entity)
        where T : unmanaged
    {
        EnsureWorldReadAccess();
        var value = default(T);
        MigrateEntity(entity, typeof(T), addComponent: true, in value, useDefault: true, null, addTag: false);
    }

    /// <summary>Adds one registered component with an explicit initial value.</summary>
    /// <typeparam name="T">The unmanaged component type.</typeparam>
    /// <param name="entity">The live Entity to update.</param>
    /// <param name="value">The copied component value.</param>
    /// <exception cref="ArgumentException"><paramref name="entity"/> is default, stale, or belongs to another World.</exception>
    /// <exception cref="InvalidOperationException">The World is not idle on its owner thread, T is unregistered, or the Entity already has T.</exception>
    public void Add<T>(Entity entity, in T value)
        where T : unmanaged
    {
        EnsureWorldReadAccess();
        MigrateEntity(entity, typeof(T), addComponent: true, in value, useDefault: false, null, addTag: false);
    }

    /// <summary>Adds one registered zero-payload tag.</summary>
    /// <param name="entity">The live Entity to update.</param>
    /// <param name="schemaId">The registered tag schema identity.</param>
    /// <exception cref="ArgumentException"><paramref name="entity"/> is default, stale, or belongs to another World.</exception>
    /// <exception cref="KeyNotFoundException"><paramref name="schemaId"/> is not registered.</exception>
    /// <exception cref="InvalidOperationException">The World is not idle on its owner thread or the Entity already has the tag.</exception>
    public void AddTag(Entity entity, Guid schemaId)
    {
        EnsureWorldReadAccess();
        ArgumentOutOfRangeException.ThrowIfEqual(schemaId, Guid.Empty);
        MigrateTag(entity, schemaId, addTag: true);
    }

    /// <summary>Destroys one live Entity and retires its stable identity.</summary>
    /// <param name="entity">The live Entity to destroy.</param>
    /// <param name="mode">The selected hierarchy-destruction mode.</param>
    /// <exception cref="ArgumentException"><paramref name="entity"/> is default, stale, or belongs to another World.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a defined value.</exception>
    /// <exception cref="InvalidOperationException">The World is not idle on its owner thread, an identity row is missing, capacity preparation fails, a live attachment references the Entity, or a transferred Release callback fails after destruction.</exception>
    public void DestroyEntity(Entity entity, HierarchyDestroyMode mode)
    {
        EnterStructuralOperation();

        var oldOwnedBytes = 0L;
        var preparationBytes = 0L;
        var cleanupStarted = false;
        var preparationReserved = false;
        WorldAttachmentTable.AttachmentEntry[] detachedAttachments;
        WorldAttachmentReleaseBinding[] releaseBindings;
        WorldAttachmentReleasePreparation? releasePreparation;
        WorldAttachmentQuarantineReservation? quarantineReservation = null;
        StableIdentityLedger.PreparedChange? identityChange = null;

        try
        {
            try
            {
                if (mode is not HierarchyDestroyMode.DetachChildrenKeepWorld and not HierarchyDestroyMode.DestroySubtree)
                {
                    throw new ArgumentOutOfRangeException(nameof(mode));
                }

                var archetype = GetLiveArchetype(entity, out _, out _);
                var ordinal = _identityLedger.GetLiveOrdinal(IdentityKind.Entity, entity.Index);

                if (ordinal == 0)
                {
                    throw new InvalidOperationException("The live Entity has no stable identity assignment.");
                }

                var entityPersistentId = _identityLedger.GetAssignedId(IdentityKind.Entity, entity.Index);
                var attachmentReleaseCount = _attachments.CountTransferredOwner(entityPersistentId);

                var generationCanBeReused = entity.Generation != uint.MaxValue;
                var requiredFreeSlotCount = checked(_entitySlots.FreeSlotCount + (generationCanBeReused ? 1 : 0));
                var slotPreparationBytes = _entitySlots.GetCapacityPreparationBytes(
                    _entitySlots.AllocatedSlotCount,
                    requiredFreeSlotCount);
                var identityPreparationBytes = _identityLedger.GetPreparationByteReservation(0, 1);
                var identityGrowthBytes = checked(identityPreparationBytes - _identityLedger.OwnedByteReservation);
                var attachmentPreparationBytes = (attachmentReleaseCount == 0)
                    ? 0
                    : checked(64L + ((long)attachmentReleaseCount * 256L));
                oldOwnedBytes = GetWorldOwnedStateBytes();
                preparationBytes = checked(slotPreparationBytes + identityGrowthBytes + attachmentPreparationBytes);
                var nextMutationRevision = checked(_canonicalMutationRevision + 1);
                releasePreparation = (attachmentReleaseCount == 0)
                    ? null
                    : new WorldAttachmentReleasePreparation(_worldGuid, attachmentReleaseCount, nextMutationRevision);

                if (checked(oldOwnedBytes + preparationBytes) > _maximumOwnedStateBytes)
                {
                    throw new InvalidOperationException("The World-owned state budget cannot admit Entity destruction preparation.");
                }

                if ((preparationBytes != 0) && !_domainReservation.TryReserveAdditional(preparationBytes))
                {
                    throw new InvalidOperationException("The admission domain cannot reserve Entity destruction preparation.");
                }

                preparationReserved = preparationBytes != 0;
                detachedAttachments = _attachments.CaptureTransferredOwnerEntries(entityPersistentId);
                releaseBindings = PrepareAttachmentReleaseBindings(detachedAttachments);
                quarantineReservation = WorldAttachmentInstanceRegistry.ReserveQuarantineCapacity(detachedAttachments.Length);
                identityChange = _identityLedger.Prepare([], [ordinal], _maximumOwnedStateBytes);

                if (!_entitySlots.PrepareCapacity(_entitySlots.AllocatedSlotCount, requiredFreeSlotCount))
                {
                    throw new InvalidOperationException("The preflighted Entity-slot retirement capacity could not be prepared.");
                }

                identityChange.ValidateForCommit();
                ValidateNoEntityReferencesTo(entity);
                _ = archetype.Storage.Remove(entity, _entitySlots);
                _entitySlots.Release(entity.Index, entity.Generation);
                identityChange.Commit();
                _attachments.RemoveOwner(entityPersistentId);
                _canonicalMutationRevision = nextMutationRevision;
                RebuildComponentTagWorkPlan();
            }
            catch
            {
                Cleanup();
                throw;
            }

            if (detachedAttachments.Length != 0)
            {
                var preparedRelease = releasePreparation
                    ?? throw new InvalidOperationException("The transferred attachment release report was not prepared.");
                ReleaseAttachmentEntries(
                    detachedAttachments,
                    releaseBindings,
                    quarantineReservation
                        ?? throw new InvalidOperationException("The transferred attachment quarantine was not prepared."),
                    preparedRelease);
                Cleanup();

                if (preparedRelease.HasFailures)
                {
                    throw preparedRelease.CreateException(systemCleanupReport: null);
                }
            }

            Cleanup();
        }
        finally
        {
            identityChange?.Dispose();
            quarantineReservation?.Release();

            if (preparationReserved && !cleanupStarted)
            {
                Cleanup();
            }

            _ = Interlocked.Exchange(ref _isStructuralOperationActive, 0);
        }

        void Cleanup()
        {
            if (cleanupStarted)
            {
                return;
            }

            cleanupStarted = true;

            if ((preparationBytes == 0) || !preparationReserved)
            {
                return;
            }

            var persistentGrowthBytes = checked(GetWorldOwnedStateBytes() - oldOwnedBytes);
            var temporaryBytes = preparationBytes - persistentGrowthBytes;

            if (temporaryBytes > 0)
            {
                _domainReservation.ReleaseAdditional(temporaryBytes);
            }
            else if (temporaryBytes < 0 && !_domainReservation.TryReserveAdditional(-temporaryBytes))
            {
                throw new InvalidOperationException("The admission domain cannot retain prepared Entity-destruction storage.");
            }

            preparationReserved = false;
        }
    }

    /// <summary>Removes one registered component from a live Entity.</summary>
    /// <typeparam name="T">The unmanaged component type.</typeparam>
    /// <param name="entity">The live Entity to update.</param>
    /// <exception cref="ArgumentException"><paramref name="entity"/> is default, stale, or belongs to another World.</exception>
    /// <exception cref="InvalidOperationException">The World is not idle on its owner thread, T is unregistered, or the Entity lacks T.</exception>
    public void Remove<T>(Entity entity)
        where T : unmanaged
    {
        EnsureWorldReadAccess();
        var value = default(T);
        MigrateEntity(entity, typeof(T), addComponent: false, in value, useDefault: false, null, addTag: false);
    }

    /// <summary>Removes one registered zero-payload tag.</summary>
    /// <param name="entity">The live Entity to update.</param>
    /// <param name="schemaId">The registered tag schema identity.</param>
    /// <exception cref="ArgumentException"><paramref name="entity"/> is default, stale, or belongs to another World.</exception>
    /// <exception cref="KeyNotFoundException"><paramref name="schemaId"/> is not registered.</exception>
    /// <exception cref="InvalidOperationException">The World is not idle on its owner thread or the Entity does not have the tag.</exception>
    public void RemoveTag(Entity entity, Guid schemaId)
    {
        EnsureWorldReadAccess();
        ArgumentOutOfRangeException.ThrowIfEqual(schemaId, Guid.Empty);
        MigrateTag(entity, schemaId, addTag: false);
    }

    private ImmutableArray<IComponentRegistration> BuildMigratedComponents(
        WorldArchetype source,
        IComponentRegistration? changedComponent,
        bool addComponent)
    {
        var builder = ImmutableArray.CreateBuilder<IComponentRegistration>(
            source.Components.Length + ((changedComponent is not null) ? (addComponent ? 1 : -1) : 0));

        foreach (var registration in Registration.Components)
        {
            if (registration.ComponentType == changedComponent?.ComponentType)
            {
                if (addComponent)
                {
                    builder.Add(registration);
                }
            }
            else if (source.GetComponentIndex(registration.ComponentType) >= 0)
            {
                builder.Add(registration);
            }
        }

        return builder.MoveToImmutable();
    }

    private ImmutableArray<Guid> BuildMigratedTags(WorldArchetype source, Guid? changedTag, bool addTag)
    {
        var builder = ImmutableArray.CreateBuilder<Guid>(
            source.Tags.Length + ((changedTag is not null) ? (addTag ? 1 : -1) : 0));

        foreach (var registration in Registration.Tags)
        {
            if (registration.SchemaId == changedTag)
            {
                if (addTag)
                {
                    builder.Add(registration.SchemaId);
                }
            }
            else if (source.HasTag(registration.SchemaId))
            {
                builder.Add(registration.SchemaId);
            }
        }

        return builder.MoveToImmutable();
    }

    private WorldArchetype? FindArchetype(
        ImmutableArray<IComponentRegistration> components,
        ImmutableArray<Guid> tags)
    {
        for (var index = 0; index < _archetypeCount; index++)
        {
            var archetype = _archetypes[index]
                ?? throw new InvalidOperationException("The World archetype directory contains an empty active slot.");

            if (archetype.MatchesComponentsAndTags(components, tags))
            {
                return archetype;
            }
        }

        return null;
    }

    private void MigrateEntity<T>(
        Entity entity,
        Type? changedComponentType,
        bool addComponent,
        in T componentValue,
        bool useDefault,
        Guid? changedTag,
        bool addTag)
        where T : unmanaged
    {
        EnterStructuralOperation();

        var oldOwnedBytes = 0L;
        var reservedPeakBytes = 0L;
        WorldArchetype? candidateArchetype = null;
        WorldArchetype? targetArchetype = null;
        var stagedRowInitialized = false;
        var rowAppended = false;
        var candidateInstalled = false;
        var committed = false;
        var cleanupStarted = false;
        var targetChunkIndex = -1;
        var targetRow = -1;
        var candidateWorkPlanByteReservation = 0L;
        WorldChunkWorkPlanEntry[]? candidateWorkPlan = null;

        try
        {
            try
            {
                var sourceArchetype = GetLiveArchetype(entity, out var sourceChunkIndex, out var sourceRow);
                var changedComponent = (changedComponentType is null)
                    ? null
                    : FindComponentRegistration(changedComponentType);

                if ((changedComponent is not null) && (changedComponent.ComponentType != typeof(T)))
                {
                    throw new InvalidOperationException("The selected component registration does not match the typed migration value.");
                }

                var changedComponentIndex = (changedComponent is null)
                    ? -1
                    : sourceArchetype.GetComponentIndex(changedComponent.ComponentType);
                var changedTagRegistered = false;

                if (changedTag is { } schemaId)
                {
                    EnsureRegisteredTag(schemaId);
                    changedTagRegistered = true;
                }

                if (changedComponent is not null)
                {
                    if (addComponent == (changedComponentIndex >= 0))
                    {
                        throw new InvalidOperationException(addComponent
                            ? "The live Entity already has the selected component."
                            : "The live Entity does not have the selected component.");
                    }
                }

                if (changedTagRegistered)
                {
                    var hasTag = sourceArchetype.HasTag(changedTag!.Value);

                    if (addTag == hasTag)
                    {
                        throw new InvalidOperationException(addTag
                            ? "The live Entity already has the selected tag."
                            : "The live Entity does not have the selected tag.");
                    }
                }

                oldOwnedBytes = GetWorldOwnedStateBytes();
                var componentCount = sourceArchetype.Components.Length
                    + ((changedComponent is not null) ? (addComponent ? 1 : -1) : 0);
                var tagCount = sourceArchetype.Tags.Length
                    + (changedTagRegistered ? (addTag ? 1 : -1) : 0);
                var componentScratchBytes = checked((long)componentCount * IntPtr.Size);
                var tagScratchBytes = checked((long)tagCount * Unsafe.SizeOf<Guid>());
                var initializationBytes = (changedComponent is null)
                    ? 0L
                    : addComponent
                    ? (useDefault ? 256L : 128L)
                    : 128L;
                var candidateMetadataBytes = WorldArchetype.GetMetadataByteReservation(componentCount, tagCount);
                var preparationBytes = checked(componentScratchBytes + tagScratchBytes + initializationBytes + candidateMetadataBytes);

                if (checked(oldOwnedBytes + preparationBytes) > _maximumOwnedStateBytes)
                {
                    throw new InvalidOperationException("The World-owned state budget cannot admit archetype migration preparation.");
                }

                if (!_domainReservation.TryReserveAdditional(preparationBytes))
                {
                    throw new InvalidOperationException("The admission domain cannot reserve archetype migration preparation.");
                }

                reservedPeakBytes = preparationBytes;
                var components = BuildMigratedComponents(sourceArchetype, changedComponent, addComponent);
                var tags = BuildMigratedTags(sourceArchetype, changedTag, addTag);
                ValidateDerivedDependencies(components);
                targetArchetype = FindArchetype(components, tags);

                if (targetArchetype is null)
                {
                    if (_archetypeCount >= _maximumArchetypes)
                    {
                        throw new InvalidOperationException("The configured archetype limit was reached.");
                    }

                    candidateArchetype = new WorldArchetype(
                        _ownerId,
                        _archetypeCount,
                        _maximumLiveEntities,
                        components,
                        tags);
                    targetArchetype = candidateArchetype;
                }

                var storagePreparationBytes = (candidateArchetype is null)
                    ? targetArchetype.Storage.GetCapacityPreparationBytes(targetArchetype.Storage.EntityCount + 1)
                    : EntityArchetypeStorage.GetInitialCapacityPreparationBytes(components);

                if (storagePreparationBytes != targetArchetype.Storage.GetCapacityPreparationBytes(targetArchetype.Storage.EntityCount + 1))
                {
                    throw new InvalidOperationException("The new archetype storage estimate does not match its prepared capacity.");
                }

                if (checked(oldOwnedBytes + reservedPeakBytes + storagePreparationBytes) > _maximumOwnedStateBytes)
                {
                    throw new InvalidOperationException("The World-owned state budget cannot admit the destination archetype storage peak.");
                }

                if ((storagePreparationBytes != 0) && !_domainReservation.TryReserveAdditional(storagePreparationBytes))
                {
                    throw new InvalidOperationException("The admission domain cannot reserve the destination archetype storage peak.");
                }

                reservedPeakBytes = checked(reservedPeakBytes + storagePreparationBytes);

                if (!targetArchetype.Storage.PrepareCapacity(targetArchetype.Storage.EntityCount + 1))
                {
                    throw new InvalidOperationException("The preflighted destination archetype capacity could not be prepared.");
                }

                if (!targetArchetype.Storage.TryGetNextAppendLocation(_nextChunkOrdinal, out targetChunkIndex, out targetRow))
                {
                    throw new InvalidOperationException("The shared World chunk-ordinal cursor is exhausted.");
                }

                var sourceChunkEntityCount = sourceArchetype.Storage.GetEntities(sourceChunkIndex).Length;
                var workPlanPartitionDelta = ((targetRow % 64 == 0) ? 1 : 0)
                    - ((sourceChunkEntityCount % 64 == 1) ? 1 : 0);
                var requiredWorkPlanCount = checked(_componentTagWorkPlanCount + workPlanPartitionDelta);
                var candidateWorkPlanCapacity = GetComponentTagWorkPlanCapacityFor(requiredWorkPlanCount);

                if (candidateWorkPlanCapacity > _componentTagWorkPlan.Length)
                {
                    candidateWorkPlanByteReservation = CalculateComponentTagWorkPlanByteReservation(candidateWorkPlanCapacity);

                    if (checked(oldOwnedBytes + reservedPeakBytes + candidateWorkPlanByteReservation) > _maximumOwnedStateBytes)
                    {
                        throw new InvalidOperationException("The World-owned state budget cannot admit component/tag work-plan growth.");
                    }

                    if (!_domainReservation.TryReserveAdditional(candidateWorkPlanByteReservation))
                    {
                        throw new InvalidOperationException("The admission domain cannot reserve component/tag work-plan growth.");
                    }

                    reservedPeakBytes = checked(reservedPeakBytes + candidateWorkPlanByteReservation);
                    candidateWorkPlan = new WorldChunkWorkPlanEntry[candidateWorkPlanCapacity];
                }

                var targetChunkHasOrdinal = targetArchetype.Storage.TryGetChunkOrdinal(targetChunkIndex, out _);
                stagedRowInitialized = targetArchetype.Components.Length != 0;
                ComponentInitialization? initialization = null;

                if (changedComponent is not null && addComponent)
                {
                    initialization = useDefault
                        ? ComponentInitialization.Defaults<T>()
                        : ComponentInitialization.Value(in componentValue);
                }

                if (!targetChunkHasOrdinal && (_nextChunkOrdinal == long.MaxValue))
                {
                    throw new InvalidOperationException("The shared World chunk-ordinal cursor is exhausted.");
                }

                for (var destinationIndex = 0; destinationIndex < targetArchetype.Components.Length; destinationIndex++)
                {
                    var component = targetArchetype.Components[destinationIndex];

                    if (component.ComponentType == changedComponent?.ComponentType && addComponent)
                    {
                        var initialized = component.CreateInitializedValue(initialization
                            ?? throw new InvalidOperationException("The added component has no initialization request."));
                        targetArchetype.Storage.InitializeColumnRow(destinationIndex, targetChunkIndex, targetRow, initialized);
                    }
                    else
                    {
                        var sourceIndex = sourceArchetype.GetComponentIndex(component.ComponentType);

                        if (sourceIndex < 0)
                        {
                            throw new InvalidOperationException("The destination archetype contains an unexpected component.");
                        }

                        targetArchetype.Storage.CopyColumnRowFrom(
                            destinationIndex,
                            sourceArchetype.Storage,
                            sourceIndex,
                            sourceChunkIndex,
                            sourceRow,
                            targetChunkIndex,
                            targetRow);
                    }
                }

                var validationParameterName = (changedComponent is not null && addComponent && !useDefault)
                    ? "value"
                    : null;

                for (var destinationIndex = 0; destinationIndex < targetArchetype.Components.Length; destinationIndex++)
                {
                    ThrowIfValidationFailed(
                        targetArchetype.Storage.ValidateColumnRow(
                            destinationIndex,
                            targetChunkIndex,
                            targetRow,
                            CancellationToken.None),
                        validationParameterName,
                        "A component value in the destination archetype failed its registered validation.");
                    targetArchetype.Storage.ValidateEntityReferencesColumnRow(
                        destinationIndex,
                        targetChunkIndex,
                        targetRow,
                        this,
                        excludedEntity: null,
                        validationParameterName);
                }

                var nextMutationRevision = checked(_canonicalMutationRevision + 1);
                var removedComponentInitialization = (changedComponent is not null && !addComponent)
                    ? sourceArchetype.Storage.CaptureColumnRow(changedComponentIndex, sourceChunkIndex, sourceRow)
                    : null;
                var previousNextChunkOrdinal = _nextChunkOrdinal;
                _ = sourceArchetype.Storage.Remove(entity, _entitySlots);

                if (!targetArchetype.Storage.TryAppend(entity, _entitySlots, ref _nextChunkOrdinal))
                {
                    RestoreMigratedEntity(
                        entity,
                        sourceArchetype,
                        targetArchetype,
                        targetChunkIndex,
                        targetRow,
                        changedComponent,
                        removedComponentInitialization,
                        previousNextChunkOrdinal);
                    throw new InvalidOperationException("The preflighted destination archetype row could not be committed.");
                }

                rowAppended = true;

                if (candidateArchetype is not null)
                {
                    InstallArchetype(candidateArchetype);
                    candidateInstalled = true;
                }

                if (candidateWorkPlan is not null)
                {
                    _componentTagWorkPlan = candidateWorkPlan;
                    _componentTagWorkPlanByteReservation = candidateWorkPlanByteReservation;
                }

                _canonicalMutationRevision = nextMutationRevision;
                committed = true;
                RebuildComponentTagWorkPlan();
            }
            catch
            {
                Cleanup();
                throw;
            }

            Cleanup();
        }
        finally
        {
            _ = Interlocked.Exchange(ref _isStructuralOperationActive, 0);
        }

        void Cleanup()
        {
            if (cleanupStarted)
            {
                return;
            }

            cleanupStarted = true;

            if (!committed && stagedRowInitialized && !rowAppended && (targetArchetype is not null) && (targetChunkIndex >= 0))
            {
                targetArchetype.Storage.ClearUncommittedRow(targetChunkIndex, targetRow);
            }

            if (!candidateInstalled)
            {
                candidateArchetype?.DiscardUncommittedStorage();
            }

            if (reservedPeakBytes != 0)
            {
                var persistentGrowthBytes = checked(GetWorldOwnedStateBytes() - oldOwnedBytes);
                var temporaryBytes = reservedPeakBytes - persistentGrowthBytes;

                if (temporaryBytes > 0)
                {
                    _domainReservation.ReleaseAdditional(temporaryBytes);
                }
                else if (temporaryBytes < 0 && !_domainReservation.TryReserveAdditional(-temporaryBytes))
                {
                    throw new InvalidOperationException("The admission domain cannot retain prepared archetype storage.");
                }
            }
        }
    }

    private void MigrateTag(Entity entity, Guid schemaId, bool addTag)
    {
        var unusedValue = (byte)0;
        MigrateEntity(entity, null, addComponent: false, in unusedValue, useDefault: false, schemaId, addTag);
    }

    private void RestoreMigratedEntity(
        Entity entity,
        WorldArchetype source,
        WorldArchetype destination,
        int destinationChunkIndex,
        int destinationRow,
        IComponentRegistration? removedComponent,
        ComponentInitialization? removedComponentInitialization,
        long previousNextChunkOrdinal)
    {
        if (!source.Storage.TryGetNextAppendLocation(_nextChunkOrdinal, out var restoreChunkIndex, out var restoreRow))
        {
            throw new InvalidOperationException("The source archetype cannot restore a refused migration.");
        }

        for (var sourceComponentIndex = 0; sourceComponentIndex < source.Components.Length; sourceComponentIndex++)
        {
            var component = source.Components[sourceComponentIndex];
            var destinationComponentIndex = destination.GetComponentIndex(component.ComponentType);

            if (destinationComponentIndex >= 0)
            {
                source.Storage.CopyColumnRowFrom(
                    sourceComponentIndex,
                    destination.Storage,
                    destinationComponentIndex,
                    destinationChunkIndex,
                    destinationRow,
                    restoreChunkIndex,
                    restoreRow);
            }
            else if ((component.ComponentType == removedComponent?.ComponentType) && (removedComponentInitialization is not null))
            {
                source.Storage.InitializeColumnRow(
                    sourceComponentIndex,
                    restoreChunkIndex,
                    restoreRow,
                    removedComponentInitialization);
            }
            else
            {
                throw new InvalidOperationException("The migration destination lost a source component.");
            }
        }

        if (!source.Storage.TryAppend(entity, _entitySlots, ref _nextChunkOrdinal))
        {
            throw new InvalidOperationException("The source archetype could not restore a refused migration.");
        }

        if (_nextChunkOrdinal != previousNextChunkOrdinal)
        {
            throw new InvalidOperationException("A refused archetype migration changed the shared chunk-ordinal cursor.");
        }

        if (destination.Storage.GetEntities(destinationChunkIndex).Length > destinationRow)
        {
            throw new InvalidOperationException("The refused destination row cannot be cleared.");
        }

        destination.Storage.ClearUncommittedRow(destinationChunkIndex, destinationRow);
    }
}
