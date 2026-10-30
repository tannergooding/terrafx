// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace TerraFX.Entities;

public sealed partial class World
{
    internal void BeginSystemInvocation(WorldSystemInvocationDispatch dispatch)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        var slot = GetSystemInvocationSlot(Environment.CurrentManagedThreadId);

        if ((Volatile.Read(ref _isTickActive) == 0)
            || (Volatile.Read(ref _isStructuralOperationActive) == 0)
            || (slot < 0)
            || (Interlocked.CompareExchange(ref _activeSystemInvocations[slot], dispatch, null) is not null))
        {
            throw new InvalidOperationException("The World cannot issue this system invocation context.");
        }

        _ = Interlocked.Increment(ref _activeSystemInvocationCount);
    }

    internal void EndSystemInvocation(WorldSystemInvocationDispatch dispatch)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        var slot = GetSystemInvocationSlot(Environment.CurrentManagedThreadId);

        if ((slot < 0)
            || !ReferenceEquals(Interlocked.CompareExchange(ref _activeSystemInvocations[slot], null, dispatch), dispatch))
        {
            throw new InvalidOperationException("The World system invocation does not own its active context slot.");
        }

        if (Interlocked.Decrement(ref _activeSystemInvocationCount) < 0)
        {
            throw new InvalidOperationException("The World system invocation count became negative.");
        }
    }

    internal void EnsureSystemInvocation(WorldSystemInvocationDispatch dispatch)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        var slot = GetSystemInvocationSlot(Environment.CurrentManagedThreadId);

        if (!dispatch.IsActive
            || (Volatile.Read(ref _isTickActive) == 0)
            || (slot < 0)
            || !ReferenceEquals(Volatile.Read(ref _activeSystemInvocations[slot]), dispatch))
        {
            throw dispatch.RecordFailure(new InvalidOperationException("The World system context has expired or is not the active invocation."));
        }

        if (Environment.CurrentManagedThreadId != dispatch.OwnerThreadId)
        {
            throw dispatch.RecordFailure(new InvalidOperationException("The World system context is restricted to its issuing thread."));
        }
    }

    [SuppressMessage("Usage", "CA1031:Do not catch general exception types", Justification = "Resource callbacks and scope-close validation must preserve the first sticky invocation failure and still revoke every view.")]
    internal void ForEachResourcePartitionFromSystem<T, TAction>(
        WorldSystemInvocationDispatch dispatch,
        Guid resourceId,
        SystemPartitionRange range,
        scoped ref TAction action)
        where T : unmanaged
        where TAction : struct, IAction<WorldResourcePartitionView<T>>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        dispatch.EnsureActive();
        ArgumentOutOfRangeException.ThrowIfEqual(resourceId, Guid.Empty);

        if (!range.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(range));
        }

        if (!Registration.TryGetResource<T>(resourceId, out var resourceIndex, out var resourceRegistration)
            || (resourceRegistration is null))
        {
            throw new InvalidOperationException("The selected resource identity and type are not registered in this World.");
        }

        var access = FindSystemAccess(dispatch.Description, SystemResourceKind.Resource, resourceId);

        if (access.Mode is not SystemAccessMode.Write and not SystemAccessMode.Read)
        {
            throw new InvalidOperationException("The system does not declare valid access to this resource.");
        }

        if ((dispatch.Description.ThreadMode == SystemThreadMode.Parallel) && !resourceRegistration.AllowParallelAccess)
        {
            throw new InvalidOperationException("The registered resource does not allow access from a Parallel system.");
        }

        if (range.EndExclusive > resourceRegistration.PartitionCount)
        {
            throw new ArgumentOutOfRangeException(nameof(range), "The requested resource range exceeds its partition domain.");
        }

        ValidateDeclaredRange(access, range);
        var partitions = _resourceStorage[resourceIndex] as T[]
            ?? throw new InvalidOperationException("The selected resource backing does not match its closed partition type.");
        var partitionAccess = new SystemResourcePartitionAccess<T>(this, dispatch, partitions, range);
        var viewControl = new WorldResourcePartitionControl(partitionAccess, access.Mode == SystemAccessMode.Write);
        Exception? callbackFailure = null;
        Exception? validationFailure = null;

        try
        {
            for (var index = range.Start; index < range.EndExclusive; index++)
            {
                var view = new WorldResourcePartitionView<T>(viewControl, index);

                try
                {
                    action.Invoke(view);
                }
                catch (Exception exception)
                {
                    callbackFailure = dispatch.RecordFailure(exception);
                }

                callbackFailure ??= viewControl.Failure;
                callbackFailure ??= dispatch.Failure;

                if (callbackFailure is not null)
                {
                    break;
                }
            }

            if (access.Mode == SystemAccessMode.Write)
            {
                try
                {
                    resourceRegistration.ValidateForExecution(partitions);
                }
                catch (Exception exception)
                {
                    validationFailure = dispatch.RecordFailure(exception);
                }
            }

            callbackFailure ??= dispatch.Failure;
        }
        finally
        {
            viewControl.Revoke();
        }

        if (callbackFailure is not null)
        {
            ExceptionDispatchInfo.Capture(callbackFailure).Throw();
        }

        if (validationFailure is not null)
        {
            ExceptionDispatchInfo.Capture(validationFailure).Throw();
        }
    }

    internal Guid GetEntityIdFromSystem(WorldSystemInvocationDispatch dispatch, Entity entity)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        dispatch.EnsureActive();
        EnsureMembershipReadAccess(dispatch.Description);
        _ = GetLiveArchetype(entity, out _, out _);
        var entityId = _identityLedger.GetAssignedId(IdentityKind.Entity, entity.Index);

        if (entityId == Guid.Empty)
        {
            throw new InvalidOperationException("The live Entity has no stable identity assignment.");
        }

        return entityId;
    }

    internal WorldEntityIdentity GetEntityIdentityFromSystem(WorldSystemInvocationDispatch dispatch, Entity entity)
    {
        var entityId = GetEntityIdFromSystem(dispatch, entity);
        return new WorldEntityIdentity(_worldGuid, _replayLineageId, GetCreationBranchId(entity.Index), entityId);
    }

    internal bool TryGetEntityFromSystem(
        WorldSystemInvocationDispatch dispatch,
        Guid stableEntityId,
        out Entity entity)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        dispatch.EnsureActive();
        EnsureMembershipReadAccess(dispatch.Description);

        if (stableEntityId == Guid.Empty)
        {
            throw new ArgumentException("A nonempty stable Entity identity is required.", nameof(stableEntityId));
        }

        if (!_identityLedger.TryGetLiveLocator(stableEntityId, IdentityKind.Entity, out var locator))
        {
            entity = default;
            return false;
        }

        if (!_entitySlots.TryGetLive(locator, out var generation)
            || !_entitySlots.TryGetLocation(locator, generation, out _, out _, out _))
        {
            throw new InvalidOperationException("The stable identity ledger references an Entity slot without a live storage location.");
        }

        entity = new Entity(_ownerId, locator, generation);
        return true;
    }

    internal bool TryGetEntityFromSystem(
        WorldSystemInvocationDispatch dispatch,
        WorldEntityIdentity identity,
        out Entity entity)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        dispatch.EnsureActive();
        EnsureMembershipReadAccess(dispatch.Description);

        if (!identity.IsValid)
        {
            throw new ArgumentException("A valid qualified Entity identity is required.", nameof(identity));
        }

        if ((identity.WorldGuid != _worldGuid)
            || (identity.CreationLineageId != _replayLineageId)
            || (identity.CreationBranchId != _identityBranchId))
        {
            entity = default;
            return false;
        }

        return TryGetEntityFromSystem(dispatch, identity.EntityId, out entity);
    }
}
