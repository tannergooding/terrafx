// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using TerraFX;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

internal struct SceneWorldHierarchyRecordAssertions : IAction<EbinRecordReadView>
{
    private readonly Guid[] _childEntityIds;
    private readonly SceneWorldHierarchyCaptureRow[] _hierarchyRows;
    private readonly Guid[] _rootEntityIds;

    internal SceneWorldHierarchyRecordAssertions(
        SceneWorldHierarchyCaptureRow[] hierarchyRows,
        Guid[] rootEntityIds,
        Guid[] childEntityIds)
    {
        _hierarchyRows = hierarchyRows;
        _childEntityIds = childEntityIds;
        _rootEntityIds = rootEntityIds;
        IsMatch = false;
    }

    internal bool IsMatch { get; private set; }

    private static Guid ReadGuid(EbinFieldReadView field, bool required)
    {
        if ((field.Kind != 11)
            || (field.IsRequired != required)
            || (field.Count != 1)
            || (field.Payload.Length != 16))
        {
            return Guid.Empty;
        }

        return new Guid(field.Payload, bigEndian: true);
    }

    public void Invoke(scoped EbinRecordReadView record)
    {
        IsMatch = (record.Header.SchemaId == new ContentSchemaIdentityAssignment("scene/world-hierarchy").SchemaId)
            && (record.Header.Major == 1)
            && (record.Header.Minor == 0)
            && (record.FieldCount == 2);

        if (!IsMatch)
        {
            return;
        }

        var nodes = record.GetField(0);
        var roots = record.GetField(1);
        IsMatch = (nodes.Tag == 1)
            && (nodes.Kind == 24)
            && nodes.IsRequired
            && (nodes.Count == checked((ulong)_hierarchyRows.Length))
            && (nodes.Payload.Length >= sizeof(ulong))
            && (BinaryPrimitives.ReadUInt64LittleEndian(nodes.Payload) == checked((ulong)_hierarchyRows.Length))
            && (roots.Tag == 2)
            && (roots.Kind == 23)
            && roots.IsRequired
            && (roots.Count == checked((ulong)_rootEntityIds.Length))
            && (roots.Payload.Length == checked(32 + (_rootEntityIds.Length * 16)))
            && (BinaryPrimitives.ReadUInt16LittleEndian(roots.Payload[16..]) == 11)
            && (BinaryPrimitives.ReadUInt32LittleEndian(roots.Payload[20..]) == 16);

        for (var index = 0; IsMatch && (index < _rootEntityIds.Length); index++)
        {
            IsMatch = new Guid(
                roots.Payload.Slice(32 + (index * 16), 16),
                bigEndian: true) == _rootEntityIds[index];
        }

        for (var nodeIndex = 0; IsMatch && (nodeIndex < _hierarchyRows.Length); nodeIndex++)
        {
            var expected = _hierarchyRows[nodeIndex];
            var node = record.GetRecordArrayElement(1, nodeIndex);
            var hasParent = expected.ParentEntityId.HasValue;
            var expectedFieldCount = hasParent ? 3 : 2;
            IsMatch = (node.Header.SchemaId == new ContentSchemaIdentityAssignment("scene/world-hierarchy-node").SchemaId)
                && (node.Header.Major == 1)
                && (node.Header.Minor == 0)
                && (node.FieldCount == expectedFieldCount)
                && (node.GetField(0).Tag == 1)
                && (ReadGuid(node.GetField(0), required: true) == expected.EntityId);

            var childrenFieldIndex = 1;

            if (IsMatch && (expected.ParentEntityId is Guid expectedParentId))
            {
                IsMatch = (node.GetField(1).Tag == 2)
                    && (ReadGuid(node.GetField(1), required: false) == expectedParentId);
                childrenFieldIndex = 2;
            }

            if (!IsMatch)
            {
                break;
            }

            var children = node.GetField(childrenFieldIndex);
            var childEnd = checked(expected.ChildOffset + expected.ChildCount);
            IsMatch = (children.Tag == 3)
                && (children.Kind == 23)
                && children.IsRequired
                && (children.Count == checked((ulong)expected.ChildCount))
                && (children.Payload.Length == checked(32 + (expected.ChildCount * 16)))
                && (BinaryPrimitives.ReadUInt16LittleEndian(children.Payload[16..]) == 11)
                && (BinaryPrimitives.ReadUInt32LittleEndian(children.Payload[20..]) == 16);

            for (var childIndex = 0; IsMatch && (childIndex < expected.ChildCount); childIndex++)
            {
                IsMatch = new Guid(
                    children.Payload.Slice(32 + (childIndex * 16), 16),
                    bigEndian: true) == _childEntityIds[expected.ChildOffset + childIndex];
            }

            IsMatch &= childEnd <= _childEntityIds.Length;
        }
    }
}
