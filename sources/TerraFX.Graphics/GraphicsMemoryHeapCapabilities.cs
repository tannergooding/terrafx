// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Contains actual size and memory properties for one native graphics heap.</summary>
public sealed class GraphicsMemoryHeapCapabilities
{
    internal GraphicsMemoryHeapCapabilities(
        int index,
        GraphicsMemoryHeapProperties properties,
        long sizeBytes,
        long? budgetBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeBytes);

        const GraphicsMemoryHeapProperties knownProperties =
            GraphicsMemoryHeapProperties.DeviceLocal
            | GraphicsMemoryHeapProperties.HostVisible
            | GraphicsMemoryHeapProperties.HostCoherent
            | GraphicsMemoryHeapProperties.HostCached;

        if ((properties == GraphicsMemoryHeapProperties.None) || ((properties & ~knownProperties) != 0))
        {
            throw new ArgumentOutOfRangeException(nameof(properties));
        }

        if (((properties & (GraphicsMemoryHeapProperties.HostCoherent | GraphicsMemoryHeapProperties.HostCached)) != 0)
            && ((properties & GraphicsMemoryHeapProperties.HostVisible) == 0))
        {
            throw new ArgumentException("Host-coherent and host-cached heaps must be host visible.", nameof(properties));
        }

        if (budgetBytes is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(budgetBytes));
        }

        Index = index;
        Properties = properties;
        SizeBytes = sizeBytes;
        BudgetBytes = budgetBytes;
    }

    /// <summary>Gets the optional current budget estimate in bytes.</summary>
    public long? BudgetBytes { get; }

    /// <summary>Gets the native heap index.</summary>
    public int Index { get; }

    /// <summary>Gets the heap's memory properties.</summary>
    public GraphicsMemoryHeapProperties Properties { get; }

    /// <summary>Gets the native heap size in bytes.</summary>
    public long SizeBytes { get; }
}
