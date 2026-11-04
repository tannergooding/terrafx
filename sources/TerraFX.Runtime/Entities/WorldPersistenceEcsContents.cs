// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal sealed class WorldPersistenceEcsContents
{
    internal WorldPersistenceEcsContents(
        int entityCount,
        int systemCount,
        int resourceCount,
        int attachmentCount,
        int liveEntityCount,
        int liveSceneInstanceCount,
        Guid worldGuid,
        Guid replayLineageId,
        Guid activeBranchId,
        Guid issuanceNamespace,
        long nextAssignmentOrdinal,
        int identityBranchCount,
        int identityPageCount)
    {
        EntityCount = entityCount;
        SystemCount = systemCount;
        ResourceCount = resourceCount;
        AttachmentCount = attachmentCount;
        LiveEntityCount = liveEntityCount;
        LiveSceneInstanceCount = liveSceneInstanceCount;
        WorldGuid = worldGuid;
        ReplayLineageId = replayLineageId;
        ActiveBranchId = activeBranchId;
        IssuanceNamespace = issuanceNamespace;
        NextAssignmentOrdinal = nextAssignmentOrdinal;
        IdentityBranchCount = identityBranchCount;
        IdentityPageCount = identityPageCount;
    }

    internal Guid ActiveBranchId { get; }

    internal int AttachmentCount { get; }

    internal int EntityCount { get; }

    internal int IdentityBranchCount { get; }

    internal int IdentityPageCount { get; }

    internal Guid IssuanceNamespace { get; }

    internal int LiveEntityCount { get; }

    internal int LiveSceneInstanceCount { get; }

    internal long NextAssignmentOrdinal { get; }

    internal Guid ReplayLineageId { get; }

    internal int ResourceCount { get; }

    internal int SystemCount { get; }

    internal Guid WorldGuid { get; }
}
