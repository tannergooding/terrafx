// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX;

namespace TerraFX.Hosting;

internal static class WorldPersistenceMetadataRecordWriter
{
    private static readonly Guid s_applicationSchemaId = new ContentSchemaIdentityAssignment("state/application").SchemaId;
    private static readonly Guid s_moduleIdSchemaId = new ContentSchemaIdentityAssignment("state/module-id").SchemaId;
    private static readonly Guid s_moduleSchemaId = new ContentSchemaIdentityAssignment("state/module").SchemaId;
    private static readonly Guid s_numericsSchemaId = new ContentSchemaIdentityAssignment("state/numerics").SchemaId;
    private static readonly Guid s_runtimeProfileSchemaId = new ContentSchemaIdentityAssignment("state/runtime-profile").SchemaId;

    internal static (ImmutableArray<byte> ApplicationRecord, ImmutableArray<byte> RuntimeProfileRecord) CreateRecords(
        WorldPersistenceApplication application,
        WorldPersistenceRuntimeProfile runtimeProfile)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(runtimeProfile);
        return (
            [.. CreateApplicationRecord(application)],
            [.. CreateRuntimeProfileRecord(runtimeProfile)]);
    }

    private static void AddBooleanField(
        uint tag,
        bool value,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadBytes)
    {
        var status = EbinPayloadWriter.Write(value, payloadStorage[payloadBytes..], out var fieldBytes, out _);
        EnsureComplete(status, "boolean payload");
        fields[fieldCount++] = new EbinFieldSource(tag, 9, 1, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);
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
            throw new InvalidOperationException("The State2 metadata byte workspace is too small.");
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

    private static void AddRecordArrayField(
        uint tag,
        int count,
        scoped ReadOnlySpan<byte> arrayPayload,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadBytes)
    {
        if (arrayPayload.Length > payloadStorage.Length - payloadBytes)
        {
            throw new InvalidOperationException("The State2 metadata record-array workspace is too small.");
        }

        arrayPayload.CopyTo(payloadStorage[payloadBytes..]);
        fields[fieldCount++] = new EbinFieldSource(tag, 24, 1, checked((ulong)count), new EbinPayloadSlice(payloadBytes, arrayPayload.Length));
        payloadBytes = checked(payloadBytes + arrayPayload.Length);
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
            throw new InvalidOperationException("The State2 metadata nested-record workspace is too small.");
        }

        record.CopyTo(payloadStorage[payloadBytes..]);
        fields[fieldCount++] = new EbinFieldSource(tag, 22, 1, 1, new EbinPayloadSlice(payloadBytes, record.Length));
        payloadBytes = checked(payloadBytes + record.Length);
    }

    private static void AddUInt16Field(
        uint tag,
        ushort value,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadBytes)
    {
        var status = EbinPayloadWriter.Write(value, payloadStorage[payloadBytes..], out var fieldBytes, out _);
        EnsureComplete(status, "UInt16 payload");
        fields[fieldCount++] = new EbinFieldSource(tag, 2, 1, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);
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

    private static void AddUInt64Field(
        uint tag,
        ulong value,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadBytes)
    {
        var status = EbinPayloadWriter.Write(value, payloadStorage[payloadBytes..], out var fieldBytes, out _);
        EnsureComplete(status, "UInt64 payload");
        fields[fieldCount++] = new EbinFieldSource(tag, 4, 1, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);
    }

    private static byte[] CreateApplicationRecord(WorldPersistenceApplication application)
    {
        var moduleArray = CreateModuleVersionArray(application.Modules);
        var payloadStorage = new byte[checked(24 + moduleArray.Length)];
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[4];
        var fieldCount = 0;
        var payloadBytes = 0;
        AddGuidField(1, application.ApplicationId, fields, ref fieldCount, payloadStorage, ref payloadBytes);
        AddUInt32Field(2, application.MajorVersion, fields, ref fieldCount, payloadStorage, ref payloadBytes);
        AddUInt32Field(3, application.MinorVersion, fields, ref fieldCount, payloadStorage, ref payloadBytes);
        AddRecordArrayField(4, application.Modules.Length, moduleArray, fields, ref fieldCount, payloadStorage, ref payloadBytes);
        return CreateRecord(s_applicationSchemaId, fields[..fieldCount], payloadStorage.AsSpan(0, payloadBytes));
    }

    private static byte[] CreateModuleVersionArray(ImmutableArray<WorldPersistenceModuleVersion> modules)
    {
        var childRecords = new byte[modules.Length][];

        for (var index = 0; index < modules.Length; index++)
        {
            childRecords[index] = CreateModuleVersionRecord(modules[index]);
        }

        return CreateRecordArray(childRecords);
    }

    private static byte[] CreateModuleVersionRecord(WorldPersistenceModuleVersion module)
    {
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[2];
        Span<byte> payloadStorage = stackalloc byte[20];
        var fieldCount = 0;
        var payloadBytes = 0;
        AddGuidField(1, module.ModuleId, fields, ref fieldCount, payloadStorage, ref payloadBytes);
        AddUInt32Field(2, module.Version, fields, ref fieldCount, payloadStorage, ref payloadBytes);
        return CreateRecord(s_moduleIdSchemaId, fields[..fieldCount], payloadStorage[..payloadBytes]);
    }

    private static byte[] CreateNumericsRecord(WorldPersistenceRuntimeProfile runtimeProfile)
    {
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[6];
        Span<byte> payloadStorage = stackalloc byte[38];
        var fieldCount = 0;
        var payloadBytes = 0;
        AddBytesField(1, runtimeProfile.NumericsKernelSha256.AsSpan(), fields, ref fieldCount, payloadStorage, ref payloadBytes);
        AddByteField(2, checked((byte)runtimeProfile.FmaPolicy), fields, ref fieldCount, payloadStorage, ref payloadBytes);
        AddUInt16Field(3, runtimeProfile.MaximumSimdBits, fields, ref fieldCount, payloadStorage, ref payloadBytes);
        AddByteField(4, 0, fields, ref fieldCount, payloadStorage, ref payloadBytes);
        AddBooleanField(5, value: false, fields, ref fieldCount, payloadStorage, ref payloadBytes);
        AddBooleanField(6, value: false, fields, ref fieldCount, payloadStorage, ref payloadBytes);
        return CreateRecord(s_numericsSchemaId, fields[..fieldCount], payloadStorage[..payloadBytes]);
    }

    private static byte[] CreateRecord(
        Guid schemaId,
        scoped ReadOnlySpan<EbinFieldSource> fields,
        scoped ReadOnlySpan<byte> payloadStorage)
    {
        var source = new EbinRecordWriteView(schemaId, 1, 0, fields, payloadStorage);
        var status = EbinStructuralWriter.Measure(in source, EbinReadLimits.Foundation, out var recordBytes, out _);
        EnsureComplete(status, "State2 metadata record measurement");
        var candidateStorage = new byte[recordBytes];
        var destination = new byte[recordBytes];
        status = EbinStructuralWriter.Write(
            in source,
            EbinReadLimits.Foundation,
            candidateStorage,
            destination,
            out var bytesWritten,
            out _);
        EnsureComplete(status, "State2 metadata record writing");

        if (bytesWritten != destination.Length)
        {
            throw new InvalidOperationException("The State2 metadata record extent changed after measurement.");
        }

        return destination;
    }

    private static byte[] CreateRecordArray(byte[][] childRecords)
    {
        var childStorageBytes = 0;

        for (var index = 0; index < childRecords.Length; index++)
        {
            childStorageBytes = checked(childStorageBytes + childRecords[index].Length);
        }

        var childStorage = new byte[childStorageBytes];
        var slices = new EbinPayloadSlice[childRecords.Length];
        var offset = 0;

        for (var index = 0; index < childRecords.Length; index++)
        {
            var child = childRecords[index];
            child.CopyTo(childStorage, offset);
            slices[index] = new EbinPayloadSlice(offset, child.Length);
            offset = checked(offset + child.Length);
        }

        var status = EbinPayloadWriter.MeasureRecordArray(
            slices,
            childStorage,
            EbinReadLimits.Foundation,
            out var payloadBytes,
            out _);
        EnsureComplete(status, "State2 metadata record-array measurement");
        var arrayPayload = new byte[payloadBytes];
        status = EbinPayloadWriter.WriteRecordArray(
            slices,
            childStorage,
            EbinReadLimits.Foundation,
            arrayPayload,
            out var bytesWritten,
            out _);
        EnsureComplete(status, "State2 metadata record-array writing");

        if (bytesWritten != arrayPayload.Length)
        {
            throw new InvalidOperationException("The State2 metadata record-array extent changed after measurement.");
        }

        return arrayPayload;
    }

    private static byte[] CreateRuntimeModuleArray(ImmutableArray<WorldPersistenceModuleArtifact> modules)
    {
        var childRecords = new byte[modules.Length][];

        for (var index = 0; index < modules.Length; index++)
        {
            childRecords[index] = CreateRuntimeModuleRecord(modules[index]);
        }

        return CreateRecordArray(childRecords);
    }

    private static byte[] CreateRuntimeModuleRecord(WorldPersistenceModuleArtifact module)
    {
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[3];
        Span<byte> payloadStorage = stackalloc byte[52];
        var fieldCount = 0;
        var payloadBytes = 0;
        AddGuidField(1, module.ModuleId, fields, ref fieldCount, payloadStorage, ref payloadBytes);
        AddUInt32Field(2, module.Version, fields, ref fieldCount, payloadStorage, ref payloadBytes);
        AddBytesField(3, module.ArtifactSha256.AsSpan(), fields, ref fieldCount, payloadStorage, ref payloadBytes);
        return CreateRecord(s_moduleSchemaId, fields[..fieldCount], payloadStorage[..payloadBytes]);
    }

    private static byte[] CreateRuntimeProfileRecord(WorldPersistenceRuntimeProfile runtimeProfile)
    {
        var numericsRecord = CreateNumericsRecord(runtimeProfile);
        var moduleArray = CreateRuntimeModuleArray(runtimeProfile.Modules);
        var payloadStorage = new byte[checked(107 + numericsRecord.Length + moduleArray.Length)];
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[9];
        var fieldCount = 0;
        var payloadBytes = 0;
        AddBytesField(1, runtimeProfile.EngineArtifactSha256.AsSpan(), fields, ref fieldCount, payloadStorage, ref payloadBytes);
        AddBytesField(2, runtimeProfile.RuntimeArtifactSha256.AsSpan(), fields, ref fieldCount, payloadStorage, ref payloadBytes);
        AddBytesField(3, runtimeProfile.CompilerArtifactSha256.AsSpan(), fields, ref fieldCount, payloadStorage, ref payloadBytes);
        AddByteField(4, checked((byte)runtimeProfile.OperatingSystem), fields, ref fieldCount, payloadStorage, ref payloadBytes);
        AddByteField(5, 0, fields, ref fieldCount, payloadStorage, ref payloadBytes);
        AddByteField(6, checked((byte)runtimeProfile.CodeGeneration), fields, ref fieldCount, payloadStorage, ref payloadBytes);
        AddUInt64Field(7, checked((ulong)(int)runtimeProfile.InstructionSets), fields, ref fieldCount, payloadStorage, ref payloadBytes);
        AddRecordField(8, numericsRecord, fields, ref fieldCount, payloadStorage, ref payloadBytes);
        AddRecordArrayField(9, runtimeProfile.Modules.Length, moduleArray, fields, ref fieldCount, payloadStorage, ref payloadBytes);
        return CreateRecord(s_runtimeProfileSchemaId, fields[..fieldCount], payloadStorage.AsSpan(0, payloadBytes));
    }

    private static void EnsureComplete(EbinWriteStatus status, string operation)
    {
        if (status is not EbinWriteStatus.Complete)
        {
            throw new InvalidOperationException($"The {operation} failed with {status}.");
        }
    }
}
