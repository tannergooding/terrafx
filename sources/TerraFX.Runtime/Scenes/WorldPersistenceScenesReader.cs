// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using TerraFX;

namespace TerraFX.Scenes;

internal static class WorldPersistenceScenesReader
{
    private const ushort ArrayKind = 24;
    private const ushort FixedArrayKind = 23;
    private const ushort GuidKind = 11;
    private const ushort Int64Kind = 8;
    private const ushort RecordKind = 22;
    private const int SceneRootFieldCount = 2;

    private static readonly Guid s_externalBindingSchemaId = new ContentSchemaIdentityAssignment("state/external-scene-binding").SchemaId;
    private static readonly Guid s_hierarchyNodeSchemaId = new ContentSchemaIdentityAssignment("scene/world-hierarchy-node").SchemaId;
    private static readonly Guid s_hierarchySchemaId = new ContentSchemaIdentityAssignment("scene/world-hierarchy").SchemaId;
    private static readonly Guid s_nativeAssetReferenceSchemaId = new ContentSchemaIdentityAssignment("native/asset-reference").SchemaId;
    private static readonly Guid s_sceneEntityBindingSchemaId = new ContentSchemaIdentityAssignment("state/scene-entity-binding").SchemaId;
    private static readonly Guid s_sceneInstanceSchemaId = new ContentSchemaIdentityAssignment("state/scene-instance").SchemaId;
    private static readonly Guid s_sceneOccurrenceSchemaId = new ContentSchemaIdentityAssignment("state/scene-occurrence").SchemaId;
    private static readonly Guid s_scenesSchemaId = new ContentSchemaIdentityAssignment("state/scenes").SchemaId;

