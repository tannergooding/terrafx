// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;

namespace TerraFX;

/// <summary>Reads and validates bounded EBIN structure without schema admission.</summary>
public static class EbinStructuralReader
{
    /// <summary>Reads one complete structurally valid EBIN record.</summary>
    /// <typeparam name="TAction">The synchronous action type.</typeparam>
    /// <param name="source">The encoded record prefix.</param>
    /// <param name="limits">The structural limits for this scan.</param>
    /// <param name="policy">The structural or canonical layout policy.</param>
    /// <param name="action">The action that consumes the borrowed record view.</param>
    /// <param name="bytesConsumed">The complete record length, or zero when the read is incomplete or invalid.</param>
    /// <param name="diagnostic">The structural failure, when one is present.</param>
    /// <returns>The read status.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="limits"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="policy"/> is invalid.</exception>
    /// <remarks>The action runs only after complete recursive structural validation. Its exceptions propagate.</remarks>
    public static EbinReadStatus Read<TAction>(
        scoped ReadOnlySpan<byte> source,
        EbinReadLimits limits,
        EbinReadPolicy policy,
        scoped ref TAction action,
        out int bytesConsumed,
        out EbinReadDiagnostic? diagnostic)
        where TAction : struct, IAction<EbinRecordReadView>, allows ref struct
    {
        var status = Scan(source, limits, policy, out bytesConsumed, out diagnostic);

        if (status == EbinReadStatus.Complete)
        {
            action.Invoke(new EbinRecordReadView(source[..bytesConsumed]));
        }

        return status;
    }

    /// <summary>Reads and validates a bounded EBIN record header.</summary>
    /// <param name="source">The available record prefix.</param>
    /// <param name="limits">The structural limits for this scan.</param>
    /// <param name="header">The validated header, or its default value when unavailable.</param>
    /// <param name="bytesConsumed">64 when the header is complete, or zero otherwise.</param>
    /// <param name="diagnostic">The structural failure, when one is present.</param>
    /// <returns>The header status.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="limits"/> is null.</exception>
    public static EbinReadStatus ReadHeader(
        scoped ReadOnlySpan<byte> source,
        EbinReadLimits limits,
        out EbinRecordHeader header,
        out int bytesConsumed,
        out EbinReadDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(limits);

        header = default;
        bytesConsumed = 0;
        var headerBytes = source[..Math.Min(source.Length, 64)];
        var state = new EbinScanState(limits);
        var status = EbinCodec.ScanRecord(headerBytes, 1, ref state, out _, out var scanDiagnostic);

        if (status == EbinStatus.NeedMoreData && (source.Length >= 64) && (scanDiagnostic.Category is null))
        {
            header = CreateHeader(headerBytes);
            bytesConsumed = 64;
            diagnostic = null;
            return EbinReadStatus.Complete;
        }

        if (status == EbinStatus.Complete)
        {
            header = CreateHeader(headerBytes);
            bytesConsumed = 64;
            diagnostic = null;
            return EbinReadStatus.Complete;
        }

        diagnostic = CreateDiagnostic(scanDiagnostic);
        return ToReadStatus(status);
    }

    /// <summary>Validates one complete structurally valid EBIN record without exposing a view.</summary>
    /// <param name="source">The encoded record prefix.</param>
    /// <param name="limits">The structural limits for this scan.</param>
    /// <param name="policy">The structural or canonical layout policy.</param>
    /// <param name="bytesConsumed">The complete record length, or zero when validation is incomplete or invalid.</param>
    /// <param name="diagnostic">The structural failure, when one is present.</param>
    /// <returns>The validation status.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="limits"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="policy"/> is invalid.</exception>
    public static EbinReadStatus Validate(
        scoped ReadOnlySpan<byte> source,
        EbinReadLimits limits,
        EbinReadPolicy policy,
        out int bytesConsumed,
        out EbinReadDiagnostic? diagnostic)
        => Scan(source, limits, policy, out bytesConsumed, out diagnostic);

    internal static EbinDiagnostic? GetCanonicalRecordFailure(scoped ReadOnlySpan<byte> bytes)
    {
        var record = new EbinRecordView(bytes);
        var schemaId = record.SchemaId;
        var cursor = checked(64UL + (checked((ulong)record.FieldCount) * 32));

        for (var index = 0; index < record.FieldCount; index++)
        {
            _ = EbinCodec.TryAlign(cursor, out var expectedOffset);
            var entry = record.Entry(index);
            var tag = BinaryPrimitives.ReadUInt32LittleEndian(entry);
            var offset = BinaryPrimitives.ReadUInt64LittleEndian(entry[8..]);
            var length = BinaryPrimitives.ReadUInt64LittleEndian(entry[16..]);

            if (offset != expectedOffset)
            {
                _ = EbinCodec.Fail(schemaId, tag, "noncanonical-record-layout", out var diagnostic, offset: offset, limit: expectedOffset, claim: offset);
                return diagnostic;
            }

            cursor = checked(offset + length);

            var kind = BinaryPrimitives.ReadUInt16LittleEndian(entry[4..]);
            var fieldBytes = record.Payload(index);

            if (kind == 22)
            {
                var nestedFailure = GetCanonicalRecordFailure(fieldBytes);

                if (nestedFailure is not null)
                {
                    return nestedFailure;
                }
            }
            else if (kind == 24)
            {
                var arrayFailure = GetCanonicalRecordArrayFailure(fieldBytes, schemaId, tag);

                if (arrayFailure is not null)
                {
                    return arrayFailure;
                }
            }
        }

        _ = EbinCodec.TryAlign(cursor, out var expectedLength);

        if (expectedLength != (ulong)bytes.Length)
        {
            _ = EbinCodec.Fail(schemaId, 0, "noncanonical-record-layout", out var diagnostic, offset: expectedLength, limit: checked((ulong)bytes.Length), claim: checked((ulong)bytes.Length));
            return diagnostic;
        }

        return null;
    }

