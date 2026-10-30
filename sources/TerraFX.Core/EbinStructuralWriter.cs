// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace TerraFX;

/// <summary>Measures and writes canonical EBIN record structure.</summary>
public static class EbinStructuralWriter
{
    /// <summary>Validates and copies one complete canonical record prefix.</summary>
    /// <param name="canonicalCandidate">A canonical record candidate.</param>
    /// <param name="limits">The structural limits for validation.</param>
    /// <param name="destination">The final record destination.</param>
    /// <param name="bytesWritten">The record prefix length, or zero on failure.</param>
    /// <param name="diagnostic">The validation or capacity failure.</param>
    /// <returns>The write status.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="limits"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> overlaps <paramref name="canonicalCandidate"/>.</exception>
    public static EbinWriteStatus Commit(
        scoped ReadOnlySpan<byte> canonicalCandidate,
        EbinReadLimits limits,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(limits);

        bytesWritten = 0;
        diagnostic = null;
        var status = EbinStructuralReader.Validate(canonicalCandidate, limits, EbinReadPolicy.Canonical, out var candidateBytes, out var readDiagnostic);

        if (status != EbinReadStatus.Complete)
        {
            diagnostic = readDiagnostic is { } value
                ? FromRead(value, EbinStructuralDiagnosticSource.Candidate)
                : CreateIncompleteDiagnostic(default, 0, EbinStructuralDiagnosticSource.Candidate);
            return ToWriteStatus(status);
        }

        if (destination.Overlaps(canonicalCandidate))
        {
            throw new ArgumentException("The destination must not overlap the candidate.", nameof(destination));
        }

        if (destination.Length < candidateBytes)
        {
            diagnostic = CreateCapacityDiagnostic(
                default,
                "destination-too-small",
                destination.Length,
                candidateBytes,
                EbinStructuralDiagnosticSource.Candidate);
            return EbinWriteStatus.DestinationTooSmall;
        }

        canonicalCandidate[..candidateBytes].CopyTo(destination);
        bytesWritten = candidateBytes;
        return EbinWriteStatus.Complete;
    }

    /// <summary>Validates a borrowed record description and measures its canonical encoding.</summary>
    /// <param name="source">The borrowed record description.</param>
    /// <param name="limits">The structural limits for the complete record.</param>
    /// <param name="recordBytes">The required encoded extent, or zero on failure.</param>
    /// <param name="diagnostic">The construction or encoded-payload failure.</param>
    /// <returns>The write status.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="limits"/> is null.</exception>
    public static EbinWriteStatus Measure(
        scoped in EbinRecordWriteView source,
        EbinReadLimits limits,
        out int recordBytes,
        out EbinStructuralDiagnostic? diagnostic)
        => MeasureInput(source, limits, out recordBytes, out diagnostic);

    /// <summary>Packs and validates a canonical record candidate in caller-provided scratch.</summary>
    /// <param name="source">The borrowed record description.</param>
    /// <param name="limits">The structural limits for the complete record.</param>
    /// <param name="candidateStorage">The caller-owned candidate workspace.</param>
    /// <param name="candidateBytes">The candidate extent, or zero on failure.</param>
    /// <param name="diagnostic">The construction, encoded-payload, candidate, or workspace failure.</param>
    /// <returns>The preparation status.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="limits"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="candidateStorage"/> overlaps the borrowed input.</exception>
    public static EbinWriteStatus Prepare(
        scoped in EbinRecordWriteView source,
        EbinReadLimits limits,
        scoped Span<byte> candidateStorage,
        out int candidateBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = MeasureInput(source, limits, out var requiredBytes, out diagnostic);
        candidateBytes = 0;

        if (status != EbinWriteStatus.Complete)
        {
            return status;
        }

        if (Overlaps(candidateStorage, source))
        {
            throw new ArgumentException("The candidate workspace must not overlap the borrowed input.", nameof(candidateStorage));
        }

        if (candidateStorage.Length < requiredBytes)
        {
            diagnostic = CreateCapacityDiagnostic(
                source.SchemaId,
                "workspace-too-small",
                candidateStorage.Length,
                requiredBytes,
                EbinStructuralDiagnosticSource.Construction);
            return EbinWriteStatus.WorkspaceTooSmall;
        }

        Pack(source, requiredBytes, candidateStorage);

        var candidate = candidateStorage[..requiredBytes];
        var candidateStatus = EbinStructuralReader.Validate(candidate, limits, EbinReadPolicy.Canonical, out var consumed, out var readDiagnostic);

        if ((candidateStatus != EbinReadStatus.Complete) || (consumed != requiredBytes))
        {
            diagnostic = readDiagnostic is { } value
                ? FromRead(value, EbinStructuralDiagnosticSource.Candidate)
                : CreateIncompleteDiagnostic(source.SchemaId, 0, EbinStructuralDiagnosticSource.Candidate);
            return ToWriteStatus(candidateStatus == EbinReadStatus.Complete ? EbinReadStatus.InvalidRecord : candidateStatus);
        }

        candidateBytes = requiredBytes;
        diagnostic = null;
        return EbinWriteStatus.Complete;
    }

