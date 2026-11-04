// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;

namespace TerraFX.Hosting;

internal static class WorldPersistenceHeaderWriter
{
    private static readonly Guid s_schemaId = new ContentSchemaIdentityAssignment("state/header").SchemaId;

    internal static byte[] Create(
        WorldPersistenceMetadata metadata,
        WorldPersistenceMode mode,
        scoped ReadOnlySpan<byte> packageSha256,
        Guid worldGuid,
        Guid lineageGuid,
        long nextTick,
        byte captureWorkerCount,
        scoped ReadOnlySpan<byte> foundationProfileRecord)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        if (worldGuid == Guid.Empty)
        {
            throw new ArgumentException("The saved World ID must not be empty.", nameof(worldGuid));
        }

        if (lineageGuid == Guid.Empty)
        {
            throw new ArgumentException("The saved lineage ID must not be empty.", nameof(lineageGuid));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(nextTick, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(captureWorkerCount, 64);

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (mode == WorldPersistenceMode.StrictLocal)
        {
            if (packageSha256.Length != 32)
            {
                throw new ArgumentException("StrictLocal requires the exact 32-byte package SHA-256.", nameof(packageSha256));
            }
        }
        else if (!packageSha256.IsEmpty)
        {
            throw new ArgumentException("Standard checkpoints do not store a package SHA-256.", nameof(packageSha256));
        }

        ValidateRecord(
            foundationProfileRecord,
            FoundationCatalogue.ProfileId,
            major: 2,
            minor: 0,
            nameof(foundationProfileRecord));
        ValidateRecord(
            metadata.ApplicationRecord.AsSpan(),
            new ContentSchemaIdentityAssignment("state/application").SchemaId,
            major: 1,
            minor: 0,
            nameof(metadata));
        ValidateRecord(
            metadata.RuntimeProfileRecord.AsSpan(),
            new ContentSchemaIdentityAssignment("state/runtime-profile").SchemaId,
            major: 1,
            minor: 0,
            nameof(metadata));

        var fieldCount = 10 + (packageSha256.IsEmpty ? 0 : 1);
        var payloadStorage = new byte[checked(
            packageSha256.Length
            + metadata.ApplicationRecord.Length
            + 16
            + foundationProfileRecord.Length
            + 8
            + 1
            + 16
            + metadata.RuntimeProfileRecord.Length
            + 4
            + 4
            + 1)];
        var fields = new EbinFieldSource[fieldCount];
        var currentField = 0;
        var payloadBytes = 0;

        if (!packageSha256.IsEmpty)
        {
            AddBytesField(1, packageSha256, fields, ref currentField, payloadStorage, ref payloadBytes);
        }

        AddRecordField(2, metadata.ApplicationRecord.AsSpan(), fields, ref currentField, payloadStorage, ref payloadBytes);
        AddGuidField(3, worldGuid, fields, ref currentField, payloadStorage, ref payloadBytes);
        AddRecordField(4, foundationProfileRecord, fields, ref currentField, payloadStorage, ref payloadBytes);
        AddInt64Field(5, nextTick, fields, ref currentField, payloadStorage, ref payloadBytes);
        AddByteField(6, checked((byte)mode), fields, ref currentField, payloadStorage, ref payloadBytes);
        AddGuidField(7, lineageGuid, fields, ref currentField, payloadStorage, ref payloadBytes);
        AddRecordField(8, metadata.RuntimeProfileRecord.AsSpan(), fields, ref currentField, payloadStorage, ref payloadBytes);
        AddUInt32Field(9, 1, fields, ref currentField, payloadStorage, ref payloadBytes);
        AddUInt32Field(10, 0, fields, ref currentField, payloadStorage, ref payloadBytes);
        AddByteField(11, captureWorkerCount, fields, ref currentField, payloadStorage, ref payloadBytes);

        if (currentField != fieldCount)
        {
            throw new InvalidOperationException("The State2 header field count changed during construction.");
        }

        return CreateRecord(fields, payloadStorage.AsSpan(0, payloadBytes));
    }

    private static void AddByteField(
        uint tag,
        byte value,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadBytes)
    {
        var status = EbinPayloadWriter.Write(value, payloadStorage[payloadBytes..], out var fieldBytes, out _);
        EnsureComplete(status, "byte payload");
        fields[fieldCount++] = new EbinFieldSource(tag, 1, 1, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);
    }

    private static void AddBytesField(
        uint tag,
        scoped ReadOnlySpan<byte> value,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadBytes)
    {
        if (value.Length > payloadStorage.Length - payloadBytes)
        {
            throw new InvalidOperationException("The State2 header byte workspace is too small.");
        }

        value.CopyTo(payloadStorage[payloadBytes..]);
        fields[fieldCount++] = new EbinFieldSource(tag, 13, 1, checked((ulong)value.Length), new EbinPayloadSlice(payloadBytes, value.Length));
        payloadBytes = checked(payloadBytes + value.Length);
    }

    private static void AddGuidField(
        uint tag,
        Guid value,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadBytes)
    {
        var status = EbinPayloadWriter.Write(value, payloadStorage[payloadBytes..], out var fieldBytes, out _);
        EnsureComplete(status, "GUID payload");
        fields[fieldCount++] = new EbinFieldSource(tag, 11, 1, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);
    }

    private static void AddInt64Field(
        uint tag,
        long value,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadBytes)
    {
        var status = EbinPayloadWriter.Write(value, payloadStorage[payloadBytes..], out var fieldBytes, out _);
        EnsureComplete(status, "Int64 payload");
        fields[fieldCount++] = new EbinFieldSource(tag, 8, 1, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);
    }

    private static void AddRecordField(
        uint tag,
        scoped ReadOnlySpan<byte> record,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadBytes)
    {
        if (record.Length > payloadStorage.Length - payloadBytes)
        {
            throw new InvalidOperationException("The State2 header nested-record workspace is too small.");
        }

        record.CopyTo(payloadStorage[payloadBytes..]);
        fields[fieldCount++] = new EbinFieldSource(tag, 22, 1, 1, new EbinPayloadSlice(payloadBytes, record.Length));
        payloadBytes = checked(payloadBytes + record.Length);
    }

    private static void AddUInt32Field(
        uint tag,
        uint value,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadBytes)
    {
        var status = EbinPayloadWriter.Write(value, payloadStorage[payloadBytes..], out var fieldBytes, out _);
        EnsureComplete(status, "UInt32 payload");
        fields[fieldCount++] = new EbinFieldSource(tag, 3, 1, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);
    }

    private static byte[] CreateRecord(
        scoped ReadOnlySpan<EbinFieldSource> fields,
        scoped ReadOnlySpan<byte> payloadStorage)
    {
        var source = new EbinRecordWriteView(s_schemaId, 1, 0, fields, payloadStorage);
        var status = EbinStructuralWriter.Measure(in source, EbinReadLimits.Foundation, out var recordBytes, out _);
        EnsureComplete(status, "State2 header measurement");
        var candidateStorage = new byte[recordBytes];
        var destination = new byte[recordBytes];
        status = EbinStructuralWriter.Write(
            in source,
            EbinReadLimits.Foundation,
            candidateStorage,
            destination,
            out var bytesWritten,
            out _);
        EnsureComplete(status, "State2 header writing");

        if (bytesWritten != destination.Length)
        {
            throw new InvalidOperationException("The State2 header extent changed after measurement.");
        }

        return destination;
    }

    private static void EnsureComplete(EbinWriteStatus status, string operation)
    {
        if (status is not EbinWriteStatus.Complete)
        {
            throw new InvalidOperationException($"The {operation} failed with {status}.");
        }
    }

    private static void ValidateRecord(
        scoped ReadOnlySpan<byte> record,
        Guid schemaId,
        int major,
        int minor,
        string parameterName)
    {
        var status = EbinStructuralReader.ReadHeader(
            record,
            EbinReadLimits.Foundation,
            out var header,
            out _,
            out _);

        if ((status is not EbinReadStatus.Complete)
            || (header.SchemaId != schemaId)
            || (header.Major != major)
            || (header.Minor != minor))
        {
            throw new ArgumentException("The nested record has the wrong State2 schema identity or version.", parameterName);
        }

        status = EbinStructuralReader.Validate(
            record,
            EbinReadLimits.Foundation,
            EbinReadPolicy.Canonical,
            out var bytesConsumed,
            out _);

        if ((status is not EbinReadStatus.Complete) || (bytesConsumed != record.Length))
        {
            throw new ArgumentException("The nested record is not a complete canonical EBIN record.", parameterName);
        }
    }
}
