// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Assets;
using TerraFX.Graphics;

namespace TerraFX.Rendering;

internal static class RenderM05SchemaReader
{
    internal static void Validate(
        EbinRecordReadView record,
        CompiledContentSchemaRegistry schemas,
        ContentSchemaReference expected)
    {
        var header = record.Header;
        var actual = new ContentSchemaReference(header.SchemaId, header.Major, header.Minor, ContentSchemaScope.Content);

        if ((actual != expected) || !schemas.TryGetSchema(expected, out _))
        {
            throw new NotSupportedException("The selected rendering record has no exact compiled schema.");
        }

        foreach (var schema in RenderingSchemaCatalogue.M05Schemas)
        {
            if (schema.Schema == expected)
            {
                _ = AssetReferenceScanner.CountAssetIds(record, schema, schemas);
                return;
            }
        }

        throw new NotSupportedException("The Rendering M05 codec does not select this root schema.");
    }
}
