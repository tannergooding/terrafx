// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace TerraFX.Entities;

public sealed partial class World
{
    internal Entity ResolveRestoreEntity(Guid stableId)
    {
        if (stableId == Guid.Empty)
        {
            throw new ArgumentException("A restored Entity identity must be nonempty.", nameof(stableId));
        }

        ThrowIfDisposed();
        ThrowIfFaulted();
        EnsureOwnerThread();

        if (!_isRestoreCandidate || !_isCandidateInitialized)
        {
            throw new InvalidOperationException("Stable Entity identities can be resolved only by an initialized restore candidate.");
        }

        if (!_identityLedger.TryGetLiveLocator(stableId, IdentityKind.Entity, out var locator)
            || !_entitySlots.IsLive(locator, generation: 1))
        {
            throw new InvalidOperationException("The restored identity map does not contain the requested live Entity.");
        }

        return CreateEntityHandle(locator, generation: 1);
    }

    internal ComponentRecordRestoreResult RestoreComponentRecord(
        Entity entity,
        IComponentRegistration registration,
        scoped ReadOnlySpan<byte> source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ThrowIfFaulted();
        EnsureOwnerThread();

        if (!_isRestoreCandidate || !_isCandidateInitialized || (registration.Descriptor.Flags != ComponentStorageFlags.Persistent))
        {
            throw new InvalidOperationException("Only a registered Persistent component can be restored by this candidate operation.");
        }

        if (!entity.IsValid
            || (entity.OwnerId != _ownerId)
            || !_entitySlots.IsLive(entity.Index, entity.Generation)
            || (_identityLedger.GetLiveOrdinal(IdentityKind.Entity, entity.Index) == 0))
        {
            throw new ArgumentException("The selected restore Entity is not part of this candidate identity map.", nameof(entity));
        }

        var isRegistered = false;

        foreach (var candidate in Registration.Components)
        {
            isRegistered |= ReferenceEquals(candidate, registration);
        }

        if (!isRegistered)
        {
            throw new ArgumentException("The selected component registration does not belong to this candidate.", nameof(registration));
        }

        var context = _restoreContext
            ?? throw new InvalidOperationException("The restore candidate omitted its pre-admitted component context.");
        context.Activate(entity, registration, allowSameEntityRead: false);

        try
        {
            var result = registration.RestoreFromRecord(source, context, cancellationToken);
            context.ThrowIfFailed();
            return result;
        }
        finally
        {
            context.Revoke();
        }
    }

    internal ComponentInitialization RestoreDerivedComponent(
        Entity entity,
        IComponentRegistration registration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ThrowIfFaulted();
        EnsureOwnerThread();

        if (!_isRestoreCandidate
            || !_isCandidateInitialized
            || (registration.Descriptor.Flags != ComponentStorageFlags.Transient)
            || (registration.Descriptor.Restore != ComponentTransientRestore.Derived))
        {
            throw new InvalidOperationException("Only a registered Derived transient component can be rebuilt by this candidate operation.");
        }

        if (!entity.IsValid
            || (entity.OwnerId != _ownerId)
            || !_entitySlots.IsLive(entity.Index, entity.Generation)
            || (_identityLedger.GetLiveOrdinal(IdentityKind.Entity, entity.Index) == 0))
        {
            throw new ArgumentException("The selected restore Entity is not part of this candidate identity map.", nameof(entity));
        }

        var isRegistered = false;

        foreach (var candidate in Registration.Components)
        {
            isRegistered |= ReferenceEquals(candidate, registration);
        }

        if (!isRegistered)
        {
            throw new ArgumentException("The selected component registration does not belong to this candidate.", nameof(registration));
        }

        var context = _restoreContext
            ?? throw new InvalidOperationException("The restore candidate omitted its pre-admitted component context.");
        context.Activate(entity, registration, allowSameEntityRead: true);
        var binding = new WorldStateRestoreBinding(context);

        try
        {
            var initialization = registration.RebuildDerived(binding, cancellationToken);
            context.ThrowIfFailed();

            if (initialization.IsDefault || (initialization.ComponentType != registration.ComponentType))
            {
                throw new InvalidOperationException("The Derived component rebuild did not produce its exact typed value.");
            }

            return initialization;
        }
        finally
        {
            binding.Revoke();
            context.Revoke();
        }
    }

    internal void RestoreEcsRows(
        ReadOnlyMemory<byte> ecsRecord,
        WorldPersistenceEcsContents contents,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contents);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ThrowIfFaulted();
        EnsureOwnerThread();

        if (!_isRestoreCandidate || !_isCandidateInitialized || _isRestoreEcsRowsRestored)
        {
            throw new InvalidOperationException("ECS rows can be restored only once into an initialized restore candidate.");
        }

        var restoreEcsPlan = _restoreEcsPlan
            ?? throw new InvalidOperationException("The restore candidate omitted its preflighted ECS plan.");
        var identityHistory = contents.ValidatedIdentityHistory
            ?? throw new ArgumentException("Candidate ECS restoration requires the validated source identity history.", nameof(contents));

        if ((contents.WorldGuid != _worldGuid)
            || (contents.ReplayLineageId != _replayLineageId)
            || (contents.EntityCount != restoreEcsPlan.EntityCount)
            || (contents.LiveEntityCount != contents.EntityCount)
            || (_entitySlots.LiveEntityCount != contents.LiveEntityCount))
        {
            throw new ArgumentException("The validated ECS counts or identity do not match the restore candidate.", nameof(contents));
        }

