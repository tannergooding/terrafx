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
            return CreateEntitiesAtomicallyCore(
                entityCount,
                createDescriptions,
                sceneInstanceAssignment: null,
                sceneInstanceLocator: 0).Entities;
        }
        finally
        {
            _ = Interlocked.Exchange(ref _isStructuralOperationActive, 0);
        }
    }

    internal WorldSceneEntityBatchCommit CreateSceneEntitiesAtomically(
        int entityCount,
        Guid? requestedSceneInstanceId,
        Func<ImmutableArray<Entity>, ImmutableArray<EntityDescription>> createDescriptions)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entityCount);
        ArgumentNullException.ThrowIfNull(createDescriptions);

        if (requestedSceneInstanceId is Guid sceneInstanceId
            && ((sceneInstanceId == Guid.Empty) || IsReservedUuidV8ForCommand(sceneInstanceId)))
        {
            throw new ArgumentException("A requested Scene instance identity must be nonempty and non-v8.", nameof(requestedSceneInstanceId));
        }

        EnterStructuralOperation();

        try
        {
            var sceneInstanceLocator = checked((uint)(_identityLedger.AssignmentCount + 1));
            var sceneInstanceAssignment = requestedSceneInstanceId is Guid explicitId
                ? new IdentityAssignment(
                    IdentityKind.SceneInstance,
                    IdentityOrigin.Explicit,
                    sceneInstanceLocator,
                    _currentIdentityBranchIndex,
                    explicitId)
                : new IdentityAssignment(
                    IdentityKind.SceneInstance,
                    IdentityOrigin.Automatic,
                    sceneInstanceLocator,
                    _currentIdentityBranchIndex,
                    issuanceNamespace: _identityBranches[_currentIdentityBranchIndex].NamespaceId);

            return CreateEntitiesAtomicallyCore(
                entityCount,
                createDescriptions,
                sceneInstanceAssignment,
                sceneInstanceLocator);
        }
        finally
        {
            _ = Interlocked.Exchange(ref _isStructuralOperationActive, 0);
        }
    }

    private WorldSceneEntityBatchCommit CreateEntitiesAtomicallyCore(
        int entityCount,
        Func<ImmutableArray<Entity>, ImmutableArray<EntityDescription>> createDescriptions,
        IdentityAssignment? sceneInstanceAssignment,
        uint sceneInstanceLocator)
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

            var receipts = CommitCommandBatch(staging, sceneInstanceAssignment);

            if ((entityCount != 0) && (receipts.Length != 1))
            {
                throw new InvalidOperationException("An owner-control Entity batch did not produce exactly one receipt.");
            }

            var entities = ImmutableArray.CreateBuilder<Entity>(entityCount);

            for (var index = 0; index < entityCount; index++)
            {
                var receipt = receipts[0];

                if (!receipt.TryResolve(tokens[index], out var entity)
                    || (entity != candidates[index]))
                {
                    throw new InvalidOperationException("The owner-control batch resolved an unexpected Entity handle.");
                }

                entities.Add(entity);
            }

            return new WorldSceneEntityBatchCommit(
                (sceneInstanceAssignment is null)
                    ? Guid.Empty
                    : _identityLedger.GetAssignedId(IdentityKind.SceneInstance, sceneInstanceLocator),
                entities.MoveToImmutable(),
                _canonicalMutationRevision);
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

    internal readonly record struct WorldSceneEntityBatchCommit(
        Guid SceneInstanceId,
        ImmutableArray<Entity> Entities,
        long StructuralRevision);
}
