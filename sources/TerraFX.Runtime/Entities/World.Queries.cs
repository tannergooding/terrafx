// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Threading;
using TerraFX;

namespace TerraFX.Entities;

public sealed partial class World
{
    private static bool MatchesQuery(WorldArchetype archetype, EntityQueryDescription query)
    {
        foreach (var schemaId in query.RequiredSchemaIds)
        {
            if (!archetype.HasSchema(schemaId))
            {
                return false;
            }
        }

        foreach (var schemaId in query.ExcludedSchemaIds)
        {
            if (archetype.HasSchema(schemaId))
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateQueryAccess(EntityQueryDescription query, IComponentRegistration component, SystemAccessMode requiredMode)
    {
        foreach (var access in query.Access)
        {
            if ((access.Kind == SystemResourceKind.Component)
                && (access.ResourceId == component.SchemaReference.SchemaId)
                && ((requiredMode == SystemAccessMode.Read) || (access.Mode == SystemAccessMode.Write)))
            {
                return;
            }
        }

        throw new InvalidOperationException("The query did not declare the required component access.");
    }

    /// <summary>Visits matching chunks synchronously on the World owner thread.</summary>
    /// <typeparam name="TAction">The concrete callback state, which may be a ref struct.</typeparam>
    /// <param name="query">The complete membership filter, access declaration, and optional global partition range.</param>
    /// <param name="action">The caller-owned callback state, passed by reference without copying.</param>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is null.</exception>
    /// <exception cref="ArgumentException">The query references an unregistered component or tag schema.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The query range exceeds this World's partition domain.</exception>
    /// <exception cref="InvalidOperationException">The World is unavailable, the owner thread is wrong, or a callback attempts reentrant World access.</exception>
    public void ForEachChunk<TAction>(EntityQueryDescription query, scoped ref TAction action)
        where TAction : struct, IAction<EntityChunkView>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(query);
        EnterStructuralOperation();

        try
        {
            ForEachChunkCore(query, ref action, systemInvocation: false);
        }
        finally
        {
            _ = Interlocked.Exchange(ref _isStructuralOperationActive, 0);
        }
    }

    internal void ForEachChunkFromSystem<TAction>(
        WorldSystemInvocationDispatch dispatch,
        EntityQueryDescription query,
        scoped ref TAction action)
        where TAction : struct, IAction<EntityChunkView>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentNullException.ThrowIfNull(query);
        dispatch.EnsureActive();
        ValidateSystemQueryAccess(dispatch.Description, query);

        ForEachChunkCore(query, ref action, systemInvocation: true);
    }

    internal ReadOnlySpan<Entity> GetQueryEntities(
        long queryEpoch,
        WorldArchetype archetype,
        int chunkIndex,
        int startRow,
        int length)
    {
        ValidateQueryView(queryEpoch);
        return archetype.Storage.GetEntities(chunkIndex, startRow, length);
    }

    internal ReadOnlySpan<T> GetQueryReadSpan<T>(
        long queryEpoch,
        EntityQueryDescription query,
        WorldArchetype archetype,
        int chunkIndex,
        int startRow,
        int length)
        where T : unmanaged
    {
        ValidateQueryView(queryEpoch);
        var component = GetComponentRegistration<T>();
        ValidateQueryAccess(query, component, SystemAccessMode.Read);
        var componentIndex = archetype.GetComponentIndex(component.ComponentType);

        if (componentIndex < 0)
        {
            throw new InvalidOperationException("The matching archetype does not contain the queried component.");
        }

        return archetype.Storage.GetReadOnlyColumnSpan<T>(componentIndex, chunkIndex, startRow, length);
    }

    internal Span<T> GetQueryWriteSpan<T>(
        long queryEpoch,
        EntityQueryDescription query,
        WorldArchetype archetype,
        int chunkIndex,
        int startRow,
        int length)
        where T : unmanaged
    {
        ValidateQueryView(queryEpoch);
        var component = GetComponentRegistration<T>();
        ValidateQueryAccess(query, component, SystemAccessMode.Write);
        var componentIndex = archetype.GetComponentIndex(component.ComponentType);

        if (componentIndex < 0)
        {
            throw new InvalidOperationException("The matching archetype does not contain the queried component.");
        }

        var values = archetype.Storage.GetColumnSpan<T>(componentIndex, chunkIndex, startRow, length);
        var invocationSlot = GetSystemInvocationSlot(Environment.CurrentManagedThreadId);

        if (invocationSlot < 0)
        {
            throw new InvalidOperationException("The EntityChunkView is not used on an admitted World execution thread.");
        }

        _ = AdvanceCanonicalMutationRevision();
        Volatile.Write(ref _activeQueryWriteGrants[invocationSlot], 1);
        return values;
    }

