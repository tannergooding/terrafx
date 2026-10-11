// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Buffers;

/// <summary>Defines the maximum rounded native allocation size and element alignment for a buffer.</summary>
public sealed class BufferAllocationOptions
{
    /// <summary>Initializes allocation limits for unmanaged buffers.</summary>
    /// <param name="maximumAllocationBytes">The nonnegative maximum rounded allocation extent in bytes.</param>
    /// <param name="alignment">The positive power-of-two alignment in bytes.</param>
    /// <exception cref="ArgumentOutOfRangeException">A limit is negative, zero where disallowed, or not a power of two.</exception>
    public BufferAllocationOptions(nint maximumAllocationBytes, nint alignment)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumAllocationBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(alignment);

        if ((alignment & (alignment - 1)) != 0)
        {
            ThrowHelper.ThrowArgumentOutOfRangeException(nameof(alignment));
        }

        MaximumAllocationBytes = maximumAllocationBytes;
        Alignment = alignment;
    }

    /// <summary>Gets the positive power-of-two allocation alignment in bytes.</summary>
    public nint Alignment { get; }

    /// <summary>Gets the nonnegative maximum rounded native allocation extent in bytes.</summary>
    public nint MaximumAllocationBytes { get; }
}