        if (contents.AttachmentCount != 0)
        {
            throw new NotSupportedException("This candidate restore slice does not support attachments.");
        }

        if (contents.EntityCount > _maximumLiveEntities)
        {
            throw new ArgumentException("The validated ECS entity count exceeds the restore candidate's live-Entity limit.", nameof(contents));
        }

        if ((_archetypeCount != 1)
            || (_archetypes[0]?.Storage.EntityCount != 0)
            || (_componentTagWorkPlanCount != 0)
            || (_componentTagWorkPlan.Length != 0)
            || (_nextChunkOrdinal != 0))
        {
            throw new InvalidOperationException("The restore candidate already contains ECS storage.");
        }

        var limits = WorldPersistenceEbinLimits.Create(_options);
        var status = EbinStructuralReader.Validate(
            ecsRecord.Span,
            limits,
            EbinReadPolicy.Canonical,
            out var bytesConsumed,
            out _);

        if ((status is not EbinReadStatus.Complete) || (bytesConsumed != ecsRecord.Length))
        {
            throw new ArgumentException("The candidate ECS source is not one complete canonical record.", nameof(ecsRecord));
        }

        var root = new EbinRecordReadView(ecsRecord.Span);
        var ecsSchemaId = new ContentSchemaIdentityAssignment("state/ecs").SchemaId;

        if ((root.Header.SchemaId != ecsSchemaId)
            || (root.Header.Major != 1)
            || (root.Header.Minor != 0)
            || (root.FieldCount != 5)
            || !root.GetRecord(5).EncodedBytes.SequenceEqual(identityHistory.EncodedRecord.Span))
        {
            throw new ArgumentException("The candidate ECS record does not match its validated source identity history.", nameof(ecsRecord));
        }

        var entitiesField = root.GetField(0);

        if ((entitiesField.Tag != 1)
            || (entitiesField.Kind != 24)
            || !entitiesField.IsRequired
            || (entitiesField.Count != (ulong)contents.EntityCount))
        {
            throw new ArgumentException("The candidate ECS entity array does not match its validated row count.", nameof(ecsRecord));
        }

        for (var fieldIndex = 1; fieldIndex < 4; fieldIndex++)
        {
            var field = root.GetField(fieldIndex);
            var expectedCount = fieldIndex switch {
                1 => contents.SystemCount,
                2 => contents.ResourceCount,
                _ => 0,
            };

            if ((field.Tag != (uint)fieldIndex + 1)
                || (field.Kind != 24)
                || !field.IsRequired
                || (field.Count != (ulong)expectedCount))
            {
                throw new NotSupportedException("The candidate ECS owner-state arrays do not match their validated counts.");
            }
        }

        if (Registration.Resources.Length != contents.ResourceCount)
        {
            throw new InvalidOperationException("The validated resource rows do not match the candidate registration.");
        }

        var statefulSystemCount = 0;

        foreach (var registration in Registration.Systems)
        {
            statefulSystemCount += registration.Factory.Descriptor.StateSchema.HasValue ? 1 : 0;
        }

        if (statefulSystemCount != contents.SystemCount)
        {
            throw new InvalidOperationException("The validated system rows do not match the candidate registration.");
        }

        var previousEntityId = Guid.Empty;

