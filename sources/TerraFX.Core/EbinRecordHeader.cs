// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX;

/// <summary>Describes the bounded header claims of an EBIN record.</summary>
public readonly record struct EbinRecordHeader
{
    internal EbinRecordHeader(Guid schemaId, int major, int minor, int fieldCount, int recordByteLength)
    {
        SchemaId = schemaId;
        Major = major;
        Minor = minor;
        FieldCount = fieldCount;
        RecordByteLength = recordByteLength;
    }

    /// <summary>Gets the number of fields claimed by the record.</summary>
    public int FieldCount { get; }

    /// <summary>Gets the major schema version.</summary>
    public int Major { get; }

    /// <summary>Gets the minor schema version.</summary>
    public int Minor { get; }

    /// <summary>Gets the encoded record byte length.</summary>
    public int RecordByteLength { get; }

    /// <summary>Gets the schema identity.</summary>
    public Guid SchemaId { get; }
}
