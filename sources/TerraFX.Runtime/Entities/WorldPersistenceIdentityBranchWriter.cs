// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;

namespace TerraFX.Entities;

internal static class WorldPersistenceIdentityBranchWriter
{
    private static readonly Guid s_schemaId = new ContentSchemaIdentityAssignment("state/identity-branch").SchemaId;

    internal static EbinWriteStatus Measure(
        in World.IdentityBranchCaptureEnumerator branch,
        EbinReadLimits limits,
        out int recordBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var current = branch.CurrentRecord;
        return MeasureRecord(in current, limits, out recordBytes, out diagnostic);
    }

    internal static EbinWriteStatus MeasureRecord(
        in World.WorldIdentityBranchRecord branch,
        EbinReadLimits limits,
        out int recordBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[10];
        Span<byte> payloadStorage = stackalloc byte[128];
        var status = BuildFields(
            in branch,
            fields,
            payloadStorage,
            out var fieldCount,
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
            fields[..fieldCount],
            payloadStorage[..payloadBytes]);
        return EbinStructuralWriter.Measure(in source, limits, out recordBytes, out diagnostic);
    }

    internal static EbinWriteStatus Write(
        in World.IdentityBranchCaptureEnumerator branch,
        EbinReadLimits limits,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var current = branch.CurrentRecord;
        return WriteRecord(
            in current,
            limits,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }

    internal static EbinWriteStatus WriteRecord(
        in World.WorldIdentityBranchRecord branch,
        EbinReadLimits limits,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[10];
        Span<byte> payloadStorage = stackalloc byte[128];
        var status = BuildFields(
            in branch,
            fields,
            payloadStorage,
            out var fieldCount,
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
        bool isRequired,
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

        fields[fieldCount++] = new EbinFieldSource(tag, 11, isRequired ? (ushort)1 : (ushort)0, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus AddHashField(
        uint tag,
        scoped ReadOnlySpan<byte> value,
        scoped Span<EbinFieldSource> fields,
        ref int fieldCount,
        scoped Span<byte> payloadStorage,
        ref int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        diagnostic = null;

        if (value.Length != 32)
        {
            throw new InvalidOperationException("An identity branch source checkpoint digest must contain exactly 32 bytes.");
        }

        if (value.Length > payloadStorage.Length - payloadBytes)
        {
            throw new InvalidOperationException("The identity branch writer payload workspace is smaller than its exact measured extent.");
        }

        value.CopyTo(payloadStorage[payloadBytes..]);
        fields[fieldCount++] = new EbinFieldSource(
            tag,
            13,
            0,
            checked((ulong)value.Length),
            new EbinPayloadSlice(payloadBytes, value.Length));
        payloadBytes = checked(payloadBytes + value.Length);
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
        in World.WorldIdentityBranchRecord branch,
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

        if (!branch.ForkSourceRecord.IsEmpty)
        {
            throw new NotSupportedException("The identity-branch writer does not yet support fork-source records.");
        }

        var isRoot = branch.DirectoryIndex == 0;
        var hasParent = branch.ParentBranchId.HasValue;

        if (isRoot == hasParent)
        {
            throw new InvalidOperationException("The identity branch parent does not match its directory position.");
        }

        var status = AddUInt16Field(
            1,
            checked((ushort)branch.DirectoryIndex),
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
            branch.BranchId,
            isRequired: true,
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
            3,
            branch.LineageId,
            isRequired: true,
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
            branch.NamespaceId,
            isRequired: true,
            fields,
            ref fieldCount,
            payloadStorage,
            ref payloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (branch.ParentBranchId is Guid parentBranchId)
        {
            status = AddGuidField(
                5,
                parentBranchId,
                isRequired: false,
                fields,
                ref fieldCount,
                payloadStorage,
                ref payloadBytes,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }
        }

        status = AddInt64Field(
            6,
            branch.CreatedAtNextTick,
            fields,
            ref fieldCount,
            payloadStorage,
            ref payloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var sourceCheckpointDigest = branch.SourceCheckpointDigest;

        if (!sourceCheckpointDigest.IsEmpty)
        {
            status = AddHashField(
                7,
                sourceCheckpointDigest.Span,
                fields,
                ref fieldCount,
                payloadStorage,
                ref payloadBytes,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }
        }

        status = AddUInt8Field(
            9,
            branch.DirectoryIndex == 0 ? (byte)1 : (byte)2,
            fields,
            ref fieldCount,
            payloadStorage,
            ref payloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        return AddInt64Field(
            10,
            checked((long)branch.FirstAssignmentOrdinal),
            fields,
            ref fieldCount,
            payloadStorage,
            ref payloadBytes,
            out diagnostic);
    }
}
