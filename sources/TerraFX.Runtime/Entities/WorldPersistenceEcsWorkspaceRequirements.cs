// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;
using TerraFX;

namespace TerraFX.Entities;

internal readonly struct WorldPersistenceEcsWorkspaceRequirements
{
    internal const int MaximumEntitySchemaRecordCount = 256;

    internal WorldPersistenceEcsWorkspaceRequirements(
        int entityCount,
        int systemCount,
        int resourceCount,
        int attachmentCount,
        int entityStorageBytes,
        int systemStorageBytes,
        int resourceStorageBytes,
        int attachmentStorageBytes,
        int entityArrayPayloadBytes,
        int systemArrayPayloadBytes,
        int resourceArrayPayloadBytes,
        int attachmentArrayPayloadBytes,
        int entityComponentStorageBytes,
        int entityTagStorageBytes,
        int entityTransientSchemaStorageBytes,
        int entityChildCandidateBytes,
        int entityPayloadStorageBytes,
        int entityCandidateStorageBytes,
        int statePayloadRecordStorageBytes,
        int stateRowPayloadStorageBytes,
        int stateRowCandidateStorageBytes,
        int rootPayloadStorageBytes,
        int recordBytes,
        WorldPersistenceIdentityHistoryWorkspaceRequirements identityHistory)
    {
        AttachmentArrayPayloadBytes = attachmentArrayPayloadBytes;
        AttachmentCount = attachmentCount;
        AttachmentStorageBytes = attachmentStorageBytes;
        EntityArrayPayloadBytes = entityArrayPayloadBytes;
        EntityCandidateStorageBytes = entityCandidateStorageBytes;
        EntityChildCandidateBytes = entityChildCandidateBytes;
        EntityComponentStorageBytes = entityComponentStorageBytes;
        EntityCount = entityCount;
        EntityPayloadStorageBytes = entityPayloadStorageBytes;
        EntityStorageBytes = entityStorageBytes;
        EntityTagStorageBytes = entityTagStorageBytes;
        EntityTransientSchemaStorageBytes = entityTransientSchemaStorageBytes;
        IdentityHistory = identityHistory;
        RecordBytes = recordBytes;
        ResourceArrayPayloadBytes = resourceArrayPayloadBytes;
        ResourceCount = resourceCount;
        ResourceStorageBytes = resourceStorageBytes;
        RootPayloadStorageBytes = rootPayloadStorageBytes;
        StatePayloadRecordStorageBytes = statePayloadRecordStorageBytes;
        StateRowCandidateStorageBytes = stateRowCandidateStorageBytes;
        StateRowPayloadStorageBytes = stateRowPayloadStorageBytes;
        SystemArrayPayloadBytes = systemArrayPayloadBytes;
        SystemCount = systemCount;
        SystemStorageBytes = systemStorageBytes;
        WorkspaceBytes = GetWorkspaceBytes();
    }

    internal int AttachmentArrayPayloadBytes { get; }

    internal int AttachmentCount { get; }

    internal int AttachmentStorageBytes { get; }

    internal int EntityArrayPayloadBytes { get; }

    internal int EntityCandidateStorageBytes { get; }

    internal int EntityChildCandidateBytes { get; }

    internal int EntityComponentStorageBytes { get; }

    internal int EntityCount { get; }

    internal int EntityPayloadStorageBytes { get; }

    internal int EntityStorageBytes { get; }

    internal int EntityTagStorageBytes { get; }

    internal int EntityTransientSchemaStorageBytes { get; }

    internal WorldPersistenceIdentityHistoryWorkspaceRequirements IdentityHistory { get; }

    internal int RecordBytes { get; }

    internal int ResourceArrayPayloadBytes { get; }

    internal int ResourceCount { get; }

    internal int ResourceStorageBytes { get; }

    internal int RootPayloadStorageBytes { get; }

    internal int StatePayloadRecordStorageBytes { get; }

    internal int StateRowCandidateStorageBytes { get; }

    internal int StateRowPayloadStorageBytes { get; }

    internal int SystemArrayPayloadBytes { get; }

    internal int SystemCount { get; }

    internal int SystemStorageBytes { get; }

    internal long WorkspaceBytes { get; }

    private static long AddBytes(long bytes, int count, int elementSize)
        => checked(bytes + (checked((long)count) * elementSize));

    private long GetWorkspaceBytes()
    {
        long bytes = 0;
        bytes = AddBytes(bytes, EntityCount, Unsafe.SizeOf<World.EntityStateCaptureRow>());
        bytes = AddBytes(bytes, EntityCount, Unsafe.SizeOf<EbinPayloadSlice>());
        bytes = AddBytes(bytes, SystemCount, Unsafe.SizeOf<EbinPayloadSlice>());
        bytes = AddBytes(bytes, ResourceCount, Unsafe.SizeOf<EbinPayloadSlice>());
        bytes = AddBytes(bytes, AttachmentCount, Unsafe.SizeOf<World.AttachmentStateCaptureRow>());
        bytes = AddBytes(bytes, AttachmentCount, Unsafe.SizeOf<EbinPayloadSlice>());
        bytes = AddBytes(bytes, MaximumEntitySchemaRecordCount * 3, Unsafe.SizeOf<EbinPayloadSlice>());
        bytes = checked(bytes + EntityStorageBytes);
        bytes = checked(bytes + SystemStorageBytes);
        bytes = checked(bytes + ResourceStorageBytes);
        bytes = checked(bytes + AttachmentStorageBytes);
        bytes = checked(bytes + EntityComponentStorageBytes);
        bytes = checked(bytes + EntityTagStorageBytes);
        bytes = checked(bytes + EntityTransientSchemaStorageBytes);
        bytes = checked(bytes + EntityChildCandidateBytes);
        bytes = checked(bytes + EntityPayloadStorageBytes);
        bytes = checked(bytes + EntityCandidateStorageBytes);
        bytes = checked(bytes + StatePayloadRecordStorageBytes);
        bytes = checked(bytes + StateRowPayloadStorageBytes);
        bytes = checked(bytes + StateRowCandidateStorageBytes);
        bytes = AddBytes(bytes, IdentityHistory.BranchCount, Unsafe.SizeOf<EbinPayloadSlice>());
        bytes = checked(bytes + IdentityHistory.BranchStorageBytes);
        bytes = AddBytes(bytes, IdentityHistory.PageCount, Unsafe.SizeOf<EbinPayloadSlice>());
        bytes = checked(bytes + IdentityHistory.PageStorageBytes);

        if (IdentityHistory.PageCount > 0)
        {
            bytes = AddBytes(bytes, StableIdentityLedger.RecordsPerPage, Unsafe.SizeOf<Guid>());
            bytes = AddBytes(bytes, StableIdentityLedger.RecordsPerPage, Unsafe.SizeOf<uint>());
            bytes = checked(
                bytes
                + WorldPersistenceIdentityPageWriter.GetMinimumPayloadWorkspaceBytes(StableIdentityLedger.RecordsPerPage));
        }

        bytes = checked(bytes + IdentityHistory.ChildCandidateBytes);
        bytes = checked(bytes + IdentityHistory.RootPayloadStorageBytes);
        bytes = checked(bytes + IdentityHistory.RecordBytes);
        bytes = checked(bytes + RootPayloadStorageBytes);
        bytes = checked(bytes + checked((long)RecordBytes * 2));
        return bytes;
    }
}
