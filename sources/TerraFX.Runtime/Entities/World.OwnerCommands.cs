// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading;

namespace TerraFX.Entities;

public sealed partial class World
{
    private static void ValidateHierarchyParentIndices(
        scoped ReadOnlySpan<int> parentIndices,
        scoped Span<int> hierarchyDepths)
    {
        if (parentIndices.IsEmpty)
        {
            return;
        }

        if (parentIndices.Length != hierarchyDepths.Length)
        {
            throw new ArgumentException("The hierarchy depth workspace must cover every parent index.", nameof(hierarchyDepths));
        }

        for (var entityIndex = 0; entityIndex < parentIndices.Length; entityIndex++)
        {
            var parentIndex = parentIndices[entityIndex];

            if (parentIndex == -1)
            {
                hierarchyDepths[entityIndex] = 0;
                continue;
            }

            if ((uint)parentIndex >= (uint)entityIndex)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(parentIndices),
                    "A hierarchy parent must be an earlier expanded Entity or the root.");
            }

            var depth = checked(hierarchyDepths[parentIndex] + 1);

            if (depth > EntitySlotTable.MaximumHierarchyDepth)
            {
                throw new InvalidOperationException("The Scene Entity parent map exceeds the World hierarchy depth bound.");
            }

            hierarchyDepths[entityIndex] = depth;
        }
    }

    internal long ApplySceneHierarchyTransaction(
        IWorldSceneParticipant participant,
        Guid sceneInstanceId,
        uint firstEntityIdentityOrdinal,
        int entityCount,
        ImmutableArray<WorldHierarchyComponentMutation> componentMutations,
        ImmutableArray<WorldHierarchyParentMutation> parentMutations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentOutOfRangeException.ThrowIfEqual(sceneInstanceId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(entityCount);

        if (componentMutations.IsDefault || parentMutations.IsDefault)
        {
            throw new ArgumentException("The hierarchy transaction mutation arrays must be initialized.");
        }

        if ((entityCount != 0) && (firstEntityIdentityOrdinal == 0))
        {
            throw new ArgumentOutOfRangeException(nameof(firstEntityIdentityOrdinal));
        }

        cancellationToken.ThrowIfCancellationRequested();
        EnterStructuralOperation();

        try
        {
            if (!ReferenceEquals(_sceneParticipant, participant))
            {
                throw new ArgumentException("The Scene participant is not owned by this exact World.", nameof(participant));
            }

            if (!_identityLedger.TryGetLiveLocator(
                    sceneInstanceId,
                    IdentityKind.SceneInstance,
                    out var sceneInstanceLocator)
                || (_identityLedger.GetLiveOrdinal(IdentityKind.SceneInstance, sceneInstanceLocator) == 0))
            {
                throw new InvalidOperationException("The transaction's SceneInstance identity is not live in this World.");
            }

            var lastEntityIdentityOrdinalExclusive = checked(firstEntityIdentityOrdinal + (uint)entityCount);
            using var staging = new WorldCommandStaging(
                this,
                _maximumDeferredCommands,
                _maximumCommandStagingBytes,
                ownerControl: true);

            foreach (var mutation in componentMutations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureSceneTransactionMember(
                    mutation.Entity,
                    firstEntityIdentityOrdinal,
                    lastEntityIdentityOrdinalExclusive);

                if ((mutation.ComponentType != participant.HierarchyLocalTransformComponentType)
                    && (mutation.ComponentType != participant.HierarchyRootPositionComponentType))
                {
                    throw new ArgumentException("Scene hierarchy transactions may change only LocalTransform and RootPosition.", nameof(componentMutations));
                }

                staging.AppendOwnerComponentMutation(
                    mutation,
                    FindComponentRegistration(mutation.ComponentType));
            }

            foreach (var mutation in parentMutations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureSceneTransactionMember(
                    mutation.Child,
                    firstEntityIdentityOrdinal,
                    lastEntityIdentityOrdinalExclusive);

                if (mutation.Parent is Entity parent)
                {
                    _ = GetLiveArchetype(parent, out _, out _);
                }
            }

            _ = CommitCommandBatch(
                staging,
                hierarchyParentMutations: parentMutations,
                cancellationToken: cancellationToken);
            return _canonicalMutationRevision;
        }
        finally
        {
            _ = Interlocked.Exchange(ref _isStructuralOperationActive, 0);
        }

        void EnsureSceneTransactionMember(
            Entity entity,
            uint firstIdentityOrdinal,
            uint lastIdentityOrdinalExclusive)
        {
            if (!entity.IsValid
                || (entity.OwnerId != _ownerId)
                || !_entitySlots.TryGetLocation(entity.Index, entity.Generation, out _, out _, out _))
            {
                throw new InvalidOperationException("A Scene hierarchy transaction target is not a live Entity in this exact World.");
            }

            var identityOrdinal = _identityLedger.GetLiveOrdinal(IdentityKind.Entity, entity.Index);

            if ((identityOrdinal < firstIdentityOrdinal)
                || (identityOrdinal >= lastIdentityOrdinalExclusive))
            {
                throw new InvalidOperationException("A Scene hierarchy transaction child is no longer a live member of its exact SceneInstance.");
            }
        }
    }

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
                entityIndicesByAssignmentOrdinal: [],
                parentEntityIndices: default,
                cancellationToken: CancellationToken.None).Entities;
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
        Func<ImmutableArray<Entity>, ImmutableArray<EntityDescription>> createDescriptions,
        ReadOnlyMemory<int> parentEntityIndices = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entityCount);
        ArgumentNullException.ThrowIfNull(createDescriptions);

        if (!entityIndicesByAssignmentOrdinal.IsEmpty
            && (entityIndicesByAssignmentOrdinal.Length != entityCount))
        {
            throw new ArgumentException("The assignment-order map must be empty or cover every expanded Entity.", nameof(entityIndicesByAssignmentOrdinal));
        }

        if (!parentEntityIndices.IsEmpty
            && (parentEntityIndices.Length != entityCount))
        {
            throw new ArgumentException("The parent map must cover every expanded Entity.", nameof(parentEntityIndices));
        }

        if (!parentEntityIndices.IsEmpty && (_sceneParticipant is null))
        {
            throw new InvalidOperationException("The World was not constructed with Scene support.");
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
                entityIndicesByAssignmentOrdinal,
                parentEntityIndices,
                cancellationToken);
        }
        finally
        {
            _ = Interlocked.Exchange(ref _isStructuralOperationActive, 0);
        }
    }

    internal long DestroySceneEntitiesAtomically(
        Guid sceneInstanceId,
        uint firstEntityIdentityOrdinal,
        int entityCount,
        HierarchyDestroyMode mode,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sceneInstanceId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(entityCount);

        if (mode is not HierarchyDestroyMode.DetachChildrenKeepWorld and not HierarchyDestroyMode.DestroySubtree)
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if ((entityCount != 0) && (firstEntityIdentityOrdinal == 0))
        {
            throw new ArgumentOutOfRangeException(nameof(firstEntityIdentityOrdinal));
        }

        cancellationToken.ThrowIfCancellationRequested();
        EnterStructuralOperation();

        try
        {
            if (!_identityLedger.TryGetLiveLocator(
                    sceneInstanceId,
                    IdentityKind.SceneInstance,
                    out var sceneInstanceLocator))
            {
                throw new InvalidOperationException("The SceneInstance identity is not live in this World.");
            }

            var sceneInstanceOrdinal = _identityLedger.GetLiveOrdinal(
                IdentityKind.SceneInstance,
                sceneInstanceLocator);

            if (sceneInstanceOrdinal == 0)
            {
                throw new InvalidOperationException("The live SceneInstance identity has no ledger ordinal.");
            }

            var lastEntityIdentityOrdinalExclusive = checked(firstEntityIdentityOrdinal + (uint)entityCount);
            using var staging = new WorldCommandStaging(
                this,
                _maximumDeferredCommands,
                _maximumCommandStagingBytes,
                ownerControl: true);

            if (mode == HierarchyDestroyMode.DetachChildrenKeepWorld)
            {
                var liveSceneEntityCount = 0;

                for (var index = 0; index < entityCount; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var identityOrdinal = checked(firstEntityIdentityOrdinal + (uint)index);

                    if (!_identityLedger.TryGetLiveLocatorByOrdinal(
                            identityOrdinal,
                            IdentityKind.Entity,
                            out var entityLocator))
                    {
                        continue;
                    }

                    if (!_entitySlots.TryGetLive(entityLocator, out var generation)
                        || !_entitySlots.TryGetLocation(entityLocator, generation, out _, out _, out _))
                    {
                        throw new InvalidOperationException("A live Scene Entity identity references a non-live Entity slot.");
                    }

                    liveSceneEntityCount++;
                }

                var stagedSceneEntityCount = 0;

                void AppendSceneSubtree(Entity entity)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (_entitySlots.TryGetFirstChild(
                            entity.Index,
                            entity.Generation,
                            out var childIndex,
                            out var childGeneration))
                    {
                        while (true)
                        {
                            var childIdentityOrdinal = _identityLedger.GetLiveOrdinal(
                                IdentityKind.Entity,
                                childIndex);

                            if ((childIdentityOrdinal >= firstEntityIdentityOrdinal)
                                && (childIdentityOrdinal < lastEntityIdentityOrdinalExclusive))
                            {
                                AppendSceneSubtree(new Entity(_ownerId, childIndex, childGeneration));
                            }

                            if (!_entitySlots.TryGetNextSibling(
                                    childIndex,
                                    childGeneration,
                                    out childIndex,
                                    out childGeneration))
                            {
                                break;
                            }
                        }
                    }

                    staging.AppendOwnerDestroy(entity, mode);
                    stagedSceneEntityCount++;
                }

                for (var index = 0; index < entityCount; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var identityOrdinal = checked(firstEntityIdentityOrdinal + (uint)index);

                    if (!_identityLedger.TryGetLiveLocatorByOrdinal(
                            identityOrdinal,
                            IdentityKind.Entity,
                            out var entityLocator))
                    {
                        continue;
                    }

                    if (!_entitySlots.TryGetLive(entityLocator, out var generation))
                    {
                        throw new InvalidOperationException("A live Scene Entity identity references a non-live Entity slot.");
                    }

                    var entity = new Entity(_ownerId, entityLocator, generation);
                    var parentIsSceneEntity = false;

                    if (_entitySlots.TryGetParent(
                            entityLocator,
                            generation,
                            out var parentLocator,
                            out var parentGeneration)
                        && _entitySlots.IsLive(parentLocator, parentGeneration))
                    {
                        var parentIdentityOrdinal = _identityLedger.GetLiveOrdinal(IdentityKind.Entity, parentLocator);
                        parentIsSceneEntity = (parentIdentityOrdinal >= firstEntityIdentityOrdinal)
                            && (parentIdentityOrdinal < lastEntityIdentityOrdinalExclusive);
                    }

                    if (!parentIsSceneEntity)
                    {
                        AppendSceneSubtree(entity);
                    }
                }

                if (stagedSceneEntityCount != liveSceneEntityCount)
                {
                    throw new InvalidOperationException("The live Scene hierarchy did not produce a complete descendant-first unload order.");
                }
            }
            else
            {
                for (var index = 0; index < entityCount; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var identityOrdinal = checked(firstEntityIdentityOrdinal + (uint)index);

                    if (!_identityLedger.TryGetLiveLocatorByOrdinal(
                            identityOrdinal,
                            IdentityKind.Entity,
                            out var entityLocator))
                    {
                        continue;
                    }

                    if (!_entitySlots.TryGetLive(entityLocator, out var generation)
                        || !_entitySlots.TryGetLocation(entityLocator, generation, out _, out _, out _))
                    {
                        throw new InvalidOperationException("A live Scene Entity identity references a non-live Entity slot.");
                    }

                    var entity = new Entity(_ownerId, entityLocator, generation);

                    if (_entitySlots.TryGetParent(
                            entityLocator,
                            generation,
                            out var parentLocator,
                            out var parentGeneration)
                        && _entitySlots.IsLive(parentLocator, parentGeneration))
                    {
                        var parentIdentityOrdinal = _identityLedger.GetLiveOrdinal(IdentityKind.Entity, parentLocator);

                        if ((parentIdentityOrdinal >= firstEntityIdentityOrdinal)
                            && (parentIdentityOrdinal < lastEntityIdentityOrdinalExclusive))
                        {
                            continue;
                        }
                    }

                    staging.AppendOwnerDestroy(entity, mode);
                }
            }

            _ = CommitCommandBatch(
                staging,
                cancellationToken: cancellationToken,
                sceneInstanceRetirementOrdinal: sceneInstanceOrdinal);
            return _canonicalMutationRevision;
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
        scoped ReadOnlySpan<int> entityIndicesByAssignmentOrdinal,
        ReadOnlyMemory<int> parentEntityIndices,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
        var hierarchyParentIndices = parentEntityIndices.Span;
        var preparationBytes = (entityCount == 0)
            ? 0
            : checked(
                ArrayBytes(entityCount, Unsafe.SizeOf<Entity>())
                + ((assignmentMapWordCount == 0) ? 0 : ArrayBytes(assignmentMapWordCount, sizeof(ulong)))
                + (hierarchyParentIndices.IsEmpty ? 0 : ArrayBytes(entityCount, sizeof(int))));
        var oldOwnedBytes = GetWorldOwnedStateBytes();

        if ((checked(oldOwnedBytes + preparationBytes) > _maximumOwnedStateBytes)
            || (preparationBytes != 0 && !_domainReservation.TryReserveAdditional(preparationBytes)))
        {
            throw new InvalidOperationException("The World cannot reserve candidate Entity handles for owner-command preparation.");
        }

        _ownerCommandPreparationByteReservation = preparationBytes;

        try
        {
            var hierarchyDepths = hierarchyParentIndices.IsEmpty ? [] : new int[entityCount];
            ValidateHierarchyParentIndices(hierarchyParentIndices, hierarchyDepths);
            var candidates = PreviewCandidateEntityHandles(entityCount, entityIndicesByAssignmentOrdinal, assignmentMapWordCount);
            var descriptions = createDescriptions(candidates);
            cancellationToken.ThrowIfCancellationRequested();

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
                cancellationToken.ThrowIfCancellationRequested();
                var entityIndex = entityIndicesByAssignmentOrdinal.IsEmpty
                    ? assignmentIndex
                    : entityIndicesByAssignmentOrdinal[assignmentIndex];
                tokens.Add(staging.AppendOwnerCreate(descriptions[entityIndex]));
            }

            var receipts = CommitCommandBatch(
                staging,
                sceneInstanceAssignment,
                cancellationToken: cancellationToken);

            if ((entityCount != 0) && (receipts.Length != 1))
            {
                throw new InvalidOperationException("An owner-control Entity batch did not produce exactly one receipt.");
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
            }

            for (var entityIndex = 0; entityIndex < hierarchyParentIndices.Length; entityIndex++)
            {
                var parentIndex = hierarchyParentIndices[entityIndex];

                if (parentIndex < 0)
                {
                    continue;
                }

                var child = candidates[entityIndex];
                var parent = candidates[parentIndex];
                _entitySlots.SetParent(
                    child.Index,
                    child.Generation,
                    parent.Index,
                    parent.Generation);
            }

            return new WorldSceneEntityBatchCommit(
                (sceneInstanceAssignment is null)
                    ? Guid.Empty
                    : _identityLedger.GetAssignedId(IdentityKind.SceneInstance, sceneInstanceLocator),
                candidates,
                _canonicalMutationRevision,
                (sceneInstanceAssignment is null || entityCount == 0)
                    ? 0
                    : checked(sceneInstanceLocator + 1));
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
        long StructuralRevision,
        uint FirstEntityIdentityOrdinal);
}
