// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;

namespace TerraFX.Entities;

internal static class WorldPersistenceAccessWriter
{
    private const int ScalarPayloadBytes = (sizeof(byte) * 2) + 16;

    private static readonly Guid s_schemaId = new ContentSchemaIdentityAssignment("state/access").SchemaId;

    internal static EbinWriteStatus Measure(
        SystemResourceAccess access,
        EbinReadLimits limits,
        out int recordBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(limits);
        var status = MeasureRange(access, limits, out var rangeRecordBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            recordBytes = 0;
            return status;
        }

        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[4];
        Span<byte> payloadStorage = stackalloc byte[checked(ScalarPayloadBytes + rangeRecordBytes)];
        status = BuildFields(access, limits, rangeRecordBytes, fields, payloadStorage, out var fieldCount, out var payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            recordBytes = 0;
            return status;
        }

        var source = new EbinRecordWriteView(
            s_schemaId,
            major: 1,
            minor: 0,
            fields[..fieldCount],
            payloadStorage[..payloadBytes]);
        return EbinStructuralWriter.Measure(in source, limits, out recordBytes, out diagnostic);
    }

    internal static EbinWriteStatus Write(
        SystemResourceAccess access,
        EbinReadLimits limits,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(limits);
        var status = MeasureRange(access, limits, out var rangeRecordBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            bytesWritten = 0;
            return status;
        }

        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[4];
        Span<byte> payloadStorage = stackalloc byte[checked(ScalarPayloadBytes + rangeRecordBytes)];
        status = BuildFields(access, limits, rangeRecordBytes, fields, payloadStorage, out var fieldCount, out var payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            bytesWritten = 0;
            return status;
        }

        var source = new EbinRecordWriteView(
            s_schemaId,
            major: 1,
            minor: 0,
            fields[..fieldCount],
            payloadStorage[..payloadBytes]);
        return EbinStructuralWriter.Write(
            in source,
            limits,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }

    private static EbinWriteStatus AddGuidField(
        uint tag,
        Guid value,
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

        fields[fieldCount++] = new EbinFieldSource(tag, 11, 1, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus AddRecordField(
        uint tag,
        ushort flags,
        scoped ReadOnlySpan<byte> record,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        if (record.Length > payloadStorage.Length - payloadBytes)
        {
            throw new InvalidOperationException("The access workspace cannot contain its partition range.");
        }

        record.CopyTo(payloadStorage[payloadBytes..]);
        fields[fieldCount++] = new EbinFieldSource(tag, 22, flags, 1, new EbinPayloadSlice(payloadBytes, record.Length));
        payloadBytes = checked(payloadBytes + record.Length);
        diagnostic = null;
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus AddUInt8Field(
        uint tag,
        byte value,
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

        fields[fieldCount++] = new EbinFieldSource(tag, 1, 1, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus BuildFields(
        SystemResourceAccess access,
        EbinReadLimits limits,
        int rangeRecordBytes,
        Span<EbinFieldSource> fields,
        Span<byte> payloadStorage,
        out int fieldCount,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        fields.Clear();
        payloadStorage.Clear();
        fieldCount = 0;
        payloadBytes = 0;
        diagnostic = null;
        var status = AddUInt8Field(
            1,
            checked((byte)access.Kind),
            fields,
            ref fieldCount,
            payloadStorage,
            ref payloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddGuidField(
            2,
            access.ResourceId,
            fields,
            ref fieldCount,
            payloadStorage,
            ref payloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddUInt8Field(
            3,
            checked((byte)access.Mode),
            fields,
            ref fieldCount,
            payloadStorage,
            ref payloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (access.Range is { } range)
        {
            Span<byte> rangeCandidateStorage = stackalloc byte[rangeRecordBytes];
            Span<byte> rangeRecordStorage = stackalloc byte[rangeRecordBytes];
            status = WorldPersistencePartitionRangeWriter.Write(
                range,
                limits,
                rangeCandidateStorage,
                rangeRecordStorage,
                out var rangeBytesWritten,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            if (rangeBytesWritten != rangeRecordBytes)
            {
                throw new InvalidOperationException("The partition-range extent changed after workspace measurement.");
            }

            status = AddRecordField(
                4,
                flags: 0,
                rangeRecordStorage,
                fields,
                ref fieldCount,
                payloadStorage,
                ref payloadBytes,
                out diagnostic);
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

    private static EbinWriteStatus MeasureRange(
        SystemResourceAccess access,
        EbinReadLimits limits,
        out int rangeRecordBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        if (!access.IsValid)
        {
            rangeRecordBytes = 0;
            diagnostic = CreateDiagnostic("system-access-invalid", 0, 1);
            return EbinWriteStatus.InvalidRecord;
        }

        if (access.Range is not { } range)
        {
            rangeRecordBytes = 0;
            diagnostic = null;
            return EbinWriteStatus.Complete;
        }

        return WorldPersistencePartitionRangeWriter.Measure(range, limits, out rangeRecordBytes, out diagnostic);
    }
}