    private void EnsureRegisteredQuerySchema(EntityQueryDescription query, Guid schemaId)
    {
        if (TryFindComponent(schemaId, out _) || TryFindTag(schemaId))
        {
            return;
        }

        throw new ArgumentException("The query references an unregistered component or tag schema.", nameof(query));
    }

    private void ForEachChunkCore<TAction>(
        EntityQueryDescription query,
        scoped ref TAction action,
        bool systemInvocation)
        where TAction : struct, IAction<EntityChunkView>, allows ref struct
    {
        ValidateQuery(query);
        var invocationSlot = GetSystemInvocationSlot(Environment.CurrentManagedThreadId);

        if (invocationSlot < 0)
        {
            throw new InvalidOperationException("The World query is not running on an admitted execution thread.");
        }

        if (Volatile.Read(ref _activeQueryEpochs[invocationSlot]) != 0)
        {
            throw new InvalidOperationException("Nested EntityChunkView queries are not admitted.");
        }

        var queryEpoch = NextQueryEpoch();
        Volatile.Write(ref _activeQueryEpochs[invocationSlot], queryEpoch);

        try
        {
            var partitionRange = query.Range
                ?? new SystemPartitionRange(0, _maximumLiveEntities);
            var partitioned = systemInvocation || (query.Range is not null);

            for (var partitionOrdinal = 0; partitionOrdinal < _componentTagWorkPlanCount; partitionOrdinal++)
            {
                var work = _componentTagWorkPlan[partitionOrdinal];
                var archetype = _queryArchetypes[work.ArchetypeOrdinal]
                    ?? throw new InvalidOperationException("The component/tag work plan references an empty archetype.");
                var matches = MatchesQuery(archetype, query);

                if (!matches)
                {
                    continue;
                }

                if (!partitioned)
                {
                    if (work.StartRow == 0)
                    {
                        InvokeQueryAction(
                            query,
                            queryEpoch,
                            archetype,
                            work.ChunkIndex,
                            0,
                            work.ChunkLength,
                            0,
                            ref action);
                    }

                    continue;
                }

                if ((partitionOrdinal >= partitionRange.Start)
                    && (partitionOrdinal < partitionRange.EndExclusive))
                {
                    var writerPartition = systemInvocation ? checked(partitionOrdinal + 1) : 0;
                    InvokeQueryAction(
                        query,
                        queryEpoch,
                        archetype,
                        work.ChunkIndex,
                        work.StartRow,
                        work.Length,
                        writerPartition,
                        ref action);
                }
            }
        }
        finally
        {
            Volatile.Write(ref _activeQueryWriteGrants[invocationSlot], 0);
            Volatile.Write(ref _activeQueryEpochs[invocationSlot], 0);
        }
    }

