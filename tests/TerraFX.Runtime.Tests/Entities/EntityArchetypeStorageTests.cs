// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests;

[TestFixture]
internal sealed class EntityArchetypeStorageTests
{
    [Test]
    public void AvailabilityIndexFindsTheNextChunkAcrossSummaryWords()
    {
        const int FullChunks = 4096;
        const int EntityCountBeforeLastChunk = FullChunks * EntityArchetypeStorage.ChunkCapacity;
        const int MaximumEntityCount = EntityCountBeforeLastChunk + 2;

        var slots = new EntitySlotTable(MaximumEntityCount);
        Assert.That(slots.PrepareCapacity(MaximumEntityCount, 0), Is.True);
        var storage = new EntityArchetypeStorage(ownerId: 7, archetypeIndex: 0, maximumEntityCount: MaximumEntityCount);
        Assert.That(storage.PrepareCapacity(MaximumEntityCount), Is.True);
        Entity lastEntity = default;
        var nextChunkOrdinal = 0L;

        for (var index = 0; index < MaximumEntityCount; index++)
        {
            if (!slots.TryAllocate(out var slotIndex, out var generation))
            {
                Assert.Fail("The prepared entity slot capacity was refused.");
            }

            var entity = new Entity(7, slotIndex, generation);

            if (!storage.TryAppend(entity, slots, ref nextChunkOrdinal))
            {
                Assert.Fail("The prepared archetype capacity was refused.");
            }

            lastEntity = entity;
        }

        Assert.That(storage.ChunkCount, Is.EqualTo(FullChunks + 1));
        Assert.That(storage.TryGetChunkOrdinal(FullChunks, out var chunkOrdinal), Is.True);
        Assert.That(chunkOrdinal, Is.EqualTo(FullChunks));
        Assert.That(storage.GetEntities(FullChunks).Length, Is.EqualTo(2));
        Assert.That(slots.TryGetLocation(lastEntity.Index, lastEntity.Generation, out _, out chunkOrdinal, out var row), Is.True);
        Assert.That(chunkOrdinal, Is.EqualTo(FullChunks));
        Assert.That(row, Is.EqualTo(1));
    }

    [Test]
    public void CapacityPreparationAccountsForNewChunkAndEntityArrays()
    {
        var storage = new EntityArchetypeStorage(ownerId: 7, archetypeIndex: 0, maximumEntityCount: 257);
        var requiredBytes = storage.GetCapacityPreparationBytes(257);

        Assert.That(storage.PrepareCapacity(257), Is.True);
        Assert.That(storage.ChunkCount, Is.EqualTo(2));
        Assert.That(storage.OwnedByteReservation, Is.EqualTo(requiredBytes));
        Assert.That(storage.GetCapacityPreparationBytes(257), Is.Zero);
        Assert.That(storage.TryGetChunkOrdinal(0, out _), Is.False);
        Assert.That(storage.TryGetChunkOrdinal(1, out _), Is.False);
    }

    [Test]
    public void CapacityRefusalLeavesAnUnlocatedLiveEntityUnchanged()
    {
        var slots = new EntitySlotTable(2);
        Assert.That(slots.PrepareCapacity(2, 0), Is.True);
        Assert.That(slots.TryAllocate(out var firstIndex, out var firstGeneration), Is.True);
        Assert.That(slots.TryAllocate(out var secondIndex, out var secondGeneration), Is.True);
        var first = new Entity(7, firstIndex, firstGeneration);
        var second = new Entity(7, secondIndex, secondGeneration);
        var storage = new EntityArchetypeStorage(ownerId: 7, archetypeIndex: 0, maximumEntityCount: 1);
        var nextChunkOrdinal = 0L;

        Assert.That(storage.PrepareCapacity(1), Is.True);
        Assert.That(storage.TryAppend(first, slots, ref nextChunkOrdinal), Is.True);
        Assert.That(storage.TryAppend(second, slots, ref nextChunkOrdinal), Is.False);

        Assert.That(storage.EntityCount, Is.EqualTo(1));
        Assert.That(storage.GetEntities(0).Length, Is.EqualTo(1));
        Assert.That(slots.TryGetLocation(second.Index, second.Generation, out _, out _, out _), Is.False);
        Assert.That(slots.IsLive(second.Index, second.Generation), Is.True);
    }

