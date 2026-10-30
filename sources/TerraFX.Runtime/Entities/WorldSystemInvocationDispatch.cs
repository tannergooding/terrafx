// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace TerraFX.Entities;

[SuppressMessage("Usage", "CA1031:Do not catch general exception types", Justification = "Active system-context misuse is retained as a sticky invocation failure before the exact exception is rethrown.")]
internal sealed class WorldSystemInvocationDispatch : IWorldSystemContextDispatch
{
    private readonly WorldCommandStaging _commandStaging;
    private int _currentWriterPartition;
    private Exception? _failure;
    private int _isActive;
    private int _isBound;
    private int _ownerThreadId;
    private readonly World _world;

    internal WorldSystemInvocationDispatch(
        World world,
        SystemDescription description,
        long invocationEpoch,
        WorldCommandStaging commandStaging)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(commandStaging);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(invocationEpoch);
        _world = world;
        _commandStaging = commandStaging;
        Description = description;
        InvocationEpoch = invocationEpoch;
    }

    internal WorldCommandStaging CommandStaging => _commandStaging;

    internal int CurrentWriterPartition => Volatile.Read(ref _currentWriterPartition);

    internal SystemDescription Description { get; }

    internal Exception? Failure => Volatile.Read(ref _failure);

    internal long InvocationEpoch { get; }

    internal bool IsActive => Volatile.Read(ref _isActive) != 0;

    internal int OwnerThreadId => Volatile.Read(ref _ownerThreadId);

    public void Add<T>(Entity entity, in T value)
        where T : unmanaged
    {
        EnsureCommandActive();

        try
        {
            _world.AppendAddCommand(this, entity, in value);
        }
        catch (Exception exception)
        {
            throw RecordFailure(exception);
        }
    }

    public void Add<T>(EntityToken entity, in T value)
        where T : unmanaged
    {
        EnsureCommandActive();

        try
        {
            _world.AppendAddCommand(this, entity, in value);
        }
        catch (Exception exception)
        {
            throw RecordFailure(exception);
        }
    }

    public void AddTag(Entity entity, Guid schemaId)
    {
        EnsureCommandActive();

        try
        {
            _world.AppendTagCommand(this, entity, schemaId, add: true);
        }
        catch (Exception exception)
        {
            throw RecordFailure(exception);
        }
    }

    public void AddTag(EntityToken entity, Guid schemaId)
    {
        EnsureCommandActive();

        try
        {
            _world.AppendTagCommand(this, entity, schemaId, add: true);
        }
        catch (Exception exception)
        {
            throw RecordFailure(exception);
        }
    }

    public EntityToken Create(EntityDescription description)
    {
        EnsureCommandActive();

        try
        {
            ArgumentNullException.ThrowIfNull(description);

            if (description.PersistentId is { } persistentId && World.IsReservedUuidV8ForCommand(persistentId))
            {
                throw new ArgumentException(
                    "An explicit Entity identity cannot use the engine-reserved RFC UUIDv8 domain.",
                    nameof(description));
            }

            return _commandStaging.AppendCreate(this, description);
        }
        catch (Exception exception)
        {
            throw RecordFailure(exception);
        }
    }

    public void Destroy(Entity entity, HierarchyDestroyMode mode)
    {
        EnsureCommandActive();

        if (mode is not HierarchyDestroyMode.DetachChildrenKeepWorld and not HierarchyDestroyMode.DestroySubtree)
        {
            throw RecordFailure(new ArgumentOutOfRangeException(nameof(mode)));
        }

        try
        {
            _world.AppendDestroyCommand(this, entity, mode);
        }
        catch (Exception exception)
        {
            throw RecordFailure(exception);
        }
    }

    public void Destroy(EntityToken entity, HierarchyDestroyMode mode)
    {
        EnsureCommandActive();

        try
        {
            if (mode is not HierarchyDestroyMode.DetachChildrenKeepWorld and not HierarchyDestroyMode.DestroySubtree)
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }

            World.AppendDestroyTokenCommand(this, entity, mode);
        }
        catch (Exception exception)
        {
            throw RecordFailure(exception);
        }
    }

    public void EnsureActive()
    {
        if (Failure is { } failure)
        {
            throw failure;
        }

        _world.EnsureSystemInvocation(this);
    }

    public void ForEachChunk<TAction>(EntityQueryDescription query, scoped ref TAction action)
        where TAction : struct, IAction<EntityChunkView>, allows ref struct
    {
        EnsureActive();

        try
        {
            _world.ForEachChunkFromSystem(this, query, ref action);

            if (Failure is { } failure)
            {
                throw failure;
            }
        }
        catch (Exception exception)
        {
            throw RecordFailure(exception);
        }
    }

    public void ForEachResourcePartition<T, TAction>(
        Guid resourceId,
        SystemPartitionRange range,
        scoped ref TAction action)
        where T : unmanaged
        where TAction : struct, IAction<WorldResourcePartitionView<T>>, allows ref struct
    {
        EnsureActive();

        try
        {
            _world.ForEachResourcePartitionFromSystem<T, TAction>(this, resourceId, range, ref action);

            if (Failure is { } failure)
            {
                throw failure;
            }
        }
        catch (Exception exception)
        {
            throw RecordFailure(exception);
        }
    }

    public Guid GetEntityId(Entity entity)
    {
        EnsureActive();

        try
        {
            return _world.GetEntityIdFromSystem(this, entity);
        }
        catch (Exception exception)
        {
            throw RecordFailure(exception);
        }
    }

    public WorldEntityIdentity GetEntityIdentity(Entity entity)
    {
        EnsureActive();

        try
        {
            return _world.GetEntityIdentityFromSystem(this, entity);
        }
        catch (Exception exception)
        {
            throw RecordFailure(exception);
        }
    }

    public void Remove<T>(Entity entity)
        where T : unmanaged
    {
        EnsureCommandActive();

        try
        {
            _world.AppendRemoveCommand<T>(this, entity);
        }
        catch (Exception exception)
        {
            throw RecordFailure(exception);
        }
    }

    public void Remove<T>(EntityToken entity)
        where T : unmanaged
    {
        EnsureCommandActive();

        try
        {
            _world.AppendRemoveCommand<T>(this, entity);
        }
        catch (Exception exception)
        {
            throw RecordFailure(exception);
        }
    }

    public void RemoveTag(Entity entity, Guid schemaId)
    {
        EnsureCommandActive();

        try
        {
            _world.AppendTagCommand(this, entity, schemaId, add: false);
        }
        catch (Exception exception)
        {
            throw RecordFailure(exception);
        }
    }

    public void RemoveTag(EntityToken entity, Guid schemaId)
    {
        EnsureCommandActive();

        try
        {
            _world.AppendTagCommand(this, entity, schemaId, add: false);
        }
        catch (Exception exception)
        {
            throw RecordFailure(exception);
        }
    }

    public void Set<T>(Entity entity, in T value)
        where T : unmanaged
    {
        EnsureCommandActive();

        try
        {
            _world.AppendSetCommand(this, entity, in value);
        }
        catch (Exception exception)
        {
            throw RecordFailure(exception);
        }
    }

    public void Set<T>(EntityToken entity, in T value)
        where T : unmanaged
    {
        EnsureCommandActive();

        try
        {
            _world.AppendSetCommand(this, entity, in value);
        }
        catch (Exception exception)
        {
            throw RecordFailure(exception);
        }
    }

    public void SetEntityReference<T>(Entity owner, ComponentFieldPath path, Entity? target)
        where T : unmanaged
    {
        EnsureCommandActive();

        try
        {
            _world.AppendSetEntityReferenceCommand<T>(this, owner, path, target);
        }
        catch (Exception exception)
        {
            throw RecordFailure(exception);
        }
    }

    public void SetEntityReference<T>(Entity owner, ComponentFieldPath path, EntityToken? target)
        where T : unmanaged
    {
        EnsureCommandActive();

        try
        {
            _world.AppendSetEntityReferenceCommand<T>(this, owner, path, target);
        }
        catch (Exception exception)
        {
            throw RecordFailure(exception);
        }
    }

    public void SetEntityReference<T>(EntityToken owner, ComponentFieldPath path, Entity? target)
        where T : unmanaged
    {
        EnsureCommandActive();

        try
        {
            _world.AppendSetEntityReferenceCommand<T>(this, owner, path, target);
        }
        catch (Exception exception)
        {
            throw RecordFailure(exception);
        }
    }

    public void SetEntityReference<T>(EntityToken owner, ComponentFieldPath path, EntityToken? target)
        where T : unmanaged
    {
        EnsureCommandActive();

        try
        {
            _world.AppendSetEntityReferenceCommand<T>(this, owner, path, target);
        }
        catch (Exception exception)
        {
            throw RecordFailure(exception);
        }
    }

    public bool TryGetEntity(Guid stableEntityId, out Entity entity)
    {
        EnsureActive();

        try
        {
            return _world.TryGetEntityFromSystem(this, stableEntityId, out entity);
        }
        catch (Exception exception)
        {
            throw RecordFailure(exception);
        }
    }

    public bool TryGetEntity(WorldEntityIdentity identity, out Entity entity)
    {
        EnsureActive();

        try
        {
            return _world.TryGetEntityFromSystem(this, identity, out entity);
        }
        catch (Exception exception)
        {
            throw RecordFailure(exception);
        }
    }

    internal void Close()
        => _ = Interlocked.Exchange(ref _isActive, 0);

    internal void EnsureCommandActive()
    {
        if (!IsActive)
        {
            _world.EnsureSystemInvocation(this);
            return;
        }

        if (Failure is not null)
        {
            throw new InvalidOperationException("The World command writer invocation is already faulted.");
        }

        _world.EnsureSystemInvocation(this);

        if (Description.Stage > SimulationStage.Structural)
        {
            throw RecordFailure(new InvalidOperationException("Deferred structural commands are not admitted during Extract."));
        }
    }

    internal Exception RecordFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return Interlocked.CompareExchange(ref _failure, exception, null) ?? exception;
    }

    internal void Revoke()
    {
        _ = Interlocked.Exchange(ref _isActive, 0);

        if (Interlocked.Exchange(ref _isBound, 0) != 0)
        {
            _world.EndSystemInvocation(this);
        }
    }

    internal int SetWriterPartition(int writerPartition)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(writerPartition);
        return Interlocked.Exchange(ref _currentWriterPartition, writerPartition);
    }

    internal void Start()
    {
        if (Interlocked.CompareExchange(ref _isActive, 1, 0) != 0)
        {
            throw new InvalidOperationException("The World system invocation is already active.");
        }

        Volatile.Write(ref _ownerThreadId, Environment.CurrentManagedThreadId);

        try
        {
            _world.BeginSystemInvocation(this);
            Volatile.Write(ref _isBound, 1);
        }
        catch
        {
            Volatile.Write(ref _isActive, 0);
            throw;
        }
    }
}
