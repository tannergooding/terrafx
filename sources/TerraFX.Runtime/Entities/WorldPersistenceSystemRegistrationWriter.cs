// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using TerraFX;

namespace TerraFX.Entities;

internal static class WorldPersistenceSystemRegistrationWriter
{
    private const int ScalarPayloadBytes = 42;

    private static readonly Guid s_schemaId = new ContentSchemaIdentityAssignment("state/system-registration").SchemaId;

    internal static void CopySortedAccesses(
        scoped ReadOnlySpan<SystemResourceAccess> source,
        scoped Span<SystemResourceAccess> destination)
    {
        if (destination.Length != source.Length)
        {
            throw new ArgumentException("The sorted access workspace must match the source count.", nameof(destination));
        }

        source.CopyTo(destination);
        SortAccesses(destination);
    }

    internal static void CopySortedGuids(scoped ReadOnlySpan<Guid> source, scoped Span<Guid> destination)
    {
        if (destination.Length != source.Length)
        {
            throw new ArgumentException("The sorted identity workspace must match the source count.", nameof(destination));
        }

        source.CopyTo(destination);
        SortGuids(destination);
    }

    internal static EbinWriteStatus Measure(
        SystemDescription description,
        WorldSystemFactoryDescriptor factory,
        EbinReadLimits limits,
        ref WorldPersistenceSystemRegistrationBuffers buffers,
        out WorldPersistenceSystemRegistrationWorkspaceRequirements workspace,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = MeasureWorkspace(description, factory, limits, out workspace, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        ValidateWorkspaceBuffers(buffers, workspace);
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[10];
        status = BuildFields(
            description,
            factory,
            limits,
            workspace,
            ref buffers,
            fields,
            out var fieldCount,
            out var payloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var source = new EbinRecordWriteView(
            s_schemaId,
            major: 1,
            minor: 0,
            fields[..fieldCount],
            buffers.RootPayloadStorage[..payloadBytes]);
        status = EbinStructuralWriter.Measure(in source, limits, out var recordBytes, out diagnostic);

        if (status is EbinWriteStatus.Complete)
        {
            workspace = workspace.WithRecordBytes(recordBytes);
        }

        return status;
    }

    internal static EbinWriteStatus MeasureWorkspace(
        SystemDescription description,
        WorldSystemFactoryDescriptor factory,
        EbinReadLimits limits,
        out WorldPersistenceSystemRegistrationWorkspaceRequirements workspace,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(limits);
        workspace = default;
        diagnostic = null;

        if ((description.SystemId == Guid.Empty)
            || (description.Stage is < SimulationStage.Begin or > SimulationStage.Extract)
            || (description.ThreadMode is not SystemThreadMode.OwnerThread and not SystemThreadMode.Parallel)
            || (factory.FactoryId == Guid.Empty)
            || (factory.FactoryVersion <= 0)
            || (factory.SystemType != factory.SourceState.SystemType))
        {
            diagnostic = CreateDiagnostic("system-registration-metadata", 0, 1);
            return EbinWriteStatus.InvalidRecord;
        }

        var fieldCount = factory.StateSchema.HasValue ? 10 : 9;

        if (fieldCount > limits.MaximumFieldsPerRecord)
        {
            diagnostic = CreateDiagnostic("field-count-budget", checked((ulong)fieldCount), checked((ulong)limits.MaximumFieldsPerRecord));
            return EbinWriteStatus.InvalidRecord;
        }

        long accessStorageBytes = 0;
        var accessCandidateStorageBytes = 0;

        foreach (var access in description.Access)
        {
            var status = WorldPersistenceAccessWriter.Measure(
                access,
                limits,
                out var accessRecordBytes,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            accessStorageBytes = checked(accessStorageBytes + accessRecordBytes);
            accessCandidateStorageBytes = Math.Max(accessCandidateStorageBytes, accessRecordBytes);
        }

        var accessArrayStatus = WorldPersistenceRecordArrayLayout.MeasurePayloadBytes(
            description.Access.Length,
            accessStorageBytes,
            limits,
            out var accessArrayPayloadBytes,
            out diagnostic);

        if (accessArrayStatus is not EbinWriteStatus.Complete)
        {
            return accessArrayStatus;
        }

        var beforeArrayStatus = MeasureGuidFixedArray(
            description.Before.Length,
            limits,
            out var beforeArrayPayloadBytes,
            out diagnostic);

        if (beforeArrayStatus is not EbinWriteStatus.Complete)
        {
            return beforeArrayStatus;
        }

        var afterArrayStatus = MeasureGuidFixedArray(
            description.After.Length,
            limits,
            out var afterArrayPayloadBytes,
            out diagnostic);

        if (afterArrayStatus is not EbinWriteStatus.Complete)
        {
            return afterArrayStatus;
        }

        var schemaReferenceRecordBytes = 0;

        if (factory.StateSchema is ContentSchemaReference stateSchema)
        {
            var schemaStatus = WorldPersistenceSchemaReferenceWriter.Measure(
                stateSchema,
                limits,
                out schemaReferenceRecordBytes,
                out diagnostic);

            if (schemaStatus is not EbinWriteStatus.Complete)
            {
                return schemaStatus;
            }
        }

        var rootPayloadStorageBytes = checked(
            ScalarPayloadBytes
            + accessArrayPayloadBytes
            + beforeArrayPayloadBytes
            + afterArrayPayloadBytes
            + schemaReferenceRecordBytes);

        if (rootPayloadStorageBytes > limits.MaximumRecordBytes)
        {
            diagnostic = CreateDiagnostic(
                "record-byte-budget",
                checked((ulong)rootPayloadStorageBytes),
                checked((ulong)limits.MaximumRecordBytes));
            return EbinWriteStatus.InvalidRecord;
        }

        workspace = new WorldPersistenceSystemRegistrationWorkspaceRequirements(
            description.Access.Length,
            checked((int)accessStorageBytes),
            accessCandidateStorageBytes,
            accessArrayPayloadBytes,
            Math.Max(description.Before.Length, description.After.Length),
            beforeArrayPayloadBytes,
            afterArrayPayloadBytes,
            schemaReferenceRecordBytes,
            rootPayloadStorageBytes,
            recordBytes: 0);
        return EbinWriteStatus.Complete;
    }

    internal static EbinWriteStatus Write(
        SystemDescription description,
        WorldSystemFactoryDescriptor factory,
        EbinReadLimits limits,
        WorldPersistenceSystemRegistrationWorkspaceRequirements workspace,
        ref WorldPersistenceSystemRegistrationBuffers buffers,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = MeasureWorkspace(description, factory, limits, out var measuredWorkspace, out diagnostic);
        bytesWritten = 0;

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (!workspace.Matches(measuredWorkspace))
        {
            diagnostic = CreateDiagnostic("system-registration-workspace-mismatch", 0, 1);
            return EbinWriteStatus.InvalidRecord;
        }

        ValidateWorkspaceBuffers(buffers, workspace);

        if ((workspace.RecordBytes <= 0)
            || (buffers.CandidateStorage.Length != workspace.RecordBytes)
            || (buffers.Destination.Length != workspace.RecordBytes))
        {
            throw new ArgumentException("The system-registration record workspaces must match the measured record extent.");
        }

        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[10];
        status = BuildFields(
            description,
            factory,
            limits,
            workspace,
            ref buffers,
            fields,
            out var fieldCount,
            out var payloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var source = new EbinRecordWriteView(
            s_schemaId,
            major: 1,
            minor: 0,
            fields[..fieldCount],
            buffers.RootPayloadStorage[..payloadBytes]);
        status = EbinStructuralWriter.Measure(in source, limits, out var recordBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (recordBytes != workspace.RecordBytes)
        {
            diagnostic = CreateDiagnostic(
                "system-registration-record-size-mismatch",
                checked((ulong)recordBytes),
                checked((ulong)workspace.RecordBytes));
            return EbinWriteStatus.InvalidRecord;
        }

        return EbinStructuralWriter.Write(
            in source,
            limits,
            buffers.CandidateStorage,
            buffers.Destination,
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

    private static EbinWriteStatus AddInt32Field(
        uint tag,
        int value,
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

        fields[fieldCount++] = new EbinFieldSource(tag, 7, 1, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus AddPayloadField(
        uint tag,
        ushort kind,
        ulong count,
        scoped ReadOnlySpan<byte> payload,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        if (payload.Length > payloadStorage.Length - payloadBytes)
        {
            throw new InvalidOperationException("The system-registration workspace cannot contain its nested array.");
        }

        payload.CopyTo(payloadStorage[payloadBytes..]);
        fields[fieldCount++] = new EbinFieldSource(tag, kind, 1, count, new EbinPayloadSlice(payloadBytes, payload.Length));
        payloadBytes = checked(payloadBytes + payload.Length);
        diagnostic = null;
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
            throw new InvalidOperationException("The system-registration workspace cannot contain its state schema.");
        }

        record.CopyTo(payloadStorage[payloadBytes..]);
        fields[fieldCount++] = new EbinFieldSource(tag, 22, flags, 1, new EbinPayloadSlice(payloadBytes, record.Length));
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
        SystemDescription description,
        WorldSystemFactoryDescriptor factory,
        EbinReadLimits limits,
        WorldPersistenceSystemRegistrationWorkspaceRequirements workspace,
        ref WorldPersistenceSystemRegistrationBuffers buffers,
        scoped Span<EbinFieldSource> fields,
        out int fieldCount,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        fields.Clear();
        buffers.RootPayloadStorage.Clear();
        fieldCount = 0;
        payloadBytes = 0;
        diagnostic = null;
        var status = WriteAccessArray(
            description,
            limits,
            workspace,
            ref buffers,
            out var accessArrayBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = WriteDependencyArray(
            description.Before.AsSpan(),
            limits,
            workspace.BeforeArrayPayloadBytes,
            buffers.DependencyScratch,
            buffers.BeforeArrayPayloadStorage,
            out var beforeArrayBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = WriteDependencyArray(
            description.After.AsSpan(),
            limits,
            workspace.AfterArrayPayloadBytes,
            buffers.DependencyScratch,
            buffers.AfterArrayPayloadStorage,
            out var afterArrayBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var schemaRecordBytes = 0;

        if (factory.StateSchema is ContentSchemaReference stateSchema)
        {
            status = WorldPersistenceSchemaReferenceWriter.Write(
                stateSchema,
                limits,
                buffers.SchemaCandidateStorage,
                buffers.SchemaRecordStorage,
                out schemaRecordBytes,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            if (schemaRecordBytes != workspace.SchemaReferenceRecordBytes)
            {
                throw new InvalidOperationException("The system state-schema extent changed after workspace measurement.");
            }
        }

        status = AddGuidField(
            1,
            description.SystemId,
            fields,
            ref fieldCount,
            buffers.RootPayloadStorage,
            ref payloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddInt32Field(
            2,
            description.Order,
            fields,
            ref fieldCount,
            buffers.RootPayloadStorage,
            ref payloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddUInt8Field(
            3,
            checked((byte)description.Stage),
            fields,
            ref fieldCount,
            buffers.RootPayloadStorage,
            ref payloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddUInt8Field(
            4,
            checked((byte)description.ThreadMode),
            fields,
            ref fieldCount,
            buffers.RootPayloadStorage,
            ref payloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddPayloadField(
            5,
            24,
            checked((ulong)description.Access.Length),
            buffers.AccessArrayPayloadStorage[..accessArrayBytes],
            fields,
            ref fieldCount,
            buffers.RootPayloadStorage,
            ref payloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddPayloadField(
            6,
            23,
            checked((ulong)description.Before.Length),
            buffers.BeforeArrayPayloadStorage[..beforeArrayBytes],
            fields,
            ref fieldCount,
            buffers.RootPayloadStorage,
            ref payloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddPayloadField(
            7,
            23,
            checked((ulong)description.After.Length),
            buffers.AfterArrayPayloadStorage[..afterArrayBytes],
            fields,
            ref fieldCount,
            buffers.RootPayloadStorage,
            ref payloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddGuidField(
            8,
            factory.FactoryId,
            fields,
            ref fieldCount,
            buffers.RootPayloadStorage,
            ref payloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddUInt32Field(
            9,
            checked((uint)factory.FactoryVersion),
            fields,
            ref fieldCount,
            buffers.RootPayloadStorage,
            ref payloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (factory.StateSchema.HasValue)
        {
            status = AddRecordField(
                10,
                flags: 0,
                buffers.SchemaRecordStorage[..schemaRecordBytes],
                fields,
                ref fieldCount,
                buffers.RootPayloadStorage,
                ref payloadBytes,
                out diagnostic);
        }

        if (payloadBytes != workspace.RootPayloadStorageBytes)
        {
            throw new InvalidOperationException("The system-registration payload extent changed after workspace measurement.");
        }

        return status;
    }

    private static int CompareAccess(SystemResourceAccess left, SystemResourceAccess right)
    {
        var comparison = left.Kind.CompareTo(right.Kind);
        return (comparison != 0) ? comparison : FoundationCatalogue.CompareRfc(left.ResourceId, right.ResourceId);
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

    private static EbinWriteStatus MeasureGuidFixedArray(
        int count,
        EbinReadLimits limits,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        payloadBytes = 0;
        diagnostic = null;

        if ((count > limits.MaximumArrayElements)
            || (count > limits.MaximumAggregateArrayElements))
        {
            var limit = Math.Min(limits.MaximumArrayElements, limits.MaximumAggregateArrayElements);
            diagnostic = CreateDiagnostic("array-count-budget", checked((ulong)count), checked((ulong)limit));
            return EbinWriteStatus.InvalidRecord;
        }

        var extent = checked(32L + (checked((long)count) * 16L));

        if (extent > limits.MaximumRecordBytes)
        {
            diagnostic = CreateDiagnostic(
                "record-byte-budget",
                checked((ulong)extent),
                checked((ulong)limits.MaximumRecordBytes));
            return EbinWriteStatus.InvalidRecord;
        }

        payloadBytes = checked((int)extent);
        return EbinWriteStatus.Complete;
    }

    private static void SortAccesses(Span<SystemResourceAccess> values)
    {
        for (var index = 1; index < values.Length; index++)
        {
            var value = values[index];
            var insertionIndex = index;

            while ((insertionIndex > 0) && (CompareAccess(value, values[insertionIndex - 1]) < 0))
            {
                values[insertionIndex] = values[insertionIndex - 1];
                insertionIndex--;
            }

            values[insertionIndex] = value;
        }
    }

    private static void SortGuids(Span<Guid> values)
    {
        for (var gap = values.Length / 2; gap > 0; gap /= 2)
        {
            for (var index = gap; index < values.Length; index++)
            {
                var value = values[index];
                var insertionIndex = index;

                while ((insertionIndex >= gap)
                    && (FoundationCatalogue.CompareRfc(value, values[insertionIndex - gap]) < 0))
                {
                    values[insertionIndex] = values[insertionIndex - gap];
                    insertionIndex -= gap;
                }

                values[insertionIndex] = value;
            }
        }
    }

    private static void ValidateWorkspaceBuffers(
        WorldPersistenceSystemRegistrationBuffers buffers,
        WorldPersistenceSystemRegistrationWorkspaceRequirements workspace)
    {
        if ((buffers.AccessScratch.Length != workspace.AccessCount)
            || (buffers.AccessSlices.Length != workspace.AccessCount)
            || (buffers.AccessStorage.Length != workspace.AccessStorageBytes)
            || (buffers.AccessCandidateStorage.Length != workspace.AccessCandidateStorageBytes)
            || (buffers.AccessArrayPayloadStorage.Length != workspace.AccessArrayPayloadBytes)
            || (buffers.AfterArrayPayloadStorage.Length != workspace.AfterArrayPayloadBytes)
            || (buffers.BeforeArrayPayloadStorage.Length != workspace.BeforeArrayPayloadBytes)
            || (buffers.DependencyScratch.Length != workspace.DependencyScratchCount)
            || (buffers.RootPayloadStorage.Length != workspace.RootPayloadStorageBytes)
            || (buffers.SchemaCandidateStorage.Length != workspace.SchemaReferenceRecordBytes)
            || (buffers.SchemaRecordStorage.Length != workspace.SchemaReferenceRecordBytes)
            || (buffers.GetWorkspaceBytes() != workspace.WorkspaceBytes))
        {
            throw new ArgumentException("The system-registration workspaces must match the exact measured lengths.");
        }
    }

    private static EbinWriteStatus WriteAccessArray(
        SystemDescription description,
        EbinReadLimits limits,
        WorldPersistenceSystemRegistrationWorkspaceRequirements workspace,
        ref WorldPersistenceSystemRegistrationBuffers buffers,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        buffers.AccessScratch.Clear();
        description.Access.AsSpan().CopyTo(buffers.AccessScratch);
        SortAccesses(buffers.AccessScratch);
        var accessStorageOffset = 0;

        for (var accessIndex = 0; accessIndex < buffers.AccessScratch.Length; accessIndex++)
        {
            var access = buffers.AccessScratch[accessIndex];
            var status = WorldPersistenceAccessWriter.Measure(
                access,
                limits,
                out var accessRecordBytes,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                payloadBytes = 0;
                return status;
            }

            var accessDestination = buffers.AccessStorage.Slice(accessStorageOffset, accessRecordBytes);
            status = WorldPersistenceAccessWriter.Write(
                access,
                limits,
                buffers.AccessCandidateStorage[..accessRecordBytes],
                accessDestination,
                out var accessBytesWritten,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                payloadBytes = 0;
                return status;
            }

            if (accessBytesWritten != accessRecordBytes)
            {
                throw new InvalidOperationException("The access-record extent changed after workspace measurement.");
            }

            buffers.AccessSlices[accessIndex] = new EbinPayloadSlice(accessStorageOffset, accessBytesWritten);
            accessStorageOffset = checked(accessStorageOffset + accessBytesWritten);
        }

        if (accessStorageOffset != workspace.AccessStorageBytes)
        {
            throw new InvalidOperationException("The access-array child storage extent changed after workspace measurement.");
        }

        var arrayStatus = EbinPayloadWriter.WriteRecordArray(
            buffers.AccessSlices,
            buffers.AccessStorage,
            limits,
            buffers.AccessArrayPayloadStorage,
            out payloadBytes,
            out diagnostic);

        if (arrayStatus is EbinWriteStatus.Complete)
        {
            if (payloadBytes != workspace.AccessArrayPayloadBytes)
            {
                throw new InvalidOperationException("The system access-array extent changed after workspace measurement.");
            }
        }

        return arrayStatus;
    }

    private static EbinWriteStatus WriteDependencyArray(
        scoped ReadOnlySpan<Guid> values,
        EbinReadLimits limits,
        int expectedPayloadBytes,
        Span<Guid> dependencyScratch,
        Span<byte> payloadStorage,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        values.CopyTo(dependencyScratch);
        var sorted = dependencyScratch[..values.Length];
        SortGuids(sorted);
        var status = EbinPayloadWriter.WriteFixedArray(
            sorted,
            limits,
            payloadStorage,
            out payloadBytes,
            out diagnostic);

        if ((status is EbinWriteStatus.Complete) && (payloadBytes != expectedPayloadBytes))
        {
            throw new InvalidOperationException("The dependency fixed-array extent changed after workspace measurement.");
        }

        return status;
    }
}
