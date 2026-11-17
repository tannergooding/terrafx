// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal interface IWorldResourceReferences
{
    EbinReadLimits PersistenceLimits { get; }

    long? RequiredRequestTick { get; }

    Guid WorldGuid { get; }

    bool TryResolveHistoricalEntity(
        Guid entityId, Guid creationLineageId, Guid creationBranchId, out WorldEntityIdentity identity);
}
