// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Provides invocation-scoped access granted to one World system.</summary>
public sealed class WorldSystemContext
{
    private readonly CommandWriter _commands;
    private readonly IWorldSystemContextDispatch _dispatch;
    private readonly SimulationStepContext _step;

    internal WorldSystemContext(SimulationStepContext step, CommandWriter commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
        _dispatch = commands.Dispatch as IWorldSystemContextDispatch
            ?? throw new ArgumentException("The command writer is not bound to a World system context.", nameof(commands));

        if ((step.TickIndex < 0)
            || (step.TickRate is < 1 or > 1000)
            || (step.FixedDeltaTimeSeconds != (1.0f / step.TickRate)))
        {
            throw new ArgumentException("The World system context requires engine-issued simulation-step metadata.", nameof(step));
        }

        _dispatch.EnsureActive();
        _step = step;
    }

    /// <summary>Gets the invocation-scoped structural command writer.</summary>
    public CommandWriter Commands => _commands;

    /// <summary>Gets immutable fixed-tick metadata for this invocation.</summary>
    public SimulationStepContext Step => _step;

    /// <summary>Visits scoped chunks under this invocation's declared component access.</summary>
    public void ForEachChunk<TAction>(EntityQueryDescription query, scoped ref TAction action)
        where TAction : struct, IAction<EntityChunkView>, allows ref struct
        => _dispatch.ForEachChunk(query, ref action);

    /// <summary>Visits scoped resource partitions under this invocation's declared resource access.</summary>
    public void ForEachResourcePartition<T, TAction>(
        Guid resourceId,
        SystemPartitionRange range,
        scoped ref TAction action)
        where T : unmanaged
        where TAction : struct, IAction<WorldResourcePartitionView<T>>, allows ref struct
        => _dispatch.ForEachResourcePartition<T, TAction>(resourceId, range, ref action);

    /// <summary>Gets the stable identity for a live Entity in this World.</summary>
    public Guid GetEntityId(Entity entity) => _dispatch.GetEntityId(entity);

    /// <summary>Gets the World-, lineage-, and branch-qualified identity for a live Entity.</summary>
    public WorldEntityIdentity GetEntityIdentity(Entity entity) => _dispatch.GetEntityIdentity(entity);

    /// <summary>Resolves a stable Entity identity in the admitted live membership set.</summary>
    public bool TryGetEntity(Guid stableEntityId, out Entity entity)
        => _dispatch.TryGetEntity(stableEntityId, out entity);

    /// <summary>Resolves a qualified Entity identity in the admitted live membership set.</summary>
    public bool TryGetEntity(WorldEntityIdentity identity, out Entity entity)
        => _dispatch.TryGetEntity(identity, out entity);
}
