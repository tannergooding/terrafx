// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal interface IWorldSystemContextDispatch : ICommandWriterDispatch
{
    void EnsureActive();

    void ForEachChunk<TAction>(EntityQueryDescription query, scoped ref TAction action)
        where TAction : struct, IAction<EntityChunkView>, allows ref struct;

    void ForEachResourcePartition<T, TAction>(
        Guid resourceId,
        SystemPartitionRange range,
        scoped ref TAction action)
        where T : unmanaged
        where TAction : struct, IAction<WorldResourcePartitionView<T>>, allows ref struct;

    Guid GetEntityId(Entity entity);

    WorldEntityIdentity GetEntityIdentity(Entity entity);

    bool TryGetEntity(Guid stableEntityId, out Entity entity);

    bool TryGetEntity(WorldEntityIdentity identity, out Entity entity);
}