        for (var index = 0; index < contents.EntityCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entityRecord = root.GetRecordArrayElement(1, index);
            var componentField = entityRecord.GetField(1);
            var tagField = entityRecord.GetField(2);
            var transientSchemaField = entityRecord.GetField(3);
            var hasSceneInstanceId = entityRecord.TryGetField(6, out var sceneInstanceField);
            var archetypePlan = restoreEcsPlan.GetArchetype(restoreEcsPlan.GetEntityArchetypeIndex(index));

            if ((entityRecord.Header.SchemaId != new ContentSchemaIdentityAssignment("state/entity").SchemaId)
                || (entityRecord.Header.Major != 1)
                || (entityRecord.Header.Minor != 0)
                || (entityRecord.FieldCount != (hasSceneInstanceId ? 6 : 5))
                || (componentField.Tag != 2)
                || (componentField.Kind != 24)
                || !componentField.IsRequired
                || (componentField.Count != (ulong)archetypePlan.PersistentComponentCount)
                || (tagField.Tag != 3)
                || (tagField.Kind != 24)
                || !tagField.IsRequired
                || (tagField.Count != (ulong)archetypePlan.Tags.Length)
                || (transientSchemaField.Tag != 4)
                || (transientSchemaField.Kind != 24)
                || !transientSchemaField.IsRequired
                || (transientSchemaField.Count != (ulong)archetypePlan.TransientComponentCount))
            {
                throw new ArgumentException("A candidate ECS Entity row does not match its preflighted shape.", nameof(ecsRecord));
            }

            if (hasSceneInstanceId)
            {
                if ((sceneInstanceField.Kind != 11)
                    || sceneInstanceField.IsRequired
                    || (sceneInstanceField.Count != 1)
                    || (sceneInstanceField.Payload.Length != 16))
                {
                    throw new ArgumentException("A candidate ECS SceneInstanceId has an invalid wire shape.", nameof(ecsRecord));
                }

                var sceneInstanceId = new Guid(sceneInstanceField.Payload, bigEndian: true);

                if ((sceneInstanceId == Guid.Empty)
                    || !_identityLedger.TryGetLiveLocator(sceneInstanceId, IdentityKind.SceneInstance, out _))
                {
                    throw new ArgumentException("A candidate ECS Entity names no live SceneInstance identity.", nameof(ecsRecord));
                }
            }

            for (var componentIndex = 0; componentIndex < archetypePlan.PersistentComponentCount; componentIndex++)
            {
                var componentRecord = entityRecord.GetRecordArrayElement(2, componentIndex);
                var header = componentRecord.Header;
                var registration = archetypePlan.Components[componentIndex];

                if ((header.SchemaId != registration.SchemaReference.SchemaId)
                    || (header.Major != registration.SchemaReference.Major)
                    || (header.Minor != registration.SchemaReference.Minor))
                {
                    throw new ArgumentException("A candidate ECS component does not match its preflighted archetype.", nameof(ecsRecord));
                }
            }

            for (var tagIndex = 0; tagIndex < archetypePlan.Tags.Length; tagIndex++)
            {
                var tagRecord = entityRecord.GetRecordArrayElement(3, tagIndex);
                var tagSchema = WorldPersistenceEcsReader.ReadTagSchemaReferenceForRestore(
                    tagRecord,
                    nameof(ecsRecord));

                if (tagSchema != archetypePlan.Tags[tagIndex])
                {
                    throw new ArgumentException("A candidate ECS tag does not match its preflighted archetype.", nameof(ecsRecord));
                }
            }

            for (var transientIndex = 0; transientIndex < archetypePlan.TransientComponentCount; transientIndex++)
            {
                var schemaRecord = entityRecord.GetRecordArrayElement(4, transientIndex);
                var transientSchema = WorldPersistenceEcsReader.ReadTransientComponentSchemaReference(
                    schemaRecord,
                    nameof(ecsRecord));
                var registration = archetypePlan.Components[archetypePlan.PersistentComponentCount + transientIndex];

                if (transientSchema != registration.SchemaReference)
                {
                    throw new ArgumentException("A candidate ECS transient schema does not match its preflighted archetype.", nameof(ecsRecord));
                }
            }

            var identityField = entityRecord.GetField(0);
            var ordinalField = entityRecord.GetField(4);

            if ((identityField.Tag != 1)
                || (identityField.Kind != 11)
                || !identityField.IsRequired
                || (identityField.Payload.Length != 16)
                || (ordinalField.Tag != 5)
                || (ordinalField.Kind != 8)
                || !ordinalField.IsRequired
                || (ordinalField.Payload.Length != sizeof(long)))
            {
                throw new ArgumentException("A candidate ECS Entity row has an invalid identity or ordinal field.", nameof(ecsRecord));
            }

            var entityId = new Guid(identityField.Payload, bigEndian: true);
            var assignmentOrdinal = BinaryPrimitives.ReadInt64LittleEndian(ordinalField.Payload);

            if ((entityId == Guid.Empty)
                || ((previousEntityId != Guid.Empty) && (FoundationCatalogue.CompareRfc(previousEntityId, entityId) >= 0))
                || (assignmentOrdinal < 1)
                || (assignmentOrdinal > identityHistory.AssignmentsByOrdinal.Length))
            {
                throw new ArgumentException("A candidate ECS Entity identity or assignment order is invalid.", nameof(ecsRecord));
            }

            var assignment = identityHistory.AssignmentsByOrdinal[checked((int)assignmentOrdinal - 1)];

            if ((assignment.StableId != entityId)
                || (assignment.Kind != (byte)IdentityKind.Entity)
                || (assignment.AssignmentOrdinal != (uint)assignmentOrdinal)
                || !assignment.IsLive)
            {
                throw new ArgumentException("A candidate ECS Entity row disagrees with its validated identity assignment.", nameof(ecsRecord));
            }

            var restoredEntity = ResolveRestoreEntity(entityId);

            if (_identityLedger.GetLiveOrdinal(IdentityKind.Entity, restoredEntity.Index) != assignmentOrdinal)
            {
                throw new InvalidOperationException("The candidate Entity map disagrees with the validated assignment ordinal.");
            }

            if (_entitySlots.TryGetLocation(restoredEntity.Index, restoredEntity.Generation, out _, out _, out _))
            {
                throw new InvalidOperationException("A candidate Entity was already attached to ECS storage before restoration.");
            }

            previousEntityId = entityId;
        }

        if (contents.EntityCount == 0)
        {
            if ((_restoreEcsArchetypeMetadataByteReservation != 0)
                || (_restoreEcsStorageByteReservation != 0)
                || (_restoreWorkPlanCapacity != 0)
                || (_restoreWorkPlanByteReservation != 0)
                || (_restoreComponentWorkspaceByteReservation != 0))
            {
                throw new InvalidOperationException("The empty restore candidate has unexpected ECS storage reservations.");
            }

            RestoreResourceStateRecords(root, contents.ResourceCount, cancellationToken);
            RestoreSystemStateRecords(root, contents.SystemCount, cancellationToken);
            _isRestoreEcsRowsRestored = true;
            _restoreEcsPlan = null;
            return;
        }

        var archetypeCount = restoreEcsPlan.ArchetypeCount;
        var archetypesByPlan = new WorldArchetype[archetypeCount];
        var componentInitializationsByPlan = new ComponentInitialization?[archetypeCount][];
        var metadataBytes = 0L;
        var storageBytes = 0L;
        var workPlanCount = 0;
        var componentWorkspaceBytes = checked(2 * ArrayBytes(archetypeCount, IntPtr.Size));
        var nextWorldArchetypeIndex = 1;