    /// <summary>Prepares a candidate and commits its complete record prefix to the destination.</summary>
    /// <param name="source">The borrowed record description.</param>
    /// <param name="limits">The structural limits for the complete record.</param>
    /// <param name="candidateStorage">The caller-owned candidate workspace.</param>
    /// <param name="destination">The final record destination.</param>
    /// <param name="bytesWritten">The record prefix length, or zero on failure.</param>
    /// <param name="diagnostic">The construction, encoded-payload, candidate, workspace, or destination failure.</param>
    /// <returns>The write status.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="limits"/> is null.</exception>
    /// <exception cref="ArgumentException">An output overlaps borrowed input or scratch.</exception>
    public static EbinWriteStatus Write(
        scoped in EbinRecordWriteView source,
        EbinReadLimits limits,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = MeasureInput(source, limits, out _, out diagnostic);
        bytesWritten = 0;

        if (status != EbinWriteStatus.Complete)
        {
            return status;
        }

        if (Overlaps(candidateStorage, source))
        {
            throw new ArgumentException("The candidate workspace must not overlap the borrowed input.", nameof(candidateStorage));
        }

        if (Overlaps(destination, source))
        {
            throw new ArgumentException("The destination must not overlap the borrowed input.", nameof(destination));
        }

        if (destination.Overlaps(candidateStorage))
        {
            throw new ArgumentException("The destination must not overlap the candidate workspace.", nameof(destination));
        }

        status = Prepare(source, limits, candidateStorage, out var candidateBytes, out diagnostic);

        if (status != EbinWriteStatus.Complete)
        {
            return status;
        }

        return Commit(candidateStorage[..candidateBytes], limits, destination, out bytesWritten, out diagnostic);
    }

    private static EbinStructuralDiagnostic CreateCapacityDiagnostic(
        Guid schemaId,
        string category,
        int capacity,
        int required,
        EbinStructuralDiagnosticSource source)
        => new EbinStructuralDiagnostic(
            schemaId,
            0,
            category,
            null,
            checked((ulong)capacity),
            checked((ulong)required),
            source);

    private static EbinStructuralDiagnostic CreateDiagnostic(
        EbinDiagnostic diagnostic,
        EbinStructuralDiagnosticSource source,
        Guid fallbackSchemaId,
        uint fallbackTag)
    {
        var category = diagnostic.Category ?? throw new InvalidOperationException("The EBIN scanner omitted a failure category.");
        var schemaId = diagnostic.SchemaId == Guid.Empty ? fallbackSchemaId : diagnostic.SchemaId;
        var tag = diagnostic.Tag == 0 ? fallbackTag : diagnostic.Tag;

        return new EbinStructuralDiagnostic(
            schemaId,
            tag,
            category,
            (diagnostic.Offset == 0) ? null : diagnostic.Offset,
            diagnostic.Claim,
            diagnostic.Limit,
            source);
    }

    private static EbinStructuralDiagnostic CreateIncompleteDiagnostic(
        Guid schemaId,
        uint tag,
        EbinStructuralDiagnosticSource source)
        => new EbinStructuralDiagnostic(schemaId, tag, "incomplete-encoded-value", null, 0, 0, source);

    private static EbinStructuralDiagnostic FromRead(EbinReadDiagnostic diagnostic, EbinStructuralDiagnosticSource source)
        => new EbinStructuralDiagnostic(
            diagnostic.SchemaId,
            diagnostic.Tag,
            diagnostic.Category,
            diagnostic.Offset,
            diagnostic.Claim,
            diagnostic.Limit,
            source);

