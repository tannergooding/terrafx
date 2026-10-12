// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests;

[TestFixture]
internal sealed class EntitySlotTableTests
{
    [Test]
    public void AllocationRequiresPreflightedCapacityAndStartsAtSlotOneGenerationOne()
    {
        var table = new EntitySlotTable(2);

        _ = Assert.Throws<InvalidOperationException>(() => table.TryAllocate(out _, out _));
        Assert.That(table.PrepareCapacity(2, 0), Is.True);
        Assert.That(table.TryAllocate(out var index, out var generation), Is.True);
        Assert.That(index, Is.EqualTo(1));
        Assert.That(generation, Is.EqualTo(1));
        Assert.That(table.AllocatedSlotCount, Is.EqualTo(1));
        Assert.That(table.LiveEntityCount, Is.EqualTo(1));
    }

    [Test]
    public void CapacityPreparationBytesMatchRequiredBackingArrayGrowth()
    {
        var table = new EntitySlotTable(2);
        var requiredBytes = table.GetCapacityPreparationBytes(2, 1);

        Assert.That(table.PrepareCapacity(2, 1), Is.True);

        var reservedBytes = table.OwnedByteReservation;
        Assert.That(requiredBytes, Is.EqualTo(reservedBytes));
        Assert.That(table.GetCapacityPreparationBytes(2, 1), Is.Zero);
    }

    [Test]
    public void CapacityRefusalDoesNotChangeSlotState()
    {
        var table = new EntitySlotTable(1);

        Assert.That(table.PrepareCapacity(2, 0), Is.False);
        Assert.That(table.AllocatedSlotCount, Is.Zero);
        Assert.That(table.LiveEntityCount, Is.Zero);
        _ = Assert.Throws<InvalidOperationException>(() => table.TryAllocate(out _, out _));

        Assert.That(table.PrepareCapacity(1, 0), Is.True);
        Assert.That(table.TryAllocate(out var index, out var generation), Is.True);
        Assert.That(index, Is.EqualTo(1));
        Assert.That(generation, Is.EqualTo(1));
        Assert.That(table.TryAllocate(out var refusedIndex, out var refusedGeneration), Is.False);
        Assert.That(refusedIndex, Is.Zero);
        Assert.That(refusedGeneration, Is.Zero);
    }

    [Test]
    public void MaximumGenerationRetiresSlotWithoutReturningItToFreeList()
    {
        var table = EntitySlotTable.CreateForTesting(2, uint.MaxValue);
        Assert.That(table.PrepareCapacity(2, 0), Is.True);
        Assert.That(table.TryAllocate(out var index, out var generation), Is.True);
        Assert.That(generation, Is.EqualTo(uint.MaxValue));

        table.Release(index, generation);

        Assert.That(table.RetiredSlotCount, Is.EqualTo(1));
        Assert.That(table.FreeSlotCount, Is.Zero);
        Assert.That(table.AllocatedSlotCount, Is.EqualTo(1));
        Assert.That(table.TryAllocate(out var nextIndex, out var nextGeneration), Is.True);
        Assert.That(nextIndex, Is.EqualTo(2));
        Assert.That(nextGeneration, Is.EqualTo(uint.MaxValue));
    }

    [Test]
    public void ReleaseInvalidatesLocationAndReuseAdvancesGeneration()
    {
        var table = new EntitySlotTable(2);
        Assert.That(table.PrepareCapacity(2, 1), Is.True);
        Assert.That(table.TryAllocate(out var index, out var generation), Is.True);
        table.SetLocation(index, generation, archetypeIndex: 3, chunkOrdinal: 7, row: 11);

        Assert.That(table.TryGetLocation(index, generation, out var archetypeIndex, out var chunkOrdinal, out var row), Is.True);
        Assert.That(archetypeIndex, Is.EqualTo(3));
        Assert.That(chunkOrdinal, Is.EqualTo(7));
        Assert.That(row, Is.EqualTo(11));

        table.Release(index, generation);

        Assert.That(table.IsLive(index, generation), Is.False);
        Assert.That(table.TryGetLocation(index, generation, out archetypeIndex, out chunkOrdinal, out row), Is.False);
        Assert.That(archetypeIndex, Is.EqualTo(-1));
        Assert.That(chunkOrdinal, Is.EqualTo(-1));
        Assert.That(row, Is.EqualTo(-1));

        Assert.That(table.TryAllocate(out var reusedIndex, out var reusedGeneration), Is.True);
        Assert.That(reusedIndex, Is.EqualTo(index));
        Assert.That(reusedGeneration, Is.EqualTo(generation + 1));
        Assert.That(table.RetiredSlotCount, Is.Zero);
    }

    [Test]
    public void ReleaseRequiresPreflightedFreeListCapacity()
    {
        var table = new EntitySlotTable(1);
        Assert.That(table.PrepareCapacity(1, 0), Is.True);
        Assert.That(table.TryAllocate(out var index, out var generation), Is.True);
        table.SetLocation(index, generation, archetypeIndex: 0, chunkOrdinal: 0, row: 0);

        _ = Assert.Throws<InvalidOperationException>(() => table.Release(index, generation));

        Assert.That(table.IsLive(index, generation), Is.True);
        Assert.That(table.LiveEntityCount, Is.EqualTo(1));
        Assert.That(table.TryGetLocation(index, generation, out var archetypeIndex, out var chunkOrdinal, out var row), Is.True);
        Assert.That(archetypeIndex, Is.Zero);
        Assert.That(chunkOrdinal, Is.Zero);
        Assert.That(row, Is.Zero);
        Assert.That(table.PrepareCapacity(1, 1), Is.True);
        table.Release(index, generation);
        Assert.That(table.LiveEntityCount, Is.Zero);
        Assert.That(table.FreeSlotCount, Is.EqualTo(1));
    }
}
