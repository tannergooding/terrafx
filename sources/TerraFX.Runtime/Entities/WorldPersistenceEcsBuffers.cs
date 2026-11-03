// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;
using TerraFX;

namespace TerraFX.Entities;

internal ref struct WorldPersistenceEcsBuffers
{
    internal Span<World.AttachmentStateCaptureRow> AttachmentRows;

    internal Span<EbinPayloadSlice> AttachmentSlices;

    internal Span<byte> AttachmentStorage;

    internal Span<byte> Destination;

    internal Span<byte> EcsCandidateStorage;

    internal Span<byte> EntityCandidateStorage;

    internal Span<byte> EntityChildCandidateStorage;

    internal Span<EbinPayloadSlice> EntityComponentSlices;

    internal Span<byte> EntityComponentStorage;

    internal Span<byte> EntityPayloadStorage;

    internal Span<World.EntityStateCaptureRow> EntityRows;

    internal Span<EbinPayloadSlice> EntitySlices;

    internal Span<byte> EntityStorage;

    internal Span<EbinPayloadSlice> EntityTagSlices;

    internal Span<byte> EntityTagStorage;

    internal Span<EbinPayloadSlice> EntityTransientSchemaSlices;

    internal Span<byte> EntityTransientSchemaStorage;

    internal Span<EbinPayloadSlice> IdentityBranchSlices;

    internal Span<byte> IdentityBranchStorage;

    internal Span<byte> IdentityCandidateStorage;

    internal Span<byte> IdentityChildCandidateStorage;

    internal Span<uint> IdentityPageMetadata;

    internal Span<byte> IdentityPagePayloadStorage;

    internal Span<EbinPayloadSlice> IdentityPageSlices;

    internal Span<Guid> IdentityPageStableIds;

    internal Span<byte> IdentityPageStorage;

    internal Span<byte> IdentityRootPayloadStorage;

    internal Span<EbinPayloadSlice> ResourceSlices;

    internal Span<byte> ResourceStorage;

    internal Span<byte> RootPayloadStorage;

    internal Span<byte> StatePayloadRecordStorage;

    internal Span<byte> StateRowCandidateStorage;

    internal Span<byte> StateRowPayloadStorage;

    internal Span<EbinPayloadSlice> SystemSlices;

    internal Span<byte> SystemStorage;

    private static long AddBytes(long bytes, int count, int elementSize)
        => checked(bytes + (checked((long)count) * elementSize));

    internal readonly long GetWorkspaceBytes()
    {
        long bytes = 0;
        bytes = AddBytes(bytes, AttachmentRows.Length, Unsafe.SizeOf<World.AttachmentStateCaptureRow>());
        bytes = AddBytes(bytes, AttachmentSlices.Length, Unsafe.SizeOf<EbinPayloadSlice>());
        bytes = checked(bytes + AttachmentStorage.Length);
        bytes = checked(bytes + Destination.Length);
        bytes = checked(bytes + EcsCandidateStorage.Length);
        bytes = checked(bytes + EntityCandidateStorage.Length);
        bytes = checked(bytes + EntityChildCandidateStorage.Length);
        bytes = AddBytes(bytes, EntityComponentSlices.Length, Unsafe.SizeOf<EbinPayloadSlice>());
        bytes = checked(bytes + EntityComponentStorage.Length);
        bytes = checked(bytes + EntityPayloadStorage.Length);
        bytes = AddBytes(bytes, EntityRows.Length, Unsafe.SizeOf<World.EntityStateCaptureRow>());
        bytes = AddBytes(bytes, EntitySlices.Length, Unsafe.SizeOf<EbinPayloadSlice>());
        bytes = checked(bytes + EntityStorage.Length);
        bytes = AddBytes(bytes, EntityTagSlices.Length, Unsafe.SizeOf<EbinPayloadSlice>());
        bytes = checked(bytes + EntityTagStorage.Length);
        bytes = AddBytes(bytes, EntityTransientSchemaSlices.Length, Unsafe.SizeOf<EbinPayloadSlice>());
        bytes = checked(bytes + EntityTransientSchemaStorage.Length);
        bytes = AddBytes(bytes, IdentityBranchSlices.Length, Unsafe.SizeOf<EbinPayloadSlice>());
        bytes = checked(bytes + IdentityBranchStorage.Length);
        bytes = checked(bytes + IdentityCandidateStorage.Length);
        bytes = checked(bytes + IdentityChildCandidateStorage.Length);
        bytes = AddBytes(bytes, IdentityPageMetadata.Length, Unsafe.SizeOf<uint>());
        bytes = checked(bytes + IdentityPagePayloadStorage.Length);
        bytes = AddBytes(bytes, IdentityPageSlices.Length, Unsafe.SizeOf<EbinPayloadSlice>());
        bytes = AddBytes(bytes, IdentityPageStableIds.Length, Unsafe.SizeOf<Guid>());
        bytes = checked(bytes + IdentityPageStorage.Length);
        bytes = checked(bytes + IdentityRootPayloadStorage.Length);
        bytes = checked(bytes + ResourceStorage.Length);
        bytes = AddBytes(bytes, ResourceSlices.Length, Unsafe.SizeOf<EbinPayloadSlice>());
        bytes = checked(bytes + RootPayloadStorage.Length);
        bytes = checked(bytes + StatePayloadRecordStorage.Length);
        bytes = checked(bytes + StateRowCandidateStorage.Length);
        bytes = checked(bytes + StateRowPayloadStorage.Length);
        bytes = AddBytes(bytes, SystemSlices.Length, Unsafe.SizeOf<EbinPayloadSlice>());
        bytes = checked(bytes + SystemStorage.Length);
        return bytes;
    }
}