        for (var archetypeIndex = 0; archetypeIndex < archetypeCount; archetypeIndex++)
        {
            var archetypePlan = restoreEcsPlan.GetArchetype(archetypeIndex);
            var hasSignature = !archetypePlan.Components.IsEmpty || !archetypePlan.Tags.IsEmpty;
            WorldArchetype archetype;

            if (hasSignature)
            {
                if ((_archetypeCount != 1)
                    || (nextWorldArchetypeIndex >= _archetypes.Length)
                    || (_archetypes[nextWorldArchetypeIndex] is not null))
                {
                    throw new InvalidOperationException("The restore candidate does not have its preflighted archetype directory slots.");
                }

                var tagIds = ImmutableArray.CreateBuilder<Guid>(archetypePlan.Tags.Length);

                foreach (var tag in archetypePlan.Tags)
                {
                    tagIds.Add(tag.SchemaId);
                }

                archetype = new WorldArchetype(
                    _ownerId,
                    nextWorldArchetypeIndex,
                    _maximumLiveEntities,
                    archetypePlan.Components,
                    tagIds.MoveToImmutable());
                nextWorldArchetypeIndex++;
                metadataBytes = checked(metadataBytes + archetype.MetadataByteReservation);
            }
            else
            {
                archetype = _archetypes[0]
                    ?? throw new InvalidOperationException("The restore candidate omitted its empty archetype.");
            }

            archetypesByPlan[archetypeIndex] = archetype;
            storageBytes = checked(
                storageBytes
                + archetype.Storage.GetCapacityPreparationBytes(archetypePlan.EntityCount));
            var chunksPerFullPage = (archetype.Storage.RowCapacity + 63) / 64;
            var fullChunkCount = archetypePlan.EntityCount / archetype.Storage.RowCapacity;
            var lastChunkEntityCount = archetypePlan.EntityCount % archetype.Storage.RowCapacity;
            workPlanCount = checked(
                workPlanCount
                + (fullChunkCount * chunksPerFullPage)
                + ((lastChunkEntityCount + 63) / 64));

            if (!archetypePlan.Components.IsEmpty)
            {
                componentInitializationsByPlan[archetypeIndex] =
                    new ComponentInitialization?[archetypePlan.Components.Length];
                componentWorkspaceBytes = checked(
                    componentWorkspaceBytes
                    + ArrayBytes(archetypePlan.Components.Length, IntPtr.Size)
                    + (archetypePlan.Components.Length * 128L));
            }
            else
            {
                componentInitializationsByPlan[archetypeIndex] = [];
            }
        }

        var workPlanCapacity = _restoreWorkPlanCapacity;
        var workPlanBytes = _restoreWorkPlanByteReservation;
        var growthBytes = checked(
            metadataBytes
            + storageBytes
            + workPlanBytes
            + componentWorkspaceBytes);
        var existingBytes = GetWorldOwnedStateBytes();

        if ((metadataBytes != _restoreEcsArchetypeMetadataByteReservation)
            || (storageBytes != _restoreEcsStorageByteReservation)
            || (workPlanCapacity < workPlanCount)
            || (workPlanCapacity == 0)
            || (CalculateComponentTagWorkPlanByteReservation(workPlanCapacity) != workPlanBytes)
            || (componentWorkspaceBytes != _restoreComponentWorkspaceByteReservation)
            || (growthBytes > _maximumOwnedStateBytes - existingBytes))
        {
            throw new InvalidOperationException("The candidate restore plan did not pre-admit its ECS rows, archetype, query work plan, and component workspace.");
        }

        var workPlan = new WorldChunkWorkPlanEntry[workPlanCapacity];

        for (var archetypeIndex = 0; archetypeIndex < archetypeCount; archetypeIndex++)
        {
            var archetypePlan = restoreEcsPlan.GetArchetype(archetypeIndex);
            var storage = archetypesByPlan[archetypeIndex].Storage;

            if (!storage.PrepareCapacity(archetypePlan.EntityCount))
            {
                throw new InvalidOperationException("The restore candidate could not prepare its validated ECS row capacity.");
            }

            var componentInitializations = componentInitializationsByPlan[archetypeIndex];

            for (var componentIndex = archetypePlan.PersistentComponentCount;
                 componentIndex < archetypePlan.Components.Length;
                 componentIndex++)
            {
                var registration = archetypePlan.Components[componentIndex];

                if (registration.Descriptor.Restore == ComponentTransientRestore.Reset)
                {
                    componentInitializations[componentIndex] = registration.CreateDefaultInitialization();
                }
            }

            if (archetypesByPlan[archetypeIndex].Index != 0)
            {
                InstallArchetype(archetypesByPlan[archetypeIndex]);
            }
        }

        _nextChunkOrdinal = 0;

