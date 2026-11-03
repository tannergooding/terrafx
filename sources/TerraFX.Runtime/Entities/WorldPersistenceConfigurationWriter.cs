// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;

namespace TerraFX.Entities;

internal static class WorldPersistenceConfigurationWriter
{
    private static readonly Guid s_configurationSchemaId = new ContentSchemaIdentityAssignment("state/configuration").SchemaId;
    private static readonly Guid s_schedulingLimitsSchemaId = new ContentSchemaIdentityAssignment("state/scheduling-limits").SchemaId;

    internal static EbinWriteStatus Measure(
        WorldOptions options,
        out int recordBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(options);
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[16];
        Span<byte> payloadStorage = stackalloc byte[1024];
        var status = BuildFields(options, fields, payloadStorage, out var fieldCount, out var payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            recordBytes = 0;
            return status;
        }

        var source = new EbinRecordWriteView(
            s_configurationSchemaId,
            major: 1,
            minor: 0,
            fields[..fieldCount],
            payloadStorage[..payloadBytes]);
        return EbinStructuralWriter.Measure(in source, EbinReadLimits.Foundation, out recordBytes, out diagnostic);
    }

    internal static EbinWriteStatus Write(
        WorldOptions options,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(options);
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[16];
        Span<byte> payloadStorage = stackalloc byte[1024];
        var status = BuildFields(options, fields, payloadStorage, out var fieldCount, out var payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            bytesWritten = 0;
            return status;
        }

        var source = new EbinRecordWriteView(
            s_configurationSchemaId,
            major: 1,
            minor: 0,
            fields[..fieldCount],
            payloadStorage[..payloadBytes]);
        return EbinStructuralWriter.Write(
            in source,
            EbinReadLimits.Foundation,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
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
            throw new InvalidOperationException("The State2 configuration workspace cannot contain its scheduling record.");
        }

        record.CopyTo(payloadStorage[payloadBytes..]);
        fields[fieldCount++] = new EbinFieldSource(tag, 22, 1, 1, new EbinPayloadSlice(payloadBytes, record.Length));
        payloadBytes = checked(payloadBytes + record.Length);
        diagnostic = null;
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus AddUInt16Field(
        uint tag,
        ushort value,
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

        fields[fieldCount++] = new EbinFieldSource(tag, 2, 1, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);
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
        WorldOptions options,
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

        Span<EbinFieldSource> schedulingFields = stackalloc EbinFieldSource[7];
        Span<byte> schedulingPayloadStorage = stackalloc byte[128];
        Span<byte> schedulingRecordStorage = stackalloc byte[512];
        schedulingFields.Clear();
        schedulingPayloadStorage.Clear();
        schedulingRecordStorage.Clear();

        var schedulingFieldCount = 0;
        var schedulingPayloadBytes = 0;
        var limits = options.SchedulingLimits;
        var status = AddInt64Field(1, limits.MaximumRegistrationBytes, schedulingFields, ref schedulingFieldCount, schedulingPayloadStorage, ref schedulingPayloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddInt64Field(2, limits.MaximumGraphBytes, schedulingFields, ref schedulingFieldCount, schedulingPayloadStorage, ref schedulingPayloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddInt64Field(3, limits.MaximumWorkPlanBytes, schedulingFields, ref schedulingFieldCount, schedulingPayloadStorage, ref schedulingPayloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddInt64Field(4, limits.MaximumCommandStagingBytes, schedulingFields, ref schedulingFieldCount, schedulingPayloadStorage, ref schedulingPayloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddInt64Field(5, limits.MaximumOwnedStateBytes, schedulingFields, ref schedulingFieldCount, schedulingPayloadStorage, ref schedulingPayloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddInt64Field(6, limits.MaximumDiagnosticBytes, schedulingFields, ref schedulingFieldCount, schedulingPayloadStorage, ref schedulingPayloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddUInt32Field(
            7,
            checked((uint)limits.MaximumConflictComparisons),
            schedulingFields,
            ref schedulingFieldCount,
            schedulingPayloadStorage,
            ref schedulingPayloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var schedulingSource = new EbinRecordWriteView(
            s_schedulingLimitsSchemaId,
            major: 1,
            minor: 0,
            schedulingFields[..schedulingFieldCount],
            schedulingPayloadStorage[..schedulingPayloadBytes]);
        status = EbinStructuralWriter.Prepare(
            in schedulingSource,
            EbinReadLimits.Foundation,
            schedulingRecordStorage,
            out var schedulingRecordBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddUInt32Field(1, checked((uint)options.MaximumLiveEntities), fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddUInt32Field(2, checked((uint)options.MaximumEntitySlots), fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddUInt32Field(3, checked((uint)options.MaximumArchetypes), fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddUInt32Field(4, checked((uint)options.MaximumDeferredCommands), fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddUInt32Field(5, checked((uint)options.MaximumComponentTypes), fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddUInt32Field(6, checked((uint)options.MaximumPendingOwnerRequests), fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddUInt32Field(7, checked((uint)options.MaximumRecordedControls), fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddUInt32Field(8, checked((uint)options.MaximumIdentityAssignments), fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddRecordField(9, schedulingRecordStorage[..schedulingRecordBytes], fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddUInt8Field(10, 0, fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddUInt16Field(11, checked((ushort)options.MaximumIdentityBranches), fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddUInt32Field(12, checked((uint)options.MaximumEntityReferencePositions), fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddUInt32Field(13, checked((uint)options.MaximumActiveSceneInstances), fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddUInt32Field(14, checked((uint)options.MaximumSceneOccurrences), fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddInt64Field(15, options.MaximumSnapshotBytes, fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        return AddInt64Field(
            16,
            options.MaximumPersistenceAdmissionBytes,
            fields,
            ref fieldCount,
            payloadStorage,
            ref payloadBytes,
            out diagnostic);
    }
}
