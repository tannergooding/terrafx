// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

internal readonly struct GraphicsBufferAllocationInfo
{
    internal GraphicsBufferAllocationInfo(long sizeBytes, long alignment, int heapIndex, long? budgetBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(alignment);
        ArgumentOutOfRangeException.ThrowIfNegative(heapIndex);

        if (budgetBytes is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(budgetBytes));
        }

        Alignment = alignment;
        BudgetBytes = budgetBytes;
        HeapIndex = heapIndex;
        SizeBytes = sizeBytes;
    }

    internal long Alignment { get; }

    internal long? BudgetBytes { get; }

    internal int HeapIndex { get; }

    internal long SizeBytes { get; }
}
