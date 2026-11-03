// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using TerraFX;

namespace TerraFX.Runtime.Tests.Scenes;

internal struct SceneInstanceRecordAssertions : IAction<EbinRecordReadView>
{
    private readonly byte[] _bindingsArrayPayload;
    private readonly Guid[] _entityIds;
    private readonly int _externalBindingCount;
    private readonly byte[] _externalBindingsArrayPayload;
    private readonly byte[] _occurrencesArrayPayload;
    private readonly int _occurrenceCount;
    private readonly long _assignmentOrdinal;
    private readonly Guid _sceneInstanceId;
    private readonly Guid _sourceSchemaId;
    private readonly uint _sourceAssetId;

    internal SceneInstanceRecordAssertions(
        Guid sceneInstanceId,
        uint sourceAssetId,
        Guid sourceSchemaId,
        Guid[] entityIds,
        byte[] bindingsArrayPayload,
        byte[] externalBindingsArrayPayload,
        int externalBindingCount,
        byte[] occurrencesArrayPayload,
        int occurrenceCount,
        uint assignmentOrdinal)
    {
        _bindingsArrayPayload = bindingsArrayPayload;
        _entityIds = entityIds;
        _externalBindingCount = externalBindingCount;
        _externalBindingsArrayPayload = externalBindingsArrayPayload;
        _occurrencesArrayPayload = occurrencesArrayPayload;
        _occurrenceCount = occurrenceCount;
        _assignmentOrdinal = assignmentOrdinal;
        _sceneInstanceId = sceneInstanceId;
        _sourceSchemaId = sourceSchemaId;
        _sourceAssetId = sourceAssetId;
        IsMatch = false;
    }

    internal bool IsMatch { get; private set; }

    private static uint ReadUInt32(EbinFieldReadView field)
    {
        if ((field.Kind != 3) || (field.Payload.Length != sizeof(uint)))
        {
            return 0;
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(field.Payload);
    }

    public void Invoke(scoped EbinRecordReadView record)
    {
        IsMatch = (record.Header.SchemaId == new ContentSchemaIdentityAssignment("state/scene-instance").SchemaId)
            && (record.Header.Major == 1)
            && (record.Header.Minor == 0)
            && (record.FieldCount == 7);

        if (!IsMatch)
        {
            return;
        }

        var sceneInstanceId = record.GetField(0);
        var source = record.GetRecord(2);
        var entityIds = record.GetField(2);
        var bindings = record.GetField(3);
        var externalBindings = record.GetField(4);
        var assignmentOrdinal = record.GetField(5);
        var occurrences = record.GetField(6);
        IsMatch = (sceneInstanceId.Tag == 1)
            && (sceneInstanceId.Kind == 11)
            && sceneInstanceId.IsRequired
            && (new Guid(sceneInstanceId.Payload, bigEndian: true) == _sceneInstanceId)
            && (source.Header.SchemaId == new ContentSchemaIdentityAssignment("native/asset-reference").SchemaId)
            && (source.Header.Major == 1)
            && (source.Header.Minor == 0)
            && (source.FieldCount == 3)
            && (ReadUInt32(source.GetField(0)) == _sourceAssetId)
            && (new Guid(source.GetField(1).Payload, bigEndian: true) == _sourceSchemaId)
            && (ReadUInt32(source.GetField(2)) == 2)
            && (entityIds.Tag == 3)
            && (entityIds.Kind == 23)
            && entityIds.IsRequired
            && (entityIds.Count == checked((ulong)_entityIds.Length))
            && (entityIds.Payload.Length == checked(32 + (_entityIds.Length * 16)))
            && (bindings.Tag == 4)
            && (bindings.Kind == 24)
            && bindings.IsRequired
            && (bindings.Count == checked((ulong)_entityIds.Length))
            && bindings.Payload.SequenceEqual(_bindingsArrayPayload)
            && (externalBindings.Tag == 5)
            && (externalBindings.Kind == 24)
            && externalBindings.IsRequired
            && (externalBindings.Count == checked((ulong)_externalBindingCount))
            && externalBindings.Payload.SequenceEqual(_externalBindingsArrayPayload)
            && (assignmentOrdinal.Tag == 6)
            && (assignmentOrdinal.Kind == 8)
            && assignmentOrdinal.IsRequired
            && (assignmentOrdinal.Payload.Length == sizeof(long))
            && (BinaryPrimitives.ReadInt64LittleEndian(assignmentOrdinal.Payload) == _assignmentOrdinal)
            && (occurrences.Tag == 7)
            && (occurrences.Kind == 24)
            && occurrences.IsRequired
            && (occurrences.Count == checked((ulong)_occurrenceCount))
            && occurrences.Payload.SequenceEqual(_occurrencesArrayPayload);

        for (var index = 0; IsMatch && (index < _entityIds.Length); index++)
        {
            IsMatch = new Guid(
                entityIds.Payload.Slice(32 + (index * 16), 16),
                bigEndian: true) == _entityIds[index];
        }
    }
}
