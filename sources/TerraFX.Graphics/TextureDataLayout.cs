// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Describes the positive row and slice pitches of a complete texture subresource source.</summary>
public readonly struct TextureDataLayout : IEquatable<TextureDataLayout>
{
    /// <summary>Initializes source row and slice pitches.</summary>
    /// <param name="rowPitch">The positive byte pitch between source rows.</param>
    /// <param name="slicePitch">The positive byte pitch between source depth slices.</param>
    /// <exception cref="ArgumentOutOfRangeException">A pitch is not positive.</exception>
    public TextureDataLayout(nint rowPitch, nint slicePitch)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rowPitch);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slicePitch);

        RowPitch = rowPitch;
        SlicePitch = slicePitch;
    }

    /// <summary>Gets the positive byte pitch between source rows.</summary>
    public nint RowPitch { get; }

    /// <summary>Gets the positive byte pitch between source depth slices.</summary>
    public nint SlicePitch { get; }

    /// <summary>Tests whether two data layouts differ.</summary>
    public static bool operator !=(TextureDataLayout left, TextureDataLayout right)
        => !left.Equals(right);

    /// <summary>Tests whether two data layouts are equal.</summary>
    public static bool operator ==(TextureDataLayout left, TextureDataLayout right)
        => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(TextureDataLayout other)
        => (RowPitch == other.RowPitch) && (SlicePitch == other.SlicePitch);

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => (obj is TextureDataLayout other) && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(RowPitch, SlicePitch);
}
