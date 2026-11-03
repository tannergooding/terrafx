// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;

namespace TerraFX.Entities;

internal static class WorldPersistenceResourceRegistrationWriter
{
    private const int ScalarPayloadBytes = 41;

    private static readonly Guid s_schemaId = new ContentSchemaIdentityAssignment("state/resource-registration").SchemaId;

    internal static EbinWriteStatus Measure(
        WorldResourceRegistrationDescriptor descriptor,
        EbinReadLimits limits,
        out int recordBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(limits);
        var status = MeasureSchemaReference(descriptor, limits, out var schemaReferenceRecordBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            recordBytes = 0;
            return status;
        }

        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[6];
        Span<byte> payloadStorage = stackalloc byte[checked(schemaReferenceRecordBytes + ScalarPayloadBytes)];
        status = BuildFields(
            descriptor,
            limits,
            schemaReferenceRecordBytes,
            fields,
            payloadStorage,
            out var payloadBytes,
            out diagnostic);

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
        WorldResourceRegistrationDescriptor descriptor,
        EbinReadLimits limits,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(limits);
        var status = MeasureSchemaReference(descriptor, limits, out var schemaReferenceRecordBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            bytesWritten = 0;
            return status;
        }

        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[6];
        Span<byte> payloadStorage = stackalloc byte[checked(schemaReferenceRecordBytes + ScalarPayloadBytes)];
        status = BuildFields(
            descriptor,
            limits,
            schemaReferenceRecordBytes,
            fields,
            payloadStorage,
            out var payloadBytes,
            out diagnostic);

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

    private static EbinWriteStatus AddBooleanField(
        uint tag,
        bool value,
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

        fields[fieldCount++] = new EbinFieldSource(tag, 9, 1, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);
        return EbinWriteStatus.Complete;
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
        scoped ReadOnlySpan<byte> record,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        if (record.Length > payloadStorage.Length - payloadBytes)
        {
            throw new InvalidOperationException("The resource-registration workspace cannot contain its schema reference.");
        }

        record.CopyTo(payloadStorage[payloadBytes..]);
        fields[fieldCount++] = new EbinFieldSource(tag, 22, 1, 1, new EbinPayloadSlice(payloadBytes, record.Length));
        payloadBytes = checked(payloadBytes + record.Length);
        diagnostic = null;
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus AddUInt32Field(
        uint tag,
        uint value,
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

        fields[fieldCount++] = new EbinFieldSource(tag, 3, 1, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus BuildFields(
        WorldResourceRegistrationDescriptor descriptor,
        EbinReadLimits limits,
        int schemaReferenceRecordBytes,
        Span<EbinFieldSource> fields,
        Span<byte> payloadStorage,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        fields.Clear();
        payloadStorage.Clear();
        payloadBytes = 0;
        diagnostic = null;
        Span<byte> schemaCandidateStorage = stackalloc byte[schemaReferenceRecordBytes];
        Span<byte> schemaRecordStorage = stackalloc byte[schemaReferenceRecordBytes];
        var status = WorldPersistenceSchemaReferenceWriter.Write(
            descriptor.Factory.Schema,
            limits,
            schemaCandidateStorage,
            schemaRecordStorage,
            out var schemaBytesWritten,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (schemaBytesWritten != schemaReferenceRecordBytes)
        {
            throw new InvalidOperationException("The resource schema-reference extent changed after workspace measurement.");
        }

        var fieldCount = 0;
        status = AddGuidField(
            1,
            descriptor.ResourceId,
            fields,
            ref fieldCount,
            payloadStorage,
            ref payloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddUInt32Field(
            2,
            checked((uint)descriptor.PartitionCount),
            fields,
            ref fieldCount,
            payloadStorage,
            ref payloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddBooleanField(
            3,
            descriptor.AllowParallelAccess,
            fields,
            ref fieldCount,
            payloadStorage,
            ref payloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddRecordField(
            4,
            schemaRecordStorage,
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
            5,
            descriptor.Factory.FactoryId,
            fields,
            ref fieldCount,
            payloadStorage,
            ref payloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        return AddUInt32Field(
            6,
            checked((uint)descriptor.Factory.FactoryVersion),
            fields,
            ref fieldCount,
            payloadStorage,
            ref payloadBytes,
            out diagnostic);
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

    private static EbinWriteStatus MeasureSchemaReference(
        WorldResourceRegistrationDescriptor descriptor,
        EbinReadLimits limits,
        out int schemaReferenceRecordBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        if ((descriptor.ResourceId == Guid.Empty)
            || (descriptor.PartitionCount <= 0)
            || (descriptor.Factory.FactoryId == Guid.Empty)
            || (descriptor.Factory.FactoryVersion <= 0))
        {
            schemaReferenceRecordBytes = 0;
            diagnostic = CreateDiagnostic("resource-registration-metadata", 0, 1);
            return EbinWriteStatus.InvalidRecord;
        }

        return WorldPersistenceSchemaReferenceWriter.Measure(
            descriptor.Factory.Schema,
            limits,
            out schemaReferenceRecordBytes,
            out diagnostic);
    }
}
