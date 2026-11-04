// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using TerraFX;

namespace TerraFX.Entities;

internal static class WorldPersistenceEcsReader
{
    private const ushort ArrayKind = 24;
    private const ushort GuidKind = 11;
    private const int IdentityHistoryFieldCount = 7;
    private const ushort Int64Kind = 8;
    private const int MaximumIdentityPageCount = 8192;
    private const int MaximumSystemOrResourceCount = 4096;
    private const ushort RecordKind = 22;
    private const int RootFieldCount = 5;

    private static readonly Guid s_attachmentSchemaId = new ContentSchemaIdentityAssignment("state/attachment").SchemaId;
    private static readonly Guid s_ecsSchemaId = new ContentSchemaIdentityAssignment("state/ecs").SchemaId;
    private static readonly Guid s_entitySchemaId = new ContentSchemaIdentityAssignment("state/entity").SchemaId;
    private static readonly Guid s_identityBranchSchemaId = new ContentSchemaIdentityAssignment("state/identity-branch").SchemaId;
    private static readonly Guid s_identityHistorySchemaId = new ContentSchemaIdentityAssignment("state/identity-history").SchemaId;
    private static readonly Guid s_identityPageSchemaId = new ContentSchemaIdentityAssignment("state/identity-page").SchemaId;
    private static readonly Guid s_resourceStateSchemaId = new ContentSchemaIdentityAssignment("state/resource-state").SchemaId;
    private static readonly Guid s_systemStateSchemaId = new ContentSchemaIdentityAssignment("state/system-state").SchemaId;

    internal static WorldPersistenceEcsContents Read(
        ReadOnlyMemory<byte> ecsRecord,
        EbinReadLimits limits,
        WorldPersistenceConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(configuration);

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
        ValidateArrayChildren(record, 1, entityCount, s_entitySchemaId, minimumFieldCount: 5, maximumFieldCount: 6, nameof(ecsRecord));
        ValidateArrayChildren(record, 2, systemCount, s_systemStateSchemaId, minimumFieldCount: 2, maximumFieldCount: 2, nameof(ecsRecord));
        ValidateArrayChildren(record, 3, resourceCount, s_resourceStateSchemaId, minimumFieldCount: 2, maximumFieldCount: 2, nameof(ecsRecord));
        ValidateArrayChildren(record, 4, attachmentCount, s_attachmentSchemaId, minimumFieldCount: 3, maximumFieldCount: 3, nameof(ecsRecord));
        var historyField = GetRequiredField(record, 5, RecordKind, expectedCount: 1, nameof(ecsRecord));
        var history = new EbinRecordReadView(historyField.Payload);

        if ((history.Header.SchemaId != s_identityHistorySchemaId)
            || (history.Header.Major != 1)
            || (history.Header.Minor != 0)
            || (history.FieldCount != IdentityHistoryFieldCount))
        {
            throw new ArgumentException("The state/identity-history record has the wrong schema identity, version, or field count.", nameof(ecsRecord));
        }

        var worldGuid = ReadGuid(history, 1, nameof(ecsRecord));
        var replayLineageId = ReadGuid(history, 2, nameof(ecsRecord));
        var activeBranchId = ReadGuid(history, 3, nameof(ecsRecord));
        var issuanceNamespace = ReadGuid(history, 4, nameof(ecsRecord));
        var nextAssignmentOrdinal = ReadInt64(history, 5, nameof(ecsRecord));
        var identityBranchCount = ReadArrayCount(
            history,
            6,
            configuration.MaximumIdentityBranches,
            minimumCount: 1,
            limits,
            nameof(ecsRecord));
        var identityPageCount = ReadArrayCount(
            history,
            7,
            MaximumIdentityPageCount,
            minimumCount: 0,
            limits,
            nameof(ecsRecord));

        if ((worldGuid == Guid.Empty)
            || (replayLineageId == Guid.Empty)
            || (activeBranchId == Guid.Empty)
            || (issuanceNamespace == Guid.Empty)
            || (nextAssignmentOrdinal < 1)
            || (nextAssignmentOrdinal > checked((long)configuration.MaximumIdentityAssignments + 1)))
        {
            throw new ArgumentException("The state/identity-history contains invalid World, lineage, branch, namespace, or cursor metadata.", nameof(ecsRecord));
        }

        ValidateArrayChildren(
            history,
            6,
            identityBranchCount,
            s_identityBranchSchemaId,
            minimumFieldCount: 7,
            maximumFieldCount: 10,
            nameof(ecsRecord));
        ValidateArrayChildren(
            history,
            7,
            identityPageCount,
            s_identityPageSchemaId,
            minimumFieldCount: 3,
            maximumFieldCount: 3,
            nameof(ecsRecord));

        return new WorldPersistenceEcsContents(
            entityCount,
            systemCount,
            resourceCount,
            attachmentCount,
            worldGuid,
            replayLineageId,
            activeBranchId,
            issuanceNamespace,
            nextAssignmentOrdinal,
            identityBranchCount,
            identityPageCount);
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
            throw new ArgumentException("A state/identity-history GUID field has an invalid payload length.", parameterName);
        }

        return new Guid(field.Payload, bigEndian: true);
    }

    private static long ReadInt64(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, Int64Kind, expectedCount: 1, parameterName);

        if (field.Payload.Length != sizeof(long))
        {
            throw new ArgumentException("A state/identity-history Int64 field has an invalid payload length.", parameterName);
        }

        return BinaryPrimitives.ReadInt64LittleEndian(field.Payload);
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
}