    private static EbinWriteStatus MeasureInput(
        scoped in EbinRecordWriteView source,
        EbinReadLimits limits,
        out int recordBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(limits);

        recordBytes = 0;
        diagnostic = null;

        if ((source.Major <= 0) || (source.Minor < 0))
        {
            _ = EbinCodec.Fail(source.SchemaId, 0, "signed-projection-range", out var versionDiagnostic);
            diagnostic = CreateDiagnostic(versionDiagnostic, EbinStructuralDiagnosticSource.Construction, source.SchemaId, 0);
            return EbinWriteStatus.InvalidRecord;
        }

        var state = new EbinScanState(limits);

        if (!state.TryCountRecord(source.SchemaId, 1, out var scanDiagnostic)
            || !state.TryCountFields(source.SchemaId, checked((uint)source.Fields.Length), out scanDiagnostic))
        {
            diagnostic = CreateDiagnostic(scanDiagnostic, EbinStructuralDiagnosticSource.Construction, source.SchemaId, 0);
            return EbinWriteStatus.InvalidRecord;
        }

        var fields = source.Fields;
        var previousTag = 0U;
        var cursor = checked(64UL + (checked((ulong)fields.Length) * 32));

        for (var index = 0; index < fields.Length; index++)
        {
            var field = fields[index];

            if (field.Tag == 0)
            {
                _ = EbinCodec.Fail(source.SchemaId, field.Tag, "field-tag-zero", out scanDiagnostic);
                diagnostic = CreateDiagnostic(scanDiagnostic, EbinStructuralDiagnosticSource.Construction, source.SchemaId, field.Tag);
                return EbinWriteStatus.InvalidRecord;
            }

            if (field.Tag <= previousTag)
            {
                var duplicate = false;

                for (var earlier = 0; earlier < index; earlier++)
                {
                    duplicate |= fields[earlier].Tag == field.Tag;
                }

                _ = EbinCodec.Fail(source.SchemaId, field.Tag, duplicate ? "field-tag-duplicate" : "field-tag-order", out scanDiagnostic);
                diagnostic = CreateDiagnostic(scanDiagnostic, EbinStructuralDiagnosticSource.Construction, source.SchemaId, field.Tag);
                return EbinWriteStatus.InvalidRecord;
            }

            previousTag = field.Tag;

            if (field.Kind == 0)
            {
                _ = EbinCodec.Fail(source.SchemaId, field.Tag, "field-kind-zero", out scanDiagnostic);
                diagnostic = CreateDiagnostic(scanDiagnostic, EbinStructuralDiagnosticSource.Construction, source.SchemaId, field.Tag);
                return EbinWriteStatus.InvalidRecord;
            }

            if (field.Flags > 1)
            {
                _ = EbinCodec.Fail(source.SchemaId, field.Tag, "field-flags", out scanDiagnostic);
                diagnostic = CreateDiagnostic(scanDiagnostic, EbinStructuralDiagnosticSource.Construction, source.SchemaId, field.Tag);
                return EbinWriteStatus.InvalidRecord;
            }

            if (field.Payload.Offset > source.PayloadStorage.Length
                || field.Payload.ByteLength > (source.PayloadStorage.Length - field.Payload.Offset))
            {
                _ = EbinCodec.Fail(source.SchemaId, field.Tag, "field-range-past-end", out scanDiagnostic);
                diagnostic = CreateDiagnostic(scanDiagnostic, EbinStructuralDiagnosticSource.Construction, source.SchemaId, field.Tag);
                return EbinWriteStatus.InvalidRecord;
            }

            if ((field.Kind > 24) && (field.Flags == 1))
            {
                _ = EbinCodec.Fail(source.SchemaId, field.Tag, "unsupported-required-kind", out scanDiagnostic);
                diagnostic = CreateDiagnostic(scanDiagnostic, EbinStructuralDiagnosticSource.Construction, source.SchemaId, field.Tag);
                return EbinWriteStatus.UnsupportedSchema;
            }

            var payload = source.PayloadStorage.Slice(field.Payload.Offset, field.Payload.ByteLength);
            var payloadStatus = EbinCodec.ScanPayload(field.Kind, field.Count, checked((ulong)field.Payload.ByteLength), payload, 1, ref state, out scanDiagnostic);

            if (payloadStatus == EbinStatus.NeedMoreData)
            {
                diagnostic = CreateIncompleteDiagnostic(source.SchemaId, field.Tag, EbinStructuralDiagnosticSource.EncodedInput);
                return EbinWriteStatus.InvalidRecord;
            }

            if (payloadStatus != EbinStatus.Complete)
            {
                diagnostic = CreateDiagnostic(scanDiagnostic, EbinStructuralDiagnosticSource.EncodedInput, source.SchemaId, field.Tag);
                return payloadStatus == EbinStatus.UnsupportedSchema
                    ? EbinWriteStatus.UnsupportedSchema
                    : EbinWriteStatus.InvalidRecord;
            }

            payloadStatus = EbinCodec.ValidatePayloadKinds(field.Kind, payload, out scanDiagnostic);

            if (payloadStatus != EbinStatus.Complete)
            {
                diagnostic = CreateDiagnostic(scanDiagnostic, EbinStructuralDiagnosticSource.EncodedInput, source.SchemaId, field.Tag);
                return payloadStatus == EbinStatus.UnsupportedSchema
                    ? EbinWriteStatus.UnsupportedSchema
                    : EbinWriteStatus.InvalidRecord;
            }

            if (!EbinCodec.TryAlign(cursor, out var fieldOffset)
                || !EbinCodec.TryAdd(fieldOffset, checked((ulong)field.Payload.ByteLength), out cursor))
            {
                _ = EbinCodec.Fail(source.SchemaId, field.Tag, "range-arithmetic-overflow", out scanDiagnostic);
                diagnostic = CreateDiagnostic(scanDiagnostic, EbinStructuralDiagnosticSource.Construction, source.SchemaId, field.Tag);
                return EbinWriteStatus.InvalidRecord;
            }
        }

        if (!EbinCodec.TryAlign(cursor, out var alignedLength))
        {
            _ = EbinCodec.Fail(source.SchemaId, 0, "range-arithmetic-overflow", out scanDiagnostic);
            diagnostic = CreateDiagnostic(scanDiagnostic, EbinStructuralDiagnosticSource.Construction, source.SchemaId, 0);
            return EbinWriteStatus.InvalidRecord;
        }

        if (!state.TryValidateRecordBytes(source.SchemaId, alignedLength, out scanDiagnostic))
        {
            diagnostic = CreateDiagnostic(scanDiagnostic, EbinStructuralDiagnosticSource.Construction, source.SchemaId, 0);
            return EbinWriteStatus.InvalidRecord;
        }

        recordBytes = checked((int)alignedLength);
        return EbinWriteStatus.Complete;
    }

