// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Defines a source rectangle that completely fills a 2D destination subresource.</summary>
public readonly struct TextureCopyRegion : IEquatable<TextureCopyRegion>
{
    /// <summary>Initializes a texture copy region.</summary>
    /// <param name="sourceX">The nonnegative source texel X offset.</param>
    /// <param name="sourceY">The nonnegative source texel Y offset.</param>
    /// <param name="width">The positive copied width in texels.</param>
    /// <param name="height">The positive copied height in texels.</param>
    /// <exception cref="ArgumentOutOfRangeException">An offset is negative or an extent is not positive.</exception>
    public TextureCopyRegion(int sourceX, int sourceY, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sourceX);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceY);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        Height = height;
        SourceX = sourceX;
        SourceY = sourceY;
        Width = width;
    }

    /// <summary>Gets the positive copied height in texels.</summary>
    public int Height { get; }

    /// <summary>Gets the nonnegative source texel X offset.</summary>
    public int SourceX { get; }

    /// <summary>Gets the nonnegative source texel Y offset.</summary>
    public int SourceY { get; }

    /// <summary>Gets the positive copied width in texels.</summary>
    public int Width { get; }

    internal bool IsValid
        => (SourceX >= 0)
            && (SourceY >= 0)
            && (Width > 0)
            && (Height > 0);

    /// <summary>Tests whether two texture-copy regions differ.</summary>
    public static bool operator !=(TextureCopyRegion left, TextureCopyRegion right)
        => !left.Equals(right);

    /// <summary>Tests whether two texture-copy regions are equal.</summary>
    public static bool operator ==(TextureCopyRegion left, TextureCopyRegion right)
        => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(TextureCopyRegion other)
        => (SourceX == other.SourceX)
            && (SourceY == other.SourceY)
            && (Width == other.Width)
            && (Height == other.Height);

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => (obj is TextureCopyRegion other) && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(SourceX, SourceY, Width, Height);

    internal bool IsWithin(int sourceWidth, int sourceHeight, int destinationWidth, int destinationHeight)
        => IsValid
            && (SourceX <= sourceWidth)
            && (SourceY <= sourceHeight)
            && (Width <= (sourceWidth - SourceX))
            && (Height <= (sourceHeight - SourceY))
            && (Width == destinationWidth)
            && (Height == destinationHeight);
}
