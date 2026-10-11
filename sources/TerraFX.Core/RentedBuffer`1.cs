// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TerraFX;

internal ref struct RentedBuffer<T>
    where T : unmanaged
{
    private const int MaxInlineBytes = 1024;

    private T[]? _array;
    private InlineBytes1024 _inline;

    internal static Span<T> Create(int count, [UnscopedRef] out RentedBuffer<T> rentedBuffer)
    {
        var result = CreateUninitialized(count, out rentedBuffer);
        result.Clear();
        return result;
    }

    internal static Span<T> CreateUninitialized(int count, [UnscopedRef] out RentedBuffer<T> rentedBuffer)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (count > MaxInlineBytes / Unsafe.SizeOf<T>())
        {
            rentedBuffer._array = ArrayPool<T>.Shared.Rent(count);
            Unsafe.SkipInit(out rentedBuffer._inline); // The pooled-array path never reads or exposes inline storage.
            return rentedBuffer._array.AsSpan(0, count);
        }

        rentedBuffer._array = null;
        Unsafe.SkipInit(out rentedBuffer._inline); // CreateUninitialized returns this span for caller initialization.
        Span<byte> bytes = rentedBuffer._inline;
        return MemoryMarshal.Cast<byte, T>(bytes)[..count];
    }

    public void Dispose()
    {
        var array = _array;
        _array = null;

        if (array is not null)
        {
            ArrayPool<T>.Shared.Return(array);
        }
    }

    [InlineArray(MaxInlineBytes)]
    private struct InlineBytes1024
    {
        private byte _element0;
    }
}
