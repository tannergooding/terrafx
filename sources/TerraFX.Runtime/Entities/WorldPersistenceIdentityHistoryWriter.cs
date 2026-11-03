// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;

namespace TerraFX.Entities;

internal static class WorldPersistenceIdentityHistoryWriter
{
    private static readonly Guid s_schemaId = new ContentSchemaIdentityAssignment("state/identity-history").SchemaId;

    internal static EbinWriteStatus Measure(
        in World.IdentityHistoryCapture history,
        EbinReadLimits limits,
        scoped Span<EbinPayloadSlice> branchSlices,
        scoped Span<EbinPayloadSlice> pageSlices,
        scoped Span<Guid> pageStableIds,
        scoped Span<uint> pageMetadata,
        scoped Span<byte> pagePayloadStorage,
        out WorldPersistenceIdentityHistoryWorkspaceRequirements workspace,
        out EbinStructuralDiagnostic? diagnostic)
        => MeasureWorkspace(
            in history,
            limits,
            branchSlices,
            pageSlices,
            pageStableIds,
            pageMetadata,
            pagePayloadStorage,
            out workspace,
            out diagnostic);

    internal static EbinWriteStatus MeasureWorkspace(
        in World.IdentityHistoryCapture history,
        EbinReadLimits limits,
        scoped Span<EbinPayloadSlice> branchSlices,
        scoped Span<EbinPayloadSlice> pageSlices,
        scoped Span<Guid> pageStableIds,
        scoped Span<uint> pageMetadata,
        scoped Span<byte> pagePayloadStorage,
        out WorldPersistenceIdentityHistoryWorkspaceRequirements workspace,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(limits);
        workspace = default;
        diagnostic = null;
        var branchCount = history.BranchCount;
        var pageCount = history.PageCount;

        if ((branchCount is < 1 or > 1024) || (pageCount is < 0 or > 8192))
        {
            diagnostic = CreateDiagnostic("array-count-budget", checked((ulong)Math.Max(branchCount, pageCount)), 8192);
            return EbinWriteStatus.InvalidRecord;
        }

        if (limits.MaximumFieldsPerRecord < 7)
        {
            diagnostic = CreateDiagnostic("field-count-budget", 7, checked((ulong)limits.MaximumFieldsPerRecord));
            return EbinWriteStatus.InvalidRecord;
        }

        if (branchSlices.Length != branchCount)
        {
            throw new ArgumentException("The branch-slice table must match the exact identity-branch count.", nameof(branchSlices));
        }

        if (pageSlices.Length != pageCount)
        {
            throw new ArgumentException("The page-slice table must match the exact identity-page count.", nameof(pageSlices));
        }

        if (pageCount == 0)
        {
            if (!pageStableIds.IsEmpty || !pageMetadata.IsEmpty || !pagePayloadStorage.IsEmpty)
            {
                throw new ArgumentException("An empty identity history must not reserve identity-page workspaces.", nameof(pageStableIds));
            }
        }
        else if ((pageStableIds.Length != StableIdentityLedger.RecordsPerPage)
            || (pageMetadata.Length != StableIdentityLedger.RecordsPerPage)
            || (pagePayloadStorage.Length != WorldPersistenceIdentityPageWriter.GetMinimumPayloadWorkspaceBytes(StableIdentityLedger.RecordsPerPage)))
        {
            throw new ArgumentException("Nonempty identity history requires one full-page reusable workspace.", nameof(pageStableIds));
        }

        branchSlices.Clear();
        pageSlices.Clear();

        long branchStorageBytes = 0;
        long pageStorageBytes = 0;
        var childCandidateBytes = 0;
        var aggregateFields = 7L;
        var branches = history.GetBranchEnumerator();
        var branchIndex = 0;

        while (branches.MoveNext())
        {
            var branchStatus = WorldPersistenceIdentityBranchWriter.Measure(
                in branches,
                limits,
                out var childRecordBytes,
                out diagnostic);

            if (branchStatus is not EbinWriteStatus.Complete)
            {
                return branchStatus;
            }

            var branchOffset = branchStorageBytes;
            branchStorageBytes = checked(branchStorageBytes + childRecordBytes);

            if (branchStorageBytes > limits.MaximumRecordBytes)
            {
                diagnostic = CreateDiagnostic(
                    "record-byte-budget",
                    checked((ulong)branchStorageBytes),
                    checked((ulong)limits.MaximumRecordBytes));
                return EbinWriteStatus.InvalidRecord;
            }

            if (branchIndex >= branchSlices.Length)
            {
                throw new InvalidOperationException("The identity-branch cursor exceeds its exact workspace.");
            }

            branchSlices[branchIndex++] = new EbinPayloadSlice(checked((int)branchOffset), childRecordBytes);
            childCandidateBytes = Math.Max(childCandidateBytes, childRecordBytes);
            aggregateFields = checked(aggregateFields + (branches.CurrentParentBranchId.HasValue ? 8 : 7));
        }

        if (branchIndex != branchCount)
        {
            throw new InvalidOperationException("The identity-branch cursor count changed during workspace measurement.");
        }

        var aggregateArrayElements = checked((long)branchCount + pageCount);

        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            var pageRecordCount = history.GetPageRecordCount(pageIndex);
            var pageWorkspaceBytes = WorldPersistenceIdentityPageWriter.GetMinimumPayloadWorkspaceBytes(pageRecordCount);
            var pageStatus = WorldPersistenceIdentityPageWriter.Measure(
                in history,
                limits,
                pageIndex,
                pageStableIds[..pageRecordCount],
                pageMetadata[..pageRecordCount],
                pagePayloadStorage[..pageWorkspaceBytes],
                out var childRecordBytes,
                out diagnostic);

            if (pageStatus is not EbinWriteStatus.Complete)
            {
                return pageStatus;
            }

            var pageOffset = pageStorageBytes;
            pageStorageBytes = checked(pageStorageBytes + childRecordBytes);

            if (pageStorageBytes > limits.MaximumRecordBytes)
            {
                diagnostic = CreateDiagnostic(
                    "record-byte-budget",
                    checked((ulong)pageStorageBytes),
                    checked((ulong)limits.MaximumRecordBytes));
                return EbinWriteStatus.InvalidRecord;
            }

            pageSlices[pageIndex] = new EbinPayloadSlice(checked((int)pageOffset), childRecordBytes);
            childCandidateBytes = Math.Max(childCandidateBytes, childRecordBytes);
            aggregateFields = checked(aggregateFields + 3);
            aggregateArrayElements = checked(aggregateArrayElements + checked((long)pageRecordCount * 2));
        }

