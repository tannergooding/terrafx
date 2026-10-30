// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;

namespace TerraFX.Entities;

internal sealed class EntitySlotTable
{
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
        slot._archetypeIndex = -1;
        slot._chunkOrdinal = -1;
        slot._row = -1;
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

    private bool IsCapacityValid(int requiredAllocatedSlots, int requiredFreeSlotCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(requiredAllocatedSlots);
        ArgumentOutOfRangeException.ThrowIfNegative(requiredFreeSlotCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(requiredAllocatedSlots, _allocatedSlotCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(requiredFreeSlotCount, _freeSlotCount);
        return (requiredAllocatedSlots <= _maximumSlots) && (requiredFreeSlotCount <= _maximumSlots);
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
    }

    private enum SlotState : byte
    {
        Unallocated,
        Live,
        Free,
        Retired,
    }
}
