// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;

namespace TerraFX.Entities;

internal static class WorldPersistenceEcsReader
{
    private const ushort ArrayKind = 24;
    private const ushort GuidKind = 11;
    private const ushort Int64Kind = 8;
    private const int MaximumSystemOrResourceCount = 4096;
    private const ushort RecordKind = 22;
    private const int RootFieldCount = 5;

    private static readonly Guid s_attachmentSchemaId = new ContentSchemaIdentityAssignment("state/attachment").SchemaId;
    private static readonly Guid s_ecsSchemaId = new ContentSchemaIdentityAssignment("state/ecs").SchemaId;
    private static readonly Guid s_entitySchemaId = new ContentSchemaIdentityAssignment("state/entity").SchemaId;
    private static readonly Guid s_resourceStateSchemaId = new ContentSchemaIdentityAssignment("state/resource-state").SchemaId;
    private static readonly Guid s_systemStateSchemaId = new ContentSchemaIdentityAssignment("state/system-state").SchemaId;

    internal static WorldPersistenceEcsContents Read(
        ReadOnlyMemory<byte> ecsRecord,
        EbinReadLimits limits,
        WorldPersistenceConfiguration configuration,
        WorldPersistenceClockContents clock,
        WorldPersistenceLiveIdentitySet liveIdentities,
        scoped Span<Guid> identityScratch)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(liveIdentities);

        var status = EbinStructuralReader.Validate(
            ecsRecord.Span,
            limits,
            EbinReadPolicy.Canonical,
            out var bytesConsumed,
            out _);

        if ((status is not EbinReadStatus.Complete)
            || (bytesConsumed != ecsRecord.Length))
        {
            throw new ArgumentException("The state/ecs record is not a complete canonical EBIN record.", nameof(ecsRecord));
        }

        var record = new EbinRecordReadView(ecsRecord.Span);

        if ((record.Header.SchemaId != s_ecsSchemaId)
            || (record.Header.Major != 1)
            || (record.Header.Minor != 0)
            || (record.FieldCount != RootFieldCount))
        {
            throw new ArgumentException("The state/ecs record has the wrong schema identity, version, or field count.", nameof(ecsRecord));
        }

        var entityCount = ReadArrayCount(record, 1, configuration.MaximumLiveEntities, minimumCount: 0, limits, nameof(ecsRecord));
        var systemCount = ReadArrayCount(record, 2, MaximumSystemOrResourceCount, minimumCount: 0, limits, nameof(ecsRecord));
        var resourceCount = ReadArrayCount(record, 3, MaximumSystemOrResourceCount, minimumCount: 0, limits, nameof(ecsRecord));
        var attachmentCount = ReadArrayCount(record, 4, limits.MaximumArrayElements, minimumCount: 0, limits, nameof(ecsRecord));
        ValidateArrayChildren(record, 2, systemCount, s_systemStateSchemaId, minimumFieldCount: 2, maximumFieldCount: 2, nameof(ecsRecord));
        ValidateArrayChildren(record, 3, resourceCount, s_resourceStateSchemaId, minimumFieldCount: 2, maximumFieldCount: 2, nameof(ecsRecord));
        ValidateArrayChildren(record, 4, attachmentCount, s_attachmentSchemaId, minimumFieldCount: 2, maximumFieldCount: 3, nameof(ecsRecord));
        var historyField = GetRequiredField(record, 5, RecordKind, expectedCount: 1, nameof(ecsRecord));
        var history = new EbinRecordReadView(historyField.Payload);
        var (worldGuid, replayLineageId, activeBranchId, issuanceNamespace, nextAssignmentOrdinal, branchCount, pageCount, liveEntityCount, liveSceneInstanceCount) =
            WorldPersistenceIdentityHistoryReader.Read(
                history,
                limits,
                configuration,
                liveIdentities,
                identityScratch,
                clock.NextTick,
                nameof(ecsRecord));
        ValidateEntityChildren(record, entityCount, liveIdentities, nameof(ecsRecord));

        for (var index = 0; index < attachmentCount; index++)
        {
            ValidateAttachmentOwner(record.GetRecordArrayElement(4, index), liveIdentities, nameof(ecsRecord));
        }