        for (var index = 0; index < contents.EntityCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entityRecord = root.GetRecordArrayElement(1, index);
            var entityId = new Guid(entityRecord.GetField(0).Payload, bigEndian: true);
            var entity = ResolveRestoreEntity(entityId);
            var archetypeIndex = restoreEcsPlan.GetEntityArchetypeIndex(index);
            var archetypePlan = restoreEcsPlan.GetArchetype(archetypeIndex);
            var archetypeStorage = archetypesByPlan[archetypeIndex].Storage;
            var componentInitializations = componentInitializationsByPlan[archetypeIndex];

            if (!archetypeStorage.TryGetNextAppendLocation(_nextChunkOrdinal, out var chunkIndex, out var row))
            {
                throw new InvalidOperationException("The restore candidate could not locate a preflighted ECS row.");
            }

            for (var componentIndex = 0; componentIndex < archetypePlan.PersistentComponentCount; componentIndex++)
            {
                var registration = archetypePlan.Components[componentIndex];
                var componentRecord = entityRecord.GetRecordArrayElement(2, componentIndex);
                var restoreResult = RestoreComponentRecord(
                    entity,
                    registration,
                    componentRecord.EncodedBytes,
                    cancellationToken);

                if (restoreResult.Status is ComponentReadStatus.Rejected)
                {
                    var diagnostic = restoreResult.Diagnostics[0];
                    throw new InvalidDataException($"{diagnostic.Code}: {diagnostic.Message}");
                }

                if (!restoreResult.TryGetInitialization(out var initialization)
                    || (initialization is null))
                {
                    throw new InvalidOperationException("The successful Persistent component restore did not produce an initialization.");
                }

                var initializedValue = registration.CreateInitializedValue(initialization, _spatialSettings);
                componentInitializations[componentIndex] = initializedValue;
            }

            for (var componentIndex = 0; componentIndex < componentInitializations.Length; componentIndex++)
            {
                if (componentInitializations[componentIndex] is { } initialization)
                {
                    archetypeStorage.InitializeColumnRow(componentIndex, chunkIndex, row, initialization, _spatialSettings);
                }
            }

            if (!archetypeStorage.TryAppend(entity, _entitySlots, ref _nextChunkOrdinal))
            {
                throw new InvalidOperationException("The restore candidate could not append a prevalidated ECS row.");
            }

            Array.Clear(componentInitializations, 0, archetypePlan.PersistentComponentCount);
        }

        _componentTagWorkPlan = workPlan;
        _componentTagWorkPlanByteReservation = workPlanBytes;

        RebuildComponentTagWorkPlan();

        for (var archetypeIndex = 0; archetypeIndex < archetypeCount; archetypeIndex++)
        {
            var archetypePlan = restoreEcsPlan.GetArchetype(archetypeIndex);
            var storage = archetypesByPlan[archetypeIndex].Storage;

            for (var componentIndex = 0; componentIndex < archetypePlan.Components.Length; componentIndex++)
            {
                if (archetypePlan.Components[componentIndex].Descriptor.Restore == ComponentTransientRestore.Derived)
                {
                    continue;
                }

                for (var chunkIndex = 0; chunkIndex < storage.ChunkCount; chunkIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    for (var row = 0; row < storage.GetChunkRowCount(chunkIndex); row++)
                    {
                        storage.ValidateEntityReferencesColumnRow(
                            componentIndex,
                            chunkIndex,
                            row,
                            this,
                            excludedEntity: null,
                            parameterName: nameof(ecsRecord));
                    }
                }
            }
        }

        for (var entityIndex = 0; entityIndex < contents.EntityCount; entityIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entityRecord = root.GetRecordArrayElement(1, entityIndex);
            var entityId = new Guid(entityRecord.GetField(0).Payload, bigEndian: true);
            var entity = ResolveRestoreEntity(entityId);
            var archetypePlanIndex = restoreEcsPlan.GetEntityArchetypeIndex(entityIndex);
            var archetypePlan = restoreEcsPlan.GetArchetype(archetypePlanIndex);
            var archetype = archetypesByPlan[archetypePlanIndex];
            var storage = archetype.Storage;

            if (!_entitySlots.TryGetLocation(entity.Index, entity.Generation, out var archetypeIndex, out var chunkOrdinal, out var row)
                || (archetypeIndex != archetype.Index))
            {
                throw new InvalidOperationException("The restored Entity is not located in its preflighted component archetype.");
            }

            if (!storage.TryFindChunkIndex(chunkOrdinal, out var chunkIndex))
            {
                throw new InvalidOperationException("The restored Entity has no preflighted archetype chunk.");
            }

            for (var componentIndex = archetypePlan.PersistentComponentCount;
                 componentIndex < archetypePlan.Components.Length;
                 componentIndex++)
            {
                var registration = archetypePlan.Components[componentIndex];

                if (registration.Descriptor.Restore != ComponentTransientRestore.Derived)
                {
                    continue;
                }

                var initialization = RestoreDerivedComponent(entity, registration, cancellationToken);
                storage.InitializeExistingColumnRow(componentIndex, chunkIndex, row, initialization, _spatialSettings);
            }
        }

        for (var archetypeIndex = 0; archetypeIndex < archetypeCount; archetypeIndex++)
        {
            var archetypePlan = restoreEcsPlan.GetArchetype(archetypeIndex);
            var storage = archetypesByPlan[archetypeIndex].Storage;

            for (var chunkIndex = 0; chunkIndex < storage.ChunkCount; chunkIndex++)
            {
                for (var row = 0; row < storage.GetChunkRowCount(chunkIndex); row++)
                {
                    for (var componentIndex = 0; componentIndex < archetypePlan.Components.Length; componentIndex++)
                    {
                        ThrowIfValidationFailed(
                            storage.ValidateColumnRow(componentIndex, chunkIndex, row, cancellationToken),
                            nameof(ecsRecord),
                            "A restored component failed its registered validation.");
                        storage.ValidateEntityReferencesColumnRow(
                            componentIndex,
                            chunkIndex,
                            row,
                            this,
                            excludedEntity: null,
                            parameterName: nameof(ecsRecord));
                    }
                }
            }
        }

        for (var archetypeIndex = 0; archetypeIndex < componentInitializationsByPlan.Length; archetypeIndex++)
        {
            Array.Clear(componentInitializationsByPlan[archetypeIndex]);
        }

        Array.Clear(archetypesByPlan);
        Array.Clear(componentInitializationsByPlan);

        if (componentWorkspaceBytes != 0)
        {
            _domainReservation.ReleaseAdditional(componentWorkspaceBytes);
            _restoreComponentWorkspaceByteReservation = 0;
        }