    private int GetComponentTagWorkPlanCapacityFor(int requiredPartitionCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(requiredPartitionCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(requiredPartitionCount, _maximumLiveEntities);

        if (requiredPartitionCount <= _componentTagWorkPlan.Length)
        {
            return _componentTagWorkPlan.Length;
        }

        var capacity = Math.Min(_maximumLiveEntities, Math.Max(4, _componentTagWorkPlan.Length));

        while (capacity < requiredPartitionCount)
        {
            capacity = (int)Math.Min(_maximumLiveEntities, Math.Max((long)requiredPartitionCount, (long)capacity * 2));
        }

        var candidateBytes = CalculateComponentTagWorkPlanByteReservation(capacity);

        while ((capacity > requiredPartitionCount)
            && (checked(_componentTagWorkPlanByteReservation + candidateBytes) > _maximumWorkPlanBytes))
        {
            capacity = Math.Max(requiredPartitionCount, capacity / 2);
            candidateBytes = CalculateComponentTagWorkPlanByteReservation(capacity);
        }

        if (checked(_componentTagWorkPlanByteReservation + candidateBytes) > _maximumWorkPlanBytes)
        {
            throw new InvalidOperationException("The current and candidate component/tag work plans exceed the configured budget.");
        }

        return capacity;
    }

    [SuppressMessage("Usage", "CA1031:Do not catch general exception types", Justification = "A writable chunk must be validated even when its callback throws; preserve both failures if scope-close validation also fails.")]
    private void InvokeQueryAction<TAction>(
        EntityQueryDescription query,
        long queryEpoch,
        WorldArchetype archetype,
        int chunkIndex,
        int startRow,
        int length,
        int writerPartition,
        scoped ref TAction action)
        where TAction : struct, IAction<EntityChunkView>, allows ref struct
    {
        var view = new EntityChunkView(this, queryEpoch, query, archetype, chunkIndex, startRow, length);
        var invocationSlot = GetSystemInvocationSlot(Environment.CurrentManagedThreadId);

        if (invocationSlot < 0)
        {
            throw new InvalidOperationException("The EntityChunkView is not used on an admitted World execution thread.");
        }

        var dispatch = Volatile.Read(ref _activeSystemInvocations[invocationSlot]);
        var previousWriterPartition = (dispatch is null)
            ? 0
            : dispatch.SetWriterPartition(writerPartition);
        Volatile.Write(ref _activeQueryWriteGrants[invocationSlot], 0);
        Exception? callbackFailure = null;

        try
        {
            action.Invoke(view);
        }
        catch (Exception exception)
        {
            callbackFailure = exception;
        }

        try
        {
            if (Volatile.Read(ref _activeQueryWriteGrants[invocationSlot]) != 0)
            {
                ValidateQueryWriteValues(query, archetype, chunkIndex, startRow, length);
            }

            if (Volatile.Read(ref _activeSystemInvocations[invocationSlot]) is { Failure: { } protocolFailure })
            {
                ExceptionDispatchInfo.Capture(protocolFailure).Throw();
            }
        }
        catch (Exception validationFailure)
        {
            if (Volatile.Read(ref _isTickActive) == 0)
            {
                FaultWorld(null, validationFailure);
            }

            if (callbackFailure is not null)
            {
                var activeDispatch = Volatile.Read(ref _activeSystemInvocations[invocationSlot]);

                if (activeDispatch is not null)
                {
                    var selectedFailure = activeDispatch.Failure ?? activeDispatch.RecordFailure(callbackFailure);
                    ExceptionDispatchInfo.Capture(selectedFailure).Throw();
                    return;
                }

                throw new AggregateException("The query callback and its component validation both failed.", callbackFailure, validationFailure);
            }

            throw;
        }
        finally
        {
            Volatile.Write(ref _activeQueryWriteGrants[invocationSlot], 0);

            if (dispatch is not null)
            {
                _ = dispatch.SetWriterPartition(previousWriterPartition);
            }
        }

        if (callbackFailure is not null)
        {
            ExceptionDispatchInfo.Capture(callbackFailure).Throw();
        }
    }

    private void RebuildComponentTagWorkPlan()
    {
        var previousCount = _componentTagWorkPlanCount;
        var nextEpoch = checked(_componentTagWorkPlanEpoch + 1);
        var count = 0;

        for (var archetypeOrdinal = 0; archetypeOrdinal < _archetypeCount; archetypeOrdinal++)
        {
            var archetype = _queryArchetypes[archetypeOrdinal]
                ?? throw new InvalidOperationException("The World query-archetype order contains an empty active slot.");

            for (var chunkIndex = 0; chunkIndex < archetype.Storage.ChunkCount; chunkIndex++)
            {
                var hasChunkOrdinal = archetype.Storage.TryGetChunkOrdinal(chunkIndex, out var chunkOrdinal);
                var entities = archetype.Storage.GetEntities(chunkIndex);

                if (entities.IsEmpty)
                {
                    continue;
                }

                if (!hasChunkOrdinal)
                {
                    throw new InvalidOperationException("A populated archetype chunk has no stable chunk ordinal.");
                }

                for (var startRow = 0; startRow < entities.Length; startRow += 64)
                {
                    if (count >= _componentTagWorkPlan.Length)
                    {
                        throw new InvalidOperationException("The component/tag work plan exceeds its admitted partition capacity.");
                    }

                    var length = Math.Min(64, entities.Length - startRow);
                    _componentTagWorkPlan[count++] = new WorldChunkWorkPlanEntry(
                        archetypeOrdinal,
                        chunkOrdinal,
                        chunkIndex,
                        startRow,
                        length,
                        entities.Length);
                }
            }
        }

        Array.Sort(_componentTagWorkPlan, 0, count);

        if (previousCount > count)
        {
            Array.Clear(_componentTagWorkPlan, count, previousCount - count);
        }

        _componentTagWorkPlanCount = count;
        _componentTagWorkPlanEpoch = nextEpoch;
    }

    private bool TryFindComponent(Guid schemaId, out IComponentRegistration? component)
    {
        foreach (var candidate in Registration.Components)
        {
            if (candidate.SchemaReference.SchemaId == schemaId)
            {
                component = candidate;
                return true;
            }
        }

        component = null;
        return false;
    }

    private bool TryFindTag(Guid schemaId)
    {
        foreach (var tag in Registration.Tags)
        {
            if (tag.SchemaId == schemaId)
            {
                return true;
            }
        }

        return false;
    }

    private void ValidateQuery(EntityQueryDescription query)
    {
        if (query.Range is { } range && (range.EndExclusive > _maximumLiveEntities))
        {
            throw new ArgumentOutOfRangeException(nameof(query), "The query range exceeds the World's global partition domain.");
        }

        foreach (var schemaId in query.RequiredSchemaIds)
        {
            EnsureRegisteredQuerySchema(query, schemaId);
        }

        foreach (var schemaId in query.ExcludedSchemaIds)
        {
            EnsureRegisteredQuerySchema(query, schemaId);
        }

        foreach (var access in query.Access)
        {
            if (access.Kind == SystemResourceKind.Component)
            {
                if (!TryFindComponent(access.ResourceId, out _))
                {
                    throw new ArgumentException("The query declares access to an unregistered component schema.", nameof(query));
                }
            }
            else if (!TryFindTag(access.ResourceId))
            {
                throw new ArgumentException("The query declares access to an unregistered tag schema.", nameof(query));
            }
        }
    }

    private void ValidateQueryView(long queryEpoch)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var invocationSlot = GetSystemInvocationSlot(Environment.CurrentManagedThreadId);

        if ((invocationSlot < 0)
            || (Volatile.Read(ref _isStructuralOperationActive) == 0)
            || (Volatile.Read(ref _activeQueryEpochs[invocationSlot]) != queryEpoch))
        {
            throw new InvalidOperationException("The EntityChunkView has expired or is not used on its admitted World execution thread.");
        }
    }

