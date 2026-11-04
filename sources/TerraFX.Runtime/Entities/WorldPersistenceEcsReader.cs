// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;

namespace TerraFX.Entities;

internal static class WorldPersistenceEcsReader
{
    private const ushort ArrayKind = 24;
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
        WorldPersistenceClockContents clock)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(clock);

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
        var (worldGuid, replayLineageId, activeBranchId, issuanceNamespace, nextAssignmentOrdinal, branchCount, pageCount, liveEntityCount, liveSceneInstanceCount) =
            WorldPersistenceIdentityHistoryReader.Read(history, limits, configuration, clock.NextTick, nameof(ecsRecord));

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
