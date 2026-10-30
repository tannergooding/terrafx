// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX;

/// <summary>Describes one borrowed EBIN field for structural writing.</summary>
public readonly record struct EbinFieldSource
{
    /// <summary>Initializes a borrowed field description.</summary>
    /// <param name="tag">The field tag.</param>
    /// <param name="kind">The wire kind.</param>
    /// <param name="flags">The field flags.</param>
    /// <param name="count">The field element count.</param>
    /// <param name="payload">The borrowed payload range.</param>
    public EbinFieldSource(uint tag, ushort kind, ushort flags, ulong count, EbinPayloadSlice payload)
    {
        Count = count;
        Flags = flags;
        Kind = kind;
        Payload = payload;
        Tag = tag;
    }

    /// <summary>Gets the field element count.</summary>
    public ulong Count { get; }

    /// <summary>Gets the field flags.</summary>
    public ushort Flags { get; }

    /// <summary>Gets the field wire kind.</summary>
    public ushort Kind { get; }

    /// <summary>Gets the borrowed payload range.</summary>
    public EbinPayloadSlice Payload { get; }

    /// <summary>Gets the field tag.</summary>
    public uint Tag { get; }
}
