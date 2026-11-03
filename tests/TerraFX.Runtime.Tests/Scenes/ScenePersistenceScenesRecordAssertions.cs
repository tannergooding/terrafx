// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

internal struct ScenePersistenceScenesRecordAssertions : IAction<EbinRecordReadView>
{
    private readonly byte[] _hierarchyRecord;
    private readonly byte[] _instancesArrayPayload;
    private readonly Guid[] _sceneInstanceIds;

    internal ScenePersistenceScenesRecordAssertions(
        byte[] instancesArrayPayload,
        Guid[] sceneInstanceIds,
        byte[] hierarchyRecord)
    {
        _instancesArrayPayload = instancesArrayPayload;
        _hierarchyRecord = hierarchyRecord;
        _sceneInstanceIds = sceneInstanceIds;
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
            && (instances.Count == checked((ulong)_sceneInstanceIds.Length))
            && instances.Payload.SequenceEqual(_instancesArrayPayload)
            && (hierarchy.Tag == 2)
            && (hierarchy.Kind == 22)
            && hierarchy.IsRequired
            && (hierarchy.Count == 1)
            && hierarchy.Payload.SequenceEqual(_hierarchyRecord)
            && (hierarchyRecord.Header.SchemaId == new ContentSchemaIdentityAssignment("scene/world-hierarchy").SchemaId)
            && (hierarchyRecord.Header.Major == 1)
            && (hierarchyRecord.Header.Minor == 0)
            && (hierarchyRecord.FieldCount == 2);

        for (var index = 0; IsMatch && (index < _sceneInstanceIds.Length); index++)
        {
            var sceneInstanceRecord = record.GetRecordArrayElement(1, index);
            var sceneInstanceId = sceneInstanceRecord.GetField(0);
            IsMatch = (sceneInstanceRecord.Header.SchemaId == new ContentSchemaIdentityAssignment("state/scene-instance").SchemaId)
                && (sceneInstanceRecord.Header.Major == 1)
                && (sceneInstanceRecord.Header.Minor == 0)
                && (sceneInstanceRecord.FieldCount == 7)
                && (sceneInstanceId.Tag == 1)
                && (sceneInstanceId.Kind == 11)
                && sceneInstanceId.IsRequired
                && (sceneInstanceId.Count == 1)
                && (sceneInstanceId.Payload.Length == 16)
                && (new Guid(sceneInstanceId.Payload, bigEndian: true) == _sceneInstanceIds[index]);
        }
    }
}