    [Test]
    public void ChunkOrdinalExhaustionRefusesWithoutChangingTheSlotOrCursor()
    {
        var slots = new EntitySlotTable(1);
        Assert.That(slots.PrepareCapacity(1, 0), Is.True);
        Assert.That(slots.TryAllocate(out var index, out var generation), Is.True);
        var entity = new Entity(7, index, generation);
        var storage = new EntityArchetypeStorage(ownerId: 7, archetypeIndex: 0, maximumEntityCount: 1);
        Assert.That(storage.PrepareCapacity(1), Is.True);
        var nextChunkOrdinal = long.MaxValue;

        Assert.That(storage.TryAppend(entity, slots, ref nextChunkOrdinal), Is.False);

        Assert.That(nextChunkOrdinal, Is.EqualTo(long.MaxValue));
        Assert.That(storage.EntityCount, Is.Zero);
        Assert.That(storage.TryGetChunkOrdinal(0, out _), Is.False);
        Assert.That(slots.IsLive(index, generation), Is.True);
        Assert.That(slots.TryGetLocation(index, generation, out _, out _, out _), Is.False);
    }

    [Test]
    public void ChunkOrdinalsUseTheSharedWorldCursorAcrossArchetypes()
    {
        var slots = new EntitySlotTable(2);
        Assert.That(slots.PrepareCapacity(2, 0), Is.True);
        Assert.That(slots.TryAllocate(out var firstIndex, out var firstGeneration), Is.True);
        Assert.That(slots.TryAllocate(out var secondIndex, out var secondGeneration), Is.True);
        var firstStorage = new EntityArchetypeStorage(ownerId: 7, archetypeIndex: 0, maximumEntityCount: 1);
        var secondStorage = new EntityArchetypeStorage(ownerId: 7, archetypeIndex: 1, maximumEntityCount: 1);
        Assert.That(firstStorage.PrepareCapacity(1), Is.True);
        Assert.That(secondStorage.PrepareCapacity(1), Is.True);
        var nextChunkOrdinal = 0L;

        Assert.That(firstStorage.TryAppend(new Entity(7, firstIndex, firstGeneration), slots, ref nextChunkOrdinal), Is.True);
        Assert.That(secondStorage.TryAppend(new Entity(7, secondIndex, secondGeneration), slots, ref nextChunkOrdinal), Is.True);

        Assert.That(nextChunkOrdinal, Is.EqualTo(2));
        Assert.That(firstStorage.TryGetChunkOrdinal(0, out var firstChunkOrdinal), Is.True);
        Assert.That(secondStorage.TryGetChunkOrdinal(0, out var secondChunkOrdinal), Is.True);
        Assert.That(firstChunkOrdinal, Is.Zero);
        Assert.That(secondChunkOrdinal, Is.EqualTo(1));
    }