    private static bool Overlaps(scoped ReadOnlySpan<byte> destination, scoped in EbinRecordWriteView source)
        => destination.Overlaps(source.PayloadStorage)
        || destination.Overlaps(MemoryMarshal.AsBytes(source.Fields));

    private static void Pack(scoped in EbinRecordWriteView source, int recordBytes, scoped Span<byte> destination)
    {
        var fields = source.Fields;
        destination[..recordBytes].Clear();
        "EBIN"u8.CopyTo(destination);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], 64);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[12..], checked((uint)fields.Length));
        _ = source.SchemaId.TryWriteBytes(destination.Slice(16, 16), bigEndian: true, out _);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[32..], checked((ulong)recordBytes));
        BinaryPrimitives.WriteUInt64LittleEndian(destination[40..], checked((ulong)fields.Length * 32));
        BinaryPrimitives.WriteUInt32LittleEndian(destination[48..], checked((uint)source.Major));
        BinaryPrimitives.WriteUInt32LittleEndian(destination[52..], checked((uint)source.Minor));

        var cursor = checked(64UL + (checked((ulong)fields.Length) * 32));

        for (var index = 0; index < fields.Length; index++)
        {
            var field = fields[index];
            _ = EbinCodec.TryAlign(cursor, out var offset);
            var entry = destination.Slice(64 + (index * 32), 32);
            BinaryPrimitives.WriteUInt32LittleEndian(entry, field.Tag);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[4..], field.Kind);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[6..], field.Flags);
            BinaryPrimitives.WriteUInt64LittleEndian(entry[8..], offset);
            BinaryPrimitives.WriteUInt64LittleEndian(entry[16..], checked((ulong)field.Payload.ByteLength));
            BinaryPrimitives.WriteUInt64LittleEndian(entry[24..], field.Count);
            source.PayloadStorage.Slice(field.Payload.Offset, field.Payload.ByteLength).CopyTo(destination.Slice(checked((int)offset)));
            cursor = checked(offset + checked((ulong)field.Payload.ByteLength));
        }
    }

    private static EbinWriteStatus ToWriteStatus(EbinReadStatus status) => status switch {
        EbinReadStatus.Complete => EbinWriteStatus.Complete,
        EbinReadStatus.NeedMoreData => EbinWriteStatus.InvalidRecord,
        EbinReadStatus.InvalidRecord => EbinWriteStatus.InvalidRecord,
        EbinReadStatus.UnsupportedSchema => EbinWriteStatus.UnsupportedSchema,
        _ => throw new InvalidOperationException("The EBIN reader returned an unsupported status."),
    };
}
