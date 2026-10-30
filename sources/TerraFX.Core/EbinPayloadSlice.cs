// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX;

/// <summary>Identifies a borrowed byte range in caller-owned payload storage.</summary>
public readonly record struct EbinPayloadSlice
{
    /// <summary>Initializes a payload range.</summary>
    /// <param name="offset">The byte offset into the associated storage.</param>
    /// <param name="byteLength">The payload byte length.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="offset"/> or <paramref name="byteLength"/> is negative.</exception>
    public EbinPayloadSlice(int offset, int byteLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(byteLength);

        ByteLength = byteLength;
        Offset = offset;
    }

    /// <summary>Gets the payload byte length.</summary>
    public int ByteLength { get; }

    /// <summary>Gets the byte offset into the associated storage.</summary>
    public int Offset { get; }
}
