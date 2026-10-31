// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Scenes;

internal sealed class SceneAuthoredRecord
{
    internal SceneAuthoredRecord(ContentSchemaReference schema, scoped ReadOnlySpan<byte> encodedRecord)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(schema.SchemaId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schema.Major);
        ArgumentOutOfRangeException.ThrowIfNegative(schema.Minor);
        Schema = schema;
        EncodedRecord = [.. encodedRecord];
    }

    internal ImmutableArray<byte> EncodedRecord { get; }

    internal ContentSchemaReference Schema { get; }
}
