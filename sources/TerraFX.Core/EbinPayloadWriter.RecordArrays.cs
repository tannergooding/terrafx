// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace TerraFX;

public static partial class EbinPayloadWriter
{
    /// <summary>Measures a canonical record-array payload from borrowed child slices.</summary>
    /// <param name="children">The ordered encoded child ranges.</param>
    /// <param name="childStorage">The caller-owned storage containing the child ranges.</param>
    /// <param name="limits">The structural limits for the complete payload.</param>
    /// <param name="payloadBytes">The required payload extent, or zero on failure.</param>
    /// <param name="diagnostic">The construction or encoded-input failure.</param>
    /// <returns>The payload measurement status.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="limits"/> is null.</exception>
    public static EbinWriteStatus MeasureRecordArray(
        scoped ReadOnlySpan<EbinPayloadSlice> children,
        scoped ReadOnlySpan<byte> childStorage,
        EbinReadLimits limits,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
        => MeasureRecordArrayCore(children, childStorage, limits, out payloadBytes, out diagnostic, out _);

    /// <summary>Writes a canonical record-array payload from borrowed child slices.</summary>
    /// <param name="children">The ordered encoded child ranges.</param>
    /// <param name="childStorage">The caller-owned storage containing the child ranges.</param>
    /// <param name="limits">The structural limits for the complete payload.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction, encoded-input, overlap, or workspace failure.</param>
    /// <returns>The payload write status.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="limits"/> is null.</exception>
    /// <exception cref="ArgumentException">The destination overlaps the child table or a consumed child range.</exception>
    public static EbinWriteStatus WriteRecordArray(
        scoped ReadOnlySpan<EbinPayloadSlice> children,
        scoped ReadOnlySpan<byte> childStorage,
        EbinReadLimits limits,
        scoped Span<byte> payloadDestination,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = MeasureRecordArrayCore(children, childStorage, limits, out var requiredBytes, out diagnostic, out var firstChildOffset);
        payloadBytes = 0;

        if (status != EbinWriteStatus.Complete)
        {
            return status;
        }

        if (payloadDestination.Overlaps(MemoryMarshal.AsBytes(children)))
        {
            throw new ArgumentException("The payload destination must not overlap the child slice table.", nameof(payloadDestination));
        }

        for (var index = 0; index < children.Length; index++)
        {
            var child = children[index];
            var childBytes = childStorage.Slice(child.Offset, child.ByteLength);

            if (payloadDestination.Overlaps(childBytes))
            {
                throw new ArgumentException("The payload destination must not overlap a consumed child range.", nameof(payloadDestination));
            }
        }

        if (payloadDestination.Length < requiredBytes)
        {
            diagnostic = CreateRecordArrayDiagnostic(
                "workspace-too-small",
                checked((ulong)payloadDestination.Length),
                checked((ulong)requiredBytes),
                EbinStructuralDiagnosticSource.Construction);
            return EbinWriteStatus.WorkspaceTooSmall;
        }

        var destination = payloadDestination[..requiredBytes];
        destination.Clear();
        BinaryPrimitives.WriteUInt64LittleEndian(destination, checked((ulong)children.Length));
        BinaryPrimitives.WriteUInt64LittleEndian(destination[8..], firstChildOffset);

        var cursor = firstChildOffset;

        for (var index = 0; index < children.Length; index++)
        {
            var child = children[index];
            childStorage.Slice(child.Offset, child.ByteLength).CopyTo(destination[checked((int)cursor)..]);
            cursor = checked(cursor + checked((ulong)child.ByteLength));
            BinaryPrimitives.WriteUInt64LittleEndian(destination[(16 + (index * 8))..], cursor);
        }

        payloadBytes = requiredBytes;
        diagnostic = null;
        return EbinWriteStatus.Complete;
    }

    private static EbinStructuralDiagnostic CreateRecordArrayDiagnostic(
        string category,
        ulong claim,
        ulong limit,
        EbinStructuralDiagnosticSource source)
        => new EbinStructuralDiagnostic(Guid.Empty, 0, category, null, claim, limit, source);

    private static EbinWriteStatus MeasureRecordArrayCore(
        scoped ReadOnlySpan<EbinPayloadSlice> children,
        scoped ReadOnlySpan<byte> childStorage,
        EbinReadLimits limits,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic,
        out ulong firstChildOffset)
    {
        ArgumentNullException.ThrowIfNull(limits);

        payloadBytes = 0;
        diagnostic = null;
        firstChildOffset = 0;
        var state = new EbinScanState(limits);

        if (!state.TryAddArrayElements(Guid.Empty, 0, checked((ulong)children.Length), out var scanDiagnostic))
        {
            diagnostic = new EbinStructuralDiagnostic(
                scanDiagnostic.SchemaId,
                scanDiagnostic.Tag,
                scanDiagnostic.Category ?? throw new InvalidOperationException("The EBIN scanner omitted a failure category."),
                (scanDiagnostic.Offset == 0) ? null : scanDiagnostic.Offset,
                scanDiagnostic.Claim,
                scanDiagnostic.Limit,
                EbinStructuralDiagnosticSource.Construction);
            return EbinWriteStatus.InvalidRecord;
        }

        if (!EbinCodec.TryAdd(checked((ulong)children.Length), 1, out var offsetCount)
            || !EbinCodec.TryMultiply(offsetCount, 8, out var tableBytes)
            || !EbinCodec.TryAdd(8, tableBytes, out tableBytes)
            || !EbinCodec.TryAlign(tableBytes, out firstChildOffset))
        {
            diagnostic = CreateRecordArrayDiagnostic("array-product-overflow", checked((ulong)children.Length), checked((ulong)limits.MaximumArrayElements), EbinStructuralDiagnosticSource.Construction);
            return EbinWriteStatus.InvalidRecord;
        }

        var totalBytes = firstChildOffset;

        for (var index = 0; index < children.Length; index++)
        {
            var child = children[index];

            if ((child.Offset > childStorage.Length) || (child.ByteLength > (childStorage.Length - child.Offset)))
            {
                diagnostic = CreateRecordArrayDiagnostic(
                    "field-range-past-end",
                    checked((ulong)child.Offset + checked((ulong)child.ByteLength)),
                    checked((ulong)childStorage.Length),
                    EbinStructuralDiagnosticSource.Construction);
                return EbinWriteStatus.InvalidRecord;
            }

            var encodedChild = childStorage.Slice(child.Offset, child.ByteLength);
            var status = EbinCodec.ScanRecord(encodedChild, 1, ref state, out var consumed, out scanDiagnostic);

            if (status == EbinStatus.NeedMoreData)
            {
                diagnostic = new EbinStructuralDiagnostic(
                    encodedChild.Length >= 32 ? new Guid(encodedChild.Slice(16, 16), bigEndian: true) : Guid.Empty,
                    0,
                    "incomplete-encoded-value",
                    null,
                    checked((ulong)encodedChild.Length),
                    checked((ulong)child.ByteLength),
                    EbinStructuralDiagnosticSource.EncodedInput);
                return EbinWriteStatus.InvalidRecord;
            }

            if (status != EbinStatus.Complete)
            {
                diagnostic = new EbinStructuralDiagnostic(
                    scanDiagnostic.SchemaId,
                    scanDiagnostic.Tag,
                    scanDiagnostic.Category ?? throw new InvalidOperationException("The EBIN scanner omitted a failure category."),
                    (scanDiagnostic.Offset == 0) ? null : scanDiagnostic.Offset,
                    scanDiagnostic.Claim,
                    scanDiagnostic.Limit,
                    EbinStructuralDiagnosticSource.EncodedInput);
                return status == EbinStatus.UnsupportedSchema
                    ? EbinWriteStatus.UnsupportedSchema
                    : EbinWriteStatus.InvalidRecord;
            }

            if (consumed != child.ByteLength)
            {
                diagnostic = CreateRecordArrayDiagnostic(
                    "recordarray-nested-total",
                    checked((ulong)consumed),
                    checked((ulong)child.ByteLength),
                    EbinStructuralDiagnosticSource.EncodedInput);
                return EbinWriteStatus.InvalidRecord;
            }

            status = EbinCodec.ValidateRequiredKinds(new EbinRecordView(encodedChild), out scanDiagnostic);

            if (status != EbinStatus.Complete)
            {
                diagnostic = new EbinStructuralDiagnostic(
                    scanDiagnostic.SchemaId,
                    scanDiagnostic.Tag,
                    scanDiagnostic.Category ?? throw new InvalidOperationException("The EBIN scanner omitted a failure category."),
                    (scanDiagnostic.Offset == 0) ? null : scanDiagnostic.Offset,
                    scanDiagnostic.Claim,
                    scanDiagnostic.Limit,
                    EbinStructuralDiagnosticSource.EncodedInput);
                return status == EbinStatus.UnsupportedSchema
                    ? EbinWriteStatus.UnsupportedSchema
                    : EbinWriteStatus.InvalidRecord;
            }

            if (EbinStructuralReader.GetCanonicalRecordFailure(encodedChild) is { } canonicalFailure)
            {
                diagnostic = new EbinStructuralDiagnostic(
                    canonicalFailure.SchemaId,
                    canonicalFailure.Tag,
                    canonicalFailure.Category ?? throw new InvalidOperationException("The EBIN scanner omitted a failure category."),
                    (canonicalFailure.Offset == 0) ? null : canonicalFailure.Offset,
                    canonicalFailure.Claim,
                    canonicalFailure.Limit,
                    EbinStructuralDiagnosticSource.EncodedInput);
                return EbinWriteStatus.InvalidRecord;
            }

            if (!EbinCodec.TryAdd(totalBytes, checked((ulong)child.ByteLength), out totalBytes))
            {
                diagnostic = CreateRecordArrayDiagnostic("range-arithmetic-overflow", ulong.MaxValue, checked((ulong)limits.MaximumRecordBytes), EbinStructuralDiagnosticSource.Construction);
                return EbinWriteStatus.InvalidRecord;
            }

            if (totalBytes > (ulong)limits.MaximumRecordBytes)
            {
                diagnostic = CreateRecordArrayDiagnostic("record-byte-budget", totalBytes, checked((ulong)limits.MaximumRecordBytes), EbinStructuralDiagnosticSource.Construction);
                return EbinWriteStatus.InvalidRecord;
            }
        }

        if (totalBytes > (ulong)limits.MaximumRecordBytes)
        {
            diagnostic = CreateRecordArrayDiagnostic("record-byte-budget", totalBytes, checked((ulong)limits.MaximumRecordBytes), EbinStructuralDiagnosticSource.Construction);
            return EbinWriteStatus.InvalidRecord;
        }

        payloadBytes = checked((int)totalBytes);
        return EbinWriteStatus.Complete;
    }
}