    [Test]
    public void RemovalFromOneChunkDoesNotMoveAnEntityAcrossChunks()
    {
        var slots = new EntitySlotTable(258);
        Assert.That(slots.PrepareCapacity(258, 1), Is.True);
        var storage = new EntityArchetypeStorage(ownerId: 7, archetypeIndex: 0, maximumEntityCount: 258);
        Assert.That(storage.PrepareCapacity(258), Is.True);
        var entities = new Entity[257];
        var nextChunkOrdinal = 0L;

        for (var index = 0; index < entities.Length; index++)
        {
            Assert.That(slots.TryAllocate(out var slotIndex, out var generation), Is.True);
            entities[index] = new Entity(7, slotIndex, generation);
            Assert.That(storage.TryAppend(entities[index], slots, ref nextChunkOrdinal), Is.True);
        }

        Assert.That(storage.ChunkCount, Is.EqualTo(2));
        Assert.That(storage.TryGetChunkOrdinal(0, out var firstChunkOrdinal), Is.True);
        Assert.That(storage.TryGetChunkOrdinal(1, out var secondChunkOrdinal), Is.True);
        Assert.That(firstChunkOrdinal, Is.EqualTo(0));
        Assert.That(secondChunkOrdinal, Is.EqualTo(1));

        _ = storage.Remove(entities[5], slots);

        var firstChunk = storage.GetEntities(0);
        var secondChunk = storage.GetEntities(1);
        Assert.That(firstChunk.Length, Is.EqualTo(255));
        Assert.That(firstChunk[5], Is.EqualTo(entities[255]));
        Assert.That(secondChunk.Length, Is.EqualTo(1));
        Assert.That(secondChunk[0], Is.EqualTo(entities[256]));
        Assert.That(slots.TryGetLocation(entities[255].Index, entities[255].Generation, out var archetypeIndex, out var chunkOrdinal, out var row), Is.True);
        Assert.That(archetypeIndex, Is.Zero);
        Assert.That(chunkOrdinal, Is.EqualTo(firstChunkOrdinal));
        Assert.That(row, Is.EqualTo(5));
        Assert.That(slots.TryGetLocation(entities[256].Index, entities[256].Generation, out _, out chunkOrdinal, out row), Is.True);
        Assert.That(chunkOrdinal, Is.EqualTo(secondChunkOrdinal));
        Assert.That(row, Is.Zero);
        Assert.That(slots.TryGetLocation(entities[5].Index, entities[5].Generation, out _, out _, out _), Is.False);

        slots.Release(entities[5].Index, entities[5].Generation);
        Assert.That(slots.TryAllocate(out var reusedIndex, out var reusedGeneration), Is.True);
        var reusedEntity = new Entity(7, reusedIndex, reusedGeneration);
        Assert.That(storage.TryAppend(reusedEntity, slots, ref nextChunkOrdinal), Is.True);

        firstChunk = storage.GetEntities(0);
        secondChunk = storage.GetEntities(1);
        Assert.That(firstChunk.Length, Is.EqualTo(256));
        Assert.That(firstChunk[255], Is.EqualTo(reusedEntity));
        Assert.That(secondChunk.Length, Is.EqualTo(1));
        Assert.That(secondChunk[0], Is.EqualTo(entities[256]));
        Assert.That(slots.TryGetLocation(reusedEntity.Index, reusedEntity.Generation, out archetypeIndex, out chunkOrdinal, out row), Is.True);
        Assert.That(archetypeIndex, Is.Zero);
        Assert.That(chunkOrdinal, Is.EqualTo(firstChunkOrdinal));
        Assert.That(row, Is.EqualTo(255));
    }

    [Test]
    public void RemoveSwapMovesTheLastRowAndRepairsItsSlot()
    {
        var slots = new EntitySlotTable(3);
        Assert.That(slots.PrepareCapacity(3, 0), Is.True);
        var storage = new EntityArchetypeStorage(ownerId: 7, archetypeIndex: 2, maximumEntityCount: 3);
        Assert.That(storage.PrepareCapacity(3), Is.True);
        Assert.That(slots.TryAllocate(out var firstIndex, out var firstGeneration), Is.True);
        Assert.That(slots.TryAllocate(out var secondIndex, out var secondGeneration), Is.True);
        Assert.That(slots.TryAllocate(out var thirdIndex, out var thirdGeneration), Is.True);
        var first = new Entity(7, firstIndex, firstGeneration);
        var second = new Entity(7, secondIndex, secondGeneration);
        var third = new Entity(7, thirdIndex, thirdGeneration);
        var nextChunkOrdinal = 0L;

        Assert.That(storage.TryAppend(first, slots, ref nextChunkOrdinal), Is.True);
        Assert.That(storage.TryAppend(second, slots, ref nextChunkOrdinal), Is.True);
        Assert.That(storage.TryAppend(third, slots, ref nextChunkOrdinal), Is.True);
        Assert.That(storage.Remove(second, slots), Is.EqualTo(second));

        var entities = storage.GetEntities(0);
        Assert.That(storage.EntityCount, Is.EqualTo(2));
        Assert.That(entities.Length, Is.EqualTo(2));
        Assert.That(entities[0], Is.EqualTo(first));
        Assert.That(entities[1], Is.EqualTo(third));
        Assert.That(slots.TryGetLocation(second.Index, second.Generation, out _, out _, out _), Is.False);
        Assert.That(slots.TryGetLocation(third.Index, third.Generation, out var archetypeIndex, out _, out var row), Is.True);
        Assert.That(archetypeIndex, Is.EqualTo(2));
        Assert.That(row, Is.EqualTo(1));

        Assert.That(storage.TryAppend(second, slots, ref nextChunkOrdinal), Is.True);
        Assert.That(slots.TryGetLocation(second.Index, second.Generation, out archetypeIndex, out _, out row), Is.True);
        Assert.That(archetypeIndex, Is.EqualTo(2));
        Assert.That(row, Is.EqualTo(2));
    }

