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
    public void AllocationSequencePreviewMatchesFreeListOrderWithoutMutation()
    {
        var table = new EntitySlotTable(4);
        Assert.That(table.PrepareCapacity(3, 2), Is.True);
        Assert.That(table.TryAllocate(out var first, out var firstGeneration), Is.True);
        Assert.That(table.TryAllocate(out _, out _), Is.True);
        Assert.That(table.TryAllocate(out var third, out var thirdGeneration), Is.True);
        table.Release(first, firstGeneration);
        table.Release(third, thirdGeneration);

        Assert.That(table.TryGetAllocationPreview(0, out var preview0, out var generation0), Is.True);
        Assert.That(table.TryGetAllocationPreview(1, out var preview1, out var generation1), Is.True);
        Assert.That(table.TryGetAllocationPreview(2, out var preview2, out var generation2), Is.True);
        Assert.That(table.TryGetAllocationPreview(3, out _, out _), Is.False);

        Assert.That(preview0, Is.EqualTo(third));
        Assert.That(generation0, Is.EqualTo(thirdGeneration + 1));
        Assert.That(preview1, Is.EqualTo(first));
        Assert.That(generation1, Is.EqualTo(firstGeneration + 1));
        Assert.That(preview2, Is.EqualTo(4u));
        Assert.That(generation2, Is.EqualTo(1));
        Assert.That(table.AllocatedSlotCount, Is.EqualTo(3));
        Assert.That(table.FreeSlotCount, Is.EqualTo(2));
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
    public void ClearLocationRequiresTheExpectedLiveLocation()
    {
        var table = new EntitySlotTable(1);
        Assert.That(table.PrepareCapacity(1, 0), Is.True);
        Assert.That(table.TryAllocate(out var index, out var generation), Is.True);
        table.SetLocation(index, generation, archetypeIndex: 3, chunkOrdinal: 7, row: 11);

        _ = Assert.Throws<InvalidOperationException>(() => table.ClearLocation(index, generation, archetypeIndex: 3, chunkOrdinal: 7, row: 10));

        Assert.That(table.TryGetLocation(index, generation, out var archetypeIndex, out var chunkOrdinal, out var row), Is.True);
        Assert.That(archetypeIndex, Is.EqualTo(3));
        Assert.That(chunkOrdinal, Is.EqualTo(7));
        Assert.That(row, Is.EqualTo(11));

        table.ClearLocation(index, generation, archetypeIndex: 3, chunkOrdinal: 7, row: 11);

        Assert.That(table.TryGetLocation(index, generation, out _, out _, out _), Is.False);
        _ = Assert.Throws<InvalidOperationException>(() => table.ClearLocation(index, generation, archetypeIndex: 3, chunkOrdinal: 7, row: 11));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => table.ClearLocation(index, generation, archetypeIndex: -1, chunkOrdinal: -1, row: -1));
    }

    [Test]
    public void HierarchyLinksPreserveSiblingOrderAndDepthAcrossReparenting()
    {
        var table = new EntitySlotTable(4);
        Assert.That(table.PrepareCapacity(4, 4), Is.True);
        Assert.That(table.TryAllocate(out var rootIndex, out var rootGeneration), Is.True);
        Assert.That(table.TryAllocate(out var firstChildIndex, out var firstChildGeneration), Is.True);
        Assert.That(table.TryAllocate(out var secondChildIndex, out var secondChildGeneration), Is.True);
        Assert.That(table.TryAllocate(out var grandchildIndex, out var grandchildGeneration), Is.True);

        table.SetParent(firstChildIndex, firstChildGeneration, rootIndex, rootGeneration);
        table.SetParent(secondChildIndex, secondChildGeneration, rootIndex, rootGeneration);
        table.SetParent(grandchildIndex, grandchildGeneration, firstChildIndex, firstChildGeneration);

        Assert.That(table.GetHierarchyDepth(rootIndex, rootGeneration), Is.Zero);
        Assert.That(table.GetHierarchyDepth(firstChildIndex, firstChildGeneration), Is.EqualTo(1));
        Assert.That(table.GetHierarchyDepth(grandchildIndex, grandchildGeneration), Is.EqualTo(2));
        Assert.That(
            table.TryGetFirstChild(rootIndex, rootGeneration, out var firstChild, out _),
            Is.True);
        Assert.That(firstChild, Is.EqualTo(firstChildIndex));
        Assert.That(
            table.TryGetNextSibling(firstChildIndex, firstChildGeneration, out var nextSibling, out _),
            Is.True);
        Assert.That(nextSibling, Is.EqualTo(secondChildIndex));

        table.SetParent(secondChildIndex, secondChildGeneration, grandchildIndex, grandchildGeneration);

        Assert.That(table.TryGetNextSibling(firstChildIndex, firstChildGeneration, out _, out _), Is.False);
        Assert.That(table.GetHierarchyDepth(secondChildIndex, secondChildGeneration), Is.EqualTo(3));
        Assert.That(
            table.TryGetFirstChild(grandchildIndex, grandchildGeneration, out var movedChild, out _),
            Is.True);
        Assert.That(movedChild, Is.EqualTo(secondChildIndex));

        table.SetParent(secondChildIndex, secondChildGeneration, parentIndex: 0, parentGeneration: 0);

        Assert.That(table.TryGetParent(secondChildIndex, secondChildGeneration, out _, out _), Is.False);
        Assert.That(table.GetHierarchyDepth(secondChildIndex, secondChildGeneration), Is.Zero);
        Assert.That(table.TryGetFirstChild(grandchildIndex, grandchildGeneration, out _, out _), Is.False);
    }

    [Test]
    public void HierarchyParentChangesRejectCyclesAndDepthOverflowWithoutMutation()
    {
        var slotCount = EntitySlotTable.MaximumHierarchyDepth + 2;
        var table = new EntitySlotTable(slotCount);
        Assert.That(table.PrepareCapacity(slotCount, 0), Is.True);
        var generations = new uint[slotCount + 1];

        for (var index = 1; index <= slotCount; index++)
        {
            Assert.That(table.TryAllocate(out var allocatedIndex, out var generation), Is.True);
            Assert.That(allocatedIndex, Is.EqualTo((uint)index));
            generations[index] = generation;

            if (index is > 1 and <= EntitySlotTable.MaximumHierarchyDepth + 1)
            {
                table.SetParent(
                    allocatedIndex,
                    generation,
                    allocatedIndex - 1,
                    generations[index - 1]);
            }
        }

        var deepestIndex = checked((uint)(EntitySlotTable.MaximumHierarchyDepth + 1));
        Assert.That(
            table.GetHierarchyDepth(deepestIndex, generations[checked((int)deepestIndex)]),
            Is.EqualTo(EntitySlotTable.MaximumHierarchyDepth));

        _ = Assert.Throws<InvalidOperationException>(
            () => table.SetParent(
                1,
                generations[1],
                deepestIndex,
                generations[checked((int)deepestIndex)]));

        var overflowChildIndex = checked(deepestIndex + 1);
        _ = Assert.Throws<InvalidOperationException>(
            () => table.SetParent(
                overflowChildIndex,
                generations[checked((int)overflowChildIndex)],
                deepestIndex,
                generations[checked((int)deepestIndex)]));
        Assert.That(
            table.TryGetParent(
                overflowChildIndex,
                generations[checked((int)overflowChildIndex)],
                out _,
                out _),
            Is.False);
    }

    [Test]
    public void IncrementToMaximumGenerationRemainsUsableUntilThatGenerationIsDestroyed()
    {
        var table = EntitySlotTable.CreateForTesting(1, uint.MaxValue - 1);
        Assert.That(table.PrepareCapacity(1, 1), Is.True);
        Assert.That(table.TryAllocate(out var index, out var generation), Is.True);
        Assert.That(generation, Is.EqualTo(uint.MaxValue - 1));

        table.Release(index, generation);

        Assert.That(table.TryAllocate(out var reusedIndex, out var maximumGeneration), Is.True);
        Assert.That(reusedIndex, Is.EqualTo(index));
        Assert.That(maximumGeneration, Is.EqualTo(uint.MaxValue));
        Assert.That(table.IsLive(index, generation), Is.False);
        Assert.That(table.IsLive(index, maximumGeneration), Is.True);

        table.Release(index, maximumGeneration);

        Assert.That(table.RetiredSlotCount, Is.EqualTo(1));
        Assert.That(table.FreeSlotCount, Is.Zero);
        Assert.That(table.LiveEntityCount, Is.Zero);
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
    public void MultipleReleasedSlotsAreReusedInReverseReleaseOrder()
    {
        var table = new EntitySlotTable(3);
        Assert.That(table.PrepareCapacity(3, 3), Is.True);
        Assert.That(table.TryAllocate(out var firstIndex, out var firstGeneration), Is.True);
        Assert.That(table.TryAllocate(out var secondIndex, out var secondGeneration), Is.True);
        Assert.That(table.TryAllocate(out var thirdIndex, out var thirdGeneration), Is.True);

        table.Release(firstIndex, firstGeneration);
        table.Release(secondIndex, secondGeneration);

        Assert.That(table.TryAllocate(out var reusedSecondIndex, out var reusedSecondGeneration), Is.True);
        Assert.That(reusedSecondIndex, Is.EqualTo(secondIndex));
        Assert.That(reusedSecondGeneration, Is.EqualTo(secondGeneration + 1));
        Assert.That(table.TryAllocate(out var reusedFirstIndex, out var reusedFirstGeneration), Is.True);
        Assert.That(reusedFirstIndex, Is.EqualTo(firstIndex));
        Assert.That(reusedFirstGeneration, Is.EqualTo(firstGeneration + 1));
        Assert.That(table.IsLive(secondIndex, secondGeneration), Is.False);
        Assert.That(table.IsLive(firstIndex, firstGeneration), Is.False);
        Assert.That(table.IsLive(thirdIndex, thirdGeneration), Is.True);
    }

    [Test]
    public void NextAllocationPreviewDoesNotMutateTheTable()
    {
        var table = new EntitySlotTable(2);

        Assert.That(table.TryGetNextAllocation(out var firstIndex, out var firstGeneration), Is.True);
        Assert.That(firstIndex, Is.EqualTo(1));
        Assert.That(firstGeneration, Is.EqualTo(1));
        Assert.That(table.AllocatedSlotCount, Is.Zero);
        Assert.That(table.LiveEntityCount, Is.Zero);
        Assert.That(table.PrepareCapacity(2, 1), Is.True);
        Assert.That(table.TryAllocate(out var allocatedIndex, out var allocatedGeneration), Is.True);
        Assert.That(allocatedIndex, Is.EqualTo(firstIndex));
        Assert.That(allocatedGeneration, Is.EqualTo(firstGeneration));

        table.Release(allocatedIndex, allocatedGeneration);
        Assert.That(table.TryGetNextAllocation(out var reusedIndex, out var reusedGeneration), Is.True);
        Assert.That(reusedIndex, Is.EqualTo(allocatedIndex));
        Assert.That(reusedGeneration, Is.EqualTo(allocatedGeneration + 1));
        Assert.That(table.FreeSlotCount, Is.EqualTo(1));
        Assert.That(table.LiveEntityCount, Is.Zero);
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
