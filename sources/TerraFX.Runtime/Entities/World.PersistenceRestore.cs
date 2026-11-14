// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

public sealed partial class World
{
    internal Entity ResolveRestoreEntity(Guid stableId)
    {
        if (stableId == Guid.Empty)
        {
            throw new ArgumentException("A restored Entity identity must be nonempty.", nameof(stableId));
        }

        ThrowIfDisposed();
        ThrowIfFaulted();
        EnsureOwnerThread();

        if (!_isRestoreCandidate || !_isCandidateInitialized)
        {
            throw new InvalidOperationException("Stable Entity identities can be resolved only by an initialized restore candidate.");
        }

        if (!_identityLedger.TryGetLiveLocator(stableId, IdentityKind.Entity, out var locator)
            || !_entitySlots.IsLive(locator, generation: 1))
        {
            throw new InvalidOperationException("The restored identity map does not contain the requested live Entity.");
        }

        return CreateEntityHandle(locator, generation: 1);
    }
}
