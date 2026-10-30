// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX;

/// <summary>Provides borrowed field descriptions for one EBIN record.</summary>
public readonly ref struct EbinRecordWriteView
{
    private readonly ReadOnlySpan<EbinFieldSource> _fields;
    private readonly ReadOnlySpan<byte> _payloadStorage;

    /// <summary>Initializes a borrowed EBIN record description.</summary>
    /// <param name="schemaId">The record schema identity.</param>
    /// <param name="major">The positive major schema version.</param>
    /// <param name="minor">The nonnegative minor schema version.</param>
    /// <param name="fields">The sorted field descriptions.</param>
    /// <param name="payloadStorage">The caller-owned storage containing each payload range.</param>
    public EbinRecordWriteView(
        Guid schemaId,
        int major,
        int minor,
        ReadOnlySpan<EbinFieldSource> fields,
        ReadOnlySpan<byte> payloadStorage)
    {
        _fields = fields;
        _payloadStorage = payloadStorage;
        Major = major;
        Minor = minor;
        SchemaId = schemaId;
    }

    /// <summary>Gets the borrowed field descriptions.</summary>
    public ReadOnlySpan<EbinFieldSource> Fields => _fields;

    /// <summary>Gets the major schema version.</summary>
    public int Major { get; }

    /// <summary>Gets the minor schema version.</summary>
    public int Minor { get; }

    /// <summary>Gets the caller-owned payload storage.</summary>
    public ReadOnlySpan<byte> PayloadStorage => _payloadStorage;

    /// <summary>Gets the record schema identity.</summary>
    public Guid SchemaId { get; }
}
