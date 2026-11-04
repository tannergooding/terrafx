// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;

namespace TerraFX.Entities;

internal ref struct WorldPersistenceStrictLayoutReadBuffers
{
    internal Span<byte> ArchetypeDigestScratch;

    internal Span<WorldPersistenceStrictLayoutChunkOrdinalEntry> ChunkOrdinalScratch;

    internal Span<byte> FreeSlotBitset;

    internal static WorldPersistenceStrictLayoutReadBuffers Create(WorldPersistenceStrictLayoutReadWorkspaceRequirements workspace)
        => new WorldPersistenceStrictLayoutReadBuffers {
            ArchetypeDigestScratch = new byte[workspace.ArchetypeDigestBytes],
            ChunkOrdinalScratch = new WorldPersistenceStrictLayoutChunkOrdinalEntry[workspace.ChunkCount],
            FreeSlotBitset = new byte[workspace.FreeSlotBitsetBytes],
        };

    internal readonly long GetWorkspaceBytes()
        => checked(
            (long)ArchetypeDigestScratch.Length
            + FreeSlotBitset.Length
            + ((long)ChunkOrdinalScratch.Length * Unsafe.SizeOf<WorldPersistenceStrictLayoutChunkOrdinalEntry>()));

    internal readonly bool Matches(WorldPersistenceStrictLayoutReadWorkspaceRequirements workspace)
        => (ArchetypeDigestScratch.Length == workspace.ArchetypeDigestBytes)
        && (ChunkOrdinalScratch.Length == workspace.ChunkCount)
        && (FreeSlotBitset.Length == workspace.FreeSlotBitsetBytes)
        && (GetWorkspaceBytes() == workspace.WorkspaceBytes);
}
