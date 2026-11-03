// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;

namespace TerraFX.Entities;

internal static class WorldPersistenceStateRowWriter
{
    private static readonly Guid s_attachmentSchemaId = new ContentSchemaIdentityAssignment("state/attachment").SchemaId;
    private static readonly Guid s_resourceSchemaId = new ContentSchemaIdentityAssignment("state/resource-state").SchemaId;
    private static readonly Guid s_systemSchemaId = new ContentSchemaIdentityAssignment("state/system-state").SchemaId;

    internal static EbinWriteStatus MeasureAttachment(
        ContentSchemaReference schema,
        bool hasOwner,
        int payloadRecordBytes,
        EbinReadLimits limits,
        out int schemaReferenceRecordBytes,
        out int rowRecordBytes,
        out int rowPayloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(limits);
        schemaReferenceRecordBytes = 0;
        rowRecordBytes = 0;
        rowPayloadBytes = 0;
        diagnostic = null;

        if (payloadRecordBytes <= 0)
        {
            diagnostic = CreateDiagnostic("attachment-state-record-size", 0, 1);
            return EbinWriteStatus.InvalidRecord;
        }

        var status = WorldPersistenceSchemaReferenceWriter.Measure(
            schema,
            limits,
            out schemaReferenceRecordBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var rowPayloadLength = checked((long)schemaReferenceRecordBytes + (hasOwner ? 16L : 0L) + payloadRecordBytes);

        if (rowPayloadLength > int.MaxValue)
        {
            diagnostic = CreateDiagnostic(
                "record-byte-budget",
                checked((ulong)rowPayloadLength),
                checked((ulong)limits.MaximumRecordBytes));
            return EbinWriteStatus.InvalidRecord;
        }

        var fieldCount = hasOwner ? 3 : 2;
        Span<int> fieldPayloadBytes = stackalloc int[fieldCount];
        fieldPayloadBytes[0] = schemaReferenceRecordBytes;
        var payloadIndex = 1;

        if (hasOwner)
        {
            fieldPayloadBytes[payloadIndex++] = 16;
        }

        fieldPayloadBytes[payloadIndex] = payloadRecordBytes;
        rowPayloadBytes = checked((int)rowPayloadLength);
        return MeasureRecord(fieldPayloadBytes, limits, out rowRecordBytes, out diagnostic);
    }

    internal static EbinWriteStatus MeasureResource(
        int payloadRecordBytes,
        EbinReadLimits limits,
        out int rowRecordBytes,
        out int rowPayloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
        => MeasureGuidPayloadRecord(
            "resource-state-record-size",
            payloadRecordBytes,
            limits,
            out rowRecordBytes,
            out rowPayloadBytes,
            out diagnostic);

    internal static EbinWriteStatus MeasureSystem(
        int payloadRecordBytes,
        EbinReadLimits limits,
        out int rowRecordBytes,
        out int rowPayloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
        => MeasureGuidPayloadRecord(
            "system-state-record-size",
            payloadRecordBytes,
            limits,
            out rowRecordBytes,
            out rowPayloadBytes,
            out diagnostic);

    internal static EbinWriteStatus WriteAttachment(
        ContentSchemaReference schema,
        Guid? ownerEntityId,
        scoped ReadOnlySpan<byte> payloadRecord,
        EbinReadLimits limits,
        scoped Span<byte> rowPayloadStorage,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(limits);
        var status = MeasureAttachment(
            schema,
            ownerEntityId.HasValue,
            payloadRecord.Length,
            limits,
            out var schemaReferenceRecordBytes,
            out var rowRecordBytes,
            out var rowPayloadBytes,
            out diagnostic);
        bytesWritten = 0;

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if ((rowPayloadStorage.Length != rowPayloadBytes)
            || (candidateStorage.Length != rowRecordBytes)
            || (destination.Length != rowRecordBytes))
        {
            throw new ArgumentException("The attachment-state row workspaces must match the exact measured lengths.");
        }

        if (ownerEntityId == Guid.Empty)
        {
            diagnostic = CreateDiagnostic("empty-attachment-owner", 0, 1);
            return EbinWriteStatus.InvalidRecord;
        }

        status = ValidatePayloadRecord(payloadRecord, schema, limits, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = WorldPersistenceSchemaReferenceWriter.Write(
            schema,
            limits,
            candidateStorage[..schemaReferenceRecordBytes],
            rowPayloadStorage[..schemaReferenceRecordBytes],
            out var schemaBytesWritten,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (schemaBytesWritten != schemaReferenceRecordBytes)
        {
            throw new InvalidOperationException("The attachment schema-reference extent changed after workspace measurement.");
        }

        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[3];
        fields[0] = new EbinFieldSource(
            1,
            22,
            1,
            1,
            new EbinPayloadSlice(0, schemaReferenceRecordBytes));
        var fieldCount = 1;
        var payloadOffset = schemaReferenceRecordBytes;

        if (ownerEntityId is Guid owner)
        {
            status = EbinPayloadWriter.Write(
                owner,
                rowPayloadStorage[payloadOffset..],
                out var ownerBytes,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            fields[fieldCount++] = new EbinFieldSource(
                2,
                11,
                0,
                1,
                new EbinPayloadSlice(payloadOffset, ownerBytes));
            payloadOffset = checked(payloadOffset + ownerBytes);
        }

        payloadRecord.CopyTo(rowPayloadStorage[payloadOffset..]);
        fields[fieldCount++] = new EbinFieldSource(
            3,
            22,
            1,
            1,
            new EbinPayloadSlice(payloadOffset, payloadRecord.Length));
        payloadOffset = checked(payloadOffset + payloadRecord.Length);

        if (payloadOffset != rowPayloadBytes)
        {
            throw new InvalidOperationException("The attachment-state row payload extent changed after workspace measurement.");
        }

        var source = new EbinRecordWriteView(
            s_attachmentSchemaId,
            major: 1,
            minor: 0,
            fields[..fieldCount],
            rowPayloadStorage);
        return WriteMeasuredRecord(
            in source,
            rowRecordBytes,
            limits,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }

    internal static EbinWriteStatus WriteResource(
        Guid resourceId,
        ContentSchemaReference payloadSchema,
        scoped ReadOnlySpan<byte> payloadRecord,
        EbinReadLimits limits,
        scoped Span<byte> rowPayloadStorage,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
        => WriteGuidPayloadRecord(
            s_resourceSchemaId,
            resourceId,
            payloadSchema,
            "resource-state-record-size",
            payloadRecord,
            limits,
            rowPayloadStorage,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);

    internal static EbinWriteStatus WriteSystem(
        Guid systemId,
        ContentSchemaReference payloadSchema,
        scoped ReadOnlySpan<byte> payloadRecord,
        EbinReadLimits limits,
        scoped Span<byte> rowPayloadStorage,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
        => WriteGuidPayloadRecord(
            s_systemSchemaId,
            systemId,
            payloadSchema,
            "system-state-record-size",
            payloadRecord,
            limits,
            rowPayloadStorage,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);

    private static long Align16(long value)
        => checked((value + 15L) & ~15L);

    private static EbinStructuralDiagnostic CreateDiagnostic(string category, ulong claim, ulong limit)
        => new EbinStructuralDiagnostic(
            Guid.Empty,
            0,
            category,
            null,
            claim,
            limit,
            EbinStructuralDiagnosticSource.Construction);

    private static EbinWriteStatus MeasureGuidPayloadRecord(
        string payloadSizeDiagnostic,
        int payloadRecordBytes,
        EbinReadLimits limits,
        out int rowRecordBytes,
        out int rowPayloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(limits);
        rowRecordBytes = 0;
        rowPayloadBytes = 0;
        diagnostic = null;

        if (payloadRecordBytes <= 0)
        {
            diagnostic = CreateDiagnostic(payloadSizeDiagnostic, 0, 1);
            return EbinWriteStatus.InvalidRecord;
        }

        Span<int> fieldPayloadBytes = [16, payloadRecordBytes];
        var rowPayloadLength = checked(16L + payloadRecordBytes);

        if (rowPayloadLength > int.MaxValue)
        {
            diagnostic = CreateDiagnostic(
                "record-byte-budget",
                checked((ulong)rowPayloadLength),
                checked((ulong)limits.MaximumRecordBytes));
            return EbinWriteStatus.InvalidRecord;
        }

        rowPayloadBytes = checked((int)rowPayloadLength);
        return MeasureRecord(fieldPayloadBytes, limits, out rowRecordBytes, out diagnostic);
    }

    private static EbinWriteStatus MeasureRecord(
        scoped ReadOnlySpan<int> fieldPayloadBytes,
        EbinReadLimits limits,
        out int rowRecordBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        rowRecordBytes = 0;
        diagnostic = null;

        if (fieldPayloadBytes.Length > limits.MaximumFieldsPerRecord)
        {
            diagnostic = CreateDiagnostic(
                "field-count-budget",
                checked((ulong)fieldPayloadBytes.Length),
                checked((ulong)limits.MaximumFieldsPerRecord));
            return EbinWriteStatus.InvalidRecord;
        }

        var recordBytes = Align16(checked(64L + (checked((long)fieldPayloadBytes.Length) * 32L)));

        for (var index = 0; index < fieldPayloadBytes.Length; index++)
        {
            recordBytes = checked(Align16(recordBytes) + fieldPayloadBytes[index]);
        }

        recordBytes = Align16(recordBytes);

        if (recordBytes > limits.MaximumRecordBytes)
        {
            diagnostic = CreateDiagnostic(
                "record-byte-budget",
                checked((ulong)recordBytes),
                checked((ulong)limits.MaximumRecordBytes));
            return EbinWriteStatus.InvalidRecord;
        }

        rowRecordBytes = checked((int)recordBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus ValidatePayloadRecord(
        scoped ReadOnlySpan<byte> payloadRecord,
        ContentSchemaReference schema,
        EbinReadLimits limits,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var assertions = new PayloadHeaderAssertions(schema);
        var status = EbinStructuralReader.Read(
            payloadRecord,
            limits,
            EbinReadPolicy.Canonical,
            ref assertions,
            out var bytesConsumed,
            out var readDiagnostic);

        if (status is not EbinReadStatus.Complete)
        {
            diagnostic = readDiagnostic is { } value
                ? new EbinStructuralDiagnostic(
                    value.SchemaId,
                    value.Tag,
                    value.Category,
                    value.Offset == 0 ? null : value.Offset,
                    value.Claim,
                    value.Limit,
                    EbinStructuralDiagnosticSource.EncodedInput)
                : CreateDiagnostic("incomplete-encoded-value", 0, 0);
            return status is EbinReadStatus.UnsupportedSchema
                ? EbinWriteStatus.UnsupportedSchema
                : EbinWriteStatus.InvalidRecord;
        }

        if (bytesConsumed != payloadRecord.Length)
        {
            diagnostic = CreateDiagnostic(
                "nested-record-trailing-bytes",
                checked((ulong)payloadRecord.Length),
                checked((ulong)bytesConsumed));
            return EbinWriteStatus.InvalidRecord;
        }

        if (!assertions.IsMatching)
        {
            diagnostic = CreateDiagnostic("nested-state-schema-mismatch", 0, 1);
            return EbinWriteStatus.InvalidRecord;
        }

        diagnostic = null;
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus WriteGuidPayloadRecord(
        Guid rowSchemaId,
        Guid id,
        ContentSchemaReference payloadSchema,
        string payloadSizeDiagnostic,
        scoped ReadOnlySpan<byte> payloadRecord,
        EbinReadLimits limits,
        scoped Span<byte> rowPayloadStorage,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = MeasureGuidPayloadRecord(
            payloadSizeDiagnostic,
            payloadRecord.Length,
            limits,
            out var rowRecordBytes,
            out var rowPayloadBytes,
            out diagnostic);
        bytesWritten = 0;

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if ((rowPayloadStorage.Length != rowPayloadBytes)
            || (candidateStorage.Length != rowRecordBytes)
            || (destination.Length != rowRecordBytes))
        {
            throw new ArgumentException("The state-row workspaces must match the exact measured lengths.");
        }

        status = ValidatePayloadRecord(payloadRecord, payloadSchema, limits, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = EbinPayloadWriter.Write(id, rowPayloadStorage, out var idBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (idBytes != 16)
        {
            throw new InvalidOperationException("The state-row identifier extent changed after workspace measurement.");
        }

        payloadRecord.CopyTo(rowPayloadStorage[idBytes..]);
        Span<EbinFieldSource> fields = [
            new EbinFieldSource(1, 11, 1, 1, new EbinPayloadSlice(0, idBytes)),
            new EbinFieldSource(2, 22, 1, 1, new EbinPayloadSlice(idBytes, payloadRecord.Length)),
        ];
        var source = new EbinRecordWriteView(
            rowSchemaId,
            major: 1,
            minor: 0,
            fields,
            rowPayloadStorage);
        return WriteMeasuredRecord(
            in source,
            rowRecordBytes,
            limits,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }

    private static EbinWriteStatus WriteMeasuredRecord(
        scoped in EbinRecordWriteView source,
        int expectedRecordBytes,
        EbinReadLimits limits,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = EbinStructuralWriter.Measure(in source, limits, out var measuredRecordBytes, out diagnostic);
        bytesWritten = 0;

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (measuredRecordBytes != expectedRecordBytes)
        {
            diagnostic = CreateDiagnostic(
                "state-row-record-size-mismatch",
                checked((ulong)measuredRecordBytes),
                checked((ulong)expectedRecordBytes));
            return EbinWriteStatus.InvalidRecord;
        }

        return EbinStructuralWriter.Write(
            in source,
            limits,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }

    private struct PayloadHeaderAssertions : IAction<EbinRecordReadView>
    {
        private readonly ContentSchemaReference _schema;

        internal PayloadHeaderAssertions(ContentSchemaReference schema)
        {
            _schema = schema;
            IsMatching = false;
        }

        internal bool IsMatching { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
        {
            var header = record.Header;
            IsMatching = (header.SchemaId == _schema.SchemaId)
                && (header.Major == _schema.Major)
                && (header.Minor == _schema.Minor);
        }
    }
}
