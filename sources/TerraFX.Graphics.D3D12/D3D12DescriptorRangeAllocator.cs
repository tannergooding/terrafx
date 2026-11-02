// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics.D3D12;

internal sealed class D3D12DescriptorRangeAllocator
{
    private readonly bool[] _allocated;
    private int _allocatedCount;

    internal D3D12DescriptorRangeAllocator(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _allocated = new bool[capacity];
    }

    internal int AllocatedCount => _allocatedCount;

    internal int Capacity => _allocated.Length;

    internal int Allocate(int descriptorCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(descriptorCount);

        if (descriptorCount > _allocated.Length)
        {
            throw new InvalidOperationException("The descriptor range exceeds heap capacity.");
        }

        for (var start = 0; start <= (_allocated.Length - descriptorCount); start++)
        {
            var availableCount = 0;

            while ((availableCount < descriptorCount) && !_allocated[start + availableCount])
            {
                availableCount++;
            }

            if (availableCount != descriptorCount)
            {
                start = checked(start + availableCount);
                continue;
            }

            for (var index = start; index < (start + descriptorCount); index++)
            {
                _allocated[index] = true;
            }

            _allocatedCount = checked(_allocatedCount + descriptorCount);
            return start;
        }

        throw new InvalidOperationException("The D3D12 descriptor heap has no contiguous range of the requested size.");
    }

    internal void Release(int start, int descriptorCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(descriptorCount);

        if ((start > _allocated.Length) || (descriptorCount > (_allocated.Length - start)))
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        for (var index = start; index < (start + descriptorCount); index++)
        {
            if (!_allocated[index])
            {
                throw new InvalidOperationException("The D3D12 descriptor range contains an unallocated slot.");
            }
        }

        for (var index = start; index < (start + descriptorCount); index++)
        {
            _allocated[index] = false;
        }

        _allocatedCount = checked(_allocatedCount - descriptorCount);
    }
}
