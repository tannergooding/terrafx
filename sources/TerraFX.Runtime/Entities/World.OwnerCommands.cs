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
                sceneInstanceLocator: 0,
                entityIndicesByAssignmentOrdinal: []).Entities;
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
        => CreateSceneEntitiesAtomically(
            entityCount,
            requestedSceneInstanceId,
            entityIndicesByAssignmentOrdinal: [],
            createDescriptions: createDescriptions);

    internal WorldSceneEntityBatchCommit CreateSceneEntitiesAtomically(
        int entityCount,
        Guid? requestedSceneInstanceId,
        scoped ReadOnlySpan<int> entityIndicesByAssignmentOrdinal,
        Func<ImmutableArray<Entity>, ImmutableArray<EntityDescription>> createDescriptions)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entityCount);
        ArgumentNullException.ThrowIfNull(createDescriptions);

        if (!entityIndicesByAssignmentOrdinal.IsEmpty
            && (entityIndicesByAssignmentOrdinal.Length != entityCount))
        {
            throw new ArgumentException("The assignment-order map must be empty or cover every expanded Entity.", nameof(entityIndicesByAssignmentOrdinal));
        }

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
                sceneInstanceLocator,
                entityIndicesByAssignmentOrdinal);
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
        uint sceneInstanceLocator,
        scoped ReadOnlySpan<int> entityIndicesByAssignmentOrdinal)
    {
        if (entityCount > _maximumDeferredCommands)
        {
            throw new InvalidOperationException("The owner-control Entity batch exceeds the deferred-command limit.");
        }

        if (entityCount > (_maximumLiveEntities - _entitySlots.LiveEntityCount))
        {
            throw new InvalidOperationException("The configured live Entity limit was reached during owner-command preflight.");
        }

        var assignmentMapWordCount = (entityIndicesByAssignmentOrdinal.IsEmpty || (entityCount == 0))
            ? 0
            : checked((entityCount + 63) / 64);
        var preparationBytes = (entityCount == 0)
            ? 0
            : checked(
                ArrayBytes(entityCount, Unsafe.SizeOf<Entity>())
                + ((assignmentMapWordCount == 0) ? 0 : ArrayBytes(assignmentMapWordCount, sizeof(ulong))));
        var oldOwnedBytes = GetWorldOwnedStateBytes();

        if ((checked(oldOwnedBytes + preparationBytes) > _maximumOwnedStateBytes)
            || (preparationBytes != 0 && !_domainReservation.TryReserveAdditional(preparationBytes)))
        {
            throw new InvalidOperationException("The World cannot reserve candidate Entity handles for owner-command preparation.");
        }

        _ownerCommandPreparationByteReservation = preparationBytes;

        try
        {
            var candidates = PreviewCandidateEntityHandles(entityCount, entityIndicesByAssignmentOrdinal, assignmentMapWordCount);
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

            for (var assignmentIndex = 0; assignmentIndex < entityCount; assignmentIndex++)
            {
                var entityIndex = entityIndicesByAssignmentOrdinal.IsEmpty
                    ? assignmentIndex
                    : entityIndicesByAssignmentOrdinal[assignmentIndex];
                tokens.Add(staging.AppendOwnerCreate(descriptions[entityIndex]));
            }

            var receipts = CommitCommandBatch(staging, sceneInstanceAssignment);

            if ((entityCount != 0) && (receipts.Length != 1))
            {
                throw new InvalidOperationException("An owner-control Entity batch did not produce exactly one receipt.");
            }

            var entities = ImmutableArray.CreateBuilder<Entity>(entityCount);

            for (var index = 0; index < entityCount; index++)
            {
                entities.Add(default);
            }

            for (var assignmentIndex = 0; assignmentIndex < entityCount; assignmentIndex++)
            {
                var receipt = receipts[0];
                var entityIndex = entityIndicesByAssignmentOrdinal.IsEmpty
                    ? assignmentIndex
                    : entityIndicesByAssignmentOrdinal[assignmentIndex];

                if (!receipt.TryResolve(tokens[assignmentIndex], out var entity)
                    || (entity != candidates[entityIndex]))
                {
                    throw new InvalidOperationException("The owner-control batch resolved an unexpected Entity handle.");
                }

                entities[entityIndex] = entity;
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

    private ImmutableArray<Entity> PreviewCandidateEntityHandles(
        int entityCount,
        scoped ReadOnlySpan<int> entityIndicesByAssignmentOrdinal,
        int assignmentMapWordCount)
    {
        var candidates = ImmutableArray.CreateBuilder<Entity>(entityCount);

        for (var index = 0; index < entityCount; index++)
        {
            candidates.Add(default);
        }

        var assignedIndices = (assignmentMapWordCount == 0)
            ? []
            : new ulong[assignmentMapWordCount];

        for (var assignmentIndex = 0; assignmentIndex < entityCount; assignmentIndex++)
        {
            var entityIndex = entityIndicesByAssignmentOrdinal.IsEmpty
                ? assignmentIndex
                : entityIndicesByAssignmentOrdinal[assignmentIndex];

            if ((uint)entityIndex >= (uint)entityCount)
            {
                throw new ArgumentOutOfRangeException(nameof(entityIndicesByAssignmentOrdinal));
            }

            if (assignmentMapWordCount != 0)
            {
                var wordIndex = entityIndex / 64;
                var mask = 1UL << (entityIndex % 64);

                if ((assignedIndices[wordIndex] & mask) != 0)
                {
                    throw new ArgumentException("The assignment-order map contains a duplicate Entity index.", nameof(entityIndicesByAssignmentOrdinal));
                }

                assignedIndices[wordIndex] |= mask;
            }

            if (!_entitySlots.TryGetAllocationPreview(assignmentIndex, out var slot, out var generation))
            {
                throw new InvalidOperationException("The configured Entity-slot limit was reached during owner-command preflight.");
            }

            candidates[entityIndex] = new Entity(_ownerId, slot, generation);
        }

        return candidates.MoveToImmutable();
    }

    internal readonly record struct WorldSceneEntityBatchCommit(
        Guid SceneInstanceId,
        ImmutableArray<Entity> Entities,
        long StructuralRevision);
}