    private static EbinReadDiagnostic? CreateDiagnostic(EbinDiagnostic diagnostic)
    {
        var category = diagnostic.Category;

        if (category is null)
        {
            return null;
        }

        return new EbinReadDiagnostic(diagnostic.SchemaId, diagnostic.Tag, category, diagnostic.Offset, diagnostic.Claim, diagnostic.Limit);
    }

    private static EbinRecordHeader CreateHeader(scoped ReadOnlySpan<byte> bytes)
    {
        var schemaId = new Guid(bytes.Slice(16, 16), bigEndian: true);
        var fieldCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]));
        var recordByteLength = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(bytes[32..]));
        var major = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes[48..]));
        var minor = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes[52..]));

        return new EbinRecordHeader(schemaId, major, minor, fieldCount, recordByteLength);
    }

    private static EbinDiagnostic? GetCanonicalRecordArrayFailure(scoped ReadOnlySpan<byte> bytes, Guid schemaId, uint tag)
    {
        var count = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(bytes));
        var cursor = BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]);

        for (var index = 0; index < count; index++)
        {
            var start = BinaryPrimitives.ReadUInt64LittleEndian(bytes[(8 + (index * 8))..]);
            var end = BinaryPrimitives.ReadUInt64LittleEndian(bytes[(16 + (index * 8))..]);

            if (start != cursor)
            {
                _ = EbinCodec.Fail(schemaId, tag, "noncanonical-recordarray-layout", out var diagnostic, offset: start, limit: cursor, claim: start);
                return diagnostic;
            }

            var childFailure = GetCanonicalRecordFailure(EbinCodec.ArrayElement(bytes, index));

            if (childFailure is not null)
            {
                return childFailure;
            }

            cursor = end;
        }

        var finalOffset = BinaryPrimitives.ReadUInt64LittleEndian(bytes[(8 + (count * 8))..]);

        if ((cursor != (ulong)bytes.Length) || (finalOffset != (ulong)bytes.Length))
        {
            _ = EbinCodec.Fail(schemaId, tag, "noncanonical-recordarray-layout", out var diagnostic, offset: cursor, limit: checked((ulong)bytes.Length), claim: finalOffset);
            return diagnostic;
        }

        return null;
    }

    private static EbinReadStatus Scan(
        scoped ReadOnlySpan<byte> source,
        EbinReadLimits limits,
        EbinReadPolicy policy,
        out int bytesConsumed,
        out EbinReadDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(limits);

        if (policy is not EbinReadPolicy.Structural and not EbinReadPolicy.Canonical)
        {
            throw new ArgumentOutOfRangeException(nameof(policy));
        }

        bytesConsumed = 0;
        diagnostic = null;
        var state = new EbinScanState(limits);
        var status = EbinCodec.ScanRecord(source, 1, ref state, out var total, out var scanDiagnostic);

        if (status == EbinStatus.NeedMoreData)
        {
            var prefixStatus = EbinCodec.ValidateRequiredPrefix(source, out var prefixDiagnostic);

            if (prefixStatus == EbinStatus.UnsupportedSchema)
            {
                status = prefixStatus;
                scanDiagnostic = prefixDiagnostic;
            }
        }

        if (status == EbinStatus.Complete)
        {
            var view = new EbinRecordView(source[..total]);
            status = EbinCodec.ValidateRequiredKinds(view, out scanDiagnostic);

            if ((status == EbinStatus.Complete)
                && (policy == EbinReadPolicy.Canonical)
                && (GetCanonicalRecordFailure(source[..total]) is { } canonicalDiagnostic))
            {
                status = EbinStatus.InvalidRecord;
                scanDiagnostic = canonicalDiagnostic;
            }

            if (status == EbinStatus.Complete)
            {
                bytesConsumed = total;
            }
        }

        diagnostic = CreateDiagnostic(scanDiagnostic);
        return ToReadStatus(status);
    }

    private static EbinReadStatus ToReadStatus(EbinStatus status) => status switch {
        EbinStatus.Complete => EbinReadStatus.Complete,
        EbinStatus.NeedMoreData => EbinReadStatus.NeedMoreData,
        EbinStatus.InvalidRecord => EbinReadStatus.InvalidRecord,
        EbinStatus.UnsupportedSchema => EbinReadStatus.UnsupportedSchema,
        _ => throw new InvalidOperationException("The EBIN scanner returned a non-read status."),
    };
}
