// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;

namespace TerraFX.Entities;

internal static class WorldPersistenceCursorWriter
{
    private const int PayloadStorageBytes = 56;

    private static readonly Guid s_schemaId = new ContentSchemaIdentityAssignment("state/cursors").SchemaId;

    internal static EbinWriteStatus Measure(
        WorldPersistenceCaptureLease lease,
        EbinReadLimits limits,
        out int recordBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(limits);
        lease.ValidateCurrent();
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[4];
        Span<byte> payloadStorage = stackalloc byte[PayloadStorageBytes];
        var status = BuildFields(lease.NextControlSequence, limits, fields, payloadStorage, out var payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            recordBytes = 0;
            return status;
        }

        var source = new EbinRecordWriteView(
            s_schemaId,
            major: 1,
            minor: 0,
            fields,
            payloadStorage[..payloadBytes]);
        return EbinStructuralWriter.Measure(in source, limits, out recordBytes, out diagnostic);
    }

    internal static EbinWriteStatus Write(
        WorldPersistenceCaptureLease lease,
        EbinReadLimits limits,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(limits);
        lease.ValidateCurrent();
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[4];
        Span<byte> payloadStorage = stackalloc byte[PayloadStorageBytes];
        var status = BuildFields(lease.NextControlSequence, limits, fields, payloadStorage, out var payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            bytesWritten = 0;
            return status;
        }

        var source = new EbinRecordWriteView(
            s_schemaId,
            major: 1,
            minor: 0,
            fields,
            payloadStorage[..payloadBytes]);
        return EbinStructuralWriter.Write(
            in source,
            limits,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }

    private static EbinWriteStatus AddEmptyRecordArrayField(
        uint tag,
        EbinReadLimits limits,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        Span<EbinPayloadSlice> children = [];
        ReadOnlySpan<byte> childStorage = [];
        var status = EbinPayloadWriter.WriteRecordArray(
            children,
            childStorage,
            limits,
            payloadStorage[payloadBytes..],
            out var fieldBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (fieldBytes != 16)
        {
            throw new InvalidOperationException("An empty cursor record array must occupy exactly 16 bytes.");
        }

        fields[fieldCount++] = new EbinFieldSource(tag, 24, 1, 0, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus AddInt64Field(
        uint tag,
        long value,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = EbinPayloadWriter.Write(value, payloadStorage[payloadBytes..], out var fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(tag, 8, 1, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus BuildFields(
        long nextControlSequence,
        EbinReadLimits limits,
        Span<EbinFieldSource> fields,
        Span<byte> payloadStorage,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        fields.Clear();
        payloadStorage.Clear();
        payloadBytes = 0;
        diagnostic = null;

        if (nextControlSequence <= 0)
        {
            diagnostic = CreateDiagnostic("next-control-sequence", 0, 1);
            return EbinWriteStatus.InvalidRecord;
        }

        var fieldCount = 0;
        var status = AddInt64Field(
            1,
            nextControlSequence,
            fields,
            ref fieldCount,
            payloadStorage,
            ref payloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddEmptyRecordArrayField(
            4,
            limits,
            fields,
            ref fieldCount,
            payloadStorage,
            ref payloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddEmptyRecordArrayField(
            5,
            limits,
            fields,
            ref fieldCount,
            payloadStorage,
            ref payloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddEmptyRecordArrayField(
            6,
            limits,
            fields,
            ref fieldCount,
            payloadStorage,
            ref payloadBytes,
            out diagnostic);

        if (payloadBytes != PayloadStorageBytes)
        {
            throw new InvalidOperationException("The state/cursors payload extent changed after workspace measurement.");
        }

        return status;
    }

    private static EbinStructuralDiagnostic CreateDiagnostic(string category, ulong claim, ulong limit)
        => new EbinStructuralDiagnostic(
            Guid.Empty,
            0,
            category,
            null,
            claim,
            limit,
            EbinStructuralDiagnosticSource.Construction);
}
