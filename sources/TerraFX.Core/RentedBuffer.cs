// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;

namespace TerraFX;

internal static class RentedBuffer
{
    internal static Span<T> Create<T>(int count, [UnscopedRef] out RentedBuffer<T> rentedBuffer)
        where T : unmanaged
        => RentedBuffer<T>.Create(count, out rentedBuffer);

    internal static Span<T> CreateUninitialized<T>(int count, [UnscopedRef] out RentedBuffer<T> rentedBuffer)
        where T : unmanaged
        => RentedBuffer<T>.CreateUninitialized(count, out rentedBuffer);
}
