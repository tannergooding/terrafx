// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace TerraFX.Entities;

internal sealed class EntityArchetypeStorage
{
    internal const int ChunkCapacity = 256;

    private readonly int _archetypeIndex;
    private ulong[] _availableChunkSummary;
    private ulong[] _availableChunkWords;
    private Chunk[] _chunks;
    private int _entityCount;
    private readonly int _maximumEntityCount;
    private readonly uint _ownerId;

    internal EntityArchetypeStorage(uint ownerId, int archetypeIndex, int maximumEntityCount)
    {
        ArgumentOutOfRangeException.ThrowIfZero(ownerId);
        ArgumentOutOfRangeException.ThrowIfNegative(archetypeIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumEntityCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumEntityCount, EntitySlotTable.MaximumSlots);

        _archetypeIndex = archetypeIndex;
        _maximumEntityCount = maximumEntityCount;
        _ownerId = ownerId;
        _availableChunkSummary = [];
        _availableChunkWords = [];
        _chunks = [];
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
            }

            return bytes;
        }
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

    private static int GetChunkCount(int entityCount)
        => checked((int)(((long)entityCount + ChunkCapacity - 1) / ChunkCapacity));

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

    internal long GetCapacityPreparationBytes(int requiredEntityCount)
    {
        ValidateRequiredCapacity(requiredEntityCount);

        var requiredChunkCount = GetChunkCount(requiredEntityCount);

        if (requiredChunkCount <= _chunks.Length)
        {
            return 0;
        }

        var additionalChunkCount = requiredChunkCount - _chunks.Length;
        var requiredChunkWordCount = GetChunkWordCount(requiredChunkCount);
        var requiredChunkSummaryWordCount = GetChunkSummaryWordCount(requiredChunkWordCount);
        var bytes = checked(
            ArrayBytes(requiredChunkCount, Unsafe.SizeOf<Chunk>())
            + (additionalChunkCount * ArrayBytes(ChunkCapacity, Unsafe.SizeOf<Entity>())));

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

    internal ReadOnlySpan<Entity> GetEntities(int chunkIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(chunkIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(chunkIndex, _chunks.Length);

        var chunk = _chunks[chunkIndex];
        return chunk._entities.AsSpan(0, chunk._count);
    }

    internal bool PrepareCapacity(int requiredEntityCount)
    {
        if (!IsCapacityValid(requiredEntityCount))
        {
            return false;
        }

        var requiredChunkCount = GetChunkCount(requiredEntityCount);

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
            chunks[index] = new Chunk(new Entity[ChunkCapacity]);
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
        }

        var chunkWasFull = chunk._count == ChunkCapacity;
        chunk._entities[lastRow] = default;
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

        if (chunk._count == ChunkCapacity)
        {
            ClearAvailableChunk(_availableChunkWords, _availableChunkSummary, chunkIndex);
        }

        return true;
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

                if ((word == 0) || (chunkIndex >= _chunks.Length) || (_chunks[chunkIndex]._count >= ChunkCapacity))
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
        internal int _count;
        internal bool _hasOrdinal;
        internal long _ordinal;

        internal Chunk(Entity[] entities)
        {
            _entities = entities;
            _ordinal = -1;
            _count = 0;
            _hasOrdinal = false;
        }
    }
}