    internal static WorldPersistenceScenesContents Read(
        ReadOnlyMemory<byte> scenesRecord,
        EbinReadLimits limits,
        int maximumActiveSceneInstances,
        int maximumLiveEntities,
        int maximumIdentityAssignments,
        int maximumSceneOccurrences)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumActiveSceneInstances);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumLiveEntities);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumIdentityAssignments);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSceneOccurrences);

        var status = EbinStructuralReader.Validate(
            scenesRecord.Span,
            limits,
            EbinReadPolicy.Canonical,
            out var bytesConsumed,
            out _);

        if ((status is not EbinReadStatus.Complete)
            || (bytesConsumed != scenesRecord.Length))
        {
            throw new ArgumentException("The state/scenes record is not a complete canonical EBIN record.", nameof(scenesRecord));
        }

        var record = new EbinRecordReadView(scenesRecord.Span);

        if ((record.Header.SchemaId != s_scenesSchemaId)
            || (record.Header.Major != 1)
            || (record.Header.Minor != 0)
            || (record.FieldCount != SceneRootFieldCount))
        {
            throw new ArgumentException("The state/scenes record has the wrong schema identity, version, or field count.", nameof(scenesRecord));
        }

        var instanceCount = ReadArrayCount(
            record,
            1,
            maximumActiveSceneInstances,
            minimumCount: 0,
            limits,
            nameof(scenesRecord));
        var hierarchyField = GetRequiredField(record, 2, RecordKind, expectedCount: 1, nameof(scenesRecord));
        var hierarchy = new EbinRecordReadView(hierarchyField.Payload);

        if ((hierarchy.Header.SchemaId != s_hierarchySchemaId)
            || (hierarchy.Header.Major != 1)
            || (hierarchy.Header.Minor != 0)
            || (hierarchy.FieldCount != SceneRootFieldCount))
        {
            throw new ArgumentException("The scene/world-hierarchy record has the wrong schema identity, version, or field count.", nameof(scenesRecord));
        }

        var previousInstanceId = Guid.Empty;

        for (var index = 0; index < instanceCount; index++)
        {
            var instanceId = ValidateSceneInstance(
                record.GetRecordArrayElement(1, index),
                maximumLiveEntities,
                maximumIdentityAssignments,
                maximumSceneOccurrences,
                limits,
                nameof(scenesRecord));

            if ((index > 0) && (FoundationCatalogue.CompareRfc(previousInstanceId, instanceId) >= 0))
            {
                throw new ArgumentException("The state/scene-instance array is not in unique RFC SceneInstanceId order.", nameof(scenesRecord));
            }

            previousInstanceId = instanceId;
        }

        var hierarchyNodeCount = ReadArrayCount(
            hierarchy,
            1,
            maximumLiveEntities,
            minimumCount: 0,
            limits,
            nameof(scenesRecord));
        var rootsField = GetRequiredField(hierarchy, 2, FixedArrayKind, expectedCount: null, nameof(scenesRecord));
        var rootCount = ValidateGuidFixedArray(
            rootsField,
            maximumLiveEntities,
            nameof(scenesRecord));

        if (rootCount > hierarchyNodeCount)
        {
            throw new ArgumentException("The scene/world-hierarchy root count exceeds its node count.", nameof(scenesRecord));
        }

        var previousNodeId = Guid.Empty;

        for (var index = 0; index < hierarchyNodeCount; index++)
        {
            var node = hierarchy.GetRecordArrayElement(1, index);
            var nodeId = ValidateHierarchyNode(node, maximumLiveEntities, nameof(scenesRecord));

            if ((index > 0) && (FoundationCatalogue.CompareRfc(previousNodeId, nodeId) >= 0))
            {
                throw new ArgumentException("The scene/world-hierarchy node array is not in unique RFC EntityId order.", nameof(scenesRecord));
            }

            previousNodeId = nodeId;
        }

        return new WorldPersistenceScenesContents(instanceCount, hierarchyNodeCount, rootCount);
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
            throw new ArgumentException("A State2 Scene field has an invalid wire shape.", parameterName);
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
            throw new ArgumentException("A State2 Scene array count is outside its supported range.", parameterName);
        }

        return checked((int)field.Count);
    }

    private static Guid ReadGuid(EbinRecordReadView record, uint tag, bool isRequired, string parameterName)
    {
        if (!record.TryGetField(tag, out var field)
            || (field.Kind != GuidKind)
            || (field.IsRequired != isRequired)
            || (field.Count != 1)
            || (field.Payload.Length != 16))
        {
            throw new ArgumentException("A State2 Scene GUID field has an invalid wire shape.", parameterName);
        }

        var value = new Guid(field.Payload, bigEndian: true);

        if (value == Guid.Empty)
        {
            throw new ArgumentException("A State2 Scene identity cannot be empty.", parameterName);
        }

        return value;
    }

    private static long ReadInt64(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, Int64Kind, expectedCount: 1, parameterName);

        if (field.Payload.Length != sizeof(long))
        {
            throw new ArgumentException("A State2 Scene Int64 field has an invalid payload length.", parameterName);
        }

        return BinaryPrimitives.ReadInt64LittleEndian(field.Payload);
    }

    private static int ValidateGuidFixedArray(
        EbinFieldReadView field,
        int maximumCount,
        string parameterName)
    {
        if ((field.Kind != FixedArrayKind)
            || !field.IsRequired
            || (field.Count > (ulong)maximumCount)
            || (field.Count > int.MaxValue))
        {
            throw new ArgumentException("A State2 Scene fixed GUID array has an invalid wire shape or count.", parameterName);
        }

        var expectedLength = checked(32L + (16L * checked((long)field.Count)));
        var payload = field.Payload;

        if (((long)payload.Length != expectedLength)
            || (BinaryPrimitives.ReadUInt16LittleEndian(payload[16..]) != GuidKind)
            || (BinaryPrimitives.ReadUInt32LittleEndian(payload[20..]) != 16))
        {
            throw new ArgumentException("A State2 Scene fixed GUID array has an invalid payload header.", parameterName);
        }

        for (var index = 0; index < (int)field.Count; index++)
        {
            var offset = checked(32 + (index * 16));
            var id = new Guid(payload.Slice(offset, 16), bigEndian: true);

            if (id == Guid.Empty)
            {
                throw new ArgumentException("A State2 Scene fixed GUID array contains an empty identity.", parameterName);
            }
        }

        return checked((int)field.Count);
    }

    private static Guid ValidateHierarchyNode(
        EbinRecordReadView node,
        int maximumLiveEntities,
        string parameterName)
    {
        if ((node.Header.SchemaId != s_hierarchyNodeSchemaId)
            || (node.Header.Major != 1)
            || (node.Header.Minor != 0)
            || (node.FieldCount is not 2 and not 3))
        {
            throw new ArgumentException("A scene/world-hierarchy-node has the wrong schema identity, version, or field count.", parameterName);
        }

        var entityId = ReadGuid(node, 1, isRequired: true, parameterName);
        var hasParent = node.TryGetField(2, out var parentField);

        if (node.FieldCount != (hasParent ? 3 : 2))
        {
            throw new ArgumentException("A scene/world-hierarchy-node has an unknown or missing field.", parameterName);
        }

        if (hasParent)
        {
            if ((parentField.Kind != GuidKind)
                || parentField.IsRequired
                || (parentField.Count != 1)
                || (parentField.Payload.Length != 16))
            {
                throw new ArgumentException("A scene/world-hierarchy parent field has an invalid wire shape.", parameterName);
            }

            var parentId = new Guid(parentField.Payload, bigEndian: true);

            if ((parentId == Guid.Empty) || (parentId == entityId))
            {
                throw new ArgumentException("A scene/world-hierarchy parent identity is invalid.", parameterName);
            }
        }

        var childrenField = GetRequiredField(node, 3, FixedArrayKind, expectedCount: null, parameterName);
        _ = ValidateGuidFixedArray(childrenField, maximumLiveEntities, parameterName);
        return entityId;
    }

    private static void ValidateRecordArrayChildren(
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
            ValidateRecordHeader(
                record.GetRecordArrayElement(fieldTag, index),
                schemaId,
                minimumFieldCount,
                maximumFieldCount,
                parameterName);
        }
    }

    private static void ValidateRecordHeader(
        EbinRecordReadView record,
        Guid schemaId,
        int minimumFieldCount,
        int maximumFieldCount,
        string parameterName)
    {
        if ((record.Header.SchemaId != schemaId)
            || (record.Header.Major != 1)
            || (record.Header.Minor != 0)
            || (record.FieldCount < minimumFieldCount)
            || (record.FieldCount > maximumFieldCount))
        {
            throw new ArgumentException("A State2 Scene child has the wrong schema identity, version, or field count.", parameterName);
        }
    }

    private static Guid ValidateSceneInstance(
        EbinRecordReadView instance,
        int maximumLiveEntities,
        int maximumIdentityAssignments,
        int maximumSceneOccurrences,
        EbinReadLimits limits,
        string parameterName)
    {
        ValidateRecordHeader(instance, s_sceneInstanceSchemaId, minimumFieldCount: 7, maximumFieldCount: 7, parameterName);
        var instanceId = ReadGuid(instance, 1, isRequired: true, parameterName);
        var sourceField = GetRequiredField(instance, 2, RecordKind, expectedCount: 1, parameterName);
        ValidateRecordHeader(
            new EbinRecordReadView(sourceField.Payload),
            s_nativeAssetReferenceSchemaId,
            minimumFieldCount: 3,
            maximumFieldCount: 3,
            parameterName);
        var entityIdsField = GetRequiredField(instance, 3, FixedArrayKind, expectedCount: null, parameterName);
        var entityCount = ValidateGuidFixedArray(entityIdsField, maximumLiveEntities, parameterName);
        var bindingCount = ReadArrayCount(instance, 4, maximumLiveEntities, minimumCount: 0, limits, parameterName);
        var externalBindingCount = ReadArrayCount(instance, 5, 65_536, minimumCount: 0, limits, parameterName);
        var assignmentOrdinal = ReadInt64(instance, 6, parameterName);
        var occurrenceCount = ReadArrayCount(instance, 7, maximumSceneOccurrences, minimumCount: 1, limits, parameterName);

        if ((instanceId == Guid.Empty)
            || (assignmentOrdinal < 1)
            || (assignmentOrdinal > maximumIdentityAssignments)
            || (bindingCount != entityCount))
        {
            throw new ArgumentException("A state/scene-instance has inconsistent identity, assignment, or Entity binding counts.", parameterName);
        }

        ValidateRecordArrayChildren(instance, 4, bindingCount, s_sceneEntityBindingSchemaId, 6, 6, parameterName);
        ValidateRecordArrayChildren(instance, 5, externalBindingCount, s_externalBindingSchemaId, 1, 2, parameterName);
        ValidateRecordArrayChildren(instance, 7, occurrenceCount, s_sceneOccurrenceSchemaId, 3, 5, parameterName);
        return instanceId;
    }
}
