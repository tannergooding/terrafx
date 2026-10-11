// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX;

internal sealed class EbinRecord
{
    internal EbinRecord(Guid schemaId, uint major, uint minor, scoped ReadOnlySpan<EbinField> fields)
    {
        SchemaId = schemaId;
        Major = major;
        Minor = minor;
        var sorted = fields.ToArray();
        Array.Sort(sorted, static (left, right) => left.Tag.CompareTo(right.Tag));
        Fields = ImmutableArray.Create(sorted);
    }

    internal ImmutableArray<EbinField> Fields { get; }

    internal uint Major { get; }

    internal uint Minor { get; }

    internal Guid SchemaId { get; }

    internal EbinField GetField(uint tag)
    {
        foreach (var field in Fields)
        {
            if (field.Tag == tag)
            {
                return field;
            }
        }

        throw new InvalidOperationException("The admitted record does not contain the requested field.");
    }
}
