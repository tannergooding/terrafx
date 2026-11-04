// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Threading;
using TerraFX;

namespace TerraFX.Entities;

internal static class WorldPersistenceStrictLayoutWriter
{
    private const ushort ArrayKind = 24;
    private const ushort ByteArrayKind = 13;
    private const int ChunkFieldCount = 4;
    private const int ComponentLayoutFieldCount = 3;
    private const int FieldEntryBytes = 32;
    private const ushort FixedArrayKind = 23;
    private const int GuidBytes = 16;
    private const ushort GuidKind = 11;
    private const int HeaderBytes = 64;
    private const ushort Int64Kind = 8;
    private const int MaximumArchetypeSchemas = 256;
    private const int MaximumArchetypes = 4096;
    private const int MaximumChunkCapacity = 256;
    private const int MaximumComponents = 256;
    private const int MaximumSlotFields = 7;
    private const int MaximumSlotPayloadBytes = 67;
    private const int MaximumSlots = 2_097_152;
    private const ushort RecordKind = 22;
    private const int RootFieldCount = 5;
    private const ushort UInt16Kind = 2;
    private const ushort UInt32Kind = 3;
    private const ushort UInt8Kind = 1;

    private static readonly Guid s_archetypeSchemaId = new ContentSchemaIdentityAssignment("state/archetype").SchemaId;
    private static readonly Guid s_chunkSchemaId = new ContentSchemaIdentityAssignment("state/chunk").SchemaId;
    private static readonly Guid s_componentLayoutSchemaId = new ContentSchemaIdentityAssignment("state/component-layout").SchemaId;
    private static readonly Guid s_schemaId = new ContentSchemaIdentityAssignment("state/strict-layout").SchemaId;
    private static readonly Guid s_slotSchemaId = new ContentSchemaIdentityAssignment("state/slot").SchemaId;

    internal static EbinWriteStatus Measure(
        WorldPersistenceCaptureLease lease,
        WorldRegistration registration,
        EbinReadLimits limits,
        CancellationToken cancellationToken,
        out WorldPersistenceStrictLayoutWorkspaceRequirements workspace,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(limits);
        lease.ValidateCurrent();
        workspace = default;
        diagnostic = null;

        var archetypeCount = lease.StrictLayoutArchetypeCount;
        var slotCount = lease.StrictLayoutAllocatedSlotCount;
        var freeSlotCount = lease.StrictLayoutFreeSlotCount;
        var componentCount = registration.Components.Length;

        if ((archetypeCount > MaximumArchetypes)
            || (slotCount > MaximumSlots)
            || (freeSlotCount > slotCount)
            || (componentCount > MaximumComponents))
        {
            diagnostic = CreateDiagnostic("strict-layout-count", checked((ulong)Math.Max(archetypeCount, Math.Max(slotCount, componentCount))), MaximumSlots);
            return EbinWriteStatus.InvalidRecord;
        }

        if (limits.MaximumFieldsPerRecord < MaximumSlotFields)
        {
            diagnostic = CreateDiagnostic("field-count-budget", MaximumSlotFields, checked((ulong)limits.MaximumFieldsPerRecord));
            return EbinWriteStatus.InvalidRecord;
        }

        long archetypeRecordStorageBytes = 0;
        long maximumArchetypePayloadBytes = 0;
        long maximumChunkRecordStorageBytes = 0;
        var maximumChunkCount = 0;
        var maximumRowIdCount = 0;
        var maximumChildRecordBytes = 0;
        var aggregateArrayElements = checked(
            (long)archetypeCount
            + slotCount
            + freeSlotCount
            + componentCount);

        for (var archetypeIndex = 0; archetypeIndex < archetypeCount; archetypeIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var archetype = lease.GetStrictLayoutArchetypeByOrder(archetypeIndex);
            var schemaIds = archetype.SchemaIds;

            if (schemaIds.Length > MaximumArchetypeSchemas)
            {
                diagnostic = CreateDiagnostic("archetype-schema-count", checked((ulong)schemaIds.Length), MaximumArchetypeSchemas);
                return EbinWriteStatus.InvalidRecord;
            }

            aggregateArrayElements = checked(aggregateArrayElements + schemaIds.Length);
            long chunkStorageBytes = 0;
            var chunkCount = 0;

            for (var chunkIndex = 0; chunkIndex < archetype.Storage.ChunkCount; chunkIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!archetype.Storage.TryGetChunkOrdinal(chunkIndex, out var chunkOrdinal))
                {
                    continue;
                }

                var rowCount = archetype.Storage.GetChunkRowCount(chunkIndex);
                var rowCapacity = archetype.Storage.RowCapacity;

                if ((chunkOrdinal < 0)
                    || (rowCount > rowCapacity)
                    || (rowCapacity is < 1 or > MaximumChunkCapacity))
                {
                    throw new InvalidOperationException("The World contains an invalid allocated archetype chunk.");
                }

                var chunkRecordBytes = GetChunkRecordBytes(rowCount);
                chunkStorageBytes = checked(chunkStorageBytes + chunkRecordBytes);
                chunkCount = checked(chunkCount + 1);
                aggregateArrayElements = checked(aggregateArrayElements + rowCount);
                maximumRowIdCount = Math.Max(maximumRowIdCount, rowCount);
                maximumChildRecordBytes = Math.Max(maximumChildRecordBytes, chunkRecordBytes);
            }

            var chunkArrayStatus = WorldPersistenceRecordArrayLayout.MeasurePayloadBytes(
                chunkCount,
                chunkStorageBytes,
                limits,
                out var chunkArrayPayloadBytes,
                out diagnostic);

            if (chunkArrayStatus is not EbinWriteStatus.Complete)
            {
                return chunkArrayStatus;
            }

            aggregateArrayElements = checked(aggregateArrayElements + chunkCount);
            var keyPayloadBytes = checked(32L + (GuidBytes * (long)schemaIds.Length));
            var archetypePayloadBytes = checked(keyPayloadBytes + chunkArrayPayloadBytes);
            var archetypeRecordBytes = checked(HeaderBytes + (2L * FieldEntryBytes) + archetypePayloadBytes);

            if (archetypeRecordBytes > limits.MaximumRecordBytes)
            {
                diagnostic = CreateDiagnostic(
                    "record-byte-budget",
                    checked((ulong)archetypeRecordBytes),
                    checked((ulong)limits.MaximumRecordBytes));
                return EbinWriteStatus.InvalidRecord;
            }

            archetypeRecordStorageBytes = checked(archetypeRecordStorageBytes + archetypeRecordBytes);
            maximumArchetypePayloadBytes = Math.Max(maximumArchetypePayloadBytes, archetypePayloadBytes);
            maximumChunkRecordStorageBytes = Math.Max(maximumChunkRecordStorageBytes, chunkStorageBytes);
            maximumChunkCount = Math.Max(maximumChunkCount, chunkCount);
            maximumChildRecordBytes = Math.Max(maximumChildRecordBytes, checked((int)archetypeRecordBytes));
        }

