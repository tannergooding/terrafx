// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading;

namespace TerraFX.Entities;

public sealed partial class World
{
    internal ImmutableArray<Entity> CreateEntitiesAtomically(
        int entityCount,
        Func<ImmutableArray<Entity>, ImmutableArray<EntityDescription>> createDescriptions)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entityCount);
        ArgumentNullException.ThrowIfNull(createDescriptions);
        EnterStructuralOperation();

        try
        {
            return CreateEntitiesAtomicallyCore(entityCount, createDescriptions);
        }
        finally
        {
            _ = Interlocked.Exchange(ref _isStructuralOperationActive, 0);
        }
    }

    private ImmutableArray<Entity> CreateEntitiesAtomicallyCore(
        int entityCount,
        Func<ImmutableArray<Entity>, ImmutableArray<EntityDescription>> createDescriptions)
    {
        if (entityCount > _maximumDeferredCommands)
        {
            throw new InvalidOperationException("The owner-control Entity batch exceeds the deferred-command limit.");
        }

        if (entityCount > (_maximumLiveEntities - _entitySlots.LiveEntityCount))
        {
            throw new InvalidOperationException("The configured live Entity limit was reached during owner-command preflight.");
        }

        var preparationBytes = (entityCount == 0)
            ? 0
            : ArrayBytes(entityCount, Unsafe.SizeOf<Entity>());
        var oldOwnedBytes = GetWorldOwnedStateBytes();

        if ((checked(oldOwnedBytes + preparationBytes) > _maximumOwnedStateBytes)
            || (preparationBytes != 0 && !_domainReservation.TryReserveAdditional(preparationBytes)))
        {
            throw new InvalidOperationException("The World cannot reserve candidate Entity handles for owner-command preparation.");
        }

        _ownerCommandPreparationByteReservation = preparationBytes;

        try
        {
            var candidates = PreviewCandidateEntityHandles(entityCount);
            var descriptions = createDescriptions(candidates);

            if (descriptions.IsDefault || (descriptions.Length != entityCount))
            {
                throw new ArgumentException("The Entity description count must match the admitted candidate count.", nameof(createDescriptions));
            }

            using var staging = new WorldCommandStaging(
                this,
                _maximumDeferredCommands,
                _maximumCommandStagingBytes,
                ownerControl: true);
            var tokens = ImmutableArray.CreateBuilder<EntityToken>(entityCount);

            foreach (var description in descriptions)
            {
                tokens.Add(staging.AppendOwnerCreate(description));
            }

            var receipts = CommitCommandBatch(staging);

            if (entityCount == 0)
            {
                return [];
            }

            if (receipts.Length != 1)
            {
                throw new InvalidOperationException("An owner-control Entity batch did not produce exactly one receipt.");
            }

            var receipt = receipts[0];
            var entities = ImmutableArray.CreateBuilder<Entity>(entityCount);

            for (var index = 0; index < entityCount; index++)
            {
                if (!receipt.TryResolve(tokens[index], out var entity)
                    || (entity != candidates[index]))
                {
                    throw new InvalidOperationException("The owner-control batch resolved an unexpected Entity handle.");
                }

                entities.Add(entity);
            }

            return entities.MoveToImmutable();
        }
        finally
        {
            _ownerCommandPreparationByteReservation = 0;

            if (preparationBytes != 0)
            {
                _domainReservation.ReleaseAdditional(preparationBytes);
            }
        }
    }

    private ImmutableArray<Entity> PreviewCandidateEntityHandles(int entityCount)
    {
        var candidates = ImmutableArray.CreateBuilder<Entity>(entityCount);

        for (var index = 0; index < entityCount; index++)
        {
            if (!_entitySlots.TryGetAllocationPreview(index, out var slot, out var generation))
            {
                throw new InvalidOperationException("The configured Entity-slot limit was reached during owner-command preflight.");
            }

            candidates.Add(new Entity(_ownerId, slot, generation));
        }

        return candidates.MoveToImmutable();
    }
}