    [Test]
    public void StaleEntityCannotRemoveStorageForAReusedSlot()
    {
        var slots = new EntitySlotTable(1);
        Assert.That(slots.PrepareCapacity(1, 1), Is.True);
        Assert.That(slots.TryAllocate(out var index, out var generation), Is.True);
        var staleEntity = new Entity(7, index, generation);
        var storage = new EntityArchetypeStorage(ownerId: 7, archetypeIndex: 0, maximumEntityCount: 1);
        Assert.That(storage.PrepareCapacity(1), Is.True);
        var nextChunkOrdinal = 0L;
        Assert.That(storage.TryAppend(staleEntity, slots, ref nextChunkOrdinal), Is.True);

        _ = storage.Remove(staleEntity, slots);
        slots.Release(index, generation);
        Assert.That(slots.TryAllocate(out var reusedIndex, out var reusedGeneration), Is.True);
        var currentEntity = new Entity(7, reusedIndex, reusedGeneration);
        Assert.That(storage.TryAppend(currentEntity, slots, ref nextChunkOrdinal), Is.True);

        _ = Assert.Throws<InvalidOperationException>(() => storage.Remove(staleEntity, slots));

        var entities = storage.GetEntities(0);
        Assert.That(storage.EntityCount, Is.EqualTo(1));
        Assert.That(entities.Length, Is.EqualTo(1));
        Assert.That(entities[0], Is.EqualTo(currentEntity));
        Assert.That(slots.TryGetLocation(currentEntity.Index, currentEntity.Generation, out _, out _, out _), Is.True);
    }

    [Test]
    public void StorageRejectsForeignStaleAndAlreadyLocatedEntities()
    {
        var slots = new EntitySlotTable(1);
        Assert.That(slots.PrepareCapacity(1, 0), Is.True);
        Assert.That(slots.TryAllocate(out var index, out var generation), Is.True);
        var entity = new Entity(7, index, generation);
        var storage = new EntityArchetypeStorage(ownerId: 7, archetypeIndex: 0, maximumEntityCount: 1);
        Assert.That(storage.PrepareCapacity(1), Is.True);
        var nextChunkOrdinal = 0L;

        _ = Assert.Throws<ArgumentException>(() => storage.TryAppend(new Entity(8, index, generation), slots, ref nextChunkOrdinal));
        _ = Assert.Throws<InvalidOperationException>(() => storage.TryAppend(new Entity(7, index, generation + 1), slots, ref nextChunkOrdinal));
        Assert.That(storage.TryAppend(entity, slots, ref nextChunkOrdinal), Is.True);
        _ = Assert.Throws<InvalidOperationException>(() => storage.TryAppend(entity, slots, ref nextChunkOrdinal));

        Assert.That(storage.EntityCount, Is.EqualTo(1));
        var entities = storage.GetEntities(0);
        Assert.That(entities.Length, Is.EqualTo(1));
        Assert.That(entities[0], Is.EqualTo(entity));
    }
}
