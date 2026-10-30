// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Provides borrowed Entity and component spans for one synchronous query callback.</summary>
public readonly ref struct EntityChunkView
{
    private readonly int _chunkIndex;
    private readonly int _length;
    private readonly EntityQueryDescription _query;
    private readonly long _queryEpoch;
    private readonly int _startRow;
    private readonly WorldArchetype _worldArchetype;
    private readonly World _world;

    internal EntityChunkView(
        World world,
        long queryEpoch,
        EntityQueryDescription query,
        WorldArchetype worldArchetype,
        int chunkIndex,
        int startRow,
        int length)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(queryEpoch);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(worldArchetype);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(startRow);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);

        _world = world;
        _queryEpoch = queryEpoch;
        _query = query;
        _worldArchetype = worldArchetype;
        _chunkIndex = chunkIndex;
        _startRow = startRow;
        _length = length;
    }

    /// <summary>Gets the borrowed Entity handles for this exact callback slice.</summary>
    public readonly ReadOnlySpan<Entity> Entities
        => _world.GetQueryEntities(_queryEpoch, _worldArchetype, _chunkIndex, _startRow, _length);

    /// <summary>Gets a read-only component span admitted by this query.</summary>
    /// <typeparam name="T">The unmanaged component type.</typeparam>
    /// <returns>The component values aligned with <see cref="Entities"/>.</returns>
    /// <exception cref="InvalidOperationException">The view has expired, T is not registered, or the query did not grant read access.</exception>
    public readonly ReadOnlySpan<T> Read<T>()
        where T : unmanaged
        => _world.GetQueryReadSpan<T>(_queryEpoch, _query, _worldArchetype, _chunkIndex, _startRow, _length);

    /// <summary>Gets a writable component span admitted by this query.</summary>
    /// <typeparam name="T">The unmanaged component type.</typeparam>
    /// <returns>The component values aligned with <see cref="Entities"/>.</returns>
    /// <exception cref="InvalidOperationException">The view has expired, T is not registered, or the query did not grant write access.</exception>
    public readonly Span<T> Write<T>()
        where T : unmanaged
        => _world.GetQueryWriteSpan<T>(_queryEpoch, _query, _worldArchetype, _chunkIndex, _startRow, _length);
}