        long slotRecordStorageBytes = 0;

        for (var index = 1; index <= slotCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lease.GetStrictLayoutSlot(
                index,
                out _,
                out var status,
                out _,
                out _,
                out _);
            var slotRecordBytes = GetSlotRecordBytes(status);
            slotRecordStorageBytes = checked(slotRecordStorageBytes + slotRecordBytes);
            maximumChildRecordBytes = Math.Max(maximumChildRecordBytes, slotRecordBytes);
        }

        long componentRecordStorageBytes = 0;
        var previousSchema = default(ContentSchemaReference);

        for (var index = 0; index < componentCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var component = registration.Components[index];
            var schema = component.SchemaReference;

            if ((index > 0) && (FoundationCatalogue.CompareRfc(previousSchema.SchemaId, schema.SchemaId) >= 0))
            {
                throw new InvalidOperationException("The target registration component table is not in canonical schema order.");
            }

            var schemaStatus = WorldPersistenceSchemaReferenceWriter.Measure(
                schema,
                limits,
                out var schemaRecordBytes,
                out diagnostic);

            if (schemaStatus is not EbinWriteStatus.Complete)
            {
                return schemaStatus;
            }

            var componentRecordBytes = checked(
                HeaderBytes
                + (ComponentLayoutFieldCount * FieldEntryBytes)
                + schemaRecordBytes
                + 16
                + GuidBytes);
            componentRecordStorageBytes = checked(componentRecordStorageBytes + componentRecordBytes);
            maximumChildRecordBytes = Math.Max(maximumChildRecordBytes, checked((int)componentRecordBytes));
            previousSchema = schema;
        }

        var archetypeArrayStatus = WorldPersistenceRecordArrayLayout.MeasurePayloadBytes(
            archetypeCount,
            archetypeRecordStorageBytes,
            limits,
            out var archetypeArrayPayloadBytes,
            out diagnostic);

        if (archetypeArrayStatus is not EbinWriteStatus.Complete)
        {
            return archetypeArrayStatus;
        }

        var slotArrayStatus = WorldPersistenceRecordArrayLayout.MeasurePayloadBytes(
            slotCount,
            slotRecordStorageBytes,
            limits,
            out var slotArrayPayloadBytes,
            out diagnostic);

        if (slotArrayStatus is not EbinWriteStatus.Complete)
        {
            return slotArrayStatus;
        }

        var componentArrayStatus = WorldPersistenceRecordArrayLayout.MeasurePayloadBytes(
            componentCount,
            componentRecordStorageBytes,
            limits,
            out var componentArrayPayloadBytes,
            out diagnostic);

        if (componentArrayStatus is not EbinWriteStatus.Complete)
        {
            return componentArrayStatus;
        }

        if (aggregateArrayElements > limits.MaximumAggregateArrayElements)
        {
            diagnostic = CreateDiagnostic(
                "aggregate-array-count-budget",
                checked((ulong)aggregateArrayElements),
                checked((ulong)limits.MaximumAggregateArrayElements));
            return EbinWriteStatus.InvalidRecord;
        }

        var freeSlotPayloadBytes = checked(32L + (sizeof(uint) * (long)freeSlotCount));
        var rootPayloadBytes = checked(
            (long)archetypeArrayPayloadBytes
            + slotArrayPayloadBytes
            + freeSlotPayloadBytes
            + sizeof(long)
            + componentArrayPayloadBytes);
        var alignedFreeSlotPayloadBytes = checked((freeSlotPayloadBytes + 15L) & ~15L);
        var recordBytes = checked(
            HeaderBytes
            + (RootFieldCount * FieldEntryBytes)
            + archetypeArrayPayloadBytes
            + slotArrayPayloadBytes
            + alignedFreeSlotPayloadBytes
            + 16
            + componentArrayPayloadBytes);

