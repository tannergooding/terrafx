// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX;

/// <summary>Describes an EBIN structural write failure.</summary>
public readonly record struct EbinStructuralDiagnostic
{
    internal EbinStructuralDiagnostic(
        Guid schemaId,
        uint tag,
        string category,
        ulong? offset,
        ulong claim,
        ulong limit,
        EbinStructuralDiagnosticSource source)
    {
        Claim = claim;
        Category = category;
        Limit = limit;
        Offset = offset;
        SchemaId = schemaId;
        Source = source;
        Tag = tag;
    }

    /// <summary>Gets the rejected claim, when the failure concerns a bounded value.</summary>
    public ulong Claim { get; }

    /// <summary>Gets the failure category.</summary>
    public string Category { get; }

    /// <summary>Gets the applicable limit, when the failure concerns a bounded value.</summary>
    public ulong Limit { get; }

    /// <summary>Gets the byte offset associated with the failure.</summary>
    public ulong? Offset { get; }

    /// <summary>Gets the record schema identity associated with the failure.</summary>
    public Guid SchemaId { get; }

    /// <summary>Gets the source phase that produced the failure.</summary>
    public EbinStructuralDiagnosticSource Source { get; }

    /// <summary>Gets the field tag associated with the failure.</summary>
    public uint Tag { get; }

    /// <summary>Creates a write diagnostic from a structural read diagnostic.</summary>
    /// <param name="diagnostic">The structural read diagnostic.</param>
    /// <returns>A diagnostic identified as encoded input.</returns>
    public static EbinStructuralDiagnostic FromRead(EbinReadDiagnostic diagnostic)
        => new EbinStructuralDiagnostic(
            diagnostic.SchemaId,
            diagnostic.Tag,
            diagnostic.Category,
            diagnostic.Offset,
            diagnostic.Claim,
            diagnostic.Limit,
            EbinStructuralDiagnosticSource.EncodedInput);
}
