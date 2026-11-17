// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;

namespace TerraFX.Animation;

internal readonly record struct AnimationTickRemainder192(ulong Low, ulong Middle, ulong High)
{
    internal const int ByteLength = 24;

    internal static AnimationTickRemainder192 FromLittleEndian(ReadOnlySpan<byte> source)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(source.Length, ByteLength);
        return new AnimationTickRemainder192(
            BinaryPrimitives.ReadUInt64LittleEndian(source),
            BinaryPrimitives.ReadUInt64LittleEndian(source[sizeof(ulong)..]),
            BinaryPrimitives.ReadUInt64LittleEndian(source[(2 * sizeof(ulong))..]));
    }

    internal void WriteLittleEndian(Span<byte> destination)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, ByteLength);
        BinaryPrimitives.WriteUInt64LittleEndian(destination, Low);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[sizeof(ulong)..], Middle);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[(2 * sizeof(ulong))..], High);
    }
}
