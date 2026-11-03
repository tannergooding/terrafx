// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using TerraFX;

namespace TerraFX.Runtime.Tests.Scenes;

internal struct ScenePersistenceScenesRecordAssertions : IAction<EbinRecordReadView>
{
    private readonly byte[] _hierarchyRecord;

    internal ScenePersistenceScenesRecordAssertions(byte[] hierarchyRecord)
    {
        _hierarchyRecord = hierarchyRecord;
        IsMatch = false;
    }

    internal bool IsMatch { get; private set; }

    public void Invoke(scoped EbinRecordReadView record)
    {
        IsMatch = (record.Header.SchemaId == new ContentSchemaIdentityAssignment("state/scenes").SchemaId)
            && (record.Header.Major == 1)
            && (record.Header.Minor == 0)
            && (record.FieldCount == 2);

        if (!IsMatch)
        {
            return;
        }

        var instances = record.GetField(0);
        var hierarchy = record.GetField(1);
        var hierarchyRecord = record.GetRecord(2);
        IsMatch = (instances.Tag == 1)
            && (instances.Kind == 24)
            && instances.IsRequired
            && (instances.Count == 0)
            && (instances.Payload.Length == 16)
            && (BinaryPrimitives.ReadUInt64LittleEndian(instances.Payload) == 0)
            && (hierarchy.Tag == 2)
            && (hierarchy.Kind == 22)
            && hierarchy.IsRequired
            && (hierarchy.Count == 1)
            && hierarchy.Payload.SequenceEqual(_hierarchyRecord)
            && (hierarchyRecord.Header.SchemaId == new ContentSchemaIdentityAssignment("scene/world-hierarchy").SchemaId)
            && (hierarchyRecord.Header.Major == 1)
            && (hierarchyRecord.Header.Minor == 0)
            && (hierarchyRecord.FieldCount == 2);
    }
}
