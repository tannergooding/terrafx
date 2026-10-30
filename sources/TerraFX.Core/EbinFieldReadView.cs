// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX;

/// <summary>Provides borrowed access to one structurally validated EBIN field.</summary>
public readonly ref struct EbinFieldReadView
{
    private readonly ulong _count;
    private readonly ushort _flags;
    private readonly ushort _kind;
    private readonly ReadOnlySpan<byte> _payload;
    private readonly uint _tag;

    internal EbinFieldReadView(uint tag, ushort kind, ushort flags, ulong count, ReadOnlySpan<byte> payload)
    {
        _count = count;
        _flags = flags;
        _kind = kind;
        _payload = payload;
        _tag = tag;
    }

    /// <summary>Gets the element count declared by the field.</summary>
    public ulong Count => _count;

    /// <summary>Gets whether the field is required.</summary>
    public bool IsRequired => _flags == 1;

    /// <summary>Gets the field wire kind.</summary>
    public ushort Kind => _kind;

    /// <summary>Gets the borrowed field payload.</summary>
    public ReadOnlySpan<byte> Payload => _payload;

    /// <summary>Gets the field tag.</summary>
    public uint Tag => _tag;
}
