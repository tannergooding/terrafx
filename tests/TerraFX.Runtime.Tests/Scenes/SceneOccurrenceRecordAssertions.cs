// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using TerraFX;

namespace TerraFX.Runtime.Tests.Scenes;

internal struct SceneOccurrenceRecordAssertions : IAction<EbinRecordReadView>
{
    private readonly Guid _occurrenceId;
    private readonly Guid? _parentOccurrenceId;
    private readonly int _placementPathLength;
    private readonly Guid? _placementPathTail;
    private readonly Guid? _placementId;
    private readonly Guid _sourceSchemaId;
    private readonly uint _sourceAssetId;

    internal SceneOccurrenceRecordAssertions(
        Guid occurrenceId,
        uint sourceAssetId,
        Guid sourceSchemaId,
        int placementPathLength,
        Guid? placementPathTail,
        Guid? parentOccurrenceId,
        Guid? placementId)
    {
        _occurrenceId = occurrenceId;
        _parentOccurrenceId = parentOccurrenceId;
        _placementPathLength = placementPathLength;
        _placementPathTail = placementPathTail;
        _placementId = placementId;
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
        IsMatch = (record.Header.SchemaId == new ContentSchemaIdentityAssignment("state/scene-occurrence").SchemaId)
            && (record.Header.Major == 1)
            && (record.Header.Minor == 0)
            && (record.FieldCount == (_parentOccurrenceId.HasValue ? 5 : 3));

        if (!IsMatch)
        {
            return;
        }

        var path = record.GetField(0);
        var occurrenceId = record.GetField(1);
        var source = record.GetRecord(3);
        IsMatch = (path.Tag == 1)
            && (path.Kind == 23)
            && path.IsRequired
            && (path.Count == checked((ulong)_placementPathLength))
            && (path.Payload.Length == checked(32 + (_placementPathLength * 16)))
            && (BinaryPrimitives.ReadUInt16LittleEndian(path.Payload[16..]) == 11)
            && (BinaryPrimitives.ReadUInt32LittleEndian(path.Payload[20..]) == 16)
            && (new Guid(occurrenceId.Payload, bigEndian: true) == _occurrenceId)
            && (source.Header.SchemaId == new ContentSchemaIdentityAssignment("native/asset-reference").SchemaId)
            && (source.Header.Major == 1)
            && (source.Header.Minor == 0)
            && (source.FieldCount == 3)
            && (ReadUInt32(source.GetField(0)) == _sourceAssetId)
            && (new Guid(source.GetField(1).Payload, bigEndian: true) == _sourceSchemaId)
            && (ReadUInt32(source.GetField(2)) == 2);

        if (_placementPathTail is Guid expectedPlacementPathTail)
        {
            IsMatch &= new Guid(path.Payload.Slice(32 + ((_placementPathLength - 1) * 16), 16), bigEndian: true)
                == expectedPlacementPathTail;
        }

        if (_parentOccurrenceId is Guid expectedParentOccurrenceId)
        {
            var parent = record.GetField(3);
            var placement = record.GetField(4);
            IsMatch &= (parent.Tag == 4)
                && (parent.Kind == 11)
                && !parent.IsRequired
                && (new Guid(parent.Payload, bigEndian: true) == expectedParentOccurrenceId)
                && (placement.Tag == 5)
                && (placement.Kind == 11)
                && !placement.IsRequired
                && (_placementId is Guid expectedPlacementId)
                && (new Guid(placement.Payload, bigEndian: true) == expectedPlacementId);
        }
    }
}
