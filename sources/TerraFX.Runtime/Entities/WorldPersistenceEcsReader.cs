// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
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
    private const ushort UInt32Kind = 3;
    private const ushort UInt8Kind = 1;

    private static readonly Guid s_attachmentSchemaId = new ContentSchemaIdentityAssignment("state/attachment").SchemaId;
    private static readonly Guid s_ecsSchemaId = new ContentSchemaIdentityAssignment("state/ecs").SchemaId;
    private static readonly Guid s_entitySchemaId = new ContentSchemaIdentityAssignment("state/entity").SchemaId;
    private static readonly Guid s_localTransformSchemaId = new ContentSchemaIdentityAssignment("scene/local-transform").SchemaId;
    private static readonly Guid s_resourceStateSchemaId = new ContentSchemaIdentityAssignment("state/resource-state").SchemaId;
    private static readonly Guid s_schemaReferenceSchemaId = new ContentSchemaIdentityAssignment("content/schema-ref").SchemaId;
    private static readonly Guid s_systemStateSchemaId = new ContentSchemaIdentityAssignment("state/system-state").SchemaId;

    internal static WorldPersistenceEcsContents Read(
        ReadOnlyMemory<byte> ecsRecord,
        EbinReadLimits limits,
        WorldPersistenceConfiguration configuration,
        WorldPersistenceClockContents clock,
        WorldRegistration? targetRegistration,
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
        ValidateEntityChildren(
            record,
            entityCount,
            configuration.MaximumComponentTypes,
            liveIdentities,
            nameof(ecsRecord));

        for (var index = 0; index < attachmentCount; index++)
        {
            ValidateAttachmentOwner(
                record.GetRecordArrayElement(4, index),
                targetRegistration,
                liveIdentities,
                nameof(ecsRecord));
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
        WorldRegistration? targetRegistration,
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

        var schemaField = GetRequiredField(attachment, 1, RecordKind, expectedCount: 1, parameterName);
        var payloadField = GetRequiredField(attachment, 3, RecordKind, expectedCount: 1, parameterName);
        var schema = ReadSchemaReference(new EbinRecordReadView(schemaField.Payload), parameterName);
        var payload = new EbinRecordReadView(payloadField.Payload);

        if ((payload.Header.SchemaId != schema.SchemaId)
            || (payload.Header.Major != schema.Major)
            || (payload.Header.Minor != schema.Minor))
        {
            throw new ArgumentException("A state/attachment payload does not match its saved schema reference.", parameterName);
        }

        if (targetRegistration is not null)
        {
            var scope = attachment.TryGetField(2, out _)
                ? AttachmentScope.Entity
                : AttachmentScope.World;
            WorldAttachmentFactoryDescriptor descriptor;

            try
            {
                descriptor = targetRegistration.GetAttachmentBinding(scope, schema);
            }
            catch (KeyNotFoundException exception)
            {
                throw new ArgumentException("A saved attachment schema and scope are not registered in the target World.", parameterName, exception);
            }

            ValidateAttachmentEntityReferences(payload, descriptor, liveIdentities, parameterName);
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

    private static Guid ReadGuid(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, GuidKind, expectedCount: 1, parameterName);

        if (field.Payload.Length != 16)
        {
            throw new ArgumentException("A state/attachment GUID field has an invalid payload length.", parameterName);
        }

        return new Guid(field.Payload, bigEndian: true);
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

    private static ContentSchemaReference ReadSchemaReference(EbinRecordReadView record, string parameterName)
    {
        if ((record.Header.SchemaId != s_schemaReferenceSchemaId)
            || (record.Header.Major != 1)
            || (record.Header.Minor != 0)
            || (record.FieldCount != 4))
        {
            throw new ArgumentException("An attachment schema reference has the wrong schema identity, version, or field count.", parameterName);
        }

        var schemaId = ReadGuid(record, 1, parameterName);
        var major = ReadUInt32(record, 2, parameterName);
        var minor = ReadUInt32(record, 3, parameterName);
        var scopeValue = ReadUInt8(record, 4, parameterName);

        if ((major > int.MaxValue)
            || (minor > int.MaxValue)
            || !Enum.IsDefined((ContentSchemaScope)scopeValue))
        {
            throw new ArgumentException("An attachment schema reference contains an invalid version or scope.", parameterName);
        }

        return new ContentSchemaReference(schemaId, checked((int)major), checked((int)minor), (ContentSchemaScope)scopeValue);
    }

    private static uint ReadUInt32(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, UInt32Kind, expectedCount: 1, parameterName);

        if (field.Payload.Length != sizeof(uint))
        {
            throw new ArgumentException("An attachment schema-reference UInt32 field has an invalid payload length.", parameterName);
        }

        return System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(field.Payload);
    }

    private static byte ReadUInt8(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, UInt8Kind, expectedCount: 1, parameterName);

        if (field.Payload.Length != sizeof(byte))
        {
            throw new ArgumentException("An attachment schema-reference UInt8 field has an invalid payload length.", parameterName);
        }

        return field.Payload[0];
    }

    private static DataSchema ResolveAttachmentNestedSchema(
        CompiledContentSchemaRegistry schemas,
        ContentFieldDescriptor field,
        EbinRecordReadView record,
        string parameterName)
    {
        DataSchema? resolved = null;

        foreach (var candidate in schemas.Schemas)
        {
            if ((candidate.Schema.SchemaId != record.Header.SchemaId)
                || (candidate.Schema.Major != record.Header.Major)
                || (candidate.Schema.Minor != record.Header.Minor))
            {
                continue;
            }

            var isAllowed = false;

            foreach (var nestedSchema in field.NestedSchemas)
            {
                isAllowed |= nestedSchema == candidate.Schema;
            }

            if ((field.NestedRoleMask & (1UL << (int)candidate.Role)) != 0)
            {
                isAllowed = true;
            }

            if (!isAllowed)
            {
                continue;
            }

            if (resolved is not null)
            {
                throw new ArgumentException("An AttachmentState nested record schema selection is ambiguous.", parameterName);
            }

            resolved = candidate;
        }

        return resolved
            ?? throw new ArgumentException("An AttachmentState nested record does not match its declared schema selection.", parameterName);
    }

    private static bool TryGetAttachmentField(
        EbinRecordReadView payload,
        WorldAttachmentFieldDescriptor descriptor,
        string parameterName,
        out EbinFieldReadView field)
    {
        field = default;
        var current = payload;
        var schemas = descriptor.Schemas;

        if (!schemas.TryGetSchema(descriptor.Schema, out var currentSchema))
        {
            throw new ArgumentException("The target attachment schema is absent from its compiled registry.", parameterName);
        }

        var segments = descriptor.Path.Segments;

        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];

            if (segment.Kind != ComponentPropertySegmentKind.FieldTag)
            {
                throw new ArgumentException("An AttachmentState field path contains an unsupported segment.", parameterName);
            }

            if ((current.Header.SchemaId != currentSchema.Schema.SchemaId)
                || (current.Header.Major != currentSchema.Schema.Major)
                || (current.Header.Minor != currentSchema.Schema.Minor))
            {
                throw new ArgumentException("An AttachmentState nested record does not match its compiled schema path.", parameterName);
            }

            ContentFieldDescriptor? fieldSchema = null;

            foreach (var candidate in currentSchema.Fields)
            {
                if (candidate.Tag == segment.Value)
                {
                    fieldSchema = candidate;
                    break;
                }
            }

            if (fieldSchema is null)
            {
                throw new ArgumentException("An AttachmentState field path is absent from its compiled schema.", parameterName);
            }

            if (!current.TryGetField(segment.Value, out field))
            {
                if ((index == (segments.Length - 1)) && !fieldSchema.IsRequired)
                {
                    return false;
                }

                throw new ArgumentException("An AttachmentState nested record path is incomplete.", parameterName);
            }

            if ((field.Kind != fieldSchema.WireKind)
                || (field.IsRequired != fieldSchema.IsRequired)
                || (field.Count < fieldSchema.MinimumCount)
                || (field.Count > fieldSchema.MaximumCount))
            {
                throw new ArgumentException("An AttachmentState field does not match its compiled wire schema.", parameterName);
            }

            if (index != (segments.Length - 1))
            {
                if ((fieldSchema.WireKind != RecordKind)
                    || !field.IsRequired
                    || (field.Count != 1))
                {
                    throw new ArgumentException("An AttachmentState nested record has an invalid wire shape.", parameterName);
                }

                current = current.GetRecord(segment.Value);
                currentSchema = ResolveAttachmentNestedSchema(schemas, fieldSchema, current, parameterName);
            }
            else if (!ReferenceEquals(currentSchema, descriptor.DataSchema)
                || !ReferenceEquals(fieldSchema, descriptor.Field))
            {
                throw new ArgumentException("An AttachmentState field path does not match its registered descriptor.", parameterName);
            }
        }

        return true;
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

    private static void ValidateAttachmentEntityReferences(
        EbinRecordReadView payload,
        WorldAttachmentFactoryDescriptor descriptor,
        WorldPersistenceLiveIdentitySet liveIdentities,
        string parameterName)
    {
        foreach (var fieldDescriptor in descriptor.Fields)
        {
            var schemaField = fieldDescriptor.Field;

            if (schemaField.Reference != ContentReferenceKind.Entity)
            {
                continue;
            }

            if (schemaField.IsRequired
                || (schemaField.WireKind != GuidKind))
            {
                throw new ArgumentException("The target attachment registration has an unsupported Entity-reference field.", parameterName);
            }

            if (!TryGetAttachmentField(payload, fieldDescriptor, parameterName, out var field))
            {
                continue;
            }

            if ((field.Kind != GuidKind)
                || field.IsRequired
                || (field.Count != 1)
                || (field.Payload.Length != 16))
            {
                throw new ArgumentException("An AttachmentState Entity-reference field has an invalid wire shape.", parameterName);
            }

            var targetEntityId = new Guid(field.Payload, bigEndian: true);

            if ((targetEntityId == Guid.Empty) || !liveIdentities.IsMatchedEntity(targetEntityId))
            {
                throw new ArgumentException("An AttachmentState Entity reference does not identify a live ECS Entity.", parameterName);
            }
        }
    }

    private static void ValidateEntityChildren(
        EbinRecordReadView record,
        int count,
        int maximumComponentTypes,
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
            var componentField = GetRequiredField(entity, 2, ArrayKind, expectedCount: null, parameterName);

            if ((componentField.Count > (ulong)maximumComponentTypes)
                || (componentField.Count > 256)
                || (componentField.Count > int.MaxValue))
            {
                throw new ArgumentException("A state/entity component array exceeds its registered capacity.", parameterName);
            }

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

            var componentCount = checked((int)componentField.Count);

            for (var componentIndex = 0; componentIndex < componentCount; componentIndex++)
            {
                var component = entity.GetRecordArrayElement(2, componentIndex);

                if (component.Header.SchemaId == s_localTransformSchemaId)
                {
                    if ((component.Header.Major != 1) || (component.Header.Minor != 0))
                    {
                        throw new ArgumentException("A saved LocalTransform component has an unsupported schema version.", parameterName);
                    }

                    liveIdentities.MarkLocalTransform(entityId, parameterName);
                }
            }
        }
    }
}
