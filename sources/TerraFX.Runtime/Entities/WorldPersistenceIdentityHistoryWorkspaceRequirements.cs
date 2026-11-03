// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

internal readonly struct WorldPersistenceIdentityHistoryWorkspaceRequirements
{
    internal WorldPersistenceIdentityHistoryWorkspaceRequirements(
        int branchCount,
        int pageCount,
        int branchStorageBytes,
        int pageStorageBytes,
        int branchArrayPayloadBytes,
        int pageArrayPayloadBytes,
        int childCandidateBytes,
        int rootPayloadStorageBytes,
        int recordBytes)
    {
        BranchArrayPayloadBytes = branchArrayPayloadBytes;
        BranchCount = branchCount;
        BranchStorageBytes = branchStorageBytes;
        ChildCandidateBytes = childCandidateBytes;
        PageArrayPayloadBytes = pageArrayPayloadBytes;
        PageCount = pageCount;
        PageStorageBytes = pageStorageBytes;
        RecordBytes = recordBytes;
        RootPayloadStorageBytes = rootPayloadStorageBytes;
    }

    internal int BranchArrayPayloadBytes { get; }

    internal int BranchCount { get; }

    internal int BranchStorageBytes { get; }

    internal int ChildCandidateBytes { get; }

    internal int PageArrayPayloadBytes { get; }

    internal int PageCount { get; }

    internal int PageStorageBytes { get; }

    internal int RecordBytes { get; }

    internal int RootPayloadStorageBytes { get; }
}
