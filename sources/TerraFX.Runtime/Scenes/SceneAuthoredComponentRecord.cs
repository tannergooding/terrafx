// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Scenes;

internal sealed class SceneAuthoredComponentRecord
{
    private readonly byte[] _encodedRecord;

    internal SceneAuthoredComponentRecord(ContentSchemaReference schema, scoped ReadOnlySpan<byte> encodedRecord)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(schema.SchemaId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schema.Major);
        ArgumentOutOfRangeException.ThrowIfNegative(schema.Minor);
        Schema = schema;
        _encodedRecord = encodedRecord.ToArray();
    }

    internal ReadOnlySpan<byte> EncodedRecord => _encodedRecord;

    internal ContentSchemaReference Schema { get; }
}
