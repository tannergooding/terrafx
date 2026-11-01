// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Identifies one complete texture mip, array layer, and aspect.</summary>
public readonly struct TextureSubresource : IEquatable<TextureSubresource>
{
    /// <summary>Initializes a texture subresource identity.</summary>
    /// <param name="mipLevel">The nonnegative mip level.</param>
    /// <param name="arrayLayer">The nonnegative array layer; a 3D texture uses zero.</param>
    /// <param name="aspect">The selected color or depth aspect.</param>
    /// <exception cref="ArgumentOutOfRangeException">An index is negative or the aspect is not defined.</exception>
    public TextureSubresource(int mipLevel, int arrayLayer, TextureAspect aspect)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(mipLevel);
        ArgumentOutOfRangeException.ThrowIfNegative(arrayLayer);

        if (!Enum.IsDefined(aspect))
        {
            throw new ArgumentOutOfRangeException(nameof(aspect));
        }

        ArrayLayer = arrayLayer;
        Aspect = aspect;
        MipLevel = mipLevel;
    }

    /// <summary>Gets the nonnegative array layer.</summary>
    public int ArrayLayer { get; }

    /// <summary>Gets the selected color or depth aspect.</summary>
    public TextureAspect Aspect { get; }

    /// <summary>Gets the nonnegative mip level.</summary>
    public int MipLevel { get; }

    /// <summary>Tests whether two subresource values differ.</summary>
    public static bool operator !=(TextureSubresource left, TextureSubresource right)
        => !left.Equals(right);

    /// <summary>Tests whether two subresource values are equal.</summary>
    public static bool operator ==(TextureSubresource left, TextureSubresource right)
        => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(TextureSubresource other)
        => (MipLevel == other.MipLevel) && (ArrayLayer == other.ArrayLayer) && (Aspect == other.Aspect);

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => (obj is TextureSubresource other) && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(MipLevel, ArrayLayer, Aspect);
}