        RestoreResourceStateRecords(root, contents.ResourceCount, cancellationToken);
        RestoreSystemStateRecords(root, contents.SystemCount, cancellationToken);
        _isRestoreEcsRowsRestored = true;
        _restoreEcsPlan = null;
    }

    internal void RestoreHierarchyLinks(
        scoped ReadOnlySpan<Entity> entities,
        scoped ReadOnlySpan<Entity> parents)
    {
        ThrowIfDisposed();
        ThrowIfFaulted();
        EnsureOwnerThread();

        if (!_isRestoreCandidate || !_isCandidateInitialized || !_isRestoreEcsRowsRestored)
        {
            throw new InvalidOperationException("Hierarchy links can be restored only after ECS rows are installed in an initialized candidate.");
        }

        if (_isRestoreHierarchyInstalled)
        {
            throw new InvalidOperationException("Candidate hierarchy links can be restored only once.");
        }

        if (entities.Length != parents.Length)
        {
            throw new ArgumentException("The candidate hierarchy node and parent arrays must have equal lengths.");
        }

        for (var index = 0; index < entities.Length; index++)
        {
            var child = entities[index];

            if (!child.IsValid
                || (child.OwnerId != _ownerId)
                || !_entitySlots.IsLive(child.Index, child.Generation)
                || (_identityLedger.GetLiveOrdinal(IdentityKind.Entity, child.Index) == 0))
            {
                throw new ArgumentException("A candidate hierarchy node is not a live Entity in this exact restore map.", nameof(entities));
            }

            if (_entitySlots.TryGetParent(child.Index, child.Generation, out _, out _))
            {
                throw new InvalidOperationException("A candidate hierarchy node already has a parent before restoration.");
            }

            var parent = parents[index];

            if (!parent.IsValid)
            {
                if (parent != default)
                {
                    throw new ArgumentException("A candidate hierarchy root must use the default Entity value.", nameof(parents));
                }

                continue;
            }

            if ((parent.OwnerId != _ownerId)
                || !_entitySlots.IsLive(parent.Index, parent.Generation)
                || (_identityLedger.GetLiveOrdinal(IdentityKind.Entity, parent.Index) == 0))
            {
                throw new ArgumentException("A candidate hierarchy parent is not a live Entity in this exact restore map.", nameof(parents));
            }

            _entitySlots.SetParent(child.Index, child.Generation, parent.Index, parent.Generation);
        }

        _isRestoreHierarchyInstalled = true;
    }

    private void RestoreResourceStateRecords(
        EbinRecordReadView ecsRecord,
        int resourceCount,
        CancellationToken cancellationToken)
    {
        for (var resourceIndex = 0; resourceIndex < resourceCount; resourceIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resourceRecord = ecsRecord.GetRecordArrayElement(3, resourceIndex);
            var resourceIdField = resourceRecord.GetField(0);

            if ((resourceIdField.Tag != 1)
                || (resourceIdField.Kind != 11)
                || !resourceIdField.IsRequired
                || (resourceIdField.Payload.Length != 16))
            {
                throw new ArgumentException("A candidate state/resource-state row has an invalid ResourceId.", nameof(ecsRecord));
            }

            var resourceId = new Guid(resourceIdField.Payload, bigEndian: true);
            var payload = resourceRecord.GetRecord(2);
            var payloadHeader = payload.Header;
            var schema = new ContentSchemaReference(
                payloadHeader.SchemaId,
                payloadHeader.Major,
                payloadHeader.Minor,
                ContentSchemaScope.Content);
            var registration = Registration.Resources[resourceIndex];

            if ((resourceId != registration.ResourceId) || (schema != registration.Schema))
            {
                throw new ArgumentException("A candidate state/resource-state row does not match its exact registration.", nameof(ecsRecord));
            }

            registration.RestoreStateRecord(
                payload.EncodedBytes,
                _resourceStorage[resourceIndex],
                cancellationToken);
        }
    }

    private void RestoreSystemStateRecords(
        EbinRecordReadView ecsRecord,
        int systemCount,
        CancellationToken cancellationToken)
    {
        var systemRegistrationIndex = 0;

        for (var systemRecordIndex = 0; systemRecordIndex < systemCount; systemRecordIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            while ((systemRegistrationIndex < Registration.Systems.Length)
                && !Registration.Systems[systemRegistrationIndex].Factory.Descriptor.StateSchema.HasValue)
            {
                systemRegistrationIndex++;
            }

            if (systemRegistrationIndex >= Registration.Systems.Length)
            {
                throw new InvalidOperationException("The validated stateful system row has no target registration.");
            }

            var registration = Registration.Systems[systemRegistrationIndex];
            var systemRecord = ecsRecord.GetRecordArrayElement(2, systemRecordIndex);
            var systemIdField = systemRecord.GetField(0);

            if ((systemIdField.Tag != 1)
                || (systemIdField.Kind != 11)
                || !systemIdField.IsRequired
                || (systemIdField.Payload.Length != 16))
            {
                throw new ArgumentException("A candidate state/system-state row has an invalid SystemId.", nameof(ecsRecord));
            }

            var systemId = new Guid(systemIdField.Payload, bigEndian: true);
            var payload = systemRecord.GetRecord(2);
            var payloadHeader = payload.Header;
            var schema = registration.Factory.Descriptor.StateSchema
                ?? throw new InvalidOperationException("The target stateful system omitted its state schema.");

            if ((systemId != registration.Description.SystemId)
                || (payloadHeader.SchemaId != schema.SchemaId)
                || (payloadHeader.Major != schema.Major)
                || (payloadHeader.Minor != schema.Minor))
            {
                throw new ArgumentException("A candidate state/system-state row does not match its exact target registration.", nameof(ecsRecord));
            }

            var dispatch = registration.Factory.Dispatch as IWorldSystemRestoreDispatch
                ?? throw new InvalidOperationException("The target stateful system omitted its Restore dispatch.");
            var system = _systemInstances[systemRegistrationIndex]
                ?? throw new InvalidOperationException("The candidate omitted its fresh registered system instance.");
            var author = dispatch.ReadStateRecord(payload.EncodedBytes, systemId, cancellationToken);
            var context = new WorldSystemRestoreContext(this, registration, systemId);
            var binding = new WorldStateRestoreBinding(context);

            try
            {
                dispatch.RestoreState(system, author, binding, cancellationToken);
                context.ThrowIfFailed();
            }
            finally
            {
                binding.Revoke();
                context.Revoke();
            }

            registration.Factory.ValidationControl.Validate(system, systemId);
            systemRegistrationIndex++;
        }

        while ((systemRegistrationIndex < Registration.Systems.Length)
            && !Registration.Systems[systemRegistrationIndex].Factory.Descriptor.StateSchema.HasValue)
        {
            systemRegistrationIndex++;
        }

        if (systemRegistrationIndex != Registration.Systems.Length)
        {
            throw new InvalidOperationException("The validated ECS state omitted a registered stateful system.");
        }
    }

    private sealed class WorldComponentRestoreContext : IComponentRestoreContext
    {
        private bool _allowSameEntityRead;
        private Entity _entity;
        private Exception? _failure;
        private int _isActive;
        private IComponentRegistration? _registration;
        private readonly World _world;

        internal WorldComponentRestoreContext(World world)
        {
            ArgumentNullException.ThrowIfNull(world);
            _world = world;
            OwnerThreadId = world._ownerThreadId;
        }

        public Exception? Failure => Volatile.Read(ref _failure);

        public bool IsActive => Volatile.Read(ref _isActive) == 1;

        public int OwnerThreadId { get; }

        private static bool ContainsDerivedDependency(
            IComponentRegistration registration,
            IComponentRegistration dependencyRegistration)
        {
            foreach (var dependency in registration.Descriptor.DerivedDependencies)
            {
                if ((dependency.ComponentType == dependencyRegistration.ComponentType)
                    && (dependency.Schema == dependencyRegistration.SchemaReference))
                {
                    return true;
                }
            }

            return false;
        }

        public TDependency ReadSameEntity<TDependency>()
            where TDependency : unmanaged
        {
            EnsureActive();

            if (!_allowSameEntityRead)
            {
                return Fail<TDependency>(new InvalidOperationException("Only a Derived component restore may read same-Entity dependencies."));
            }

            var registration = _registration
                ?? throw new InvalidOperationException("The component restore context omitted its registration.");
            IComponentRegistration? dependencyRegistration = null;

            foreach (var candidate in _world.Registration.Components)
            {
                if (candidate.ComponentType == typeof(TDependency))
                {
                    dependencyRegistration = candidate;
                    break;
                }
            }

            if (dependencyRegistration is null
                || (dependencyRegistration.Descriptor.Flags != ComponentStorageFlags.Persistent)
                || !ContainsDerivedDependency(registration, dependencyRegistration))
            {
                return Fail<TDependency>(new InvalidOperationException("The requested component is not an exact declared Persistent Derived dependency."));
            }

            if (!_world.TryGet(_entity, out TDependency value))
            {
                return Fail<TDependency>(new InvalidOperationException("The declared Persistent Derived dependency is not present on the receiving Entity."));
            }

            return value;
        }

        public void RecordFailure(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            _ = Interlocked.CompareExchange(ref _failure, exception, null);
        }

        public Entity ResolveEntity(ComponentFieldDescriptor referenceField, Guid targetEntityId)
        {
            EnsureActive();

            if (referenceField is null)
            {
                ThrowActiveMisuse(new ArgumentNullException(nameof(referenceField)));
            }

            if ((_registration is null)
                || (referenceField.ComponentSchema != _registration.SchemaReference)
                || !_registration.IsEntityReferencePath(referenceField.Path))
            {
                ThrowActiveMisuse(new ArgumentException("The selected field is not an exact registered Entity reference.", nameof(referenceField)));
            }

            if (targetEntityId == Guid.Empty)
            {
                ThrowActiveMisuse(new ArgumentException("A stable Entity identity must be nonempty.", nameof(targetEntityId)));
            }

            return _world.ResolveRestoreEntity(targetEntityId);
        }

        public Entity ResolveEntity(WorldSystemFieldDescriptor referenceField, Guid targetEntityId)
        {
            _ = referenceField;
            _ = targetEntityId;
            EnsureActive();
            return Fail<Entity>(new InvalidOperationException("A component restore context cannot resolve system state references."));
        }

        public Entity? ResolveOptionalEntity(ComponentFieldDescriptor referenceField, Guid targetEntityId)
        {
            EnsureActive();

            if (referenceField is null)
            {
                ThrowActiveMisuse(new ArgumentNullException(nameof(referenceField)));
            }

            if ((_registration is null)
                || (referenceField.ComponentSchema != _registration.SchemaReference)
                || !_registration.IsEntityReferencePath(referenceField.Path)
                || referenceField.Field.IsRequired)
            {
                ThrowActiveMisuse(new ArgumentException("The selected field is not an exact optional Entity reference.", nameof(referenceField)));
            }

            if (targetEntityId == Guid.Empty)
            {
                ThrowActiveMisuse(new ArgumentException("A stable Entity identity must be nonempty.", nameof(targetEntityId)));
            }

            if (!_world._identityLedger.TryGetLiveLocator(targetEntityId, IdentityKind.Entity, out _))
            {
                return null;
            }

            return _world.ResolveRestoreEntity(targetEntityId);
        }

        public void ThrowIfFailed()
        {
            if (Failure is { } failure)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        internal void Activate(Entity entity, IComponentRegistration registration, bool allowSameEntityRead)
        {
            ArgumentNullException.ThrowIfNull(registration);

            if (Interlocked.CompareExchange(ref _isActive, 2, 0) != 0)
            {
                throw new InvalidOperationException("The World already has an active component restore callback.");
            }

            _entity = entity;
            _registration = registration;
            _allowSameEntityRead = allowSameEntityRead;
            Volatile.Write(ref _failure, null);
            Volatile.Write(ref _isActive, 1);
        }

        internal void Revoke()
        {
            Volatile.Write(ref _isActive, 0);
            _registration = null;
            _entity = default;
            _allowSameEntityRead = false;
        }

        private void EnsureActive()
        {
            if (!IsActive)
            {
                throw new InvalidOperationException("The component restore context has expired.");
            }

            if (Environment.CurrentManagedThreadId != OwnerThreadId)
            {
                ThrowActiveMisuse(new InvalidOperationException("The component restore context is restricted to its issuing thread."));
            }
        }

        private T Fail<T>(Exception exception)
        {
            ThrowActiveMisuse(exception);
            throw new InvalidOperationException("The restore callback continued after an active authority failure.");
        }

        [DoesNotReturn]
        private void ThrowActiveMisuse(Exception exception)
        {
            RecordFailure(exception);
            ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }

    private sealed class WorldSystemRestoreContext : IComponentRestoreContext
    {
        private Exception? _failure;
        private int _isActive = 1;
        private readonly SystemRegistration _registration;
        private readonly Guid _systemId;
        private readonly World _world;

        internal WorldSystemRestoreContext(World world, SystemRegistration registration, Guid systemId)
        {
            ArgumentNullException.ThrowIfNull(world);
            ArgumentNullException.ThrowIfNull(registration);
            ArgumentOutOfRangeException.ThrowIfEqual(systemId, Guid.Empty);
            _world = world;
            _registration = registration;
            _systemId = systemId;
            OwnerThreadId = Environment.CurrentManagedThreadId;
        }

        public Exception? Failure => Volatile.Read(ref _failure);

        public bool IsActive => Volatile.Read(ref _isActive) == 1;

        public int OwnerThreadId { get; }

        public TDependency ReadSameEntity<TDependency>()
            where TDependency : unmanaged
        {
            EnsureActive();
            return Fail<TDependency>(new InvalidOperationException("System Restore cannot read component columns."));
        }

        public void RecordFailure(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            _ = Interlocked.CompareExchange(ref _failure, exception, null);
        }

        public Entity ResolveEntity(ComponentFieldDescriptor referenceField, Guid targetEntityId)
        {
            _ = referenceField;
            _ = targetEntityId;
            EnsureActive();
            return Fail<Entity>(new InvalidOperationException("System Restore cannot resolve component reference fields."));
        }

        public Entity ResolveEntity(WorldSystemFieldDescriptor referenceField, Guid targetEntityId)
        {
            EnsureActive();

            if (referenceField is null)
            {
                ThrowActiveMisuse(new ArgumentNullException(nameof(referenceField)));
            }

            var schema = _registration.Factory.Descriptor.StateSchema;
            var isRegistered = false;

            foreach (var candidate in _registration.Factory.Descriptor.Fields)
            {
                isRegistered |= ReferenceEquals(candidate, referenceField);
            }

            if (!schema.HasValue
                || (referenceField.StateSchema != schema.Value)
                || !isRegistered
                || (referenceField.Field.WireKind != 11)
                || (referenceField.Field.Reference != ContentReferenceKind.Entity))
            {
                ThrowActiveMisuse(new ArgumentException("The selected field is not an exact registered system Entity reference.", nameof(referenceField)));
            }

            if (targetEntityId == Guid.Empty)
            {
                ThrowActiveMisuse(new ArgumentException("A stable Entity identity must be nonempty.", nameof(targetEntityId)));
            }

            return _world.ResolveRestoreEntity(targetEntityId);
        }

        public Entity? ResolveOptionalEntity(ComponentFieldDescriptor referenceField, Guid targetEntityId)
        {
            _ = referenceField;
            _ = targetEntityId;
            EnsureActive();
            return Fail<Entity?>(new InvalidOperationException("System Restore cannot resolve component reference fields."));
        }

        public void ThrowIfFailed()
        {
            if (Failure is { } failure)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        internal void Revoke()
            => Volatile.Write(ref _isActive, 0);

        private void EnsureActive()
        {
            if (!IsActive)
            {
                throw new InvalidOperationException("The system restore context has expired.");
            }

            if (Environment.CurrentManagedThreadId != OwnerThreadId)
            {
                ThrowActiveMisuse(new InvalidOperationException("The system restore context is restricted to its issuing thread."));
            }
        }

        private T Fail<T>(Exception exception)
        {
            ThrowActiveMisuse(exception);
            throw new InvalidOperationException("The system Restore callback continued after an active authority failure.");
        }

        [DoesNotReturn]
        private void ThrowActiveMisuse(Exception exception)
        {
            RecordFailure(exception);
            ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }
}
