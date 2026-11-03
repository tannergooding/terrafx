// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;

namespace TerraFX.Entities;

internal static class WorldPersistenceComponentRegistrationWriter
{
    private const int MaximumScalarPayloadBytes = (sizeof(byte) * 2) + 16 + sizeof(uint);

    private static readonly Guid s_schemaId = new ContentSchemaIdentityAssignment("state/component-registration").SchemaId;

    internal static EbinWriteStatus Measure(
        ComponentFactoryDescriptor descriptor,
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

        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[5];
        Span<byte> payloadStorage = stackalloc byte[checked(schemaReferenceRecordBytes + MaximumScalarPayloadBytes)];
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
        ComponentFactoryDescriptor descriptor,
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

        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[5];
        Span<byte> payloadStorage = stackalloc byte[checked(schemaReferenceRecordBytes + MaximumScalarPayloadBytes)];
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
            throw new InvalidOperationException("The component-registration workspace cannot contain its schema reference.");
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
        ComponentFactoryDescriptor descriptor,
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
            descriptor.Schema,
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
            throw new InvalidOperationException("The component schema-reference extent changed after workspace measurement.");
        }

        var fieldCount = 0;
        status = AddRecordField(
            1,
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

        status = AddUInt8Field(
            2,
            checked((byte)descriptor.Flags),
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
            checked((byte)descriptor.Restore),
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
            4,
            descriptor.FactoryId,
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
            5,
            checked((uint)descriptor.FactoryVersion),
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
        ComponentFactoryDescriptor descriptor,
        EbinReadLimits limits,
        out int schemaReferenceRecordBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var invalidIdentity = (descriptor.Flags is not ComponentStorageFlags.Persistent and not ComponentStorageFlags.Transient)
            || descriptor.FactoryId == Guid.Empty
            || descriptor.FactoryVersion <= 0;
        var invalidRestore = (descriptor.Flags == ComponentStorageFlags.Persistent)
            ? descriptor.Restore != ComponentTransientRestore.None
            : descriptor.Restore is not ComponentTransientRestore.Reset and not ComponentTransientRestore.Derived;

        if (invalidIdentity || invalidRestore)
        {
            schemaReferenceRecordBytes = 0;
            diagnostic = CreateDiagnostic("component-registration-metadata", 0, 1);
            return EbinWriteStatus.InvalidRecord;
        }

        return WorldPersistenceSchemaReferenceWriter.Measure(
            descriptor.Schema,
            limits,
            out schemaReferenceRecordBytes,
            out diagnostic);
    }
}