        aggregateArrayElements = checked(aggregateArrayElements + branchCount + pageCount);
        var aggregateRecordCount = checked(1L + branchCount + pageCount);

        if (aggregateRecordCount > limits.MaximumAggregateRecords)
        {
            diagnostic = CreateDiagnostic(
                "aggregate-record-budget",
                checked((ulong)aggregateRecordCount),
                checked((ulong)limits.MaximumAggregateRecords));
            return EbinWriteStatus.InvalidRecord;
        }

        if (aggregateFields > limits.MaximumAggregateFields)
        {
            diagnostic = CreateDiagnostic(
                "aggregate-field-budget",
                checked((ulong)aggregateFields),
                checked((ulong)limits.MaximumAggregateFields));
            return EbinWriteStatus.InvalidRecord;
        }

        if (aggregateArrayElements > limits.MaximumAggregateArrayElements)
        {
            diagnostic = CreateDiagnostic(
                "aggregate-array-budget",
                checked((ulong)aggregateArrayElements),
                checked((ulong)limits.MaximumAggregateArrayElements));
            return EbinWriteStatus.InvalidRecord;
        }

        var status = GetRecordArrayPayloadBytes(
            branchCount,
            branchStorageBytes,
            limits,
            out var branchArrayPayloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = GetRecordArrayPayloadBytes(
            pageCount,
            pageStorageBytes,
            limits,
            out var pageArrayPayloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var rootPayloadStorageBytes = checked(72 + branchArrayPayloadBytes + pageArrayPayloadBytes);
        var recordBytes = GetRootRecordBytes(branchArrayPayloadBytes, pageArrayPayloadBytes);

        if (recordBytes > limits.MaximumRecordBytes)
        {
            diagnostic = CreateDiagnostic(
                "record-byte-budget",
                checked((ulong)recordBytes),
                checked((ulong)limits.MaximumRecordBytes));
            return EbinWriteStatus.InvalidRecord;
        }

        workspace = new WorldPersistenceIdentityHistoryWorkspaceRequirements(
            branchCount,
            pageCount,
            checked((int)branchStorageBytes),
            checked((int)pageStorageBytes),
            branchArrayPayloadBytes,
            pageArrayPayloadBytes,
            childCandidateBytes,
            rootPayloadStorageBytes,
            recordBytes);
        return EbinWriteStatus.Complete;
    }

    internal static EbinWriteStatus Write(
        in World.IdentityHistoryCapture history,
        EbinReadLimits limits,
        scoped Span<EbinPayloadSlice> branchSlices,
        scoped Span<byte> branchStorage,
        scoped Span<EbinPayloadSlice> pageSlices,
        scoped Span<byte> pageStorage,
        scoped Span<Guid> pageStableIds,
        scoped Span<uint> pageMetadata,
        scoped Span<byte> pagePayloadStorage,
        scoped Span<byte> childCandidateStorage,
        scoped Span<byte> rootPayloadStorage,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = MeasureWorkspace(
            in history,
            limits,
            branchSlices,
            pageSlices,
            pageStableIds,
            pageMetadata,
            pagePayloadStorage,
            out var workspace,
            out diagnostic);
        bytesWritten = 0;

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if ((branchStorage.Length != workspace.BranchStorageBytes)
            || (pageStorage.Length != workspace.PageStorageBytes)
            || (childCandidateStorage.Length != workspace.ChildCandidateBytes)
            || (rootPayloadStorage.Length != workspace.RootPayloadStorageBytes)
            || (candidateStorage.Length != workspace.RecordBytes)
            || (destination.Length != workspace.RecordBytes))
        {
            throw new ArgumentException("The identity-history writer workspaces must match the exact measured lengths.");
        }

        status = WriteBranchRecords(
            in history,
            limits,
            branchSlices,
            branchStorage,
            childCandidateStorage,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = WritePageRecords(
            in history,
            limits,
            pageSlices,
            pageStorage,
            pageStableIds,
            pageMetadata,
            pagePayloadStorage,
            childCandidateStorage,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        rootPayloadStorage.Clear();
        status = WriteGuidPayload(history.WorldGuid, rootPayloadStorage, out var fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var fieldCount = 0;
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[7];
        fields[fieldCount++] = new EbinFieldSource(1, 11, 1, 1, new EbinPayloadSlice(0, fieldBytes));
        var payloadBytes = fieldBytes;
        status = WriteGuidPayload(history.ReplayLineageId, rootPayloadStorage[payloadBytes..], out fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(2, 11, 1, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);
        status = WriteGuidPayload(history.ActiveBranchId, rootPayloadStorage[payloadBytes..], out fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(3, 11, 1, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);
        status = WriteGuidPayload(history.IssuanceNamespaceId, rootPayloadStorage[payloadBytes..], out fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(4, 11, 1, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);
        status = EbinPayloadWriter.Write(
            history.NextAssignmentOrdinal,
            rootPayloadStorage[payloadBytes..],
            out fieldBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(5, 8, 1, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);
        var branchArrayOffset = payloadBytes;
        status = EbinPayloadWriter.WriteRecordArray(
            branchSlices,
            branchStorage,
            limits,
            rootPayloadStorage.Slice(branchArrayOffset, workspace.BranchArrayPayloadBytes),
            out var branchArrayBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (branchArrayBytes != workspace.BranchArrayPayloadBytes)
        {
            throw new InvalidOperationException("The identity-branch array extent changed after workspace measurement.");
        }

        fields[fieldCount++] = new EbinFieldSource(
            6,
            24,
            1,
            checked((ulong)workspace.BranchCount),
            new EbinPayloadSlice(branchArrayOffset, branchArrayBytes));
        payloadBytes = checked(payloadBytes + branchArrayBytes);
        var pageArrayOffset = payloadBytes;
        status = EbinPayloadWriter.WriteRecordArray(
            pageSlices,
            pageStorage,
            limits,
            rootPayloadStorage.Slice(pageArrayOffset, workspace.PageArrayPayloadBytes),
            out var pageArrayBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (pageArrayBytes != workspace.PageArrayPayloadBytes)
        {
            throw new InvalidOperationException("The identity-page array extent changed after workspace measurement.");
        }

        fields[fieldCount++] = new EbinFieldSource(
            7,
            24,
            1,
            checked((ulong)workspace.PageCount),
            new EbinPayloadSlice(pageArrayOffset, pageArrayBytes));
        payloadBytes = checked(payloadBytes + pageArrayBytes);

        if (payloadBytes != workspace.RootPayloadStorageBytes)
        {
            throw new InvalidOperationException("The identity-history payload extent changed after workspace measurement.");
        }

        var source = new EbinRecordWriteView(
            s_schemaId,
            major: 1,
            minor: 0,
            fields[..fieldCount],
            rootPayloadStorage[..payloadBytes]);
        status = EbinStructuralWriter.Measure(in source, limits, out var measuredRecordBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (measuredRecordBytes != workspace.RecordBytes)
        {
            diagnostic = CreateDiagnostic(
                "identity-history-record-size-mismatch",
                checked((ulong)measuredRecordBytes),
                checked((ulong)workspace.RecordBytes));
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

    private static EbinWriteStatus GetRecordArrayPayloadBytes(
        int childCount,
        long childStorageBytes,
        EbinReadLimits limits,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        payloadBytes = 0;
        diagnostic = null;

        if ((childCount > limits.MaximumArrayElements)
            || (childCount > limits.MaximumAggregateArrayElements))
        {
            var limit = Math.Min((long)limits.MaximumArrayElements, limits.MaximumAggregateArrayElements);
            diagnostic = CreateDiagnostic(
                "array-count-budget",
                checked((ulong)childCount),
                checked((ulong)limit));
            return EbinWriteStatus.InvalidRecord;
        }

        // A record array places its children after a 16-byte-aligned count and count-plus-one offset table.
        var offsetTableBytes = checked(8L + ((checked((long)childCount) + 1L) * 8L));
        var firstChildOffset = Align16(offsetTableBytes);
        var totalBytes = checked(firstChildOffset + childStorageBytes);

        if (totalBytes > limits.MaximumRecordBytes)
        {
            diagnostic = CreateDiagnostic(
                "record-byte-budget",
                checked((ulong)totalBytes),
                checked((ulong)limits.MaximumRecordBytes));
            return EbinWriteStatus.InvalidRecord;
        }

        payloadBytes = checked((int)totalBytes);
        return EbinWriteStatus.Complete;
    }

    private static int GetRootRecordBytes(int branchArrayPayloadBytes, int pageArrayPayloadBytes)
    {
        // The root has a 64-byte header, seven 32-byte field entries, four GUIDs, one I64, and two aligned record arrays.
        var recordOffset = Align16(64 + (7 * 32));
        recordOffset = checked(Align16(recordOffset) + 16);
        recordOffset = checked(Align16(recordOffset) + 16);
        recordOffset = checked(Align16(recordOffset) + 16);
        recordOffset = checked(Align16(recordOffset) + 16);
        recordOffset = checked(Align16(recordOffset) + sizeof(long));
        recordOffset = checked(Align16(recordOffset) + branchArrayPayloadBytes);
        recordOffset = checked(Align16(recordOffset) + pageArrayPayloadBytes);
        return checked((int)Align16(recordOffset));
    }

    private static EbinWriteStatus WriteBranchRecords(
        in World.IdentityHistoryCapture history,
        EbinReadLimits limits,
        scoped ReadOnlySpan<EbinPayloadSlice> branchSlices,
        scoped Span<byte> branchStorage,
        scoped Span<byte> candidateStorage,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var branches = history.GetBranchEnumerator();
        var branchIndex = 0;

        while (branches.MoveNext())
        {
            var slice = branchSlices[branchIndex++];
            var status = WorldPersistenceIdentityBranchWriter.Write(
                in branches,
                limits,
                candidateStorage,
                branchStorage.Slice(slice.Offset, slice.ByteLength),
                out var bytesWritten,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            if (bytesWritten != slice.ByteLength)
            {
                throw new InvalidOperationException("An identity-branch writer changed its measured record extent.");
            }
        }

        if (branchIndex != branchSlices.Length)
        {
            throw new InvalidOperationException("The identity-branch count changed during serialization.");
        }

        diagnostic = null;
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus WriteGuidPayload(
        Guid value,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
        => EbinPayloadWriter.Write(value, destination, out bytesWritten, out diagnostic);

    private static EbinWriteStatus WritePageRecords(
        in World.IdentityHistoryCapture history,
        EbinReadLimits limits,
        scoped ReadOnlySpan<EbinPayloadSlice> pageSlices,
        scoped Span<byte> pageStorage,
        scoped Span<Guid> pageStableIds,
        scoped Span<uint> pageMetadata,
        scoped Span<byte> pagePayloadStorage,
        scoped Span<byte> candidateStorage,
        out EbinStructuralDiagnostic? diagnostic)
    {
        for (var pageIndex = 0; pageIndex < pageSlices.Length; pageIndex++)
        {
            var recordCount = history.GetPageRecordCount(pageIndex);
            var payloadBytes = WorldPersistenceIdentityPageWriter.GetMinimumPayloadWorkspaceBytes(recordCount);
            var slice = pageSlices[pageIndex];
            var status = WorldPersistenceIdentityPageWriter.Write(
                in history,
                limits,
                pageIndex,
                pageStableIds[..recordCount],
                pageMetadata[..recordCount],
                pagePayloadStorage[..payloadBytes],
                candidateStorage,
                pageStorage.Slice(slice.Offset, slice.ByteLength),
                out var bytesWritten,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            if (bytesWritten != slice.ByteLength)
            {
                throw new InvalidOperationException("An identity-page writer changed its measured record extent.");
            }
        }

        diagnostic = null;
        return EbinWriteStatus.Complete;
    }
}