    private void ValidateQueryWriteValues(
        EntityQueryDescription query,
        WorldArchetype archetype,
        int chunkIndex,
        int startRow,
        int length)
    {
        foreach (var access in query.Access)
        {
            if ((access.Kind != SystemResourceKind.Component) || (access.Mode != SystemAccessMode.Write))
            {
                continue;
            }

            if (!TryFindComponent(access.ResourceId, out var component) || (component is null))
            {
                throw new InvalidOperationException("A writable query component is no longer registered.");
            }

            var componentIndex = archetype.GetComponentIndex(component.ComponentType);

            if (componentIndex < 0)
            {
                throw new InvalidOperationException("A writable query archetype does not contain its declared component.");
            }

            for (var row = startRow; row < startRow + length; row++)
            {
                var diagnostics = archetype.Storage.ValidateColumnRow(
                    componentIndex,
                    chunkIndex,
                    row,
                    CancellationToken.None);

                if (diagnostics.IsDefault)
                {
                    throw new InvalidOperationException("The component validation dispatcher returned a default diagnostic array.");
                }

                foreach (var diagnostic in diagnostics)
                {
                    if (diagnostic is null)
                    {
                        throw new InvalidOperationException("The component validation dispatcher returned a null diagnostic.");
                    }
                }

                if (!diagnostics.IsEmpty)
                {
                    var failure = new InvalidOperationException("A writable query component failed its registered validation.");

                    if (Volatile.Read(ref _isTickActive) == 0)
                    {
                        FaultWorld(diagnostics[0], failure);
                    }

                    throw failure;
                }

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

    private readonly struct WorldChunkWorkPlanEntry : IComparable<WorldChunkWorkPlanEntry>
    {
        private readonly int _archetypeOrdinal;
        private readonly int _chunkIndex;
        private readonly int _chunkLength;
        private readonly long _chunkOrdinal;
        private readonly int _length;
        private readonly int _startRow;

        internal WorldChunkWorkPlanEntry(
            int archetypeOrdinal,
            long chunkOrdinal,
            int chunkIndex,
            int startRow,
            int length,
            int chunkLength)
        {
            _archetypeOrdinal = archetypeOrdinal;
            _chunkIndex = chunkIndex;
            _chunkLength = chunkLength;
            _chunkOrdinal = chunkOrdinal;
            _length = length;
            _startRow = startRow;
        }

        internal int ArchetypeOrdinal => _archetypeOrdinal;

        internal int ChunkIndex => _chunkIndex;

        internal int ChunkLength => _chunkLength;

        internal int Length => _length;

        internal int StartRow => _startRow;

        public int CompareTo(WorldChunkWorkPlanEntry other)
        {
            var result = _archetypeOrdinal.CompareTo(other._archetypeOrdinal);

            if (result != 0)
            {
                return result;
            }

            result = _chunkOrdinal.CompareTo(other._chunkOrdinal);
            return (result != 0) ? result : _startRow.CompareTo(other._startRow);
        }
    }
}
