// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;

namespace TerraFX.Runtime.Tests.Scenes;

internal struct SceneEntityBindingRecordAssertions : IAction<EbinRecordReadView>
{
    internal bool IsMatch { get; private set; }

    public void Invoke(scoped EbinRecordReadView record)
    {
        IsMatch = (record.Header.SchemaId == new ContentSchemaIdentityAssignment("state/scene-entity-binding").SchemaId)
            && (record.Header.Major == 1)
            && (record.Header.Minor == 0)
            && (record.FieldCount == 6);

        if (!IsMatch)
        {
            return;
        }

        var path = record.GetField(0);
        var templateAsset = record.GetField(1);
        IsMatch = (path.Tag == 1)
            && (path.Kind == 23)
            && path.IsRequired
            && (path.Count <= 32)
            && (path.Payload.Length == checked(32 + (checked((int)path.Count) * 16)))
            && (templateAsset.Tag == 2)
            && (templateAsset.Kind == 22)
            && templateAsset.IsRequired
            && (templateAsset.Count == 1);

        for (var index = 2; index < record.FieldCount; index++)
        {
            var field = record.GetField(index);
            IsMatch &= (field.Tag == (uint)(index + 1))
                && (field.Kind == 11)
                && field.IsRequired
                && (field.Count == 1)
                && (field.Payload.Length == 16);
        }
    }
}
