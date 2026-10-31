// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.IO;
using TerraFX;

namespace TerraFX.Scenes;

internal static class SceneHierarchyReader
{
    private static readonly Guid s_hierarchyEntrySchemaId = new ContentSchemaIdentityAssignment("native/hierarchy-entry").SchemaId;
    private static readonly Guid s_hierarchyNodeSchemaId = new ContentSchemaIdentityAssignment("native/hierarchy-node").SchemaId;
    private static readonly Guid s_hierarchySchemaId = new ContentSchemaIdentityAssignment("native/hierarchy").SchemaId;

    internal static SceneHierarchy Read(scoped ReadOnlySpan<byte> source, EbinReadLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        var action = new ReadAction(limits);
        var status = EbinStructuralReader.Read(
            source,
            limits,
            EbinReadPolicy.Canonical,
            ref action,
            out var bytesConsumed,
            out var diagnostic);

        if (status is not EbinReadStatus.Complete)
        {
            throw new InvalidDataException(
                $"The hierarchy record is structurally invalid: {diagnostic?.Category ?? status.ToString()}.");
        }

        if (bytesConsumed != source.Length)
        {
            throw new InvalidDataException("The hierarchy record contains trailing bytes.");
        }

        return action.Result
            ?? throw new InvalidOperationException("The hierarchy reader did not produce a value.");
    }

    private static EbinFieldReadView GetField(
        EbinRecordReadView record,
        uint tag,
        ushort kind,
        bool isRequired,
        ulong count,
        int payloadLength)
    {
        if (!record.TryGetField(tag, out var field)
            || (field.Kind != kind)
            || (field.IsRequired != isRequired)
            || (field.Count != count)
            || (field.Payload.Length != payloadLength))
        {
            throw new InvalidDataException($"Hierarchy field {tag} does not match its schema.");
        }

        return field;
    }

    private static EbinFieldReadView GetRecordArrayField(
        EbinRecordReadView record,
        uint tag,
        bool isRequired,
        EbinReadLimits limits)
    {
        if (!record.TryGetField(tag, out var field)
            || (field.Kind != 24)
            || (field.IsRequired != isRequired)
            || (field.Count > checked((ulong)limits.MaximumArrayElements))
            || (field.Count > int.MaxValue))
        {
            throw new InvalidDataException($"Hierarchy record-array field {tag} does not match its schema.");
        }

        return field;
    }

    private static SceneHierarchyEntry ReadEntry(EbinRecordReadView record)
    {
        ValidateHeader(record, s_hierarchyEntrySchemaId, 1, fieldCount: 2);
        var kindField = GetField(record, 1, kind: 1, isRequired: true, count: 1, payloadLength: sizeof(byte));
        var kind = (SceneHierarchyEntryKind)kindField.Payload[0];

        if (kind is not SceneHierarchyEntryKind.Entity and not SceneHierarchyEntryKind.PrefabPlacement)
        {
            throw new InvalidDataException("The hierarchy entry kind is not Entity or PrefabPlacement.");
        }

        var id = ReadGuid(record, 2, isRequired: true);
        return new SceneHierarchyEntry(kind, id);
    }

    private static Guid ReadGuid(EbinRecordReadView record, uint tag, bool isRequired)
        => ReadGuidPayload(GetField(record, tag, kind: 11, isRequired, count: 1, payloadLength: 16).Payload);

    private static Guid ReadGuidPayload(scoped ReadOnlySpan<byte> payload)
    {
        var id = new Guid(payload, bigEndian: true);

        if (id == Guid.Empty)
        {
            throw new InvalidDataException("Hierarchy GUID values must be nonempty.");
        }

        return id;
    }

    private static SceneHierarchy ReadHierarchy(EbinRecordReadView record, EbinReadLimits limits)
    {
        ValidateHeader(record, s_hierarchySchemaId, 2, fieldCount: 2);
        var nodesField = GetRecordArrayField(record, 1, isRequired: true, limits);
        var rootsField = GetRecordArrayField(record, 2, isRequired: true, limits);
        var nodeCount = checked((int)nodesField.Count);
        var rootCount = checked((int)rootsField.Count);
        var nodes = ImmutableArray.CreateBuilder<SceneHierarchyNode>(nodeCount);
        var roots = ImmutableArray.CreateBuilder<SceneHierarchyEntry>(rootCount);
        var previousEntityId = Guid.Empty;

        for (var nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
        {
            var node = ReadNode(record.GetRecordArrayElement(1, nodeIndex), limits);

            if (previousEntityId != Guid.Empty
                && (FoundationCatalogue.CompareRfc(previousEntityId, node.EntityId) >= 0))
            {
                throw new InvalidDataException("Hierarchy nodes are not in strictly increasing RFC EntityId order.");
            }

            previousEntityId = node.EntityId;
            nodes.Add(node);
        }

        for (var rootIndex = 0; rootIndex < rootCount; rootIndex++)
        {
            roots.Add(ReadEntry(record.GetRecordArrayElement(2, rootIndex)));
        }

        return new SceneHierarchy(nodes.MoveToImmutable(), roots.MoveToImmutable());
    }

    private static SceneHierarchyNode ReadNode(EbinRecordReadView record, EbinReadLimits limits)
    {
        var hasParent = record.TryGetField(2, out var parentField);
        ValidateHeader(record, s_hierarchyNodeSchemaId, 2, fieldCount: hasParent ? 3 : 2);
        var entityId = ReadGuid(record, 1, isRequired: true);
        Guid? parentEntityId = null;

        if (hasParent)
        {
            if ((parentField.Kind != 11)
                || parentField.IsRequired
                || (parentField.Count != 1)
                || (parentField.Payload.Length != 16))
            {
                throw new InvalidDataException("The hierarchy parent field is not an optional single Entity GUID.");
            }

            parentEntityId = ReadGuidPayload(parentField.Payload);
        }

        var childrenField = GetRecordArrayField(record, 3, isRequired: true, limits);
        var childCount = checked((int)childrenField.Count);
        var children = ImmutableArray.CreateBuilder<SceneHierarchyEntry>(childCount);

        for (var childIndex = 0; childIndex < childCount; childIndex++)
        {
            children.Add(ReadEntry(record.GetRecordArrayElement(3, childIndex)));
        }

        return new SceneHierarchyNode(entityId, parentEntityId, children.MoveToImmutable());
    }

    private static void ValidateHeader(EbinRecordReadView record, Guid schemaId, uint major, int fieldCount)
    {
        var header = record.Header;

        if ((header.SchemaId != schemaId)
            || (header.Major != major)
            || (header.Minor != 0)
            || (record.FieldCount != fieldCount))
        {
            throw new InvalidDataException("The hierarchy record does not match the selected schema tuple.");
        }
    }

    private struct ReadAction : IAction<EbinRecordReadView>
    {
        private readonly EbinReadLimits _limits;

        internal ReadAction(EbinReadLimits limits)
        {
            _limits = limits;
            Result = null;
        }

        internal SceneHierarchy? Result { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
            => Result = ReadHierarchy(record, _limits);
    }
}
