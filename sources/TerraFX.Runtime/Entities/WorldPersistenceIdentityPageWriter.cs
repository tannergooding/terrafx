// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;

namespace TerraFX.Entities;

internal static class WorldPersistenceIdentityPageWriter
{
    private static readonly Guid s_schemaId = new ContentSchemaIdentityAssignment("state/identity-page").SchemaId;

    internal static int GetMinimumPayloadWorkspaceBytes(int recordCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(recordCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(recordCount, StableIdentityLedger.RecordsPerPage);
        var idPayloadBytes = checked(32 + checked(recordCount * 16));
        var metadataPayloadBytes = checked(32 + checked(recordCount * sizeof(uint)));
        return checked(sizeof(long) + idPayloadBytes + metadataPayloadBytes);
    }

    internal static EbinWriteStatus Measure(
        in World.IdentityHistoryCapture history,
        EbinReadLimits limits,
        int pageIndex,
        scoped Span<Guid> stableIds,
        scoped Span<uint> metadata,
        scoped Span<byte> payloadStorage,
        out int recordBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[3];
        var status = BuildFields(
            in history,
            limits,
            pageIndex,
            stableIds,
            metadata,
            payloadStorage,
            fields,
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
        in World.IdentityHistoryCapture history,
        EbinReadLimits limits,
        int pageIndex,
        scoped Span<Guid> stableIds,
        scoped Span<uint> metadata,
        scoped Span<byte> payloadStorage,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[3];
        var status = BuildFields(
            in history,
            limits,
            pageIndex,
            stableIds,
            metadata,
            payloadStorage,
            fields,
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

    private static EbinWriteStatus BuildFields(
        in World.IdentityHistoryCapture history,
        EbinReadLimits limits,
        int pageIndex,
        scoped Span<Guid> stableIds,
        scoped Span<uint> metadata,
        scoped Span<byte> payloadStorage,
        Span<EbinFieldSource> fields,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        fields.Clear();
        payloadStorage.Clear();
        payloadBytes = 0;
        diagnostic = null;
        var recordCount = history.GetPageRecordCount(pageIndex);

        if (stableIds.Length != recordCount)
        {
            throw new ArgumentException("The stable-ID workspace must match the exact identity-page length.", nameof(stableIds));
        }

        if (metadata.Length != recordCount)
        {
            throw new ArgumentException("The metadata workspace must match the exact identity-page length.", nameof(metadata));
        }

        var minimumPayloadBytes = GetMinimumPayloadWorkspaceBytes(recordCount);

        if (payloadStorage.Length != minimumPayloadBytes)
        {
            throw new ArgumentException("The identity-page payload workspace must have its exact required length.", nameof(payloadStorage));
        }

        var firstOrdinal = history.CopyPage(pageIndex, stableIds, metadata);
        var status = EbinPayloadWriter.Write(
            firstOrdinal,
            payloadStorage,
            out var firstOrdinalBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var idPayloadBytes = checked(32 + checked(recordCount * 16));
        status = EbinPayloadWriter.WriteFixedArray(
            stableIds,
            limits,
            payloadStorage.Slice(firstOrdinalBytes, idPayloadBytes),
            out var idArrayBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var metadataPayloadBytes = checked(32 + checked(recordCount * sizeof(uint)));
        status = EbinPayloadWriter.WriteFixedArray(
            metadata,
            limits,
            payloadStorage.Slice(checked(firstOrdinalBytes + idArrayBytes), metadataPayloadBytes),
            out var metadataArrayBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[0] = new EbinFieldSource(1, 8, 1, 1, new EbinPayloadSlice(0, firstOrdinalBytes));
        fields[1] = new EbinFieldSource(
            2,
            23,
            1,
            checked((ulong)recordCount),
            new EbinPayloadSlice(firstOrdinalBytes, idArrayBytes));
        fields[2] = new EbinFieldSource(
            3,
            23,
            1,
            checked((ulong)recordCount),
            new EbinPayloadSlice(checked(firstOrdinalBytes + idArrayBytes), metadataArrayBytes));
        payloadBytes = checked(firstOrdinalBytes + idArrayBytes + metadataArrayBytes);
        return EbinWriteStatus.Complete;
    }
}
