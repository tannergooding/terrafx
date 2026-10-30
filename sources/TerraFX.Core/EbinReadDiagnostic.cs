// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX;

/// <summary>Describes a structural EBIN read failure.</summary>
public readonly record struct EbinReadDiagnostic
{
    internal EbinReadDiagnostic(Guid schemaId, uint tag, string category, ulong offset, ulong claim, ulong limit)
    {
        SchemaId = schemaId;
        Tag = tag;
        Category = category;
        Offset = offset;
        Claim = claim;
        Limit = limit;
    }

    /// <summary>Gets the rejected claim, when the failure concerns a bounded value.</summary>
    public ulong Claim { get; }

    /// <summary>Gets the structural failure category.</summary>
    public string Category { get; }

    /// <summary>Gets the applicable limit, when the failure concerns a bounded value.</summary>
    public ulong Limit { get; }

    /// <summary>Gets the byte offset associated with the failure.</summary>
    public ulong Offset { get; }

    /// <summary>Gets the record schema identity associated with the failure.</summary>
    public Guid SchemaId { get; }

    /// <summary>Gets the field tag associated with the failure.</summary>
    public uint Tag { get; }
}
