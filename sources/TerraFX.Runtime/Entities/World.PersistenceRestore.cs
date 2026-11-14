// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
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

    internal void RestoreEmptyEcsRows(
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

        var identityHistory = contents.ValidatedIdentityHistory
            ?? throw new ArgumentException("Candidate ECS restoration requires the validated source identity history.", nameof(contents));

        if ((contents.WorldGuid != _worldGuid)
            || (contents.ReplayLineageId != _replayLineageId)
            || (contents.LiveEntityCount != contents.EntityCount)
            || (_entitySlots.LiveEntityCount != contents.LiveEntityCount))
        {
            throw new ArgumentException("The validated ECS counts or identity do not match the restore candidate.", nameof(contents));
        }

        if ((contents.LiveSceneInstanceCount != 0)
            || (contents.SystemCount != 0)
            || (contents.ResourceCount != 0)
            || (contents.AttachmentCount != 0))
        {
            throw new NotSupportedException("This candidate restore slice does not support Scene identities or system, resource, or attachment state.");
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

            if ((field.Tag != (uint)fieldIndex + 1)
                || (field.Kind != 24)
                || !field.IsRequired
                || (field.Count != 0))
            {
                throw new NotSupportedException("This candidate restore slice does not support system, resource, or attachment state.");
            }
        }

        var previousEntityId = Guid.Empty;

        for (var index = 0; index < contents.EntityCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entityRecord = root.GetRecordArrayElement(1, index);
            var componentField = entityRecord.GetField(1);
            var tagField = entityRecord.GetField(2);
            var transientSchemaField = entityRecord.GetField(3);

            if ((entityRecord.Header.SchemaId != new ContentSchemaIdentityAssignment("state/entity").SchemaId)
                || (entityRecord.Header.Major != 1)
                || (entityRecord.Header.Minor != 0)
                || (entityRecord.FieldCount != 5)
                || (componentField.Tag != 2)
                || (componentField.Kind != 24)
                || !componentField.IsRequired
                || (componentField.Count != 0)
                || (tagField.Tag != 3)
                || (tagField.Kind != 24)
                || !tagField.IsRequired
                || (tagField.Count != 0)
                || (transientSchemaField.Tag != 4)
                || (transientSchemaField.Kind != 24)
                || !transientSchemaField.IsRequired
                || (transientSchemaField.Count != 0)
                || entityRecord.TryGetField(6, out _))
            {
                throw new NotSupportedException("This candidate restore slice supports only Entities without components, tags, transient state, or Scene ownership.");
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
            if ((_restoreEcsStorageByteReservation != 0)
                || (_restoreWorkPlanCapacity != 0)
                || (_restoreWorkPlanByteReservation != 0))
            {
                throw new InvalidOperationException("The empty restore candidate has unexpected ECS storage reservations.");
            }

            _isRestoreEcsRowsRestored = true;
            return;
        }

        var archetype = _archetypes[0]
            ?? throw new InvalidOperationException("The restore candidate omitted its empty archetype.");
        var storage = archetype.Storage;
        var storageBytes = storage.GetCapacityPreparationBytes(contents.EntityCount);
        var workPlanCount = checked((contents.EntityCount + 63) / 64);
        var workPlanCapacity = _restoreWorkPlanCapacity;
        var workPlanBytes = _restoreWorkPlanByteReservation;
        var growthBytes = checked(storageBytes + workPlanBytes);
        var existingBytes = GetWorldOwnedStateBytes();

        if ((storageBytes != _restoreEcsStorageByteReservation)
            || (workPlanCapacity < workPlanCount)
            || (workPlanCapacity == 0)
            || (CalculateComponentTagWorkPlanByteReservation(workPlanCapacity) != workPlanBytes)
            || (growthBytes > _maximumOwnedStateBytes - existingBytes))
        {
            throw new InvalidOperationException("The candidate restore plan did not pre-admit its ECS rows and query work plan.");
        }

        WorldChunkWorkPlanEntry[]? workPlan = null;

        if (workPlanCapacity > _componentTagWorkPlan.Length)
        {
            workPlan = new WorldChunkWorkPlanEntry[workPlanCapacity];
        }

        if (!storage.PrepareCapacity(contents.EntityCount))
        {
            throw new InvalidOperationException("The restore candidate could not prepare its validated ECS row capacity.");
        }

        _nextChunkOrdinal = 0;

        for (var index = 0; index < contents.EntityCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entityRecord = root.GetRecordArrayElement(1, index);
            var entityId = new Guid(entityRecord.GetField(0).Payload, bigEndian: true);
            var entity = ResolveRestoreEntity(entityId);

            if (!storage.TryAppend(entity, _entitySlots, ref _nextChunkOrdinal))
            {
                throw new InvalidOperationException("The restore candidate could not append a prevalidated ECS row.");
            }
        }

        if (workPlan is not null)
        {
            _componentTagWorkPlan = workPlan;
            _componentTagWorkPlanByteReservation = workPlanBytes;
        }

        RebuildComponentTagWorkPlan();
        _isRestoreEcsRowsRestored = true;
    }
}
