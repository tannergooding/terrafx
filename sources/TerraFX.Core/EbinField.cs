// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX;

internal sealed class EbinField
{
    private readonly byte[] _payload;

    internal EbinField(uint tag, ushort kind, ushort flags, ulong count, scoped ReadOnlySpan<byte> payload)
    {
        Tag = tag;
        Kind = kind;
        Flags = flags;
        Count = count;
        _payload = payload.ToArray();
    }

    internal ulong Count { get; }

    internal ushort Flags { get; }

    internal ushort Kind { get; }

    internal ReadOnlySpan<byte> Payload => _payload;

    internal uint Tag { get; }
}
