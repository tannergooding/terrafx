// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace TerraFX.Entities;

public sealed partial class World
{
    internal static void AppendDestroyTokenCommand(
        WorldSystemInvocationDispatch dispatch,
        EntityToken entity,
        HierarchyDestroyMode mode)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        dispatch.EnsureCommandActive();
        dispatch.CommandStaging.AppendDestroyToken(dispatch, entity, mode);
    }

    internal static bool IsReservedUuidV8ForCommand(Guid value)
        => IsReservedUuidV8(value);

    private static long GetArrayByteReservation(int elementCount, int elementSize)
        => (elementCount == 0) ? 0 : ArrayBytes(elementCount, elementSize);

    private static void ValidateWriterPartitionAccess(SystemResourceAccess access, int writerPartition)
    {
        if (access.Range is not { } range)
        {
            return;
        }

        if ((writerPartition == 0)
            || ((writerPartition - 1) < range.Start)
            || ((writerPartition - 1) >= range.EndExclusive))
        {
            throw new InvalidOperationException("The command writer's partition is outside the declared component range.");
        }
    }

    internal void AppendAddCommand<T>(WorldSystemInvocationDispatch dispatch, Entity entity, in T value)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        dispatch.EnsureCommandActive();
        var component = GetComponentRegistration<T>();
        var access = FindSystemAccess(dispatch.Description, SystemResourceKind.Component, component.SchemaReference.SchemaId);

        if (access.Mode != SystemAccessMode.Write)
        {
            throw new InvalidOperationException("The system did not declare Write access to the selected component.");
        }

        var archetype = GetLiveArchetype(entity, out var chunkIndex, out var row);

        if (access.Range is { } range)
        {
            var partition = GetGlobalPartitionOrdinal(archetype, chunkIndex, row);

            if ((partition < range.Start) || (partition >= range.EndExclusive))
            {
                throw new InvalidOperationException("The target Entity is outside the system's declared component range.");
            }
        }

        dispatch.CommandStaging.AppendAdd(dispatch, entity, component, in value);
    }

    internal void AppendAddCommand<T>(WorldSystemInvocationDispatch dispatch, EntityToken entity, in T value)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        dispatch.EnsureCommandActive();
        dispatch.CommandStaging.ValidateTokenTarget(dispatch, entity, nameof(entity));
        var component = GetComponentRegistration<T>();
        var access = FindSystemAccess(dispatch.Description, SystemResourceKind.Component, component.SchemaReference.SchemaId);

        if (access.Mode != SystemAccessMode.Write)
        {
            throw new InvalidOperationException("The system did not declare Write access to the selected component.");
        }

        ValidateWriterPartitionAccess(access, dispatch.CurrentWriterPartition);
        dispatch.CommandStaging.AppendAdd(dispatch, entity, component, in value);
    }

    internal void AppendDestroyCommand(
        WorldSystemInvocationDispatch dispatch,
        Entity entity,
        HierarchyDestroyMode mode)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        dispatch.EnsureCommandActive();
        _ = GetLiveArchetype(entity, out _, out _);
        dispatch.CommandStaging.AppendDestroyEntity(dispatch, entity, mode);
    }

    internal void AppendRemoveCommand<T>(WorldSystemInvocationDispatch dispatch, Entity entity)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        dispatch.EnsureCommandActive();
        var component = GetComponentRegistration<T>();
        var access = FindSystemAccess(dispatch.Description, SystemResourceKind.Component, component.SchemaReference.SchemaId);

        if (access.Mode != SystemAccessMode.Write)
        {
            throw new InvalidOperationException("The system did not declare Write access to the selected component.");
        }

        var archetype = GetLiveArchetype(entity, out var chunkIndex, out var row);

        if (access.Range is { } range)
        {
            var partition = GetGlobalPartitionOrdinal(archetype, chunkIndex, row);

            if ((partition < range.Start) || (partition >= range.EndExclusive))
            {
                throw new InvalidOperationException("The target Entity is outside the system's declared component range.");
            }
        }

        dispatch.CommandStaging.AppendRemove(dispatch, entity, component);
    }

    internal void AppendRemoveCommand<T>(WorldSystemInvocationDispatch dispatch, EntityToken entity)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        dispatch.EnsureCommandActive();
        dispatch.CommandStaging.ValidateTokenTarget(dispatch, entity, nameof(entity));
        var component = GetComponentRegistration<T>();
        var access = FindSystemAccess(dispatch.Description, SystemResourceKind.Component, component.SchemaReference.SchemaId);

        if (access.Mode != SystemAccessMode.Write)
        {
            throw new InvalidOperationException("The system did not declare Write access to the selected component.");
        }

        ValidateWriterPartitionAccess(access, dispatch.CurrentWriterPartition);
        dispatch.CommandStaging.AppendRemove(dispatch, entity, component);
    }

    internal void AppendSetCommand<T>(WorldSystemInvocationDispatch dispatch, Entity entity, in T value)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        dispatch.EnsureCommandActive();
        var component = GetComponentRegistration<T>();
        var access = FindSystemAccess(dispatch.Description, SystemResourceKind.Component, component.SchemaReference.SchemaId);

        if (access.Mode != SystemAccessMode.Write)
        {
            throw new InvalidOperationException("The system did not declare Write access to the selected component.");
        }

        var archetype = GetLiveArchetype(entity, out var chunkIndex, out var row);

        if (access.Range is { } range)
        {
            var partition = GetGlobalPartitionOrdinal(archetype, chunkIndex, row);

            if ((partition < range.Start) || (partition >= range.EndExclusive))
            {
                throw new InvalidOperationException("The target Entity is outside the system's declared component range.");
            }
        }

        dispatch.CommandStaging.AppendSet(dispatch, entity, component, in value);
    }

    internal void AppendSetCommand<T>(WorldSystemInvocationDispatch dispatch, EntityToken entity, in T value)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        dispatch.EnsureCommandActive();
        dispatch.CommandStaging.ValidateTokenTarget(dispatch, entity, nameof(entity));
        var component = GetComponentRegistration<T>();
        var access = FindSystemAccess(dispatch.Description, SystemResourceKind.Component, component.SchemaReference.SchemaId);

        if (access.Mode != SystemAccessMode.Write)
        {
            throw new InvalidOperationException("The system did not declare Write access to the selected component.");
        }

        ValidateWriterPartitionAccess(access, dispatch.CurrentWriterPartition);
        dispatch.CommandStaging.AppendSet(dispatch, entity, component, in value);
    }

    internal void AppendSetEntityReferenceCommand<T>(
        WorldSystemInvocationDispatch dispatch,
        Entity owner,
        ComponentFieldPath path,
        Entity? target)
        where T : unmanaged
        => AppendSetEntityReferenceCommand<T>(dispatch, owner, ownerToken: null, path, target, targetToken: null);

    internal void AppendSetEntityReferenceCommand<T>(
        WorldSystemInvocationDispatch dispatch,
        Entity owner,
        ComponentFieldPath path,
        EntityToken? target)
        where T : unmanaged
        => AppendSetEntityReferenceCommand<T>(dispatch, owner, ownerToken: null, path, target: null, target);

    internal void AppendSetEntityReferenceCommand<T>(
        WorldSystemInvocationDispatch dispatch,
        EntityToken owner,
        ComponentFieldPath path,
        Entity? target)
        where T : unmanaged
        => AppendSetEntityReferenceCommand<T>(dispatch, owner: default, owner, path, target, targetToken: null);

    internal void AppendSetEntityReferenceCommand<T>(
        WorldSystemInvocationDispatch dispatch,
        EntityToken owner,
        ComponentFieldPath path,
        EntityToken? target)
        where T : unmanaged
        => AppendSetEntityReferenceCommand<T>(dispatch, owner: default, owner, path, target: null, target);

    internal void AppendTagCommand(
        WorldSystemInvocationDispatch dispatch,
        Entity entity,
        Guid schemaId,
        bool add)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        dispatch.EnsureCommandActive();
        ArgumentOutOfRangeException.ThrowIfEqual(schemaId, Guid.Empty);
        _ = GetLiveArchetype(entity, out _, out _);
        EnsureRegisteredTag(schemaId);
        dispatch.CommandStaging.AppendTag(dispatch, entity, schemaId, add);
    }

    internal void AppendTagCommand(
        WorldSystemInvocationDispatch dispatch,
        EntityToken entity,
        Guid schemaId,
        bool add)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        dispatch.EnsureCommandActive();
        dispatch.CommandStaging.ValidateTokenTarget(dispatch, entity, nameof(entity));
        ArgumentOutOfRangeException.ThrowIfEqual(schemaId, Guid.Empty);
        EnsureRegisteredTag(schemaId);
        dispatch.CommandStaging.AppendTag(dispatch, entity, schemaId, add);
    }

    internal void ReleaseCommandStagingBytes(long bytes)
        => _domainReservation.ReleaseAdditional(bytes);

    internal bool TryReserveCommandPlaybackBytes(
        long bytes,
        long currentlyReservedBytes,
        long commandStagingBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
        ArgumentOutOfRangeException.ThrowIfNegative(currentlyReservedBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(commandStagingBytes);
        var nextReservedBytes = checked(currentlyReservedBytes + bytes);

        if (checked(GetWorldOwnedStateBytes() + commandStagingBytes + nextReservedBytes) > _maximumOwnedStateBytes)
        {
            return false;
        }

        return _domainReservation.TryReserveAdditional(bytes);
    }

    internal bool TryReserveCommandStagingBytes(long bytes, long currentlyReservedBytes, long maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
        ArgumentOutOfRangeException.ThrowIfNegative(currentlyReservedBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        var nextReservedBytes = checked(currentlyReservedBytes + bytes);

        if ((nextReservedBytes > maximumBytes)
            || (checked(GetWorldOwnedStateBytes() + nextReservedBytes) > _maximumOwnedStateBytes))
        {
            return false;
        }

        return _domainReservation.TryReserveAdditional(bytes);
    }

    private void AppendSetEntityReferenceCommand<T>(
        WorldSystemInvocationDispatch dispatch,
        Entity owner,
        EntityToken? ownerToken,
        ComponentFieldPath path,
        Entity? target,
        EntityToken? targetToken)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentNullException.ThrowIfNull(path);
        dispatch.EnsureCommandActive();

        if (ownerToken is { } tokenOwner)
        {
            dispatch.CommandStaging.ValidateTokenTarget(dispatch, tokenOwner, nameof(owner));
        }

        if (targetToken is { } tokenTarget)
        {
            dispatch.CommandStaging.ValidateTokenTarget(dispatch, tokenTarget, nameof(target));
        }

        var component = GetComponentRegistration<T>();
        var access = FindSystemAccess(dispatch.Description, SystemResourceKind.Component, component.SchemaReference.SchemaId);

        if (access.Mode != SystemAccessMode.Write)
        {
            throw new InvalidOperationException("The system did not declare Write access to the selected component.");
        }

        WorldArchetype? archetype = null;
        var chunkIndex = -1;
        var row = -1;

        if (ownerToken is null)
        {
            archetype = GetLiveArchetype(owner, out chunkIndex, out row);
        }

        if (!component.IsEntityReferencePath(path))
        {
            throw new ArgumentException("The selected component field path is not a registered Entity-reference field.", nameof(path));
        }

        if (target is { } targetEntity)
        {
            ValidateEntityReference(targetEntity, excludedEntity: null, nameof(target));
        }

        if (access.Range is { } range)
        {
            if (ownerToken is not null)
            {
                ValidateWriterPartitionAccess(access, dispatch.CurrentWriterPartition);
            }
            else
            {
                var partition = GetGlobalPartitionOrdinal(
                    archetype ?? throw new InvalidOperationException("The live Entity owner has no source archetype."),
                    chunkIndex,
                    row);

                if ((partition < range.Start) || (partition >= range.EndExclusive))
                {
                    throw new InvalidOperationException("The target Entity is outside the system's declared component range.");
                }
            }
        }

        dispatch.CommandStaging.AppendSetEntityReferenceWithTokenArms<T>(
            dispatch,
            owner,
            ownerToken,
            component,
            path,
            target,
            targetToken);
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Command playback must release its preparation reservation before rethrowing the exact operation failure.")]
    private ImmutableArray<CommandReceipt> CommitCommandBatch(WorldCommandStaging staging)
    {
        var commands = staging.Seal();

        if (commands.IsEmpty)
        {
            return [];
        }

        var oldOwnedBytes = GetWorldOwnedStateBytes();
        var commandStagingBytes = staging.ReservedBytes;
        var reservedBytes = 0L;
        var candidateWorkPlanByteReservation = 0L;
        WorldChunkWorkPlanEntry[]? candidateWorkPlan = null;
        var candidateArchetypes = new List<WorldArchetype>(
            Math.Min(commands.Length, _maximumArchetypes - _archetypeCount));
        var installedCandidateCount = 0;
        var createdProjections = new List<ProjectedCommandEntity>(commands.Length);
        var destroyedProjections = new List<ProjectedCommandEntity>(commands.Length);
        WorldAttachmentTable.AttachmentEntry[] detachedAttachments = [];
        WorldAttachmentReleaseBinding[] releaseBindings = [];
        WorldAttachmentReleasePreparation? releasePreparation = null;
        WorldAttachmentQuarantineReservation? quarantineReservation = null;
        StableIdentityLedger.PreparedChange? identityChange = null;
        var result = ImmutableArray<CommandReceipt>.Empty;
        ExceptionDispatchInfo? operationFailure = null;

        try
        {
            ReservePlanBytes(checked(4096L + ((long)commands.Length * 256L)));

            var projectionsByEntity = new Dictionary<Entity, ProjectedCommandEntity>(commands.Length);
            var projectionsByToken = new Dictionary<EntityToken, ProjectedCommandEntity>(commands.Length);
            var projections = new List<ProjectedCommandEntity>(commands.Length);
            var projectedValues = new Dictionary<(Entity Entity, Type ComponentType), ComponentInitialization>(commands.Length);
            var createdValues = new Dictionary<EntityToken, Dictionary<Type, ComponentInitialization>>(commands.Length);
            var createdEntityHandles = new HashSet<Entity>(commands.Length);
            var destroyedEntityHandles = new HashSet<Entity>(commands.Length);
            var destroyedOwnerIds = new HashSet<Guid>(commands.Length);

            void ValidateCommandInitialization(
                IComponentRegistration registration,
                ComponentInitialization initialization,
                bool allowProjectedEntityTargets = false)
            {
                var previousProjectedHandles = _activeCommandProjectedEntityHandles;

                if (allowProjectedEntityTargets)
                {
                    _activeCommandProjectedEntityHandles = createdEntityHandles;
                }

                try
                {
                    ThrowIfValidationFailed(
                        registration.ValidateInitialization(initialization, CancellationToken.None),
                        "value",
                        "A staged component value failed its registered validation.");
                    registration.ValidateEntityReferencesInitialization(
                        initialization,
                        this,
                        excludedEntity: null,
                        parameterName: "value");
                }
                finally
                {
                    _activeCommandProjectedEntityHandles = previousProjectedHandles;
                }
            }

            void SetProjectedValue(
                ProjectedCommandEntity projection,
                Type componentType,
                ComponentInitialization initialization)
            {
                if (projection.Description is not null)
                {
                    createdValues[projection.Token][componentType] = initialization;
                }
                else
                {
                    projectedValues[(projection.Entity, componentType)] = initialization;
                }
            }

            void RemoveProjectedValue(ProjectedCommandEntity projection, Type componentType)
            {
                if (projection.Description is not null)
                {
                    _ = createdValues[projection.Token].Remove(componentType);
                }
                else
                {
                    _ = projectedValues.Remove((projection.Entity, componentType));
                }
            }

            bool TryGetProjectedValue(
                ProjectedCommandEntity projection,
                Type componentType,
                out ComponentInitialization? initialization)
            {
                if (projection.Description is not null)
                {
                    return createdValues[projection.Token].TryGetValue(componentType, out initialization);
                }

                return projectedValues.TryGetValue((projection.Entity, componentType), out initialization);
            }

            Entity? GetProjectedReferenceTarget(
                WorldCommandStaging.CommandRecord command,
                Dictionary<EntityToken, ProjectedCommandEntity> tokenProjections)
            {
                if (command.ReferenceTargetToken is not { } targetToken)
                {
                    return command.ReferenceTarget;
                }

                if (!tokenProjections.TryGetValue(targetToken, out var projection))
                {
                    throw new InvalidOperationException("A staged Entity-reference target token has no earlier Create.");
                }

                return projection.Entity;
            }

            void ValidateProjectedEntityReferences()
            {
                var previousProjectedHandles = _activeCommandProjectedEntityHandles;
                var previousExcludedHandles = _activeCommandExcludedEntityHandles;
                _activeCommandProjectedEntityHandles = createdEntityHandles;
                _activeCommandExcludedEntityHandles = destroyedEntityHandles;

                try
                {
                    foreach (var projection in projections)
                    {
                        if (projection.IsDestroyed)
                        {
                            continue;
                        }

                        foreach (var componentType in projection.Components)
                        {
                            var registration = FindComponentRegistration(componentType);

                            if (TryGetProjectedValue(projection, componentType, out var initialization)
                                && (initialization is { } value))
                            {
                                registration.ValidateEntityReferencesInitialization(
                                    value,
                                    this,
                                    excludedEntity: null,
                                    parameterName: null);
                                continue;
                            }

                            var source = projection.Source
                                ?? throw new InvalidOperationException("A projected component has no prepared initialization.");
                            var componentIndex = source.GetComponentIndex(componentType);

                            if (componentIndex < 0)
                            {
                                throw new InvalidOperationException("A projected component has no source row or prepared value.");
                            }

                            source.Storage.ValidateEntityReferencesColumnRow(
                                componentIndex,
                                projection.SourceChunkIndex,
                                projection.SourceRow,
                                this,
                                excludedEntity: null,
                                parameterName: null);
                        }
                    }

                    for (var archetypeIndex = 0; archetypeIndex < _archetypeCount; archetypeIndex++)
                    {
                        var archetype = _archetypes[archetypeIndex]
                            ?? throw new InvalidOperationException("The World archetype directory contains an empty active slot.");

                        for (var chunkIndex = 0; chunkIndex < archetype.Storage.ChunkCount; chunkIndex++)
                        {
                            if (!archetype.Storage.TryGetChunkOrdinal(chunkIndex, out _))
                            {
                                continue;
                            }

                            var entities = archetype.Storage.GetEntities(chunkIndex);

                            for (var row = 0; row < entities.Length; row++)
                            {
                                if (projectionsByEntity.ContainsKey(entities[row]))
                                {
                                    continue;
                                }

                                for (var componentIndex = 0; componentIndex < archetype.Components.Length; componentIndex++)
                                {
                                    archetype.Storage.ValidateEntityReferencesColumnRow(
                                        componentIndex,
                                        chunkIndex,
                                        row,
                                        this,
                                        excludedEntity: null,
                                        parameterName: null);
                                }
                            }
                        }
                    }

                    if (destroyedOwnerIds.Count != 0)
                    {
                        ValidateAttachmentEntityReferencesExcludingOwners(destroyedOwnerIds);
                    }
                }
                finally
                {
                    _activeCommandProjectedEntityHandles = previousProjectedHandles;
                    _activeCommandExcludedEntityHandles = previousExcludedHandles;
                }
            }

            foreach (var command in commands)
            {
                if (command.Kind == WorldCommandStaging.CommandKind.CreateEntity)
                {
                    if (command.IsCanceled)
                    {
                        continue;
                    }

                    var description = command.Description
                        ?? throw new InvalidOperationException("A staged Create command has no EntityDescription.");
                    ValidateEntityDescription(description);
                    var membershipBytes = checked(
                        256L
                        + ((long)(description.Components.Length + description.Tags.Length) * 32L));
                    ReservePlanBytes(membershipBytes);
                    var components = new HashSet<Type>(description.Components.Length);
                    var componentValues = new Dictionary<Type, ComponentInitialization>(description.Components.Length);

                    foreach (var requestedInitialization in description.Components)
                    {
                        var registration = FindComponentRegistration(requestedInitialization.ComponentType);
                        var initialization = registration.CreateInitializedValue(requestedInitialization);
                        ValidateCommandInitialization(registration, initialization);
                        _ = components.Add(registration.ComponentType);
                        componentValues.Add(registration.ComponentType, initialization);
                    }

                    var tags = new HashSet<Guid>(description.Tags.Length);

                    foreach (var tag in description.Tags)
                    {
                        _ = tags.Add(tag);
                    }

                    if (!_entitySlots.TryGetAllocationPreview(
                            createdProjections.Count,
                            out var predictedSlot,
                            out var predictedGeneration))
                    {
                        throw new InvalidOperationException("The configured Entity-slot limit was reached during command playback.");
                    }

                    var createProjection = new ProjectedCommandEntity(
                        new Entity(_ownerId, predictedSlot, predictedGeneration),
                        source: null,
                        sourceChunkIndex: -1,
                        sourceRow: -1,
                        components,
                        tags,
                        command.Token,
                        description);
                    createdProjections.Add(createProjection);
                    projections.Add(createProjection);
                    projectionsByToken.Add(command.Token, createProjection);
                    _ = createdEntityHandles.Add(createProjection.Entity);
                    createdValues.Add(command.Token, componentValues);
                    continue;
                }

                if ((command.Kind == WorldCommandStaging.CommandKind.DestroyToken)
                    || ((command.EntityTokenTarget is { } entityTokenTarget)
                        && staging.IsTokenCanceled(entityTokenTarget))
                    || ((command.OwnerToken is { } ownerToken)
                        && staging.IsTokenCanceled(ownerToken)))
                {
                    continue;
                }

                ProjectedCommandEntity projection;

                var commandOwnerToken = command.OwnerToken ?? command.EntityTokenTarget;

                if (commandOwnerToken is { } targetToken)
                {
                    if (!projectionsByToken.TryGetValue(targetToken, out var tokenProjection))
                    {
                        throw new InvalidOperationException("A staged token command does not resolve to an earlier Create.");
                    }

                    projection = tokenProjection;
                }
                else if (projectionsByEntity.TryGetValue(command.Entity, out var liveProjection))
                {
                    projection = liveProjection;
                }
                else
                {
                    var source = GetLiveArchetype(command.Entity, out var sourceChunkIndex, out var sourceRow);
                    var membershipBytes = checked(
                        256L
                        + ((long)(source.Components.Length + source.Tags.Length) * 32L));
                    ReservePlanBytes(membershipBytes);
                    var components = new HashSet<Type>(source.Components.Length);

                    foreach (var component in source.Components)
                    {
                        _ = components.Add(component.ComponentType);
                    }

                    var tags = new HashSet<Guid>(source.Tags.Length);

                    foreach (var tag in source.Tags)
                    {
                        _ = tags.Add(tag);
                    }

                    projection = new ProjectedCommandEntity(
                        command.Entity,
                        source,
                        sourceChunkIndex,
                        sourceRow,
                        components,
                        tags,
                        token: default,
                        description: null);
                    projectionsByEntity.Add(command.Entity, projection);
                    projections.Add(projection);
                }

                if (projection.IsDestroyed)
                {
                    throw new InvalidOperationException("A staged command targets an Entity already destroyed in this batch.");
                }

                if (command.Kind == WorldCommandStaging.CommandKind.DestroyEntity)
                {
                    var identityOrdinal = _identityLedger.GetLiveOrdinal(IdentityKind.Entity, projection.Entity.Index);
                    var entityId = _identityLedger.GetAssignedId(IdentityKind.Entity, projection.Entity.Index);

                    if ((identityOrdinal == 0) || (entityId == Guid.Empty))
                    {
                        throw new InvalidOperationException("The live Entity has no stable identity assignment.");
                    }

                    var identityBranchIndex = _identityLedger.GetBranchIndexForOrdinal(identityOrdinal);
                    projection.IdentityOrdinal = identityOrdinal;
                    projection.IdentityBranchIndex = identityBranchIndex;
                    projection.IdentityId = entityId;
                    projection.IsDestroyed = true;
                    destroyedProjections.Add(projection);
                    _ = destroyedEntityHandles.Add(projection.Entity);
                    _ = destroyedOwnerIds.Add(entityId);
                    command.DestroyedEntityIdentities =
                    [
                        new WorldEntityIdentity(
                            _worldGuid,
                            _replayLineageId,
                            _identityBranches[identityBranchIndex].BranchId,
                            entityId),
                    ];
                    continue;
                }

                switch (command.Kind)
                {
                    case WorldCommandStaging.CommandKind.AddComponent:
                    {
                        var registration = command.Registration
                            ?? throw new InvalidOperationException("A staged component Add command has no registration.");

                        if (!projection.Components.Add(registration.ComponentType))
                        {
                            throw new InvalidOperationException("A staged Add command targets an Entity that already has the component.");
                        }

                        var requestedInitialization = command.Initialization
                            ?? throw new InvalidOperationException("A staged component Add command has no explicit value.");
                        var initialization = registration.CreateInitializedValue(requestedInitialization);
                        ValidateCommandInitialization(registration, initialization);
                        SetProjectedValue(projection, registration.ComponentType, initialization);
                        break;
                    }

                    case WorldCommandStaging.CommandKind.AddTag:
                    {
                        if (!projection.Tags.Add(command.SchemaId))
                        {
                            throw new InvalidOperationException("A staged AddTag command targets an Entity that already has the tag.");
                        }

                        break;
                    }

                    case WorldCommandStaging.CommandKind.RemoveComponent:
                    {
                        var registration = command.Registration
                            ?? throw new InvalidOperationException("A staged component Remove command has no registration.");

                        if (!projection.Components.Remove(registration.ComponentType))
                        {
                            throw new InvalidOperationException("A staged Remove command targets an Entity without the component.");
                        }

                        RemoveProjectedValue(projection, registration.ComponentType);
                        break;
                    }

                    case WorldCommandStaging.CommandKind.RemoveTag:
                    {
                        if (!projection.Tags.Remove(command.SchemaId))
                        {
                            throw new InvalidOperationException("A staged RemoveTag command targets an Entity without the tag.");
                        }

                        break;
                    }

                    case WorldCommandStaging.CommandKind.Set:
                    {
                        var registration = command.Registration
                            ?? throw new InvalidOperationException("A staged Set command has no component registration.");

                        if (!projection.Components.Contains(registration.ComponentType))
                        {
                            throw new InvalidOperationException("A staged Set command targets an Entity without the component.");
                        }

                        var initialization = command.Initialization
                            ?? throw new InvalidOperationException("A staged Set command has no explicit component value.");
                        ValidateCommandInitialization(registration, initialization);
                        SetProjectedValue(projection, registration.ComponentType, initialization);
                        break;
                    }

                    case WorldCommandStaging.CommandKind.SetEntityReference:
                    {
                        var registration = command.Registration
                            ?? throw new InvalidOperationException("A staged Entity-reference command has no component registration.");

                        if (!projection.Components.Contains(registration.ComponentType))
                        {
                            throw new InvalidOperationException("A staged Entity-reference command targets an Entity without the component.");
                        }

                        ComponentInitialization current;

                        if (TryGetProjectedValue(projection, registration.ComponentType, out var currentValue)
                            && (currentValue is not null))
                        {
                            current = currentValue;
                        }
                        else
                        {
                            var source = projection.Source
                                ?? throw new InvalidOperationException("A token-created reference command has no source component row.");
                            var componentIndex = source.GetComponentIndex(registration.ComponentType);

                            if (componentIndex < 0)
                            {
                                throw new InvalidOperationException("The projected Entity-reference component has no source row.");
                            }

                            current = source.Storage.CaptureColumnRow(
                                componentIndex,
                                projection.SourceChunkIndex,
                                projection.SourceRow);
                        }

                        var initialization = registration.SetEntityReference(
                            current,
                            command.Path
                                ?? throw new InvalidOperationException("A staged Entity-reference command has no field path."),
                            GetProjectedReferenceTarget(command, projectionsByToken));
                        ValidateCommandInitialization(
                            registration,
                            initialization,
                            allowProjectedEntityTargets: command.ReferenceTargetToken is not null);
                        SetProjectedValue(projection, registration.ComponentType, initialization);
                        break;
                    }

                    default:
                    {
                        throw new InvalidOperationException("The staged command kind is not admitted for playback.");
                    }
                }
            }

            if (destroyedProjections.Count != 0)
            {
                ValidateProjectedEntityReferences();
            }

            var nextMutationRevision = checked(_canonicalMutationRevision + 1);

            if ((createdProjections.Count != 0) || (destroyedProjections.Count != 0))
            {
                var createCount = createdProjections.Count;
                var destroyCount = destroyedProjections.Count;
                var liveEntityCountAfterDestroy = _entitySlots.LiveEntityCount - destroyCount;

                if (createCount > (_maximumLiveEntities - liveEntityCountAfterDestroy))
                {
                    throw new InvalidOperationException("The configured live Entity limit was reached during command playback.");
                }

                var additionalAllocatedSlots = Math.Max(0, createCount - _entitySlots.FreeSlotCount);
                var requiredAllocatedSlots = checked(_entitySlots.AllocatedSlotCount + additionalAllocatedSlots);
                var freeSlotsUsedByCreates = Math.Min(createCount, _entitySlots.FreeSlotCount);
                var reusableDestroyedSlots = 0;

                foreach (var projection in destroyedProjections)
                {
                    reusableDestroyedSlots += projection.Entity.Generation == uint.MaxValue ? 0 : 1;
                }

                var requiredFreeSlotCount = checked(
                    _entitySlots.FreeSlotCount
                    - freeSlotsUsedByCreates
                    + reusableDestroyedSlots);
                var slotPreparationBytes = _entitySlots.GetCapacityPreparationBytes(
                    requiredAllocatedSlots,
                    requiredFreeSlotCount);
                var identityPreparationBytes = _identityLedger.GetPreparationByteReservation(createCount, destroyCount);
                var identityGrowthBytes = checked(identityPreparationBytes - _identityLedger.OwnedByteReservation);
                ReservePlanBytes(checked(slotPreparationBytes + identityGrowthBytes));
                var assignments = new IdentityAssignment[createCount];
                var retirements = new uint[destroyCount];

                for (var index = 0; index < createCount; index++)
                {
                    if (!_entitySlots.TryGetAllocationPreview(index, out var slot, out var generation))
                    {
                        throw new InvalidOperationException("The configured Entity-slot limit was reached during command playback.");
                    }

                    var projection = createdProjections[index];
                    var description = projection.Description
                        ?? throw new InvalidOperationException("A token-created Entity has no EntityDescription.");
                    projection.Entity = new Entity(_ownerId, slot, generation);

                    assignments[index] = (description.PersistentId is { } persistentId)
                        ? new IdentityAssignment(
                            IdentityKind.Entity,
                            IdentityOrigin.Explicit,
                            slot,
                            _currentIdentityBranchIndex,
                            persistentId)
                        : new IdentityAssignment(
                            IdentityKind.Entity,
                            IdentityOrigin.Automatic,
                            slot,
                            _currentIdentityBranchIndex,
                            issuanceNamespace: _identityBranches[_currentIdentityBranchIndex].NamespaceId);
                }

                for (var index = 0; index < destroyCount; index++)
                {
                    retirements[index] = destroyedProjections[index].IdentityOrdinal;
                }

                identityChange = _identityLedger.Prepare(assignments, retirements, _maximumOwnedStateBytes);
                identityChange.ValidateForCommit();

                if (!_entitySlots.PrepareCapacity(requiredAllocatedSlots, requiredFreeSlotCount))
                {
                    throw new InvalidOperationException("The preflighted Entity-slot capacity could not be prepared.");
                }
            }

            if (destroyedProjections.Count != 0)
            {
                var attachmentReleaseCount = 0;

                foreach (var projection in destroyedProjections)
                {
                    attachmentReleaseCount = checked(
                        attachmentReleaseCount
                        + _attachments.CountTransferredOwner(projection.IdentityId));
                }

                if (attachmentReleaseCount != 0)
                {
                    ReservePlanBytes(checked(64L + ((long)attachmentReleaseCount * 256L)));
                    var detachedAttachmentList = new List<WorldAttachmentTable.AttachmentEntry>(attachmentReleaseCount);

                    foreach (var projection in destroyedProjections)
                    {
                        detachedAttachmentList.AddRange(
                            _attachments.CaptureTransferredOwnerEntries(projection.IdentityId));
                    }

                    detachedAttachments = [.. detachedAttachmentList];

                    if (detachedAttachments.Length != attachmentReleaseCount)
                    {
                        throw new InvalidOperationException("The prepared attachment release count changed during command playback.");
                    }

                    releaseBindings = PrepareAttachmentReleaseBindings(detachedAttachments);
                    quarantineReservation = WorldAttachmentInstanceRegistry.ReserveQuarantineCapacity(detachedAttachments.Length);
                    releasePreparation = new WorldAttachmentReleasePreparation(
                        _worldGuid,
                        detachedAttachments.Length,
                        nextMutationRevision);
                }
            }

            foreach (var projection in projections)
            {
                if (projection.IsDestroyed)
                {
                    continue;
                }

                var source = projection.Source;
                var componentsMatch = (source is not null) && MatchesProjectedComponents(projection);
                var tagsMatch = (source is not null) && projection.Tags.SetEquals(source.Tags);

                if (componentsMatch && tagsMatch)
                {
                    projection.Target = source
                        ?? throw new InvalidOperationException("A token-created Entity cannot use its source archetype.");
                    continue;
                }

                var componentArrayBytes = componentsMatch
                    ? 0
                    : GetArrayByteReservation(projection.Components.Count, IntPtr.Size);
                var tagArrayBytes = tagsMatch
                    ? 0
                    : GetArrayByteReservation(projection.Tags.Count, Unsafe.SizeOf<Guid>());
                ReservePlanBytes(checked(componentArrayBytes + tagArrayBytes));
                var components = componentsMatch
                    ? (source ?? throw new InvalidOperationException("A projected component set has no source archetype.")).Components
                    : BuildProjectedComponents(projection.Components);
                var tags = tagsMatch
                    ? (source ?? throw new InvalidOperationException("A projected tag set has no source archetype.")).Tags
                    : BuildProjectedTags(projection.Tags);
                var target = FindArchetype(components, tags);

                if (target is null)
                {
                    foreach (var candidate in candidateArchetypes)
                    {
                        if (candidate.MatchesComponentsAndTags(components, tags))
                        {
                            target = candidate;
                            break;
                        }
                    }
                }

                if (target is null)
                {
                    if ((_archetypeCount + candidateArchetypes.Count) >= _maximumArchetypes)
                    {
                        throw new InvalidOperationException("The configured archetype limit was reached during command playback.");
                    }

                    var metadataBytes = WorldArchetype.GetMetadataByteReservation(components.Length, tags.Length);
                    ReservePlanBytes(checked(metadataBytes - componentArrayBytes - tagArrayBytes));
                    target = new WorldArchetype(
                        _ownerId,
                        _archetypeCount + candidateArchetypes.Count,
                        _maximumLiveEntities,
                        components,
                        tags);
                    candidateArchetypes.Add(target);
                }

                projection.Target = target;
            }

            var transitions = new List<PreparedCommandTransition>(projections.Count);

            foreach (var projection in projections)
            {
                if (projection.IsDestroyed)
                {
                    continue;
                }

                var target = projection.Target
                    ?? throw new InvalidOperationException("The command projection has no destination archetype.");

                if ((projection.Source is not null) && ReferenceEquals(target, projection.Source))
                {
                    continue;
                }

                ValidateDerivedDependencies(target.Components);
                var initializationBytes = checked(
                    GetArrayByteReservation(target.Components.Length, IntPtr.Size)
                    + ((long)target.Components.Length * 128L)
                    + 128L);
                ReservePlanBytes(initializationBytes);
                var initializations = new ComponentInitialization[target.Components.Length];

                for (var componentIndex = 0; componentIndex < target.Components.Length; componentIndex++)
                {
                    var registration = target.Components[componentIndex];
                    ComponentInitialization? projectedInitialization = null;

                    if (projection.Description is not null)
                    {
                        _ = createdValues[projection.Token].TryGetValue(
                            registration.ComponentType,
                            out projectedInitialization);
                    }
                    else
                    {
                        _ = projectedValues.TryGetValue(
                            (projection.Entity, registration.ComponentType),
                            out projectedInitialization);
                    }

                    ComponentInitialization initialization;

                    if (projectedInitialization is { } value)
                    {
                        initialization = value;
                    }
                    else
                    {
                        var source = projection.Source
                            ?? throw new InvalidOperationException("A token-created component has no prepared initialization.");
                        var sourceIndex = source.GetComponentIndex(registration.ComponentType);

                        if (sourceIndex < 0)
                        {
                            throw new InvalidOperationException("The destination archetype contains a component without a projected value.");
                        }

                        initialization = source.Storage.CaptureColumnRow(
                            sourceIndex,
                            projection.SourceChunkIndex,
                            projection.SourceRow);
                    }

                    if (initialization.IsDefault)
                    {
                        throw new InvalidOperationException("Command playback requires resolved component values before installation.");
                    }

                    initializations[componentIndex] = initialization;
                }

                transitions.Add(new PreparedCommandTransition(projection, target, initializations));
            }

            var storageGroups = new Dictionary<WorldArchetype, CommandStorageGroup>(checked(transitions.Count * 2));

            foreach (var transition in transitions)
            {
                if (transition.Projection.Source is { } source)
                {
                    var sourceGroup = GetStorageGroup(storageGroups, source);
                    sourceGroup.OutgoingEntityCount = checked(sourceGroup.OutgoingEntityCount + 1);
                    sourceGroup.RemovedEntityCounts[transition.Projection.SourceChunkIndex] = checked(
                        sourceGroup.RemovedEntityCounts[transition.Projection.SourceChunkIndex] + 1);
                }

                var targetGroup = GetStorageGroup(storageGroups, transition.Target);
                targetGroup.IncomingEntityCount = checked(targetGroup.IncomingEntityCount + 1);
            }

            foreach (var projection in destroyedProjections)
            {
                var source = projection.Source
                    ?? throw new InvalidOperationException("A destroyed live Entity has no source archetype.");
                var sourceGroup = GetStorageGroup(storageGroups, source);
                sourceGroup.OutgoingEntityCount = checked(sourceGroup.OutgoingEntityCount + 1);
                sourceGroup.RemovedEntityCounts[projection.SourceChunkIndex] = checked(
                    sourceGroup.RemovedEntityCounts[projection.SourceChunkIndex] + 1);
            }

            var totalStoragePreparationBytes = 0L;

            foreach (var group in storageGroups.Values)
            {
                var requiredEntityCount = checked(
                    group.Archetype.Storage.EntityCount
                    - group.OutgoingEntityCount
                    + group.IncomingEntityCount);
                group.RequiredEntityCount = requiredEntityCount;
                var capacityEntityCount = Math.Max(group.Archetype.Storage.EntityCount, requiredEntityCount);

                if (capacityEntityCount != 0)
                {
                    totalStoragePreparationBytes = checked(
                        totalStoragePreparationBytes
                        + group.Archetype.Storage.GetCapacityPreparationBytes(capacityEntityCount));
                }
            }

            ReservePlanBytes(totalStoragePreparationBytes);

            var newChunkOrdinalCount = 0L;

            foreach (var group in storageGroups.Values)
            {
                if ((group.RequiredEntityCount != 0)
                    && !group.Archetype.Storage.PrepareCapacity(
                        Math.Max(group.Archetype.Storage.EntityCount, group.RequiredEntityCount)))
                {
                    throw new InvalidOperationException("The preflighted command destination capacity could not be prepared.");
                }

                newChunkOrdinalCount = checked(
                    newChunkOrdinalCount
                    + group.Archetype.Storage.CountNewChunkOrdinalAssignments(
                        group.RemovedEntityCounts,
                        group.IncomingEntityCount));
            }

            if (newChunkOrdinalCount > (long.MaxValue - _nextChunkOrdinal))
            {
                throw new InvalidOperationException("The shared World chunk-ordinal cursor is exhausted.");
            }

            var projectedWorkPlanCount = _componentTagWorkPlanCount;

            foreach (var group in storageGroups.Values)
            {
                projectedWorkPlanCount = checked(
                    projectedWorkPlanCount
                    - group.Archetype.Storage.GetPartitionBlockCount()
                    + group.Archetype.Storage.CountPartitionBlocksAfter(
                        group.RemovedEntityCounts,
                        group.IncomingEntityCount));
            }

            var candidateWorkPlanCapacity = GetComponentTagWorkPlanCapacityFor(projectedWorkPlanCount);

            if (candidateWorkPlanCapacity > _componentTagWorkPlan.Length)
            {
                candidateWorkPlanByteReservation = CalculateComponentTagWorkPlanByteReservation(candidateWorkPlanCapacity);
                ReservePlanBytes(candidateWorkPlanByteReservation);
                candidateWorkPlan = new WorldChunkWorkPlanEntry[candidateWorkPlanCapacity];
            }

            ReservePlanBytes(GetArrayByteReservation(createdProjections.Count, 64));
            ReservePlanBytes(GetArrayByteReservation(
                destroyedProjections.Count,
                Unsafe.SizeOf<WorldEntityIdentity>() * 3));
            var resolvedTokens = new Dictionary<EntityToken, Entity>(createdProjections.Count);

            foreach (var projection in createdProjections)
            {
                resolvedTokens.Add(projection.Token, projection.Entity);
            }

            var receipts = staging.CreateReceipts(resolvedTokens);
            identityChange?.ValidateForCommit();

            foreach (var transition in transitions)
            {
                if (transition.Projection.Source is { } source)
                {
                    _ = source.Storage.Remove(transition.Projection.Entity, _entitySlots);
                }
            }

            foreach (var projection in destroyedProjections)
            {
                var source = projection.Source
                    ?? throw new InvalidOperationException("A destroyed live Entity has no source archetype.");
                _ = source.Storage.Remove(projection.Entity, _entitySlots);
            }

            foreach (var projection in createdProjections)
            {
                if (!_entitySlots.TryAllocate(out var slot, out var generation)
                    || (projection.Entity.Index != slot)
                    || (projection.Entity.Generation != generation))
                {
                    throw new InvalidOperationException("The preflighted Entity slot changed before command commit.");
                }
            }

            foreach (var projection in destroyedProjections)
            {
                _entitySlots.Release(projection.Entity.Index, projection.Entity.Generation);
            }

            foreach (var transition in transitions)
            {
                if (!transition.Target.Storage.TryGetNextAppendLocation(
                        _nextChunkOrdinal,
                        out var targetChunkIndex,
                        out var targetRow))
                {
                    throw new InvalidOperationException("The preflighted destination row could not be committed.");
                }

                for (var componentIndex = 0; componentIndex < transition.Initializations.Length; componentIndex++)
                {
                    transition.Target.Storage.InitializeColumnRow(
                        componentIndex,
                        targetChunkIndex,
                        targetRow,
                        transition.Initializations[componentIndex]);
                }

                if (!transition.Target.Storage.TryAppend(transition.Projection.Entity, _entitySlots, ref _nextChunkOrdinal))
                {
                    throw new InvalidOperationException("The preflighted destination row could not be committed.");
                }
            }

            foreach (var candidate in candidateArchetypes)
            {
                InstallArchetype(candidate);
                installedCandidateCount++;
            }

            identityChange?.Commit();

            foreach (var projection in destroyedProjections)
            {
                _attachments.RemoveOwner(projection.IdentityId);
            }

            foreach (var projection in projections)
            {
                if (projection.IsDestroyed
                    || (projection.Source is null)
                    || !ReferenceEquals(projection.Target, projection.Source))
                {
                    continue;
                }

                foreach (var registration in Registration.Components)
                {
                    if (!projectedValues.TryGetValue((projection.Entity, registration.ComponentType), out var initialization))
                    {
                        continue;
                    }

                    var archetype = GetLiveArchetype(projection.Entity, out var chunkIndex, out var row);
                    var componentIndex = archetype.GetComponentIndex(registration.ComponentType);

                    if (componentIndex < 0)
                    {
                        throw new InvalidOperationException("A projected component write targets an Entity without its component.");
                    }

                    archetype.Storage.WriteColumnRow(componentIndex, chunkIndex, row, initialization);
                }
            }

            _canonicalMutationRevision = nextMutationRevision;

            if ((createdProjections.Count != 0)
                || (destroyedProjections.Count != 0)
                || (transitions.Count != 0))
            {
                if (candidateWorkPlan is not null)
                {
                    _componentTagWorkPlan = candidateWorkPlan;
                    _componentTagWorkPlanByteReservation = candidateWorkPlanByteReservation;
                }

                RebuildComponentTagWorkPlan();
            }

            if (detachedAttachments.Length != 0)
            {
                var preparedRelease = releasePreparation
                    ?? throw new InvalidOperationException("The transferred attachment release report was not prepared.");
                ReleaseAttachmentEntries(
                    detachedAttachments,
                    releaseBindings,
                    quarantineReservation
                        ?? throw new InvalidOperationException("The transferred attachment quarantine was not prepared."),
                    preparedRelease);

                if (preparedRelease.HasFailures)
                {
                    throw preparedRelease.CreateException(systemCleanupReport: null);
                }
            }

            result = receipts;
        }
        catch (Exception exception)
        {
            operationFailure = ExceptionDispatchInfo.Capture(exception);
        }

        try
        {
            Cleanup();
        }
        catch (Exception cleanupException)
        {
            if (operationFailure is null)
            {
                throw;
            }

            throw new AggregateException(
                "Command playback failed and its preparation cleanup also failed.",
                operationFailure.SourceException,
                cleanupException);
        }

        operationFailure?.Throw();
        return result;

        void Cleanup()
        {
            try
            {
                for (var candidateIndex = installedCandidateCount; candidateIndex < candidateArchetypes.Count; candidateIndex++)
                {
                    var candidate = candidateArchetypes[candidateIndex];

                    if (candidate.Storage.EntityCount == 0)
                    {
                        candidate.DiscardUncommittedStorage();
                    }
                }

                identityChange?.Dispose();

                if (reservedBytes == 0)
                {
                    return;
                }

                var persistentGrowthBytes = checked(GetWorldOwnedStateBytes() - oldOwnedBytes);
                var temporaryBytes = reservedBytes - persistentGrowthBytes;

                if (temporaryBytes > 0)
                {
                    _domainReservation.ReleaseAdditional(temporaryBytes);
                }
                else if (temporaryBytes < 0 && !_domainReservation.TryReserveAdditional(-temporaryBytes))
                {
                    throw new InvalidOperationException("The admission domain cannot retain command playback storage.");
                }
            }
            finally
            {
                quarantineReservation?.Release();
            }
        }

        void ReservePlanBytes(long bytes)
        {
            if (bytes <= 0)
            {
                return;
            }

            if (!TryReserveCommandPlaybackBytes(bytes, reservedBytes, commandStagingBytes))
            {
                throw new InvalidOperationException("The World-owned state budget cannot admit command playback preparation.");
            }

            reservedBytes = checked(reservedBytes + bytes);
        }

        ImmutableArray<IComponentRegistration> BuildProjectedComponents(HashSet<Type> componentTypes)
        {
            var builder = ImmutableArray.CreateBuilder<IComponentRegistration>(componentTypes.Count);

            foreach (var registration in Registration.Components)
            {
                if (componentTypes.Contains(registration.ComponentType))
                {
                    builder.Add(registration);
                }
            }

            if (builder.Count != componentTypes.Count)
            {
                throw new InvalidOperationException("The component shadow contains an unregistered component.");
            }

            return builder.MoveToImmutable();
        }

        ImmutableArray<Guid> BuildProjectedTags(HashSet<Guid> tagIds)
        {
            var builder = ImmutableArray.CreateBuilder<Guid>(tagIds.Count);

            foreach (var registration in Registration.Tags)
            {
                if (tagIds.Contains(registration.SchemaId))
                {
                    builder.Add(registration.SchemaId);
                }
            }

            if (builder.Count != tagIds.Count)
            {
                throw new InvalidOperationException("The tag shadow contains an unregistered schema identity.");
            }

            return builder.MoveToImmutable();
        }

        bool MatchesProjectedComponents(ProjectedCommandEntity projection)
        {
            var source = projection.Source
                ?? throw new InvalidOperationException("A token-created Entity has no source component membership.");

            if (projection.Components.Count != source.Components.Length)
            {
                return false;
            }

            foreach (var registration in source.Components)
            {
                if (!projection.Components.Contains(registration.ComponentType))
                {
                    return false;
                }
            }

            return true;
        }

        CommandStorageGroup GetStorageGroup(
            Dictionary<WorldArchetype, CommandStorageGroup> groups,
            WorldArchetype archetype)
        {
            if (groups.TryGetValue(archetype, out var group))
            {
                return group;
            }

            var chunkCount = archetype.Storage.ChunkCount;
            ReservePlanBytes(GetArrayByteReservation(chunkCount, sizeof(int)));
            var removedEntityCounts = new int[chunkCount];
            group = new CommandStorageGroup(archetype, removedEntityCounts);
            groups.Add(archetype, group);
            return group;
        }
    }

    private int GetGlobalPartitionOrdinal(WorldArchetype targetArchetype, int targetChunkIndex, int targetRow)
    {
        var globalPartitionOrdinal = 0;

        for (var archetypeOrdinal = 0; archetypeOrdinal < _archetypeCount; archetypeOrdinal++)
        {
            var archetype = _queryArchetypes[archetypeOrdinal]
                ?? throw new InvalidOperationException("The World query-archetype order contains an empty active slot.");

            for (var chunkIndex = 0; chunkIndex < archetype.Storage.ChunkCount; chunkIndex++)
            {
                if (!archetype.Storage.TryGetChunkOrdinal(chunkIndex, out _))
                {
                    continue;
                }

                var entities = archetype.Storage.GetEntities(chunkIndex);

                if (entities.IsEmpty)
                {
                    continue;
                }

                if (ReferenceEquals(archetype, targetArchetype) && (chunkIndex == targetChunkIndex))
                {
                    ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(targetRow, entities.Length);
                    return checked(globalPartitionOrdinal + (targetRow / 64));
                }

                globalPartitionOrdinal = checked(globalPartitionOrdinal + ((entities.Length + 63) / 64));
            }
        }

        throw new InvalidOperationException("The live Entity is not present in the World query-partition plan.");
    }

    private void ValidateWorldEntityReferences()
    {
        for (var archetypeIndex = 0; archetypeIndex < _archetypeCount; archetypeIndex++)
        {
            var archetype = _archetypes[archetypeIndex]
                ?? throw new InvalidOperationException("The World archetype directory contains an empty active slot.");

            for (var chunkIndex = 0; chunkIndex < archetype.Storage.ChunkCount; chunkIndex++)
            {
                if (!archetype.Storage.TryGetChunkOrdinal(chunkIndex, out _))
                {
                    continue;
                }

                var entities = archetype.Storage.GetEntities(chunkIndex);

                for (var row = 0; row < entities.Length; row++)
                {
                    for (var componentIndex = 0; componentIndex < archetype.Components.Length; componentIndex++)
                    {
                        archetype.Storage.ValidateEntityReferencesColumnRow(
                            componentIndex,
                            chunkIndex,
                            row,
                            this,
                            excludedEntity: null,
                            parameterName: null);
                    }
                }
            }
        }

        if (_attachments.HasTransferredEntries)
        {
            ValidateAllAttachmentEntityReferences();
        }
    }

    private sealed class CommandStorageGroup
    {
        internal CommandStorageGroup(WorldArchetype archetype, int[] removedEntityCounts)
        {
            ArgumentNullException.ThrowIfNull(archetype);
            ArgumentNullException.ThrowIfNull(removedEntityCounts);
            Archetype = archetype;
            RemovedEntityCounts = removedEntityCounts;
        }

        internal WorldArchetype Archetype { get; }

        internal int IncomingEntityCount { get; set; }

        internal int OutgoingEntityCount { get; set; }

        internal int[] RemovedEntityCounts { get; }

        internal int RequiredEntityCount { get; set; }
    }

    private sealed class PreparedCommandTransition
    {
        internal PreparedCommandTransition(
            ProjectedCommandEntity projection,
            WorldArchetype target,
            ComponentInitialization[] initializations)
        {
            ArgumentNullException.ThrowIfNull(projection);
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(initializations);
            Initializations = initializations;
            Projection = projection;
            Target = target;
        }

        internal ComponentInitialization[] Initializations { get; }

        internal ProjectedCommandEntity Projection { get; }

        internal WorldArchetype Target { get; }
    }

    private sealed class ProjectedCommandEntity
    {
        internal ProjectedCommandEntity(
            Entity entity,
            WorldArchetype? source,
            int sourceChunkIndex,
            int sourceRow,
            HashSet<Type> components,
            HashSet<Guid> tags,
            EntityToken token,
            EntityDescription? description)
        {
            ArgumentNullException.ThrowIfNull(components);
            ArgumentNullException.ThrowIfNull(tags);

            if ((source is null) != token.IsValid || ((description is not null) != token.IsValid))
            {
                throw new ArgumentException("A projected Entity must be either live-backed or token-created.");
            }

            Entity = entity;
            Source = source;
            SourceChunkIndex = sourceChunkIndex;
            SourceRow = sourceRow;
            Components = components;
            Tags = tags;
            Token = token;
            Description = description;
        }

        internal HashSet<Type> Components { get; }

        internal EntityDescription? Description { get; }

        internal Entity Entity { get; set; }

        internal int IdentityBranchIndex { get; set; }

        internal Guid IdentityId { get; set; }

        internal uint IdentityOrdinal { get; set; }

        internal bool IsDestroyed { get; set; }

        internal WorldArchetype? Source { get; }

        internal int SourceChunkIndex { get; }

        internal int SourceRow { get; }

        internal HashSet<Guid> Tags { get; }

        internal WorldArchetype? Target { get; set; }

        internal EntityToken Token { get; }
    }
}
