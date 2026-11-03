// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using TerraFX;

namespace TerraFX.Scenes;

internal static class ScenePersistenceWorldHierarchyWriter
{
    private static readonly Guid s_schemaId = new ContentSchemaIdentityAssignment("scene/world-hierarchy").SchemaId;

    internal static EbinWriteStatus Measure(
        int nodeCount,
        scoped ReadOnlySpan<byte> nodeArrayPayload,
        scoped ReadOnlySpan<Guid> rootEntityIds,
        EbinReadLimits limits,
        scoped Span<byte> rootPayloadStorage,
        out ScenePersistenceWorldHierarchyWorkspaceRequirements workspace,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(limits);
        workspace = default;
        diagnostic = null;
        var maximumArrayElements = Math.Min(limits.MaximumArrayElements, limits.MaximumAggregateArrayElements);

        if ((nodeCount < 0)
            || (nodeCount > limits.MaximumArrayElements)
            || (nodeCount > limits.MaximumAggregateArrayElements)
            || (nodeArrayPayload.Length < sizeof(ulong))
            || (BinaryPrimitives.ReadUInt64LittleEndian(nodeArrayPayload) != checked((ulong)nodeCount)))
        {
            diagnostic = CreateDiagnostic("world-hierarchy-node-array");
            return EbinWriteStatus.InvalidRecord;
        }

        if ((rootEntityIds.Length > limits.MaximumArrayElements)
            || (rootEntityIds.Length > limits.MaximumAggregateArrayElements))
        {
            diagnostic = CreateDiagnostic(
                "array-count-budget",
                checked((ulong)rootEntityIds.Length),
                checked((ulong)maximumArrayElements));
            return EbinWriteStatus.InvalidRecord;
        }

        for (var index = 0; index < rootEntityIds.Length; index++)
        {
            if (rootEntityIds[index] == Guid.Empty)
            {
                diagnostic = CreateDiagnostic("world-hierarchy-root-identity");
                return EbinWriteStatus.InvalidRecord;
            }
        }

        var rootArrayPayloadBytes = checked(32L + (16L * rootEntityIds.Length));
        var requiredPayloadBytes = checked(nodeArrayPayload.Length + rootArrayPayloadBytes);

        if ((requiredPayloadBytes > int.MaxValue) || (requiredPayloadBytes > limits.MaximumRecordBytes))
        {
            diagnostic = CreateDiagnostic(
                "record-byte-budget",
                checked((ulong)requiredPayloadBytes),
                checked((ulong)limits.MaximumRecordBytes));
            return EbinWriteStatus.InvalidRecord;
        }

        if (rootPayloadStorage.Length != requiredPayloadBytes)
        {
            throw new ArgumentException("The scene/world-hierarchy root payload workspace must match its measured extent.", nameof(rootPayloadStorage));
        }

        ValidateNonoverlappingPayloads(nodeArrayPayload, rootEntityIds, rootPayloadStorage);
        var rootIdScratch = MemoryMarshal.Cast<byte, Guid>(
            rootPayloadStorage[..checked(rootEntityIds.Length * 16)]);

        if (!ScenePersistenceWorldHierarchyCapture.AreRfcUnique(rootEntityIds, rootIdScratch))
        {
            diagnostic = CreateDiagnostic("world-hierarchy-root-duplicate");
            return EbinWriteStatus.InvalidRecord;
        }

        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[2];
        var status = BuildFields(
            nodeCount,
            nodeArrayPayload,
            rootEntityIds,
            limits,
            fields,
            rootPayloadStorage,
            out var payloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (payloadBytes != requiredPayloadBytes)
        {
            throw new InvalidOperationException("The scene/world-hierarchy root payload extent changed during measurement.");
        }

        var source = new EbinRecordWriteView(
            s_schemaId,
            major: 1,
            minor: 0,
            fields,
            rootPayloadStorage[..payloadBytes]);
        status = EbinStructuralWriter.Measure(in source, limits, out var recordBytes, out diagnostic);

        if (status is EbinWriteStatus.Complete)
        {
            workspace = new ScenePersistenceWorldHierarchyWorkspaceRequirements(
                nodeCount,
                nodeArrayPayload.Length,
                rootEntityIds.Length,
                checked((int)requiredPayloadBytes),
                recordBytes);
        }

        return status;
    }

    internal static EbinWriteStatus Write(
        ScenePersistenceWorldHierarchyWorkspaceRequirements workspace,
        int nodeCount,
        scoped ReadOnlySpan<byte> nodeArrayPayload,
        scoped ReadOnlySpan<Guid> rootEntityIds,
        EbinReadLimits limits,
        scoped Span<byte> rootPayloadStorage,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = Measure(
            nodeCount,
            nodeArrayPayload,
            rootEntityIds,
            limits,
            rootPayloadStorage,
            out var measuredWorkspace,
            out diagnostic);
        bytesWritten = 0;

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (!workspace.Matches(measuredWorkspace))
        {
            diagnostic = CreateDiagnostic("world-hierarchy-workspace-mismatch");
            return EbinWriteStatus.InvalidRecord;
        }

        if ((candidateStorage.Length != workspace.RecordBytes)
            || (destination.Length != workspace.RecordBytes))
        {
            throw new ArgumentException("The scene/world-hierarchy record workspaces must match their measured extent.");
        }

        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[2];
        status = BuildFields(
            nodeCount,
            nodeArrayPayload,
            rootEntityIds,
            limits,
            fields,
            rootPayloadStorage,
            out var payloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (payloadBytes != workspace.RootPayloadBytes)
        {
            throw new InvalidOperationException("The scene/world-hierarchy root payload extent changed after workspace measurement.");
        }

        var source = new EbinRecordWriteView(
            s_schemaId,
            major: 1,
            minor: 0,
            fields,
            rootPayloadStorage[..payloadBytes]);
        return EbinStructuralWriter.Write(
            in source,
            limits,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }

    private static EbinWriteStatus BuildFields(
        int nodeCount,
        scoped ReadOnlySpan<byte> nodeArrayPayload,
        scoped ReadOnlySpan<Guid> rootEntityIds,
        EbinReadLimits limits,
        Span<EbinFieldSource> fields,
        Span<byte> rootPayloadStorage,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        fields.Clear();
        payloadBytes = 0;
        nodeArrayPayload.CopyTo(rootPayloadStorage);
        fields[0] = new EbinFieldSource(
            1,
            24,
            1,
            checked((ulong)nodeCount),
            new EbinPayloadSlice(0, nodeArrayPayload.Length));
        var status = EbinPayloadWriter.WriteFixedArray(
            rootEntityIds,
            limits,
            rootPayloadStorage[nodeArrayPayload.Length..],
            out var rootArrayPayloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[1] = new EbinFieldSource(
            2,
            23,
            1,
            checked((ulong)rootEntityIds.Length),
            new EbinPayloadSlice(nodeArrayPayload.Length, rootArrayPayloadBytes));
        payloadBytes = checked(nodeArrayPayload.Length + rootArrayPayloadBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinStructuralDiagnostic CreateDiagnostic(string category, ulong claim = 0, ulong limit = 1)
        => new EbinStructuralDiagnostic(
            s_schemaId,
            0,
            category,
            null,
            claim,
            limit,
            EbinStructuralDiagnosticSource.Construction);

    private static void ValidateNonoverlappingPayloads(
        scoped ReadOnlySpan<byte> nodeArrayPayload,
        scoped ReadOnlySpan<Guid> rootEntityIds,
        scoped ReadOnlySpan<byte> rootPayloadStorage)
    {
        if (nodeArrayPayload.Overlaps(rootPayloadStorage)
            || MemoryMarshal.AsBytes(rootEntityIds).Overlaps(rootPayloadStorage))
        {
            throw new ArgumentException("The scene/world-hierarchy root payload must not overlap its source arrays.");
        }
    }
}
