// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.InteropServices;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Scenes;

internal static class ScenePersistenceWorldHierarchyNodeArrayWriter
{
    private static readonly Guid s_schemaId = new ContentSchemaIdentityAssignment("scene/world-hierarchy-node").SchemaId;

    internal static EbinWriteStatus Measure(
        scoped ReadOnlySpan<SceneWorldHierarchyCaptureRow> hierarchyRows,
        scoped ReadOnlySpan<Guid> childEntityIds,
        EbinReadLimits limits,
        scoped Span<byte> nodePayloadStorage,
        out ScenePersistenceWorldHierarchyNodeArrayWorkspaceRequirements workspace,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(limits);
        workspace = default;
        var validationStatus = CountAndValidateHierarchy(
            hierarchyRows,
            childEntityIds,
            limits,
            out var childCount,
            out var maximumNodePayloadBytes,
            out diagnostic);

        if (validationStatus is not EbinWriteStatus.Complete)
        {
            return validationStatus;
        }

        if (nodePayloadStorage.Length != maximumNodePayloadBytes)
        {
            throw new ArgumentException("The world-hierarchy-node payload workspace must match its measured extent.", nameof(nodePayloadStorage));
        }

        long totalRecordStorageBytes = 0;
        var maximumCandidateBytes = 0;

        for (var index = 0; index < hierarchyRows.Length; index++)
        {
            var row = hierarchyRows[index];
            var nodePayloadBytes = ScenePersistenceWorldHierarchyNodeWriter.GetPayloadBytes(
                row.ParentEntityId.HasValue,
                row.ChildCount);
            var children = childEntityIds.Slice(row.ChildOffset, row.ChildCount);
            var status = ScenePersistenceWorldHierarchyNodeWriter.Measure(
                row.EntityId,
                row.ParentEntityId,
                children,
                limits,
                nodePayloadStorage[..nodePayloadBytes],
                out _,
                out var recordBytes,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            totalRecordStorageBytes = checked(totalRecordStorageBytes + recordBytes);
            maximumCandidateBytes = Math.Max(maximumCandidateBytes, recordBytes);
        }

        if (totalRecordStorageBytes > int.MaxValue)
        {
            diagnostic = CreateDiagnostic("record-byte-budget");
            return EbinWriteStatus.InvalidRecord;
        }

        var recordStorageBytes = checked((int)totalRecordStorageBytes);
        var arrayStatus = WorldPersistenceRecordArrayLayout.MeasurePayloadBytes(
            hierarchyRows.Length,
            totalRecordStorageBytes,
            limits,
            out var arrayPayloadBytes,
            out diagnostic);

        if (arrayStatus is not EbinWriteStatus.Complete)
        {
            return arrayStatus;
        }

        workspace = new ScenePersistenceWorldHierarchyNodeArrayWorkspaceRequirements(
            hierarchyRows.Length,
            childCount,
            maximumNodePayloadBytes,
            recordStorageBytes,
            maximumCandidateBytes,
            arrayPayloadBytes);
        return EbinWriteStatus.Complete;
    }

    internal static EbinWriteStatus Write(
        scoped ReadOnlySpan<SceneWorldHierarchyCaptureRow> hierarchyRows,
        scoped ReadOnlySpan<Guid> childEntityIds,
        EbinReadLimits limits,
        ScenePersistenceWorldHierarchyNodeArrayWorkspaceRequirements workspace,
        scoped Span<EbinPayloadSlice> recordSlices,
        scoped Span<byte> recordStorage,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> nodePayloadStorage,
        scoped Span<byte> arrayPayloadDestination,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = Measure(
            hierarchyRows,
            childEntityIds,
            limits,
            nodePayloadStorage,
            out var measuredWorkspace,
            out diagnostic);
        payloadBytes = 0;

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (!workspace.Matches(measuredWorkspace))
        {
            diagnostic = CreateDiagnostic("world-hierarchy-node-array-workspace-mismatch");
            return EbinWriteStatus.InvalidRecord;
        }

        if ((recordSlices.Length != workspace.NodeCount)
            || (recordStorage.Length != workspace.RecordStorageBytes)
            || (candidateStorage.Length != workspace.MaximumCandidateBytes)
            || (nodePayloadStorage.Length != workspace.NodePayloadBytes)
            || (arrayPayloadDestination.Length != workspace.ArrayPayloadBytes))
        {
            throw new ArgumentException("The world-hierarchy-node array workspaces must match their measured extents.");
        }

        ValidateNonoverlappingBuffers(
            hierarchyRows,
            childEntityIds,
            recordSlices,
            recordStorage,
            candidateStorage,
            nodePayloadStorage,
            arrayPayloadDestination);
        var storageOffset = 0;

        for (var index = 0; index < hierarchyRows.Length; index++)
        {
            var row = hierarchyRows[index];
            var children = childEntityIds.Slice(row.ChildOffset, row.ChildCount);
            var nodePayloadBytes = ScenePersistenceWorldHierarchyNodeWriter.GetPayloadBytes(
                row.ParentEntityId.HasValue,
                row.ChildCount);
            status = ScenePersistenceWorldHierarchyNodeWriter.Measure(
                row.EntityId,
                row.ParentEntityId,
                children,
                limits,
                nodePayloadStorage[..nodePayloadBytes],
                out _,
                out var recordBytes,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            status = ScenePersistenceWorldHierarchyNodeWriter.Write(
                row.EntityId,
                row.ParentEntityId,
                children,
                limits,
                nodePayloadStorage[..nodePayloadBytes],
                candidateStorage[..recordBytes],
                recordStorage.Slice(storageOffset, recordBytes),
                out var bytesWritten,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            if (bytesWritten != recordBytes)
            {
                throw new InvalidOperationException("The world-hierarchy-node record extent changed after workspace measurement.");
            }

            recordSlices[index] = new EbinPayloadSlice(storageOffset, bytesWritten);
            storageOffset = checked(storageOffset + bytesWritten);
        }

        if (storageOffset != workspace.RecordStorageBytes)
        {
            throw new InvalidOperationException("The world-hierarchy-node record array extent changed after workspace measurement.");
        }

        status = EbinPayloadWriter.WriteRecordArray(
            recordSlices,
            recordStorage,
            limits,
            arrayPayloadDestination,
            out payloadBytes,
            out diagnostic);

        if ((status is EbinWriteStatus.Complete) && (payloadBytes != workspace.ArrayPayloadBytes))
        {
            diagnostic = CreateDiagnostic("world-hierarchy-node-array-size-mismatch");
            payloadBytes = 0;
            return EbinWriteStatus.InvalidRecord;
        }

        return status;
    }

    private static EbinWriteStatus CountAndValidateHierarchy(
        scoped ReadOnlySpan<SceneWorldHierarchyCaptureRow> hierarchyRows,
        scoped ReadOnlySpan<Guid> childEntityIds,
        EbinReadLimits limits,
        out int childCount,
        out int maximumNodePayloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        childCount = 0;
        maximumNodePayloadBytes = 0;
        diagnostic = null;
        var expectedChildOffset = 0;
        var actualChildCount = 0;

        for (var index = 0; index < hierarchyRows.Length; index++)
        {
            var row = hierarchyRows[index];

            if ((row.EntityId == Guid.Empty)
                || (row.ParentEntityId == Guid.Empty)
                || (row.ParentEntityId == row.EntityId)
                || (row.ChildCount < 0)
                || (row.ChildOffset != expectedChildOffset)
                || ((index > 0) && (FoundationCatalogue.CompareRfc(hierarchyRows[index - 1].EntityId, row.EntityId) >= 0)))
            {
                diagnostic = CreateDiagnostic("world-hierarchy-node-array-member");
                return EbinWriteStatus.InvalidRecord;
            }

            maximumNodePayloadBytes = Math.Max(
                maximumNodePayloadBytes,
                ScenePersistenceWorldHierarchyNodeWriter.GetPayloadBytes(row.ParentEntityId.HasValue, row.ChildCount));
            var childEnd = (long)row.ChildOffset + row.ChildCount;

            if (childEnd > childEntityIds.Length)
            {
                diagnostic = CreateDiagnostic("world-hierarchy-child-range");
                return EbinWriteStatus.InvalidRecord;
            }

            var children = childEntityIds.Slice(row.ChildOffset, row.ChildCount);

            for (var childIndex = 0; childIndex < children.Length; childIndex++)
            {
                if ((children[childIndex] == Guid.Empty)
                    || (children[childIndex] == row.EntityId))
                {
                    diagnostic = CreateDiagnostic("world-hierarchy-child-identity");
                    return EbinWriteStatus.InvalidRecord;
                }
            }

            expectedChildOffset = checked((int)childEnd);
            childCount = checked(childCount + row.ChildCount);
            actualChildCount = checked(actualChildCount + (row.ParentEntityId.HasValue ? 1 : 0));
        }

        // Unique child IDs with matching parent links plus equal edge counts cover every parent link.
        if ((expectedChildOffset != childEntityIds.Length) || (actualChildCount != childCount))
        {
            diagnostic = CreateDiagnostic("world-hierarchy-child-count");
            return EbinWriteStatus.InvalidRecord;
        }

        if (maximumNodePayloadBytes > limits.MaximumRecordBytes)
        {
            diagnostic = CreateDiagnostic(
                "record-byte-budget",
                checked((ulong)maximumNodePayloadBytes),
                checked((ulong)limits.MaximumRecordBytes));
            return EbinWriteStatus.InvalidRecord;
        }

        if (childCount > limits.MaximumAggregateArrayElements)
        {
            diagnostic = CreateDiagnostic(
                "array-count-budget",
                checked((ulong)childCount),
                checked((ulong)limits.MaximumAggregateArrayElements));
            return EbinWriteStatus.InvalidRecord;
        }

        for (var index = 0; index < hierarchyRows.Length; index++)
        {
            var row = hierarchyRows[index];

            if (row.ParentEntityId is Guid parentEntityId)
            {
                if (FindNodeIndex(hierarchyRows, parentEntityId) < 0)
                {
                    diagnostic = CreateDiagnostic("world-hierarchy-parent");
                    return EbinWriteStatus.InvalidRecord;
                }
            }

            var children = childEntityIds.Slice(row.ChildOffset, row.ChildCount);

            for (var childIndex = 0; childIndex < children.Length; childIndex++)
            {
                var childNodeIndex = FindNodeIndex(hierarchyRows, children[childIndex]);

                if ((childNodeIndex < 0) || (hierarchyRows[childNodeIndex].ParentEntityId != row.EntityId))
                {
                    diagnostic = CreateDiagnostic("world-hierarchy-child-parent-mismatch");
                    return EbinWriteStatus.InvalidRecord;
                }
            }
        }

        if ((hierarchyRows.Length > limits.MaximumArrayElements)
            || (hierarchyRows.Length > limits.MaximumAggregateArrayElements))
        {
            diagnostic = CreateDiagnostic(
                "array-count-budget",
                checked((ulong)hierarchyRows.Length),
                checked((ulong)Math.Min(limits.MaximumArrayElements, limits.MaximumAggregateArrayElements)));
            return EbinWriteStatus.InvalidRecord;
        }

        return EbinWriteStatus.Complete;
    }

    private static EbinStructuralDiagnostic CreateDiagnostic(string category, ulong claim = 0, ulong limit = 1)
        => new EbinStructuralDiagnostic(
            s_schemaId,
            1,
            category,
            null,
            claim,
            limit,
            EbinStructuralDiagnosticSource.Construction);

    private static int FindNodeIndex(scoped ReadOnlySpan<SceneWorldHierarchyCaptureRow> rows, Guid entityId)
    {
        var lower = 0;
        var upper = rows.Length - 1;

        while (lower <= upper)
        {
            var middle = lower + ((upper - lower) / 2);
            var comparison = FoundationCatalogue.CompareRfc(rows[middle].EntityId, entityId);

            if (comparison == 0)
            {
                return middle;
            }

            if (comparison < 0)
            {
                lower = middle + 1;
            }
            else
            {
                upper = middle - 1;
            }
        }

        return -1;
    }

    private static void ValidateNonoverlappingBuffers(
        scoped ReadOnlySpan<SceneWorldHierarchyCaptureRow> hierarchyRows,
        scoped ReadOnlySpan<Guid> childEntityIds,
        scoped ReadOnlySpan<EbinPayloadSlice> recordSlices,
        scoped ReadOnlySpan<byte> recordStorage,
        scoped ReadOnlySpan<byte> candidateStorage,
        scoped ReadOnlySpan<byte> nodePayloadStorage,
        scoped ReadOnlySpan<byte> arrayPayloadDestination)
    {
        var recordSliceBytes = MemoryMarshal.AsBytes(recordSlices);
        var hierarchyRowBytes = MemoryMarshal.AsBytes(hierarchyRows);

        if (recordSliceBytes.Overlaps(hierarchyRowBytes)
            || recordSliceBytes.Overlaps(MemoryMarshal.AsBytes(childEntityIds))
            || recordSliceBytes.Overlaps(recordStorage)
            || recordSliceBytes.Overlaps(candidateStorage)
            || recordSliceBytes.Overlaps(nodePayloadStorage)
            || recordSliceBytes.Overlaps(arrayPayloadDestination)
            || hierarchyRowBytes.Overlaps(MemoryMarshal.AsBytes(childEntityIds))
            || hierarchyRowBytes.Overlaps(recordStorage)
            || hierarchyRowBytes.Overlaps(candidateStorage)
            || hierarchyRowBytes.Overlaps(nodePayloadStorage)
            || hierarchyRowBytes.Overlaps(arrayPayloadDestination)
            || MemoryMarshal.AsBytes(childEntityIds).Overlaps(recordStorage)
            || MemoryMarshal.AsBytes(childEntityIds).Overlaps(candidateStorage)
            || MemoryMarshal.AsBytes(childEntityIds).Overlaps(nodePayloadStorage)
            || MemoryMarshal.AsBytes(childEntityIds).Overlaps(arrayPayloadDestination)
            || recordStorage.Overlaps(candidateStorage)
            || recordStorage.Overlaps(nodePayloadStorage)
            || recordStorage.Overlaps(arrayPayloadDestination)
            || candidateStorage.Overlaps(nodePayloadStorage)
            || candidateStorage.Overlaps(arrayPayloadDestination)
            || nodePayloadStorage.Overlaps(arrayPayloadDestination))
        {
            throw new ArgumentException("The world-hierarchy-node array workspaces must not overlap.");
        }
    }
}
