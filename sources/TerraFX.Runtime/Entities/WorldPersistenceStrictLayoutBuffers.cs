// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;
using TerraFX;

namespace TerraFX.Entities;

internal ref struct WorldPersistenceStrictLayoutBuffers
{
    internal Span<byte> ArchetypeDigestStorage;

    internal Span<byte> ArchetypePayloadStorage;

    internal Span<byte> ArchetypeRecordStorage;

    internal Span<EbinPayloadSlice> ArchetypeSlices;

    internal Span<byte> CandidateStorage;

    internal Span<byte> ChildCandidateStorage;

    internal Span<byte> ChunkRecordStorage;

    internal Span<EbinPayloadSlice> ChunkSlices;

    internal Span<byte> ComponentRecordStorage;

    internal Span<EbinPayloadSlice> ComponentSlices;

    internal Span<byte> Destination;

    internal Span<uint> FreeSlotScratch;

    internal Span<byte> RootPayloadStorage;

    internal Span<Guid> RowIdScratch;

    internal Span<byte> SlotRecordStorage;

    internal Span<EbinPayloadSlice> SlotSlices;

    internal static WorldPersistenceStrictLayoutBuffers Create(WorldPersistenceStrictLayoutWorkspaceRequirements workspace)
        => new WorldPersistenceStrictLayoutBuffers {
            ArchetypeDigestStorage = new byte[workspace.ArchetypeDigestStorageBytes],
            ArchetypePayloadStorage = new byte[workspace.ArchetypePayloadStorageBytes],
            ArchetypeRecordStorage = new byte[workspace.ArchetypeRecordStorageBytes],
            ArchetypeSlices = new EbinPayloadSlice[workspace.ArchetypeCount],
            CandidateStorage = new byte[workspace.RecordBytes],
            ChildCandidateStorage = new byte[workspace.ChildCandidateStorageBytes],
            ChunkRecordStorage = new byte[workspace.ChunkRecordStorageBytes],
            ChunkSlices = new EbinPayloadSlice[workspace.ChunkSliceCount],
            ComponentRecordStorage = new byte[workspace.ComponentRecordStorageBytes],
            ComponentSlices = new EbinPayloadSlice[workspace.ComponentCount],
            Destination = new byte[workspace.RecordBytes],
            FreeSlotScratch = new uint[workspace.FreeSlotCount],
            RootPayloadStorage = new byte[workspace.RootPayloadStorageBytes],
            RowIdScratch = new Guid[workspace.RowIdScratchCount],
            SlotRecordStorage = new byte[workspace.SlotRecordStorageBytes],
            SlotSlices = new EbinPayloadSlice[workspace.SlotCount],
        };

    internal readonly long GetWorkspaceBytes()
    {
        long bytes = 0;
        bytes = checked(bytes + ArchetypeDigestStorage.Length);
        bytes = checked(bytes + ArchetypePayloadStorage.Length);
        bytes = checked(bytes + ArchetypeRecordStorage.Length);
        bytes = checked(bytes + checked((long)ArchetypeSlices.Length * Unsafe.SizeOf<EbinPayloadSlice>()));
        bytes = checked(bytes + CandidateStorage.Length);
        bytes = checked(bytes + ChildCandidateStorage.Length);
        bytes = checked(bytes + ChunkRecordStorage.Length);
        bytes = checked(bytes + checked((long)ChunkSlices.Length * Unsafe.SizeOf<EbinPayloadSlice>()));
        bytes = checked(bytes + ComponentRecordStorage.Length);
        bytes = checked(bytes + checked((long)ComponentSlices.Length * Unsafe.SizeOf<EbinPayloadSlice>()));
        bytes = checked(bytes + Destination.Length);
        bytes = checked(bytes + checked((long)FreeSlotScratch.Length * sizeof(uint)));
        bytes = checked(bytes + RootPayloadStorage.Length);
        bytes = checked(bytes + checked((long)RowIdScratch.Length * Unsafe.SizeOf<Guid>()));
        bytes = checked(bytes + SlotRecordStorage.Length);
        bytes = checked(bytes + checked((long)SlotSlices.Length * Unsafe.SizeOf<EbinPayloadSlice>()));
        return bytes;
    }
}