        return new WorldPersistenceEcsContents(
            entityCount,
            systemCount,
            resourceCount,
            attachmentCount,
            liveEntityCount,
            liveSceneInstanceCount,
            worldGuid,
            replayLineageId,
            activeBranchId,
            issuanceNamespace,
            nextAssignmentOrdinal,
            branchCount,
            pageCount);
    }

    internal static void ValidateAttachmentOwner(
        EbinRecordReadView attachment,
        WorldPersistenceLiveIdentitySet liveIdentities,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(liveIdentities);

        if ((attachment.Header.SchemaId != s_attachmentSchemaId)
            || (attachment.Header.Major != 1)
            || (attachment.Header.Minor != 0)
            || (attachment.FieldCount is < 2 or > 3))
        {
            throw new ArgumentException("A state/attachment row has the wrong schema identity, version, or field count.", parameterName);
        }

        if (!attachment.TryGetField(1, out var schemaField)
            || (schemaField.Kind != RecordKind)
            || !schemaField.IsRequired
            || (schemaField.Count != 1)
            || !attachment.TryGetField(3, out var payloadField)
            || (payloadField.Kind != RecordKind)
            || !payloadField.IsRequired
            || (payloadField.Count != 1))
        {
            throw new ArgumentException("A state/attachment row has an invalid schema or payload field.", parameterName);
        }

        var hasOwner = attachment.TryGetField(2, out var ownerField);

        if (attachment.FieldCount != (hasOwner ? 3 : 2))
        {
            throw new ArgumentException("A state/attachment row has an unknown or missing field.", parameterName);
        }

        if (!hasOwner)
        {
            return;
        }

        if ((ownerField.Kind != GuidKind)
            || ownerField.IsRequired
            || (ownerField.Count != 1)
            || (ownerField.Payload.Length != 16))
        {
            throw new ArgumentException("A state/attachment owner has an invalid wire shape.", parameterName);
        }

        var ownerEntityId = new Guid(ownerField.Payload, bigEndian: true);

        if ((ownerEntityId == Guid.Empty) || !liveIdentities.IsMatchedEntity(ownerEntityId))
        {
            throw new ArgumentException("A state/attachment owner is not a live ECS Entity.", parameterName);
        }
    }

    private static EbinFieldReadView GetRequiredField(
        EbinRecordReadView record,
        uint tag,
        ushort kind,
        ulong? expectedCount,
        string parameterName)
    {
        if (!record.TryGetField(tag, out var field)
            || (field.Kind != kind)
            || !field.IsRequired
            || (expectedCount.HasValue && (field.Count != expectedCount.Value)))
        {
            throw new ArgumentException("A state/ecs field has an invalid wire shape.", parameterName);
        }

        return field;
    }

    private static int ReadArrayCount(
        EbinRecordReadView record,
        uint tag,
        int maximumCount,
        int minimumCount,
        EbinReadLimits limits,
        string parameterName)
    {
        var field = GetRequiredField(record, tag, ArrayKind, expectedCount: null, parameterName);

        if ((field.Count < (ulong)minimumCount)
            || (field.Count > (ulong)maximumCount)
            || (field.Count > (ulong)limits.MaximumArrayElements)
            || (field.Count > int.MaxValue))
        {
            throw new ArgumentException("A state/ecs array count is outside its supported range.", parameterName);
        }

        return checked((int)field.Count);
    }

    private static long ReadInt64(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, Int64Kind, expectedCount: 1, parameterName);

        if (field.Payload.Length != sizeof(long))
        {
            throw new ArgumentException("A state/entity Int64 field has an invalid payload length.", parameterName);
        }

        return System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(field.Payload);
    }

    private static void ValidateArrayChildren(
        EbinRecordReadView record,
        uint fieldTag,
        int count,
        Guid schemaId,
        int minimumFieldCount,
        int maximumFieldCount,
        string parameterName)
    {
        for (var index = 0; index < count; index++)
        {
            var child = record.GetRecordArrayElement(fieldTag, index);

            if ((child.Header.SchemaId != schemaId)
                || (child.Header.Major != 1)
                || (child.Header.Minor != 0)
                || (child.FieldCount < minimumFieldCount)
                || (child.FieldCount > maximumFieldCount))
            {
                throw new ArgumentException("A state/ecs array child has the wrong schema identity, version, or field count.", parameterName);
            }
        }
    }

    private static void ValidateEntityChildren(
        EbinRecordReadView record,
        int count,
        WorldPersistenceLiveIdentitySet liveIdentities,
        string parameterName)
    {
        for (var index = 0; index < count; index++)
        {
            var entity = record.GetRecordArrayElement(1, index);

            if ((entity.Header.SchemaId != s_entitySchemaId)
                || (entity.Header.Major != 1)
                || (entity.Header.Minor != 0)
                || (entity.FieldCount is < 5 or > 6))
            {
                throw new ArgumentException("A state/entity child has the wrong schema identity, version, or field count.", parameterName);
            }

            var idField = GetRequiredField(entity, 1, GuidKind, expectedCount: 1, parameterName);

            if (idField.Payload.Length != 16)
            {
                throw new ArgumentException("A state/entity identity has an invalid payload length.", parameterName);
            }

            var entityId = new Guid(idField.Payload, bigEndian: true);
            liveIdentities.MatchEntity(entityId, parameterName);
            var assignmentOrdinal = ReadInt64(entity, 5, parameterName);

            if (liveIdentities.GetAssignmentOrdinal(entityId, expectedKind: 1, parameterName) != assignmentOrdinal)
            {
                throw new ArgumentException("A state/entity ordinal does not match its identity-history assignment.", parameterName);
            }

            var hasSceneInstanceId = entity.TryGetField(6, out var sceneInstanceField);

            if (entity.FieldCount != (hasSceneInstanceId ? 6 : 5))
            {
                throw new ArgumentException("A state/entity has an unknown or missing field.", parameterName);
            }

            if (hasSceneInstanceId)
            {
                if ((sceneInstanceField.Kind != GuidKind)
                    || sceneInstanceField.IsRequired
                    || (sceneInstanceField.Count != 1)
                    || (sceneInstanceField.Payload.Length != 16))
                {
                    throw new ArgumentException("A state/entity SceneInstanceId has an invalid wire shape.", parameterName);
                }

                liveIdentities.SetSceneOwner(
                    entityId,
                    new Guid(sceneInstanceField.Payload, bigEndian: true),
                    parameterName);
            }
        }
    }
}
