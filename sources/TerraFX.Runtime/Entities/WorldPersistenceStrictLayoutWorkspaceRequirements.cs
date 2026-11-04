// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;

namespace TerraFX.Entities;

internal readonly struct WorldPersistenceStrictLayoutWorkspaceRequirements
{
    internal WorldPersistenceStrictLayoutWorkspaceRequirements(
        int archetypeCount,
        int archetypeDigestStorageBytes,
        int slotCount,
        int freeSlotCount,
        int componentCount,
        int chunkSliceCount,
        int rowIdScratchCount,
        int archetypeRecordStorageBytes,
        int slotRecordStorageBytes,
        int componentRecordStorageBytes,
        int chunkRecordStorageBytes,
        int archetypePayloadStorageBytes,
        int childCandidateStorageBytes,
        int rootPayloadStorageBytes,
        int recordBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(archetypeCount);
        ArgumentOutOfRangeException.ThrowIfNegative(archetypeDigestStorageBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(slotCount);
        ArgumentOutOfRangeException.ThrowIfNegative(freeSlotCount);
        ArgumentOutOfRangeException.ThrowIfNegative(componentCount);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkSliceCount);
        ArgumentOutOfRangeException.ThrowIfNegative(rowIdScratchCount);
        ArgumentOutOfRangeException.ThrowIfNegative(archetypeRecordStorageBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(slotRecordStorageBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(componentRecordStorageBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkRecordStorageBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(archetypePayloadStorageBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(childCandidateStorageBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rootPayloadStorageBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(recordBytes);
        ArchetypeCount = archetypeCount;
        ArchetypeDigestStorageBytes = archetypeDigestStorageBytes;
        SlotCount = slotCount;
        FreeSlotCount = freeSlotCount;
        ComponentCount = componentCount;
        ChunkSliceCount = chunkSliceCount;
        RowIdScratchCount = rowIdScratchCount;
        ArchetypeRecordStorageBytes = archetypeRecordStorageBytes;
        SlotRecordStorageBytes = slotRecordStorageBytes;
        ComponentRecordStorageBytes = componentRecordStorageBytes;
        ChunkRecordStorageBytes = chunkRecordStorageBytes;
        ArchetypePayloadStorageBytes = archetypePayloadStorageBytes;
        ChildCandidateStorageBytes = childCandidateStorageBytes;
        RootPayloadStorageBytes = rootPayloadStorageBytes;
        RecordBytes = recordBytes;
        WorkspaceBytes = checked(
            (Unsafe.SizeOf<EbinPayloadSlice>() * (archetypeCount + (long)slotCount + componentCount + chunkSliceCount))
            + archetypeDigestStorageBytes
            + archetypeRecordStorageBytes
            + slotRecordStorageBytes
            + componentRecordStorageBytes
            + chunkRecordStorageBytes
            + archetypePayloadStorageBytes
            + childCandidateStorageBytes
            + (4L * freeSlotCount)
            + (16L * rowIdScratchCount)
            + rootPayloadStorageBytes
            + (2L * recordBytes));
    }

    internal int ArchetypeCount { get; }

    internal int ArchetypeDigestStorageBytes { get; }

    internal int ArchetypePayloadStorageBytes { get; }

    internal int ArchetypeRecordStorageBytes { get; }

    internal int ChildCandidateStorageBytes { get; }

    internal int ChunkRecordStorageBytes { get; }

    internal int ChunkSliceCount { get; }

    internal int ComponentCount { get; }

    internal int ComponentRecordStorageBytes { get; }

    internal int FreeSlotCount { get; }

    internal int RecordBytes { get; }

    internal int RootPayloadStorageBytes { get; }

    internal int RowIdScratchCount { get; }

    internal int SlotCount { get; }

    internal int SlotRecordStorageBytes { get; }

    internal long WorkspaceBytes { get; }

    internal bool Matches(WorldPersistenceStrictLayoutWorkspaceRequirements other)
        => (ArchetypeCount == other.ArchetypeCount)
        && (ArchetypeDigestStorageBytes == other.ArchetypeDigestStorageBytes)
        && (ArchetypePayloadStorageBytes == other.ArchetypePayloadStorageBytes)
        && (ArchetypeRecordStorageBytes == other.ArchetypeRecordStorageBytes)
        && (ChildCandidateStorageBytes == other.ChildCandidateStorageBytes)
        && (ChunkRecordStorageBytes == other.ChunkRecordStorageBytes)
        && (ChunkSliceCount == other.ChunkSliceCount)
        && (ComponentCount == other.ComponentCount)
        && (ComponentRecordStorageBytes == other.ComponentRecordStorageBytes)
        && (FreeSlotCount == other.FreeSlotCount)
        && (RecordBytes == other.RecordBytes)
        && (RootPayloadStorageBytes == other.RootPayloadStorageBytes)
        && (RowIdScratchCount == other.RowIdScratchCount)
        && (SlotCount == other.SlotCount)
        && (SlotRecordStorageBytes == other.SlotRecordStorageBytes)
        && (WorkspaceBytes == other.WorkspaceBytes);
}
