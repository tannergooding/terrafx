// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.InteropServices;
using TerraFX;

namespace TerraFX.Scenes;

internal static class ScenePersistenceWorldHierarchyNodeWriter
{
    private static readonly Guid s_schemaId = new ContentSchemaIdentityAssignment("scene/world-hierarchy-node").SchemaId;

    internal static int GetPayloadBytes(bool hasParent, int childCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(childCount);
        return checked(16 + (hasParent ? 16 : 0) + 32 + (childCount * 16));
    }

    internal static EbinWriteStatus Measure(
        Guid entityId,
        Guid? parentEntityId,
        scoped ReadOnlySpan<Guid> childEntityIds,
        EbinReadLimits limits,
        scoped Span<byte> rootPayloadStorage,
        out int rootPayloadBytes,
        out int recordBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(limits);
        rootPayloadBytes = 0;
        recordBytes = 0;

        if ((entityId == Guid.Empty)
            || (parentEntityId == Guid.Empty)
            || (parentEntityId == entityId))
        {
            diagnostic = CreateDiagnostic("world-hierarchy-node-identity");
            return EbinWriteStatus.InvalidRecord;
        }

        for (var index = 0; index < childEntityIds.Length; index++)
        {
            if ((childEntityIds[index] == Guid.Empty)
                || (childEntityIds[index] == entityId))
            {
                diagnostic = CreateDiagnostic("world-hierarchy-node-child-identity");
                return EbinWriteStatus.InvalidRecord;
            }
        }

        if ((childEntityIds.Length > limits.MaximumArrayElements)
            || (childEntityIds.Length > limits.MaximumAggregateArrayElements))
        {
            diagnostic = CreateDiagnostic(
                "array-count-budget",
                checked((ulong)childEntityIds.Length),
                checked((ulong)Math.Min(limits.MaximumArrayElements, limits.MaximumAggregateArrayElements)));
            return EbinWriteStatus.InvalidRecord;
        }

        var requiredPayloadBytes = GetPayloadBytes(parentEntityId.HasValue, childEntityIds.Length);

        if ((requiredPayloadBytes > limits.MaximumRecordBytes)
            || (rootPayloadStorage.Length != requiredPayloadBytes))
        {
            if (requiredPayloadBytes > limits.MaximumRecordBytes)
            {
                diagnostic = CreateDiagnostic(
                    "record-byte-budget",
                    checked((ulong)requiredPayloadBytes),
                    checked((ulong)limits.MaximumRecordBytes));
                return EbinWriteStatus.InvalidRecord;
            }

            throw new ArgumentException("The world-hierarchy-node payload workspace must match its measured extent.", nameof(rootPayloadStorage));
        }

        if (MemoryMarshal.AsBytes(childEntityIds).Overlaps(rootPayloadStorage))
        {
            throw new ArgumentException("The world-hierarchy-node payload workspace must not overlap its child IDs.", nameof(rootPayloadStorage));
        }

        var childIdScratch = MemoryMarshal.Cast<byte, Guid>(
            rootPayloadStorage[..checked(childEntityIds.Length * 16)]);

        if (!ScenePersistenceWorldHierarchyCapture.AreRfcUnique(childEntityIds, childIdScratch))
        {
            diagnostic = CreateDiagnostic("world-hierarchy-node-child-duplicate");
            return EbinWriteStatus.InvalidRecord;
        }

        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[3];
        var status = BuildFields(
            entityId,
            parentEntityId,
            childEntityIds,
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
            throw new InvalidOperationException("The world-hierarchy-node payload extent changed during measurement.");
        }

        var source = new EbinRecordWriteView(
            s_schemaId,
            major: 1,
            minor: 0,
            fields[..(parentEntityId.HasValue ? 3 : 2)],
            rootPayloadStorage[..payloadBytes]);
        status = EbinStructuralWriter.Measure(in source, limits, out recordBytes, out diagnostic);

        if (status is EbinWriteStatus.Complete)
        {
            rootPayloadBytes = requiredPayloadBytes;
        }

        return status;
    }

    internal static EbinWriteStatus Write(
        Guid entityId,
        Guid? parentEntityId,
        scoped ReadOnlySpan<Guid> childEntityIds,
        EbinReadLimits limits,
        scoped Span<byte> rootPayloadStorage,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = Measure(
            entityId,
            parentEntityId,
            childEntityIds,
            limits,
            rootPayloadStorage,
            out var requiredPayloadBytes,
            out var recordBytes,
            out diagnostic);
        bytesWritten = 0;

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if ((candidateStorage.Length != recordBytes) || (destination.Length != recordBytes))
        {
            throw new ArgumentException("The world-hierarchy-node record workspaces must match their measured extent.");
        }

        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[3];
        status = BuildFields(
            entityId,
            parentEntityId,
            childEntityIds,
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
            throw new InvalidOperationException("The world-hierarchy-node payload extent changed after workspace measurement.");
        }

        var source = new EbinRecordWriteView(
            s_schemaId,
            major: 1,
            minor: 0,
            fields[..(parentEntityId.HasValue ? 3 : 2)],
            rootPayloadStorage[..payloadBytes]);
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
        Span<byte> rootPayloadStorage,
        ref int payloadOffset,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = EbinPayloadWriter.Write(value, rootPayloadStorage[payloadOffset..], out var fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(
            tag,
            11,
            isRequired ? (ushort)1 : (ushort)0,
            1,
            new EbinPayloadSlice(payloadOffset, fieldBytes));
        payloadOffset = checked(payloadOffset + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus BuildFields(
        Guid entityId,
        Guid? parentEntityId,
        scoped ReadOnlySpan<Guid> childEntityIds,
        EbinReadLimits limits,
        Span<EbinFieldSource> fields,
        Span<byte> rootPayloadStorage,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        fields.Clear();
        payloadBytes = 0;
        var fieldCount = 0;
        var offset = 0;
        var status = AddGuidField(1, entityId, isRequired: true, fields, ref fieldCount, rootPayloadStorage, ref offset, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (parentEntityId is Guid parentId)
        {
            status = AddGuidField(2, parentId, isRequired: false, fields, ref fieldCount, rootPayloadStorage, ref offset, out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }
        }

        status = EbinPayloadWriter.WriteFixedArray(
            childEntityIds,
            limits,
            rootPayloadStorage[offset..],
            out var childrenPayloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(
            3,
            23,
            1,
            checked((ulong)childEntityIds.Length),
            new EbinPayloadSlice(offset, childrenPayloadBytes));
        offset = checked(offset + childrenPayloadBytes);
        payloadBytes = offset;
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
}