        if ((rootPayloadBytes > limits.MaximumRecordBytes)
            || (recordBytes > limits.MaximumRecordBytes)
            || (rootPayloadBytes > int.MaxValue)
            || (recordBytes > int.MaxValue))
        {
            diagnostic = CreateDiagnostic(
                "record-byte-budget",
                checked((ulong)recordBytes),
                checked((ulong)limits.MaximumRecordBytes));
            return EbinWriteStatus.InvalidRecord;
        }

        workspace = new WorldPersistenceStrictLayoutWorkspaceRequirements(
            archetypeCount,
            checked(archetypeCount * SHA256.HashSizeInBytes),
            slotCount,
            freeSlotCount,
            componentCount,
            maximumChunkCount,
            maximumRowIdCount,
            checked((int)archetypeRecordStorageBytes),
            checked((int)slotRecordStorageBytes),
            checked((int)componentRecordStorageBytes),
            checked((int)maximumChunkRecordStorageBytes),
            checked((int)maximumArchetypePayloadBytes),
            maximumChildRecordBytes,
            checked((int)rootPayloadBytes),
            checked((int)recordBytes));
        return EbinWriteStatus.Complete;
    }

    internal static EbinWriteStatus Write(
        WorldPersistenceCaptureLease lease,
        WorldRegistration registration,
        WorldPersistenceStrictLayoutWorkspaceRequirements workspace,
        EbinReadLimits limits,
        scoped Span<EbinPayloadSlice> archetypeSlices,
        scoped Span<EbinPayloadSlice> slotSlices,
        scoped Span<EbinPayloadSlice> componentSlices,
        scoped Span<EbinPayloadSlice> chunkSlices,
        scoped Span<byte> archetypeRecordStorage,
        scoped Span<byte> slotRecordStorage,
        scoped Span<byte> componentRecordStorage,
        scoped Span<byte> chunkRecordStorage,
        scoped Span<byte> archetypePayloadStorage,
        scoped Span<uint> freeSlotScratch,
        scoped Span<Guid> rowIdScratch,
        scoped Span<byte> archetypeDigestStorage,
        scoped Span<byte> childCandidateStorage,
        scoped Span<byte> rootPayloadStorage,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        CancellationToken cancellationToken,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(limits);
        bytesWritten = 0;
        var status = Measure(lease, registration, limits, cancellationToken, out var measuredWorkspace, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (!workspace.Matches(measuredWorkspace))
        {
            diagnostic = CreateDiagnostic("strict-layout-workspace-mismatch", 0, 1);
            return EbinWriteStatus.InvalidRecord;
        }

        ValidateBuffers(
            workspace,
            archetypeSlices,
            slotSlices,
            componentSlices,
            chunkSlices,
            archetypeRecordStorage,
            slotRecordStorage,
            componentRecordStorage,
            chunkRecordStorage,
            archetypePayloadStorage,
            freeSlotScratch,
            rowIdScratch,
            archetypeDigestStorage,
            childCandidateStorage,
            rootPayloadStorage,
            candidateStorage,
            destination);

        var archetypeStorageOffset = 0;

        for (var index = 0; index < workspace.ArchetypeCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var archetype = lease.GetStrictLayoutArchetypeByOrder(index);
            var statusForRecord = WriteArchetypeRecord(
                lease,
                archetype,
                limits,
                chunkSlices,
                chunkRecordStorage,
                archetypePayloadStorage,
                rowIdScratch,
                childCandidateStorage,
                archetypeRecordStorage[archetypeStorageOffset..],
                out var recordBytes,
                out diagnostic,
                cancellationToken);

            if (statusForRecord is not EbinWriteStatus.Complete)
            {
                return statusForRecord;
            }

            archetypeSlices[index] = new EbinPayloadSlice(archetypeStorageOffset, recordBytes);
            archetypeStorageOffset = checked(archetypeStorageOffset + recordBytes);
        }

        if (archetypeStorageOffset != workspace.ArchetypeRecordStorageBytes)
        {
            throw new InvalidOperationException("The state/strict-layout archetype extent changed after measurement.");
        }

        for (var index = 0; index < lease.StrictLayoutArchetypeCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var archetype = lease.GetStrictLayoutArchetypeByIndex(index);
            ComputeArchetypeDigest(
                archetype.SchemaIds,
                archetypeDigestStorage.Slice(index * SHA256.HashSizeInBytes, SHA256.HashSizeInBytes));
        }

        var slotStorageOffset = 0;

        for (var index = 1; index <= workspace.SlotCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var statusForRecord = WriteSlotRecord(
                lease,
                index,
                archetypeDigestStorage,
                limits,
                childCandidateStorage,
                slotRecordStorage[slotStorageOffset..],
                out var recordBytes,
                out diagnostic);

            if (statusForRecord is not EbinWriteStatus.Complete)
            {
                return statusForRecord;
            }

            slotSlices[index - 1] = new EbinPayloadSlice(slotStorageOffset, recordBytes);
            slotStorageOffset = checked(slotStorageOffset + recordBytes);
        }

        if (slotStorageOffset != workspace.SlotRecordStorageBytes)
        {
            throw new InvalidOperationException("The state/strict-layout slot extent changed after measurement.");
        }

        var componentStorageOffset = 0;

        for (var index = 0; index < workspace.ComponentCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var statusForRecord = WriteComponentLayoutRecord(
                registration.Components[index],
                limits,
                childCandidateStorage,
                componentRecordStorage[componentStorageOffset..],
                out var recordBytes,
                out diagnostic);

            if (statusForRecord is not EbinWriteStatus.Complete)
            {
                return statusForRecord;
            }

            componentSlices[index] = new EbinPayloadSlice(componentStorageOffset, recordBytes);
            componentStorageOffset = checked(componentStorageOffset + recordBytes);
        }

        if (componentStorageOffset != workspace.ComponentRecordStorageBytes)
        {
            throw new InvalidOperationException("The state/strict-layout component extent changed after measurement.");
        }

        lease.CopyStrictLayoutFreeSlots(freeSlotScratch);
        rootPayloadStorage.Clear();
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[RootFieldCount];
        var fieldCount = 0;
        var payloadOffset = 0;

        status = EbinPayloadWriter.WriteRecordArray(
            archetypeSlices,
            archetypeRecordStorage,
            limits,
            rootPayloadStorage[payloadOffset..],
            out var arrayPayloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(
            1,
            ArrayKind,
            1,
            checked((ulong)workspace.ArchetypeCount),
            new EbinPayloadSlice(payloadOffset, arrayPayloadBytes));
        payloadOffset = checked(payloadOffset + arrayPayloadBytes);

        status = EbinPayloadWriter.WriteRecordArray(
            slotSlices,
            slotRecordStorage,
            limits,
            rootPayloadStorage[payloadOffset..],
            out arrayPayloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(
            2,
            ArrayKind,
            1,
            checked((ulong)workspace.SlotCount),
            new EbinPayloadSlice(payloadOffset, arrayPayloadBytes));
        payloadOffset = checked(payloadOffset + arrayPayloadBytes);

        status = EbinPayloadWriter.WriteFixedArray(
            freeSlotScratch,
            limits,
            rootPayloadStorage[payloadOffset..],
            out var freeSlotPayloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(
            3,
            FixedArrayKind,
            1,
            checked((ulong)workspace.FreeSlotCount),
            new EbinPayloadSlice(payloadOffset, freeSlotPayloadBytes));
        payloadOffset = checked(payloadOffset + freeSlotPayloadBytes);

        status = EbinPayloadWriter.Write(
            lease.StrictLayoutNextChunkOrdinal,
            rootPayloadStorage[payloadOffset..],
            out var nextChunkOrdinalBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(
            4,
            Int64Kind,
            1,
            1,
            new EbinPayloadSlice(payloadOffset, nextChunkOrdinalBytes));
        payloadOffset = checked(payloadOffset + nextChunkOrdinalBytes);

        status = EbinPayloadWriter.WriteRecordArray(
            componentSlices,
            componentRecordStorage,
            limits,
            rootPayloadStorage[payloadOffset..],
            out arrayPayloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(
            5,
            ArrayKind,
            1,
            checked((ulong)workspace.ComponentCount),
            new EbinPayloadSlice(payloadOffset, arrayPayloadBytes));
        payloadOffset = checked(payloadOffset + arrayPayloadBytes);

        if ((fieldCount != RootFieldCount)
            || (payloadOffset != workspace.RootPayloadStorageBytes))
        {
            throw new InvalidOperationException("The state/strict-layout root extent changed after measurement.");
        }

        var source = new EbinRecordWriteView(
            s_schemaId,
            major: 1,
            minor: 0,
            fields,
            rootPayloadStorage[..payloadOffset]);
        return EbinStructuralWriter.Write(
            in source,
            limits,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }

    private static void ComputeArchetypeDigest(scoped ReadOnlySpan<Guid> schemaIds, scoped Span<byte> destination)
    {
        const int PrefixLength = 20;
        var digestInputLength = checked(PrefixLength + 1 + sizeof(uint) + (schemaIds.Length * GuidBytes));
        Span<byte> digestInput = stackalloc byte[PrefixLength + 1 + sizeof(uint) + (MaximumArchetypeSchemas * GuidBytes)];
        var prefix = "TerraFX.Archetype.v1"u8;

        if ((schemaIds.Length > MaximumArchetypeSchemas) || (destination.Length != SHA256.HashSizeInBytes))
        {
            throw new ArgumentException("The archetype digest extent does not match the canonical schema set.", nameof(schemaIds));
        }

        prefix.CopyTo(digestInput);
        digestInput[PrefixLength] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(digestInput[(PrefixLength + 1)..], checked((uint)schemaIds.Length));
        var offset = PrefixLength + 1 + sizeof(uint);

        for (var index = 0; index < schemaIds.Length; index++)
        {
            _ = schemaIds[index].TryWriteBytes(digestInput[offset..], bigEndian: true, out var guidBytes);
            ArgumentOutOfRangeException.ThrowIfNotEqual(guidBytes, GuidBytes);
            offset = checked(offset + GuidBytes);
        }

        _ = SHA256.HashData(digestInput[..digestInputLength], destination);
    }

    private static EbinStructuralDiagnostic CreateDiagnostic(string category, ulong claim, ulong limit)
        => new EbinStructuralDiagnostic(
            s_schemaId,
            0,
            category,
            null,
            claim,
            limit,
            EbinStructuralDiagnosticSource.Construction);

    private static int GetChunkRecordBytes(int rowCount)
        => checked(
            HeaderBytes
            + (ChunkFieldCount * FieldEntryBytes)
            + 16
            + 32
            + (rowCount * GuidBytes)
            + 16
            + 16);

    private static int GetSlotRecordBytes(byte status)
    {
        if (status is not 0 and not 1 and not 2)
        {
            throw new InvalidOperationException("The World contains an unknown persisted Entity slot status.");
        }

        var isLive = status == 0;
        var fieldCount = isLive ? MaximumSlotFields : 3;
        var alignedPayloadBytes = isLive ? 128 : 48;
        return checked(HeaderBytes + (fieldCount * FieldEntryBytes) + alignedPayloadBytes);
    }

    private static void ValidateBuffers(
        WorldPersistenceStrictLayoutWorkspaceRequirements workspace,
        scoped ReadOnlySpan<EbinPayloadSlice> archetypeSlices,
        scoped ReadOnlySpan<EbinPayloadSlice> slotSlices,
        scoped ReadOnlySpan<EbinPayloadSlice> componentSlices,
        scoped ReadOnlySpan<EbinPayloadSlice> chunkSlices,
        scoped ReadOnlySpan<byte> archetypeRecordStorage,
        scoped ReadOnlySpan<byte> slotRecordStorage,
        scoped ReadOnlySpan<byte> componentRecordStorage,
        scoped ReadOnlySpan<byte> chunkRecordStorage,
        scoped ReadOnlySpan<byte> archetypePayloadStorage,
        scoped ReadOnlySpan<uint> freeSlotScratch,
        scoped ReadOnlySpan<Guid> rowIdScratch,
        scoped ReadOnlySpan<byte> archetypeDigestStorage,
        scoped ReadOnlySpan<byte> childCandidateStorage,
        scoped ReadOnlySpan<byte> rootPayloadStorage,
        scoped ReadOnlySpan<byte> candidateStorage,
        scoped ReadOnlySpan<byte> destination)
    {
        if ((archetypeSlices.Length != workspace.ArchetypeCount)
            || (slotSlices.Length != workspace.SlotCount)
            || (componentSlices.Length != workspace.ComponentCount)
            || (chunkSlices.Length != workspace.ChunkSliceCount)
            || (archetypeRecordStorage.Length != workspace.ArchetypeRecordStorageBytes)
            || (slotRecordStorage.Length != workspace.SlotRecordStorageBytes)
            || (componentRecordStorage.Length != workspace.ComponentRecordStorageBytes)
            || (chunkRecordStorage.Length != workspace.ChunkRecordStorageBytes)
            || (archetypePayloadStorage.Length != workspace.ArchetypePayloadStorageBytes)
            || (freeSlotScratch.Length != workspace.FreeSlotCount)
            || (rowIdScratch.Length != workspace.RowIdScratchCount)
            || (archetypeDigestStorage.Length != workspace.ArchetypeDigestStorageBytes)
            || (childCandidateStorage.Length != workspace.ChildCandidateStorageBytes)
            || (rootPayloadStorage.Length != workspace.RootPayloadStorageBytes)
            || (candidateStorage.Length != workspace.RecordBytes)
            || (destination.Length != workspace.RecordBytes))
        {
            throw new ArgumentException("The StrictLocal layout buffers must match the measured workspace.");
        }

        if (candidateStorage.Overlaps(destination))
        {
            throw new ArgumentException("The StrictLocal layout candidate and destination must not overlap.");
        }
    }

    private static EbinWriteStatus WriteArchetypeRecord(
        WorldPersistenceCaptureLease lease,
        WorldArchetype archetype,
        EbinReadLimits limits,
        scoped Span<EbinPayloadSlice> chunkSlices,
        scoped Span<byte> chunkRecordStorage,
        scoped Span<byte> archetypePayloadStorage,
        scoped Span<Guid> rowIdScratch,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        var storage = archetype.Storage;
        var chunkCount = 0;
        var chunkStorageOffset = 0;
        var previousOrdinal = -1L;

        for (var chunkIndex = 0; chunkIndex < storage.ChunkCount; chunkIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!storage.TryGetChunkOrdinal(chunkIndex, out var chunkOrdinal))
            {
                continue;
            }

            if (chunkOrdinal <= previousOrdinal)
            {
                throw new InvalidOperationException("The archetype chunk directory is not in increasing ordinal order.");
            }

            var chunkStatus = WriteChunkRecord(
                lease,
                storage,
                chunkIndex,
                chunkOrdinal,
                limits,
                rowIdScratch,
                candidateStorage,
                chunkRecordStorage[chunkStorageOffset..],
                out var chunkBytesWritten,
                out diagnostic,
                cancellationToken);

            if (chunkStatus is not EbinWriteStatus.Complete)
            {
                bytesWritten = 0;
                return chunkStatus;
            }

            chunkSlices[chunkCount++] = new EbinPayloadSlice(chunkStorageOffset, chunkBytesWritten);
            chunkStorageOffset = checked(chunkStorageOffset + chunkBytesWritten);
            previousOrdinal = chunkOrdinal;
        }

        var status = EbinPayloadWriter.WriteFixedArray(
            archetype.SchemaIds,
            limits,
            archetypePayloadStorage,
            out var keyBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            bytesWritten = 0;
            return status;
        }

        var chunksStatus = EbinPayloadWriter.WriteRecordArray(
            chunkSlices[..chunkCount],
            chunkRecordStorage[..chunkStorageOffset],
            limits,
            archetypePayloadStorage[keyBytes..],
            out var chunksBytes,
            out diagnostic);

        if (chunksStatus is not EbinWriteStatus.Complete)
        {
            bytesWritten = 0;
            return chunksStatus;
        }

        Span<EbinFieldSource> fields = [default, default];
        fields[0] = new EbinFieldSource(
            1,
            FixedArrayKind,
            1,
            checked((ulong)archetype.SchemaIds.Length),
            new EbinPayloadSlice(0, keyBytes));
        fields[1] = new EbinFieldSource(
            2,
            ArrayKind,
            1,
            checked((ulong)chunkCount),
            new EbinPayloadSlice(keyBytes, chunksBytes));
        var payloadOffset = checked(keyBytes + chunksBytes);
        var source = new EbinRecordWriteView(
            s_archetypeSchemaId,
            major: 1,
            minor: 0,
            fields,
            archetypePayloadStorage[..payloadOffset]);
        return WriteChildRecord(
            in source,
            limits,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }

    private static EbinWriteStatus WriteChildRecord(
        in EbinRecordWriteView source,
        EbinReadLimits limits,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = EbinStructuralWriter.Measure(in source, limits, out var recordBytes, out diagnostic);
        bytesWritten = 0;

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if ((candidateStorage.Length < recordBytes) || (destination.Length < recordBytes))
        {
            diagnostic = CreateDiagnostic(
                "strict-layout-child-workspace",
                checked((ulong)Math.Min(candidateStorage.Length, destination.Length)),
                checked((ulong)recordBytes));
            return EbinWriteStatus.WorkspaceTooSmall;
        }

        return EbinStructuralWriter.Write(
            in source,
            limits,
            candidateStorage[..recordBytes],
            destination[..recordBytes],
            out bytesWritten,
            out diagnostic);
    }

    private static EbinWriteStatus WriteChunkRecord(
        WorldPersistenceCaptureLease lease,
        EntityArchetypeStorage storage,
        int chunkIndex,
        long chunkOrdinal,
        EbinReadLimits limits,
        scoped Span<Guid> rowIdScratch,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        var rowCount = storage.GetChunkRowCount(chunkIndex);

        if (rowIdScratch.Length < rowCount)
        {
            throw new ArgumentException("The strict-layout row identity scratch is too small.", nameof(rowIdScratch));
        }

        for (var row = 0; row < rowCount; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rowIdScratch[row] = lease.GetStrictLayoutEntityId(storage.GetChunkEntity(chunkIndex, row));
        }

        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[ChunkFieldCount];
        Span<byte> payloadStorage = stackalloc byte[(MaximumChunkCapacity * GuidBytes) + 46];
        fields.Clear();
        var payloadOffset = 0;
        var status = WriteInt64Field(1, chunkOrdinal, fields, ref payloadOffset, payloadStorage, out var field, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            bytesWritten = 0;
            return status;
        }

        fields[0] = field;
        status = EbinPayloadWriter.WriteFixedArray(
            rowIdScratch[..rowCount],
            limits,
            payloadStorage[payloadOffset..],
            out var rowArrayBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            bytesWritten = 0;
            return status;
        }

        fields[1] = new EbinFieldSource(
            2,
            FixedArrayKind,
            1,
            checked((ulong)rowCount),
            new EbinPayloadSlice(payloadOffset, rowArrayBytes));
        payloadOffset = checked(payloadOffset + rowArrayBytes);
        status = WriteUInt16Field(
            3,
            checked((ushort)rowCount),
            fields,
            ref payloadOffset,
            payloadStorage,
            out field,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            bytesWritten = 0;
            return status;
        }

        fields[2] = field;
        status = WriteUInt32Field(
            4,
            checked((uint)storage.RowCapacity),
            fields,
            ref payloadOffset,
            payloadStorage,
            out field,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            bytesWritten = 0;
            return status;
        }

        fields[3] = field;
        var source = new EbinRecordWriteView(
            s_chunkSchemaId,
            major: 1,
            minor: 0,
            fields,
            payloadStorage[..payloadOffset]);
        return WriteChildRecord(
            in source,
            limits,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }

    private static EbinWriteStatus WriteComponentLayoutRecord(
        IComponentRegistration registration,
        EbinReadLimits limits,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var schemaStatus = WorldPersistenceSchemaReferenceWriter.Measure(
            registration.SchemaReference,
            limits,
            out var schemaRecordBytes,
            out diagnostic);

        if (schemaStatus is not EbinWriteStatus.Complete)
        {
            bytesWritten = 0;
            return schemaStatus;
        }

        Span<byte> schemaCandidate = stackalloc byte[schemaRecordBytes];
        Span<byte> schemaRecord = stackalloc byte[schemaRecordBytes];
        schemaStatus = WorldPersistenceSchemaReferenceWriter.Write(
            registration.SchemaReference,
            limits,
            schemaCandidate,
            schemaRecord,
            out var schemaBytesWritten,
            out diagnostic);

        if (schemaStatus is not EbinWriteStatus.Complete)
        {
            bytesWritten = 0;
            return schemaStatus;
        }

        if (schemaBytesWritten != schemaRecordBytes)
        {
            throw new InvalidOperationException("The schema-reference extent changed after measurement.");
        }

        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[ComponentLayoutFieldCount];
        Span<byte> payloadStorage = stackalloc byte[schemaRecordBytes + sizeof(uint) + GuidBytes];
        schemaRecord.CopyTo(payloadStorage);
        fields[0] = new EbinFieldSource(1, RecordKind, 1, 1, new EbinPayloadSlice(0, schemaRecordBytes));
        var payloadOffset = schemaRecordBytes;
        var fieldStatus = WriteUInt32Field(
            2,
            checked((uint)registration.Layout.LogicalBytes),
            fields,
            ref payloadOffset,
            payloadStorage,
            out var field,
            out diagnostic);

        if (fieldStatus is not EbinWriteStatus.Complete)
        {
            bytesWritten = 0;
            return fieldStatus;
        }

        fields[1] = field;
        fieldStatus = WriteGuidField(
            3,
            registration.Layout.LayoutId,
            isRequired: true,
            fields,
            ref payloadOffset,
            payloadStorage,
            out field,
            out diagnostic);

        if (fieldStatus is not EbinWriteStatus.Complete)
        {
            bytesWritten = 0;
            return fieldStatus;
        }

        fields[2] = field;
        var source = new EbinRecordWriteView(
            s_componentLayoutSchemaId,
            major: 1,
            minor: 0,
            fields,
            payloadStorage[..payloadOffset]);
        return WriteChildRecord(
            in source,
            limits,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }

    private static EbinWriteStatus WriteGuidField(
        uint tag,
        Guid value,
        bool isRequired,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        ref int payloadOffset,
        Span<byte> payloadStorage,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = WriteGuidField(
            tag,
            value,
            isRequired,
            fields,
            ref payloadOffset,
            payloadStorage,
            out var field,
            out diagnostic);

        if (status is EbinWriteStatus.Complete)
        {
            fields[fieldCount++] = field;
        }

        return status;
    }

    private static EbinWriteStatus WriteGuidField(
        uint tag,
        Guid value,
        bool isRequired,
        Span<EbinFieldSource> fields,
        ref int payloadOffset,
        Span<byte> payloadStorage,
        out EbinFieldSource field,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = EbinPayloadWriter.Write(value, payloadStorage[payloadOffset..], out var fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            field = default;
            return status;
        }

        field = new EbinFieldSource(
            tag,
            GuidKind,
            isRequired ? (ushort)1 : (ushort)0,
            1,
            new EbinPayloadSlice(payloadOffset, fieldBytes));
        payloadOffset = checked(payloadOffset + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus WriteInt64Field(
        uint tag,
        long value,
        Span<EbinFieldSource> fields,
        ref int payloadOffset,
        Span<byte> payloadStorage,
        out EbinFieldSource field,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = EbinPayloadWriter.Write(value, payloadStorage[payloadOffset..], out var fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            field = default;
            return status;
        }

        field = new EbinFieldSource(tag, Int64Kind, 1, 1, new EbinPayloadSlice(payloadOffset, fieldBytes));
        payloadOffset = checked(payloadOffset + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus WriteInt64Field(
        uint tag,
        long value,
        bool isRequired,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        ref int payloadOffset,
        Span<byte> payloadStorage,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = EbinPayloadWriter.Write(value, payloadStorage[payloadOffset..], out var fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(
            tag,
            Int64Kind,
            isRequired ? (ushort)1 : (ushort)0,
            1,
            new EbinPayloadSlice(payloadOffset, fieldBytes));
        payloadOffset = checked(payloadOffset + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus WriteSlotRecord(
        WorldPersistenceCaptureLease lease,
        int slotIndex,
        scoped ReadOnlySpan<byte> archetypeDigestStorage,
        EbinReadLimits limits,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        lease.GetStrictLayoutSlot(
            slotIndex,
            out var generation,
            out var status,
            out var archetypeIndex,
            out var chunkOrdinal,
            out var row);

        if (status is not 0 and not 1 and not 2)
        {
            throw new InvalidOperationException("The World contains an unknown persisted Entity slot status.");
        }

        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[MaximumSlotFields];
        Span<byte> payloadStorage = stackalloc byte[MaximumSlotPayloadBytes];
        fields.Clear();
        var fieldCount = 0;
        var payloadOffset = 0;
        var fieldStatus = WriteUInt32Field(1, checked((uint)slotIndex), fields, ref payloadOffset, payloadStorage, out var field, out diagnostic);

        if (fieldStatus is not EbinWriteStatus.Complete)
        {
            bytesWritten = 0;
            return fieldStatus;
        }

        fields[fieldCount++] = field;
        fieldStatus = WriteUInt32Field(2, generation, fields, ref payloadOffset, payloadStorage, out field, out diagnostic);

        if (fieldStatus is not EbinWriteStatus.Complete)
        {
            bytesWritten = 0;
            return fieldStatus;
        }

        fields[fieldCount++] = field;
        fieldStatus = WriteUInt8Field(3, status, fields, ref payloadOffset, payloadStorage, out field, out diagnostic);

        if (fieldStatus is not EbinWriteStatus.Complete)
        {
            bytesWritten = 0;
            return fieldStatus;
        }

        fields[fieldCount++] = field;

        if (status == 0)
        {
            if ((uint)archetypeIndex >= (uint)lease.StrictLayoutArchetypeCount)
            {
                throw new InvalidOperationException("A live slot references an absent archetype.");
            }

            var digestOffset = checked(archetypeIndex * SHA256.HashSizeInBytes);

            if (digestOffset > (archetypeDigestStorage.Length - SHA256.HashSizeInBytes))
            {
                throw new ArgumentException("The archetype digest scratch is too small.", nameof(archetypeDigestStorage));
            }

            var entity = new Entity(lease.OwnerId, checked((uint)slotIndex), generation);
            fieldStatus = WriteGuidField(
                4,
                lease.GetStrictLayoutEntityId(entity),
                isRequired: false,
                fields,
                ref fieldCount,
                ref payloadOffset,
                payloadStorage,
                out diagnostic);

            if (fieldStatus is not EbinWriteStatus.Complete)
            {
                bytesWritten = 0;
                return fieldStatus;
            }

            var digest = archetypeDigestStorage.Slice(digestOffset, SHA256.HashSizeInBytes);
            digest.CopyTo(payloadStorage[payloadOffset..]);
            fields[fieldCount++] = new EbinFieldSource(
                5,
                ByteArrayKind,
                0,
                SHA256.HashSizeInBytes,
                new EbinPayloadSlice(payloadOffset, SHA256.HashSizeInBytes));
            payloadOffset = checked(payloadOffset + SHA256.HashSizeInBytes);
            fieldStatus = WriteInt64Field(
                6,
                chunkOrdinal,
                isRequired: false,
                fields,
                ref fieldCount,
                ref payloadOffset,
                payloadStorage,
                out diagnostic);

            if (fieldStatus is not EbinWriteStatus.Complete)
            {
                bytesWritten = 0;
                return fieldStatus;
            }

            fieldStatus = WriteUInt16Field(
                7,
                checked((ushort)row),
                isRequired: false,
                fields,
                ref fieldCount,
                ref payloadOffset,
                payloadStorage,
                out diagnostic);

            if (fieldStatus is not EbinWriteStatus.Complete)
            {
                bytesWritten = 0;
                return fieldStatus;
            }
        }

        var source = new EbinRecordWriteView(
            s_slotSchemaId,
            major: 1,
            minor: 0,
            fields[..fieldCount],
            payloadStorage[..payloadOffset]);
        return WriteChildRecord(
            in source,
            limits,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }

    private static EbinWriteStatus WriteUInt16Field(
        uint tag,
        ushort value,
        Span<EbinFieldSource> fields,
        ref int payloadOffset,
        Span<byte> payloadStorage,
        out EbinFieldSource field,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = EbinPayloadWriter.Write(value, payloadStorage[payloadOffset..], out var fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            field = default;
            return status;
        }

        field = new EbinFieldSource(tag, UInt16Kind, 1, 1, new EbinPayloadSlice(payloadOffset, fieldBytes));
        payloadOffset = checked(payloadOffset + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus WriteUInt16Field(
        uint tag,
        ushort value,
        bool isRequired,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        ref int payloadOffset,
        Span<byte> payloadStorage,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = EbinPayloadWriter.Write(value, payloadStorage[payloadOffset..], out var fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(
            tag,
            UInt16Kind,
            isRequired ? (ushort)1 : (ushort)0,
            1,
            new EbinPayloadSlice(payloadOffset, fieldBytes));
        payloadOffset = checked(payloadOffset + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus WriteUInt32Field(
        uint tag,
        uint value,
        Span<EbinFieldSource> fields,
        ref int payloadOffset,
        Span<byte> payloadStorage,
        out EbinFieldSource field,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = EbinPayloadWriter.Write(value, payloadStorage[payloadOffset..], out var fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            field = default;
            return status;
        }

        field = new EbinFieldSource(tag, UInt32Kind, 1, 1, new EbinPayloadSlice(payloadOffset, fieldBytes));
        payloadOffset = checked(payloadOffset + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus WriteUInt8Field(
        uint tag,
        byte value,
        Span<EbinFieldSource> fields,
        ref int payloadOffset,
        Span<byte> payloadStorage,
        out EbinFieldSource field,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = EbinPayloadWriter.Write(value, payloadStorage[payloadOffset..], out var fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            field = default;
            return status;
        }

        field = new EbinFieldSource(tag, UInt8Kind, 1, 1, new EbinPayloadSlice(payloadOffset, fieldBytes));
        payloadOffset = checked(payloadOffset + fieldBytes);
        return EbinWriteStatus.Complete;
    }
}
