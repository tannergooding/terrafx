// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class EntityArchetypeStorage
{
    internal const int ChunkCapacity = 256;

    private readonly int _archetypeIndex;
    private ulong[] _availableChunkSummary;
    private ulong[] _availableChunkWords;
    private readonly int _chunkCapacity;
    private Chunk[] _chunks;
    private readonly ImmutableArray<IComponentRegistration> _components;
    private int _entityCount;
    private readonly int _maximumEntityCount;
    private readonly uint _ownerId;

    internal EntityArchetypeStorage(
        uint ownerId,
        int archetypeIndex,
        int maximumEntityCount,
        ImmutableArray<IComponentRegistration> components)
    {
        ArgumentOutOfRangeException.ThrowIfZero(ownerId);
        ArgumentOutOfRangeException.ThrowIfNegative(archetypeIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumEntityCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumEntityCount, EntitySlotTable.MaximumSlots);

        if (components.IsDefault)
        {
            throw new ArgumentException("The archetype component set must be initialized.", nameof(components));
        }

        var rowBytes = 12L;

        foreach (var component in components)
        {
            ArgumentNullException.ThrowIfNull(component);
            rowBytes = checked(rowBytes + component.Layout.LogicalBytes);
        }

        if (rowBytes > 32768)
        {
            throw new ArgumentOutOfRangeException(nameof(components), "The archetype logical row exceeds the 32768-byte limit.");
        }

        _archetypeIndex = archetypeIndex;
        _chunkCapacity = Math.Min(ChunkCapacity, checked((int)(32768 / rowBytes)));
        _components = components;
        _maximumEntityCount = maximumEntityCount;
        _ownerId = ownerId;
        _availableChunkSummary = [];
        _availableChunkWords = [];
        _chunks = [];
    }

    internal EntityArchetypeStorage(uint ownerId, int archetypeIndex, int maximumEntityCount)
        : this(ownerId, archetypeIndex, maximumEntityCount, [])
    {
    }

    internal int ChunkCount => _chunks.Length;

    internal int EntityCount => _entityCount;

    internal long OwnedByteReservation
    {
        get
        {
            var bytes = (_chunks.Length == 0) ? 0 : ArrayBytes(_chunks.Length, Unsafe.SizeOf<Chunk>());

            if (_availableChunkWords.Length != 0)
            {
                bytes = checked(bytes + ArrayBytes(_availableChunkWords.Length, sizeof(ulong)));
            }

            if (_availableChunkSummary.Length != 0)
            {
                bytes = checked(bytes + ArrayBytes(_availableChunkSummary.Length, sizeof(ulong)));
            }

            foreach (var chunk in _chunks)
            {
                bytes = checked(bytes + ArrayBytes(chunk._entities.Length, Unsafe.SizeOf<Entity>()));

                if (chunk._columns.Length != 0)
                {
                    bytes = checked(bytes + ArrayBytes(chunk._columns.Length, IntPtr.Size));
                }

                for (var componentIndex = 0; componentIndex < _components.Length; componentIndex++)
                {
                    bytes = checked(bytes + _components[componentIndex].GetColumnArrayBytes(_chunkCapacity));
                }
            }

            return bytes;
        }
    }

    internal int RowCapacity => _chunkCapacity;

    internal static long GetInitialCapacityPreparationBytes(ImmutableArray<IComponentRegistration> components)
    {
        if (components.IsDefault)
        {
            throw new ArgumentException("The archetype component set must be initialized.", nameof(components));
        }

        var rowBytes = 12L;

        foreach (var component in components)
        {
            ArgumentNullException.ThrowIfNull(component);
            rowBytes = checked(rowBytes + component.Layout.LogicalBytes);
        }

        if (rowBytes > 32768)
        {
            throw new ArgumentOutOfRangeException(nameof(components), "The resulting archetype row exceeds the 32768-byte limit.");
        }

        var chunkCapacity = Math.Min(ChunkCapacity, checked((int)(32768 / rowBytes)));
        var bytes = checked(
            ArrayBytes(1, Unsafe.SizeOf<Chunk>())
            + ArrayBytes(chunkCapacity, Unsafe.SizeOf<Entity>())
            + ArrayBytes(1, sizeof(ulong))
            + ArrayBytes(1, sizeof(ulong)));

        if (components.Length != 0)
        {
            bytes = checked(bytes + ArrayBytes(components.Length, IntPtr.Size));
        }

        foreach (var component in components)
        {
            bytes = checked(bytes + component.GetColumnArrayBytes(chunkCapacity));
        }

        return bytes;
    }

    private static long ArrayBytes(long count, int elementSize)
        => checked(64 + ((checked(count * elementSize) + 7) & ~7L));

    private static void ClearAvailableChunk(ulong[] words, ulong[] summary, int chunkIndex)
    {
        var wordIndex = chunkIndex / 64;
        var wordBit = 1UL << (chunkIndex % 64);
        var word = words[wordIndex] & ~wordBit;
        words[wordIndex] = word;

        if (word == 0)
        {
            summary[wordIndex / 64] &= ~(1UL << (wordIndex % 64));
        }
    }

    private static int GetChunkCount(int entityCount, int chunkCapacity)
        => checked((int)(((long)entityCount + chunkCapacity - 1) / chunkCapacity));

    private static int GetChunkSummaryWordCount(int chunkWordCount)
        => checked((chunkWordCount + 63) / 64);

    private static int GetChunkWordCount(int chunkCount)
        => checked((chunkCount + 63) / 64);

    private static void MarkAvailableChunk(ulong[] words, ulong[] summary, int chunkIndex)
    {
        var wordIndex = chunkIndex / 64;
        var wordBit = 1UL << (chunkIndex % 64);

        if (words[wordIndex] == 0)
        {
            summary[wordIndex / 64] |= 1UL << (wordIndex % 64);
        }

        words[wordIndex] |= wordBit;
    }

    internal ComponentInitialization CaptureColumnRow(int componentIndex, int chunkIndex, int row)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(componentIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(componentIndex, _components.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(chunkIndex, _chunks.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, _chunks[chunkIndex]._count);
        return _components[componentIndex].CaptureColumnRow(_chunks[chunkIndex]._columns[componentIndex], row);
    }

    internal void ClearRolledBackChunkOrdinal(int chunkIndex, long chunkOrdinal)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(chunkIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(chunkIndex, _chunks.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkOrdinal);
        ref var chunk = ref _chunks[chunkIndex];

        if ((chunk._count != 0) || !chunk._hasOrdinal || (chunk._ordinal != chunkOrdinal))
        {
            throw new InvalidOperationException("Only a newly assigned ordinal on an empty archetype chunk can be rolled back.");
        }

        chunk._ordinal = 0;
        chunk._hasOrdinal = false;
    }

    internal void ClearUncommittedRow(int chunkIndex, int row)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(chunkIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(chunkIndex, _chunks.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ref var chunk = ref _chunks[chunkIndex];

        if (row != chunk._count)
        {
            throw new InvalidOperationException("Only the next uncommitted archetype row can be cleared.");
        }

        for (var componentIndex = 0; componentIndex < _components.Length; componentIndex++)
        {
            _components[componentIndex].ClearColumn(chunk._columns[componentIndex], row);
        }
    }

    internal void CopyColumnRowFrom(
        int destinationComponentIndex,
        EntityArchetypeStorage source,
        int sourceComponentIndex,
        int sourceChunkIndex,
        int sourceRow,
        int destinationChunkIndex,
        int destinationRow)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(destinationComponentIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(destinationComponentIndex, _components.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceComponentIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(sourceComponentIndex, source._components.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceChunkIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(sourceChunkIndex, source._chunks.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceRow);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(sourceRow, source._chunks[sourceChunkIndex]._count);
        ArgumentOutOfRangeException.ThrowIfNegative(destinationChunkIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(destinationChunkIndex, _chunks.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(destinationRow);

        ref var destinationChunk = ref _chunks[destinationChunkIndex];

        if ((destinationRow != destinationChunk._count) || (destinationRow >= _chunkCapacity))
        {
            throw new InvalidOperationException("A migration copy must target the next uncommitted archetype row.");
        }

        var sourceRegistration = source._components[sourceComponentIndex];
        var destinationRegistration = _components[destinationComponentIndex];

        if (sourceRegistration.ComponentType != destinationRegistration.ComponentType)
        {
            throw new InvalidOperationException("A migration cannot copy between different component types.");
        }

        destinationRegistration.CopyColumnRow(
            source._chunks[sourceChunkIndex]._columns[sourceComponentIndex],
            sourceRow,
            destinationChunk._columns[destinationComponentIndex],
            destinationRow);
    }

    internal int CountNewChunkOrdinalAssignments(ReadOnlySpan<int> removedEntityCounts, int incomingEntityCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(incomingEntityCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(removedEntityCounts.Length, _chunks.Length);
        var remainingEntityCount = incomingEntityCount;
        var newOrdinalCount = 0;

        for (var chunkIndex = 0; (chunkIndex < _chunks.Length) && (remainingEntityCount != 0); chunkIndex++)
        {
            var chunk = _chunks[chunkIndex];
            var removedEntityCount = (chunkIndex < removedEntityCounts.Length)
                ? removedEntityCounts[chunkIndex]
                : 0;
            ArgumentOutOfRangeException.ThrowIfNegative(removedEntityCount);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(removedEntityCount, chunk._count);
            var remainingChunkCapacity = _chunkCapacity - (chunk._count - removedEntityCount);
            var appendedEntityCount = Math.Min(remainingChunkCapacity, remainingEntityCount);

            if ((appendedEntityCount != 0) && !chunk._hasOrdinal)
            {
                newOrdinalCount = checked(newOrdinalCount + 1);
            }

            remainingEntityCount -= appendedEntityCount;
        }

        if (remainingEntityCount != 0)
        {
            throw new InvalidOperationException("The prepared archetype storage does not have enough append capacity.");
        }

        return newOrdinalCount;
    }

    internal int CountPartitionBlocksAfter(ReadOnlySpan<int> removedEntityCounts, int incomingEntityCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(incomingEntityCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(removedEntityCounts.Length, _chunks.Length);
        var remainingEntityCount = incomingEntityCount;
        var partitionCount = 0;

        for (var chunkIndex = 0; chunkIndex < _chunks.Length; chunkIndex++)
        {
            var chunk = _chunks[chunkIndex];
            var removedEntityCount = (chunkIndex < removedEntityCounts.Length)
                ? removedEntityCounts[chunkIndex]
                : 0;
            ArgumentOutOfRangeException.ThrowIfNegative(removedEntityCount);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(removedEntityCount, chunk._count);
            var remainingChunkCapacity = _chunkCapacity - (chunk._count - removedEntityCount);
            var appendedEntityCount = Math.Min(remainingChunkCapacity, remainingEntityCount);
            var finalEntityCount = chunk._count - removedEntityCount + appendedEntityCount;
            partitionCount = checked(partitionCount + ((finalEntityCount + 63) / 64));
            remainingEntityCount -= appendedEntityCount;
        }

        if (remainingEntityCount != 0)
        {
            throw new InvalidOperationException("The prepared archetype storage does not have enough partition capacity.");
        }

        return partitionCount;
    }

    internal void DiscardUncommittedStorage()
    {
        if (_entityCount != 0)
        {
            throw new InvalidOperationException("Storage with live entities cannot be discarded.");
        }

        _chunks = [];
        _availableChunkWords = [];
        _availableChunkSummary = [];
    }

    internal long GetCapacityPreparationBytes(int requiredEntityCount)
    {
        ValidateRequiredCapacity(requiredEntityCount);

        var requiredChunkCount = GetChunkCount(requiredEntityCount, _chunkCapacity);

        if (requiredChunkCount <= _chunks.Length)
        {
            return 0;
        }

        var additionalChunkCount = requiredChunkCount - _chunks.Length;
        var requiredChunkWordCount = GetChunkWordCount(requiredChunkCount);
        var requiredChunkSummaryWordCount = GetChunkSummaryWordCount(requiredChunkWordCount);
        var bytes = checked(
            ArrayBytes(requiredChunkCount, Unsafe.SizeOf<Chunk>())
            + (additionalChunkCount * ArrayBytes(_chunkCapacity, Unsafe.SizeOf<Entity>()))
            + ((additionalChunkCount == 0 || _components.Length == 0)
                ? 0
                : additionalChunkCount * ArrayBytes(_components.Length, IntPtr.Size)));

        foreach (var component in _components)
        {
            bytes = checked(bytes + (additionalChunkCount * component.GetColumnArrayBytes(_chunkCapacity)));
        }

        if (requiredChunkWordCount > _availableChunkWords.Length)
        {
            bytes = checked(bytes + ArrayBytes(requiredChunkWordCount, sizeof(ulong)));
        }

        if (requiredChunkSummaryWordCount > _availableChunkSummary.Length)
        {
            bytes = checked(bytes + ArrayBytes(requiredChunkSummaryWordCount, sizeof(ulong)));
        }

        return bytes;
    }

    internal Span<T> GetColumnSpan<T>(int componentIndex, int chunkIndex, int startRow, int length)
        where T : unmanaged
    {
        var column = GetColumnSlice(componentIndex, chunkIndex, startRow, length);

        if (_components[componentIndex].ComponentType != typeof(T))
        {
            throw new InvalidOperationException("The selected archetype column has a different component type.");
        }

        return ComponentRegistration<T>.GetColumnSpan(column, startRow, length);
    }

    internal ReadOnlySpan<Entity> GetEntities(int chunkIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(chunkIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(chunkIndex, _chunks.Length);

        var chunk = _chunks[chunkIndex];
        return chunk._entities.AsSpan(0, chunk._count);
    }

    internal ReadOnlySpan<Entity> GetEntities(int chunkIndex, int startRow, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(chunkIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(chunkIndex, _chunks.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(startRow);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        var chunk = _chunks[chunkIndex];
        return chunk._entities.AsSpan(startRow, length <= chunk._count - startRow ? length : throw new ArgumentOutOfRangeException(nameof(length)));
    }

    internal int GetPartitionBlockCount()
    {
        var partitionCount = 0;

        foreach (var chunk in _chunks)
        {
            partitionCount = checked(partitionCount + ((chunk._count + 63) / 64));
        }

        return partitionCount;
    }

    internal ReadOnlySpan<T> GetReadOnlyColumnSpan<T>(int componentIndex, int chunkIndex, int startRow, int length)
        where T : unmanaged
    {
        var column = GetColumnSlice(componentIndex, chunkIndex, startRow, length);

        if (_components[componentIndex].ComponentType != typeof(T))
        {
            throw new InvalidOperationException("The selected archetype column has a different component type.");
        }

        return ComponentRegistration<T>.GetReadOnlyColumnSpan(column, startRow, length);
    }

    internal void InitializeColumnRow(
        int componentIndex,
        int chunkIndex,
        int row,
        ComponentInitialization initialization)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(componentIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(componentIndex, _components.Length);
        ArgumentNullException.ThrowIfNull(initialization);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(chunkIndex, _chunks.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ref var chunk = ref _chunks[chunkIndex];

        if ((row != chunk._count) || (row >= _chunkCapacity))
        {
            throw new InvalidOperationException("An initial component value must target the next uncommitted archetype row.");
        }

        _components[componentIndex].InitializeColumn(chunk._columns[componentIndex], row, initialization);
    }

    internal bool PrepareCapacity(int requiredEntityCount)
    {
        if (!IsCapacityValid(requiredEntityCount))
        {
            return false;
        }

        var requiredChunkCount = GetChunkCount(requiredEntityCount, _chunkCapacity);

        if (requiredChunkCount <= _chunks.Length)
        {
            return true;
        }

        var chunks = new Chunk[requiredChunkCount];
        Array.Copy(_chunks, chunks, _chunks.Length);
        var requiredChunkWordCount = GetChunkWordCount(requiredChunkCount);
        var availableChunkWords = _availableChunkWords;

        if (requiredChunkWordCount > availableChunkWords.Length)
        {
            availableChunkWords = new ulong[requiredChunkWordCount];
            Array.Copy(_availableChunkWords, availableChunkWords, _availableChunkWords.Length);
        }

        var requiredChunkSummaryWordCount = GetChunkSummaryWordCount(requiredChunkWordCount);
        var availableChunkSummary = _availableChunkSummary;

        if (requiredChunkSummaryWordCount > availableChunkSummary.Length)
        {
            availableChunkSummary = new ulong[requiredChunkSummaryWordCount];
            Array.Copy(_availableChunkSummary, availableChunkSummary, _availableChunkSummary.Length);
        }

        for (var index = _chunks.Length; index < chunks.Length; index++)
        {
            var columns = (_components.Length == 0) ? [] : new object[_components.Length];

            for (var componentIndex = 0; componentIndex < columns.Length; componentIndex++)
            {
                columns[componentIndex] = _components[componentIndex].CreateColumn(_chunkCapacity);
            }

            chunks[index] = new Chunk(new Entity[_chunkCapacity], columns);
        }

        for (var index = _chunks.Length; index < chunks.Length; index++)
        {
            MarkAvailableChunk(availableChunkWords, availableChunkSummary, index);
        }

        _chunks = chunks;
        _availableChunkWords = availableChunkWords;
        _availableChunkSummary = availableChunkSummary;
        return true;
    }

    internal Entity Remove(Entity entity, EntitySlotTable slots)
    {
        ValidateOwner(entity);

        if (!slots.TryGetLocation(entity.Index, entity.Generation, out var archetypeIndex, out var chunkOrdinal, out var row)
            || (archetypeIndex != _archetypeIndex))
        {
            throw new InvalidOperationException("The entity does not have a live location in this archetype.");
        }

        var chunkIndex = FindChunkIndex(chunkOrdinal);
        ref var chunk = ref _chunks[chunkIndex];

        if ((row >= chunk._count) || (chunk._entities[row] != entity))
        {
            throw new InvalidOperationException("The entity slot and archetype row are inconsistent.");
        }

        var lastRow = chunk._count - 1;
        var movedEntity = chunk._entities[lastRow];

        if (lastRow != row)
        {
            ValidateOwner(movedEntity);

            if (!slots.TryGetLocation(movedEntity.Index, movedEntity.Generation, out var movedArchetypeIndex, out var movedChunkOrdinal, out var movedRow)
                || (movedArchetypeIndex != _archetypeIndex)
                || (movedChunkOrdinal != chunkOrdinal)
                || (movedRow != lastRow))
            {
                throw new InvalidOperationException("The last archetype row does not match its entity slot.");
            }
        }

        slots.ClearLocation(entity.Index, entity.Generation, _archetypeIndex, chunkOrdinal, row);

        if (lastRow != row)
        {
            chunk._entities[row] = movedEntity;
            slots.SetLocation(movedEntity.Index, movedEntity.Generation, _archetypeIndex, chunkOrdinal, row);

            for (var componentIndex = 0; componentIndex < _components.Length; componentIndex++)
            {
                _components[componentIndex].MoveColumnRow(chunk._columns[componentIndex], lastRow, row);
            }
        }

        var chunkWasFull = chunk._count == _chunkCapacity;
        chunk._entities[lastRow] = default;

        for (var componentIndex = 0; componentIndex < _components.Length; componentIndex++)
        {
            _components[componentIndex].ClearColumn(chunk._columns[componentIndex], lastRow);
        }

        chunk._count--;
        _entityCount--;

        if (chunkWasFull)
        {
            MarkAvailableChunk(_availableChunkWords, _availableChunkSummary, chunkIndex);
        }

        return entity;
    }

    internal bool TryAppend(Entity entity, EntitySlotTable slots, ref long nextChunkOrdinal)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nextChunkOrdinal);
        ValidateOwner(entity);

        if (!slots.IsLive(entity.Index, entity.Generation))
        {
            throw new InvalidOperationException("The entity slot is not live at the supplied generation.");
        }

        if (slots.TryGetLocation(entity.Index, entity.Generation, out _, out _, out _))
        {
            throw new InvalidOperationException("The entity already has a storage location.");
        }

        if (_entityCount == _maximumEntityCount)
        {
            return false;
        }

        var chunkIndex = FindAvailableChunkIndex();

        if (chunkIndex < 0)
        {
            throw new InvalidOperationException("Entity archetype storage requires preflighted chunk capacity.");
        }

        ref var chunk = ref _chunks[chunkIndex];

        if (!chunk._hasOrdinal)
        {
            if (nextChunkOrdinal == long.MaxValue)
            {
                return false;
            }

            chunk._ordinal = nextChunkOrdinal;
            chunk._hasOrdinal = true;
            nextChunkOrdinal++;
        }

        var row = chunk._count;
        slots.SetLocation(entity.Index, entity.Generation, _archetypeIndex, chunk._ordinal, row);
        chunk._entities[row] = entity;
        chunk._count++;
        _entityCount++;

        if (chunk._count == _chunkCapacity)
        {
            ClearAvailableChunk(_availableChunkWords, _availableChunkSummary, chunkIndex);
        }

        return true;
    }

    internal bool TryFindChunkIndex(long chunkOrdinal, out int chunkIndex)
    {
        chunkIndex = FindChunkIndex(chunkOrdinal);
        return chunkIndex >= 0;
    }

    internal bool TryGetChunkOrdinal(int chunkIndex, out long chunkOrdinal)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(chunkIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(chunkIndex, _chunks.Length);

        var chunk = _chunks[chunkIndex];

        if (chunk._hasOrdinal)
        {
            chunkOrdinal = chunk._ordinal;
            return true;
        }

        chunkOrdinal = -1;
        return false;
    }

    internal bool TryGetNextAppendLocation(long nextChunkOrdinal, out int chunkIndex, out int row)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nextChunkOrdinal);
        chunkIndex = FindAvailableChunkIndex();

        if (chunkIndex < 0)
        {
            throw new InvalidOperationException("Entity archetype storage requires preflighted chunk capacity.");
        }

        ref var chunk = ref _chunks[chunkIndex];

        if (!chunk._hasOrdinal && (nextChunkOrdinal == long.MaxValue))
        {
            row = -1;
            chunkIndex = -1;
            return false;
        }

        row = chunk._count;
        return true;
    }

    internal ImmutableArray<Diagnostic> ValidateColumnRow(
        int componentIndex,
        int chunkIndex,
        int row,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(componentIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(componentIndex, _components.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(chunkIndex, _chunks.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(row);

        if ((row > _chunks[chunkIndex]._count) || (row >= _chunkCapacity))
        {
            throw new ArgumentOutOfRangeException(nameof(row));
        }

        return _components[componentIndex].ValidateColumnRow(
            _chunks[chunkIndex]._columns[componentIndex],
            row,
            cancellationToken);
    }

    internal void ValidateEntityReferencesColumnRow(
        int componentIndex,
        int chunkIndex,
        int row,
        World world,
        Entity? excludedEntity,
        string? parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(componentIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(componentIndex, _components.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(chunkIndex, _chunks.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(row);

        if ((row > _chunks[chunkIndex]._count) || (row >= _chunkCapacity))
        {
            throw new ArgumentOutOfRangeException(nameof(row));
        }

        _components[componentIndex].ValidateEntityReferencesColumnRow(
            _chunks[chunkIndex]._columns[componentIndex],
            row,
            world,
            excludedEntity,
            parameterName);
    }

    internal void WriteColumnRow(
        int componentIndex,
        int chunkIndex,
        int row,
        ComponentInitialization initialization)
    {
        ArgumentNullException.ThrowIfNull(initialization);
        ArgumentOutOfRangeException.ThrowIfNegative(componentIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(componentIndex, _components.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(chunkIndex, _chunks.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(row);

        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, _chunks[chunkIndex]._count);

        if (initialization.IsDefault)
        {
            throw new ArgumentException("Command playback requires a resolved explicit component value.", nameof(initialization));
        }

        _components[componentIndex].InitializeColumn(_chunks[chunkIndex]._columns[componentIndex], row, initialization);
    }

    private int FindAvailableChunkIndex()
    {
        for (var summaryIndex = 0; summaryIndex < _availableChunkSummary.Length; summaryIndex++)
        {
            var summaryWord = _availableChunkSummary[summaryIndex];

            if (summaryWord != 0)
            {
                var wordIndex = (summaryIndex * 64) + BitOperations.TrailingZeroCount(summaryWord);
                var word = _availableChunkWords[wordIndex];
                var chunkIndex = (wordIndex * 64) + BitOperations.TrailingZeroCount(word);

                if ((word == 0) || (chunkIndex >= _chunks.Length) || (_chunks[chunkIndex]._count >= _chunkCapacity))
                {
                    throw new InvalidOperationException("The entity archetype available-chunk index is inconsistent.");
                }

                return chunkIndex;
            }
        }

        return -1;
    }

    private int FindChunkIndex(long chunkOrdinal)
    {
        for (var index = 0; index < _chunks.Length; index++)
        {
            var chunk = _chunks[index];

            if (chunk._hasOrdinal && (chunk._ordinal == chunkOrdinal))
            {
                return index;
            }
        }

        throw new InvalidOperationException("The entity slot references a missing archetype chunk.");
    }

    private object GetColumnSlice(int componentIndex, int chunkIndex, int startRow, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(componentIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(componentIndex, _components.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(chunkIndex, _chunks.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(startRow);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        var chunk = _chunks[chunkIndex];

        if ((startRow > chunk._count) || (length > chunk._count - startRow))
        {
            throw new ArgumentOutOfRangeException(nameof(length), "The selected column slice exceeds the populated chunk rows.");
        }

        return chunk._columns[componentIndex];
    }

    private bool IsCapacityValid(int requiredEntityCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(requiredEntityCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(requiredEntityCount, _entityCount);
        return requiredEntityCount <= _maximumEntityCount;
    }

    private void ValidateOwner(Entity entity)
    {
        if (!entity.IsValid)
        {
            throw new ArgumentException("An invalid entity cannot be stored.", nameof(entity));
        }

        if (entity.OwnerId != _ownerId)
        {
            throw new ArgumentException("The entity belongs to another World.", nameof(entity));
        }
    }

    private void ValidateRequiredCapacity(int requiredEntityCount)
    {
        if (requiredEntityCount > _maximumEntityCount)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredEntityCount), "The required entity capacity exceeds the configured World limit.");
        }

        if (!IsCapacityValid(requiredEntityCount))
        {
            throw new InvalidOperationException("The requested entity capacity is invalid.");
        }
    }

    private struct Chunk
    {
        internal Entity[] _entities;
        internal object[] _columns;
        internal int _count;
        internal bool _hasOrdinal;
        internal long _ordinal;

        internal Chunk(Entity[] entities, object[] columns)
        {
            _entities = entities;
            _columns = columns;
            _ordinal = -1;
            _count = 0;
            _hasOrdinal = false;
        }
    }
}
