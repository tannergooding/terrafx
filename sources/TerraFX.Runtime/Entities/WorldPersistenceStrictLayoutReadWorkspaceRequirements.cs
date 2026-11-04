// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;

namespace TerraFX.Entities;

internal readonly struct WorldPersistenceStrictLayoutReadWorkspaceRequirements
{
    internal WorldPersistenceStrictLayoutReadWorkspaceRequirements(int archetypeCount, int slotCount, int chunkCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(archetypeCount);
        ArgumentOutOfRangeException.ThrowIfNegative(slotCount);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkCount);
        ArchetypeCount = archetypeCount;
        SlotCount = slotCount;
        ChunkCount = chunkCount;
        ArchetypeDigestBytes = checked(archetypeCount * 32);
        FreeSlotBitsetBytes = checked((slotCount + 7) / 8);
        WorkspaceBytes = checked(
            ArchetypeDigestBytes
            + FreeSlotBitsetBytes
            + (Unsafe.SizeOf<WorldPersistenceStrictLayoutChunkOrdinalEntry>() * (long)chunkCount));
    }

    internal int ArchetypeCount { get; }

    internal int ArchetypeDigestBytes { get; }

    internal int ChunkCount { get; }

    internal int FreeSlotBitsetBytes { get; }

    internal int SlotCount { get; }

    internal long WorkspaceBytes { get; }

    internal bool Matches(WorldPersistenceStrictLayoutReadWorkspaceRequirements other)
        => (ArchetypeCount == other.ArchetypeCount)
        && (ArchetypeDigestBytes == other.ArchetypeDigestBytes)
        && (ChunkCount == other.ChunkCount)
        && (FreeSlotBitsetBytes == other.FreeSlotBitsetBytes)
        && (SlotCount == other.SlotCount)
        && (WorkspaceBytes == other.WorkspaceBytes);
}
