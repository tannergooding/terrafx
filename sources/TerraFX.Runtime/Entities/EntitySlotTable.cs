// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;

namespace TerraFX.Entities;

internal sealed class EntitySlotTable
{
    internal const int MaximumHierarchyDepth = 256;

    internal const int MaximumSlots = 2_097_152;

    private int _allocatedSlotCount;
    private int _freeSlotCount;
    private int[] _freeSlots = [];
    private readonly uint _initialGeneration;
    private int _liveEntityCount;
    private readonly int _maximumSlots;
    private int _retiredSlotCount;
    private Slot[] _slots = new Slot[1];

    internal EntitySlotTable(int maximumSlots)
        : this(maximumSlots, 1)
    {
    }

    private EntitySlotTable(int maximumSlots, uint initialGeneration)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumSlots);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumSlots, MaximumSlots);
        ArgumentOutOfRangeException.ThrowIfZero(initialGeneration);

        _maximumSlots = maximumSlots;
        _initialGeneration = initialGeneration;
    }

    internal int AllocatedSlotCount => _allocatedSlotCount;

    internal int FreeSlotCount => _freeSlotCount;

    internal int LiveEntityCount => _liveEntityCount;

    internal long OwnedByteReservation
        => checked(ArrayBytes(_slots.Length, Unsafe.SizeOf<Slot>())
        + ((_freeSlots.Length == 0) ? 0 : ArrayBytes(_freeSlots.Length, sizeof(int))));

    internal int RetiredSlotCount => _retiredSlotCount;

    internal static EntitySlotTable CreateForTesting(int maximumSlots, uint initialGeneration)
        => new EntitySlotTable(maximumSlots, initialGeneration);

    private static long ArrayBytes(long count, int elementSize)
        => checked(64 + ((checked(count * elementSize) + 7) & ~7L));

    internal void ClearLocation(uint index, uint generation, int archetypeIndex, long chunkOrdinal, int row)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(archetypeIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkOrdinal);
        ArgumentOutOfRangeException.ThrowIfNegative(row);

        if (!IsLive(index, generation))
        {
            throw new InvalidOperationException("The entity slot is not live at the supplied generation.");
        }

        ref var slot = ref _slots[(int)index];

        if ((slot._archetypeIndex != archetypeIndex) || (slot._chunkOrdinal != chunkOrdinal) || (slot._row != row))
        {
            throw new InvalidOperationException("The entity slot does not have the expected storage location.");
        }

        slot._archetypeIndex = -1;
        slot._chunkOrdinal = -1;
        slot._row = -1;
    }

    // The free-list requirement is its peak depth during the next commit, not its final depth.
    internal long GetCapacityPreparationBytes(int requiredAllocatedSlots, int requiredFreeSlotCount)
    {
        ValidateRequiredCapacity(requiredAllocatedSlots, requiredFreeSlotCount);

        var requiredSlotArrayLength = checked(requiredAllocatedSlots + 1);
        var bytes = 0L;

        if (requiredSlotArrayLength > _slots.Length)
        {
            bytes = checked(bytes + ArrayBytes(requiredSlotArrayLength, Unsafe.SizeOf<Slot>()));
        }

        if (requiredFreeSlotCount > _freeSlots.Length)
        {
            bytes = checked(bytes + ArrayBytes(requiredFreeSlotCount, sizeof(int)));
        }

        return bytes;
    }

    internal int GetHierarchyDepth(uint index, uint generation)
    {
        if (!IsLive(index, generation))
        {
            throw new InvalidOperationException("The Entity slot is not live at the supplied generation.");
        }

        return _slots[(int)index]._hierarchyDepth;
    }

    internal bool IsLive(uint index, uint generation)
    {
        if ((index == 0) || (index > _allocatedSlotCount))
        {
            return false;
        }

        var slot = _slots[(int)index];
        return (slot._state == SlotState.Live) && (slot._generation == generation);
    }

    // The free-list requirement is its peak depth during the next commit, not its final depth.
    internal bool PrepareCapacity(int requiredAllocatedSlots, int requiredFreeSlotCount)
    {
        if (!IsCapacityValid(requiredAllocatedSlots, requiredFreeSlotCount))
        {
            return false;
        }

        var requiredSlotArrayLength = checked(requiredAllocatedSlots + 1);
        var slots = _slots;
        var freeSlots = _freeSlots;

        if (requiredSlotArrayLength > slots.Length)
        {
            slots = new Slot[requiredSlotArrayLength];
            Array.Copy(_slots, slots, _slots.Length);
        }

        if (requiredFreeSlotCount > freeSlots.Length)
        {
            freeSlots = new int[requiredFreeSlotCount];
            Array.Copy(_freeSlots, freeSlots, _freeSlotCount);
        }

        _slots = slots;
        _freeSlots = freeSlots;
        return true;
    }

    internal void Release(uint index, uint generation)
    {
        if (!IsLive(index, generation))
        {
            throw new InvalidOperationException("The entity slot is not live at the supplied generation.");
        }

        if ((generation != uint.MaxValue) && (_freeSlotCount == _freeSlots.Length))
        {
            throw new InvalidOperationException("Entity slot retirement requires preflighted free-list capacity.");
        }

        ref var slot = ref _slots[(int)index];

        if (slot._hierarchyFirstChildIndex != 0)
        {
            throw new InvalidOperationException("An Entity with live hierarchy children cannot be released.");
        }

        DetachFromParent(index, ref slot);
        slot._archetypeIndex = -1;
        slot._chunkOrdinal = -1;
        slot._row = -1;
        slot._hierarchyDepth = 0;
        _liveEntityCount--;

        if (generation == uint.MaxValue)
        {
            slot._state = SlotState.Retired;
            _retiredSlotCount++;
        }
        else
        {
            slot._generation = generation + 1;
            slot._state = SlotState.Free;
            _freeSlots[_freeSlotCount++] = (int)index;
        }
    }

    internal void ReleaseOwnedStorage()
    {
        _slots = [];
        _freeSlots = [];
        _allocatedSlotCount = 0;
        _freeSlotCount = 0;
        _liveEntityCount = 0;
        _retiredSlotCount = 0;
    }

    internal void RollbackAllocation(uint index, uint generation, bool reusedFreeSlot)
    {
        if (!IsLive(index, generation))
        {
            throw new InvalidOperationException("The entity slot allocation cannot be rolled back at the supplied generation.");
        }

        ref var slot = ref _slots[(int)index];

        if ((slot._archetypeIndex >= 0) || (slot._chunkOrdinal >= 0) || (slot._row >= 0))
        {
            throw new InvalidOperationException("A located entity slot allocation cannot be rolled back.");
        }

        if (reusedFreeSlot)
        {
            if (_freeSlotCount == _freeSlots.Length)
            {
                throw new InvalidOperationException("Entity slot rollback requires preflighted free-list capacity.");
            }

            slot._state = SlotState.Free;
            _freeSlots[_freeSlotCount++] = checked((int)index);
        }
        else
        {
            if ((index != _allocatedSlotCount) || (_allocatedSlotCount == 0))
            {
                throw new InvalidOperationException("Only the newest Entity slot allocation can be rolled back.");
            }

            _allocatedSlotCount--;
            slot = default;
        }

        _liveEntityCount--;
    }

    internal void SetLocation(uint index, uint generation, int archetypeIndex, long chunkOrdinal, int row)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(archetypeIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkOrdinal);
        ArgumentOutOfRangeException.ThrowIfNegative(row);

        if (!IsLive(index, generation))
        {
            throw new InvalidOperationException("The entity slot is not live at the supplied generation.");
        }

        ref var slot = ref _slots[(int)index];
        slot._archetypeIndex = archetypeIndex;
        slot._chunkOrdinal = chunkOrdinal;
        slot._row = row;
    }

    internal void SetParent(
        uint childIndex,
        uint childGeneration,
        uint parentIndex,
        uint parentGeneration)
    {
        if (!IsLive(childIndex, childGeneration))
        {
            throw new InvalidOperationException("The child Entity slot is not live at the supplied generation.");
        }

        if (parentIndex == 0)
        {
            ArgumentOutOfRangeException.ThrowIfNotEqual(parentGeneration, 0U);
        }
        else if (!IsLive(parentIndex, parentGeneration))
        {
            throw new InvalidOperationException("The parent Entity slot is not live at the supplied generation.");
        }

        if (childIndex == parentIndex)
        {
            throw new InvalidOperationException("An Entity cannot be its own hierarchy parent.");
        }

        ref var child = ref _slots[(int)childIndex];

        if ((child._hierarchyParentIndex == parentIndex)
            && (child._hierarchyParentGeneration == parentGeneration))
        {
            return;
        }

        var newDepth = (parentIndex == 0)
            ? 0
            : checked(_slots[(int)parentIndex]._hierarchyDepth + 1);
        var subtreeHeight = GetSubtreeHeight(childIndex);

        if (newDepth + subtreeHeight > MaximumHierarchyDepth)
        {
            throw new InvalidOperationException("The hierarchy change exceeds the World depth bound.");
        }

        var ancestorIndex = parentIndex;
        var ancestorGeneration = parentGeneration;
        var ancestorDepth = 0;

        while (ancestorIndex != 0)
        {
            if (ancestorIndex == childIndex)
            {
                throw new InvalidOperationException("The hierarchy change would create a cycle.");
            }

            ref var ancestor = ref _slots[(int)ancestorIndex];

            if (!IsLive(ancestorIndex, ancestorGeneration))
            {
                throw new InvalidOperationException("A hierarchy parent link references a non-live Entity slot.");
            }

            ancestorIndex = ancestor._hierarchyParentIndex;
            ancestorGeneration = ancestor._hierarchyParentGeneration;
            ancestorDepth++;

            if (ancestorDepth > MaximumHierarchyDepth)
            {
                throw new InvalidOperationException("The existing hierarchy exceeds the World depth bound.");
            }
        }

        DetachFromParent(childIndex, ref child);
        child._hierarchyParentIndex = parentIndex;
        child._hierarchyParentGeneration = parentGeneration;
        child._hierarchyPreviousSiblingIndex = 0;
        child._hierarchyNextSiblingIndex = 0;

        if (parentIndex != 0)
        {
            ref var parent = ref _slots[(int)parentIndex];
            var previousLastChildIndex = parent._hierarchyLastChildIndex;
            child._hierarchyPreviousSiblingIndex = previousLastChildIndex;

            if (previousLastChildIndex == 0)
            {
                parent._hierarchyFirstChildIndex = childIndex;
            }
            else
            {
                _slots[(int)previousLastChildIndex]._hierarchyNextSiblingIndex = childIndex;
            }

            parent._hierarchyLastChildIndex = childIndex;
        }

        SetSubtreeDepth(childIndex, newDepth);
    }

    internal bool TryAllocate(out uint index, out uint generation)
    {
        if (_freeSlotCount != 0)
        {
            index = (uint)_freeSlots[_freeSlotCount - 1];
            ref var slot = ref _slots[(int)index];

            if ((slot._state != SlotState.Free) || (slot._generation == 0))
            {
                throw new InvalidOperationException("The entity slot free list is inconsistent.");
            }

            _freeSlotCount--;
            slot._state = SlotState.Live;
            slot._archetypeIndex = -1;
            slot._chunkOrdinal = -1;
            slot._row = -1;
            generation = slot._generation;
            _liveEntityCount++;
            return true;
        }

        if (_allocatedSlotCount == _maximumSlots)
        {
            index = 0;
            generation = 0;
            return false;
        }

        if (_slots.Length == (_allocatedSlotCount + 1))
        {
            throw new InvalidOperationException("Entity slot allocation requires preflighted slot capacity.");
        }

        index = (uint)++_allocatedSlotCount;
        ref var newSlot = ref _slots[(int)index];
        newSlot._generation = _initialGeneration;
        newSlot._archetypeIndex = -1;
        newSlot._chunkOrdinal = -1;
        newSlot._row = -1;
        newSlot._state = SlotState.Live;
        generation = newSlot._generation;
        _liveEntityCount++;
        return true;
    }

    internal bool TryGetAllocationPreview(int allocationOrdinal, out uint index, out uint generation)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(allocationOrdinal);

        if (allocationOrdinal < _freeSlotCount)
        {
            index = (uint)_freeSlots[_freeSlotCount - allocationOrdinal - 1];
            var slot = _slots[(int)index];

            if ((slot._state != SlotState.Free) || (slot._generation == 0))
            {
                throw new InvalidOperationException("The entity slot free list is inconsistent.");
            }

            generation = slot._generation;
            return true;
        }

        var newSlotOrdinal = checked(allocationOrdinal - _freeSlotCount);
        var newSlotIndex = checked((long)_allocatedSlotCount + newSlotOrdinal + 1);

        if (newSlotIndex > _maximumSlots)
        {
            index = 0;
            generation = 0;
            return false;
        }

        index = checked((uint)newSlotIndex);
        generation = _initialGeneration;
        return true;
    }

    internal bool TryGetFirstChild(uint index, uint generation, out uint childIndex, out uint childGeneration)
    {
        if (!IsLive(index, generation))
        {
            childIndex = 0;
            childGeneration = 0;
            return false;
        }

        childIndex = _slots[(int)index]._hierarchyFirstChildIndex;

        if (childIndex == 0)
        {
            childGeneration = 0;
            return false;
        }

        if (childIndex > _allocatedSlotCount)
        {
            throw new InvalidOperationException("The hierarchy child list contains an invalid slot index.");
        }

        var child = _slots[(int)childIndex];

        if (!IsLive(childIndex, child._generation)
            || (child._hierarchyParentIndex != index)
            || (child._hierarchyParentGeneration != generation))
        {
            throw new InvalidOperationException("The hierarchy child list is inconsistent.");
        }

        childGeneration = child._generation;
        return true;
    }

    internal bool TryGetLive(uint index, out uint generation)
    {
        if ((index == 0) || (index > _allocatedSlotCount))
        {
            generation = 0;
            return false;
        }

        var slot = _slots[(int)index];
        generation = slot._generation;
        return slot._state == SlotState.Live;
    }

    internal bool TryGetLocation(uint index, uint generation, out int archetypeIndex, out long chunkOrdinal, out int row)
    {
        if (IsLive(index, generation))
        {
            var slot = _slots[(int)index];

            if ((slot._archetypeIndex >= 0) && (slot._chunkOrdinal >= 0) && (slot._row >= 0))
            {
                archetypeIndex = slot._archetypeIndex;
                chunkOrdinal = slot._chunkOrdinal;
                row = slot._row;
                return true;
            }
        }

        archetypeIndex = -1;
        chunkOrdinal = -1;
        row = -1;
        return false;
    }

    internal bool TryGetNextAllocation(out uint index, out uint generation)
    {
        if (_freeSlotCount != 0)
        {
            index = (uint)_freeSlots[_freeSlotCount - 1];
            var slot = _slots[(int)index];

            if ((slot._state != SlotState.Free) || (slot._generation == 0))
            {
                throw new InvalidOperationException("The entity slot free list is inconsistent.");
            }

            generation = slot._generation;
            return true;
        }

        if (_allocatedSlotCount == _maximumSlots)
        {
            index = 0;
            generation = 0;
            return false;
        }

        index = checked((uint)(_allocatedSlotCount + 1));
        generation = _initialGeneration;
        return true;
    }

    internal bool TryGetNextSibling(uint index, uint generation, out uint siblingIndex, out uint siblingGeneration)
    {
        if (!IsLive(index, generation))
        {
            siblingIndex = 0;
            siblingGeneration = 0;
            return false;
        }

        siblingIndex = _slots[(int)index]._hierarchyNextSiblingIndex;

        if (siblingIndex == 0)
        {
            siblingGeneration = 0;
            return false;
        }

        if (siblingIndex > _allocatedSlotCount)
        {
            throw new InvalidOperationException("The hierarchy sibling list contains an invalid slot index.");
        }

        var sibling = _slots[(int)siblingIndex];

        if (!IsLive(siblingIndex, sibling._generation)
            || (sibling._hierarchyParentIndex != _slots[(int)index]._hierarchyParentIndex)
            || (sibling._hierarchyParentGeneration != _slots[(int)index]._hierarchyParentGeneration)
            || (sibling._hierarchyPreviousSiblingIndex != index))
        {
            throw new InvalidOperationException("The hierarchy sibling list is inconsistent.");
        }

        siblingGeneration = sibling._generation;
        return true;
    }

    internal bool TryGetParent(uint index, uint generation, out uint parentIndex, out uint parentGeneration)
    {
        if (!IsLive(index, generation))
        {
            parentIndex = 0;
            parentGeneration = 0;
            return false;
        }

        ref var slot = ref _slots[(int)index];
        parentIndex = slot._hierarchyParentIndex;
        parentGeneration = slot._hierarchyParentGeneration;

        if (parentIndex == 0)
        {
            return false;
        }

        if (!IsLive(parentIndex, parentGeneration))
        {
            throw new InvalidOperationException("A live Entity hierarchy link references a non-live parent slot.");
        }

        return true;
    }

    private void DetachFromParent(uint childIndex, ref Slot child)
    {
        var parentIndex = child._hierarchyParentIndex;

        if (parentIndex == 0)
        {
            child._hierarchyParentGeneration = 0;
            child._hierarchyPreviousSiblingIndex = 0;
            child._hierarchyNextSiblingIndex = 0;
            return;
        }

        var parentGeneration = child._hierarchyParentGeneration;

        if (!IsLive(parentIndex, parentGeneration))
        {
            throw new InvalidOperationException("The hierarchy parent link is stale.");
        }

        ref var parent = ref _slots[(int)parentIndex];
        var previousSiblingIndex = child._hierarchyPreviousSiblingIndex;
        var nextSiblingIndex = child._hierarchyNextSiblingIndex;

        if (previousSiblingIndex == 0)
        {
            parent._hierarchyFirstChildIndex = nextSiblingIndex;
        }
        else
        {
            _slots[(int)previousSiblingIndex]._hierarchyNextSiblingIndex = nextSiblingIndex;
        }

        if (nextSiblingIndex == 0)
        {
            parent._hierarchyLastChildIndex = previousSiblingIndex;
        }
        else
        {
            _slots[(int)nextSiblingIndex]._hierarchyPreviousSiblingIndex = previousSiblingIndex;
        }

        child._hierarchyParentIndex = 0;
        child._hierarchyParentGeneration = 0;
        child._hierarchyPreviousSiblingIndex = 0;
        child._hierarchyNextSiblingIndex = 0;
    }

    private int GetSubtreeHeight(uint index)
    {
        var height = 0;
        var childIndex = _slots[(int)index]._hierarchyFirstChildIndex;

        while (childIndex != 0)
        {
            if (childIndex > _allocatedSlotCount)
            {
                throw new InvalidOperationException("The hierarchy child list contains an invalid slot index.");
            }

            height = Math.Max(height, checked(GetSubtreeHeight(childIndex) + 1));
            childIndex = _slots[(int)childIndex]._hierarchyNextSiblingIndex;
        }

        return height;
    }

    private bool IsCapacityValid(int requiredAllocatedSlots, int requiredFreeSlotCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(requiredAllocatedSlots);
        ArgumentOutOfRangeException.ThrowIfNegative(requiredFreeSlotCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(requiredAllocatedSlots, _allocatedSlotCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(requiredFreeSlotCount, _freeSlotCount);
        return (requiredAllocatedSlots <= _maximumSlots) && (requiredFreeSlotCount <= _maximumSlots);
    }

    private void SetSubtreeDepth(uint index, int depth)
    {
        ref var slot = ref _slots[(int)index];
        slot._hierarchyDepth = depth;
        var childIndex = slot._hierarchyFirstChildIndex;

        while (childIndex != 0)
        {
            var nextSiblingIndex = _slots[(int)childIndex]._hierarchyNextSiblingIndex;
            SetSubtreeDepth(childIndex, checked(depth + 1));
            childIndex = nextSiblingIndex;
        }
    }

    private void ValidateRequiredCapacity(int requiredAllocatedSlots, int requiredFreeSlotCount)
    {
        if (requiredAllocatedSlots > _maximumSlots)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredAllocatedSlots), "The required entity slot capacity exceeds the configured World limit.");
        }

        if (requiredFreeSlotCount > _maximumSlots)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredFreeSlotCount), "The required entity free-list capacity exceeds the configured World limit.");
        }

        if (!IsCapacityValid(requiredAllocatedSlots, requiredFreeSlotCount))
        {
            throw new InvalidOperationException("The requested entity slot capacity is invalid.");
        }
    }

    private struct Slot
    {
        internal long _chunkOrdinal;
        internal int _archetypeIndex;
        internal uint _generation;
        internal int _row;
        internal SlotState _state;
        internal uint _hierarchyParentIndex;
        internal uint _hierarchyParentGeneration;
        internal uint _hierarchyFirstChildIndex;
        internal uint _hierarchyLastChildIndex;
        internal uint _hierarchyPreviousSiblingIndex;
        internal uint _hierarchyNextSiblingIndex;
        internal int _hierarchyDepth;
    }

    private enum SlotState : byte
    {
        Unallocated,
        Live,
        Free,
        Retired,
    }
}
