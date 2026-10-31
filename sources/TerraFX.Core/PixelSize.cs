// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX;

/// <summary>Stores an immutable two-dimensional pixel extent.</summary>
public readonly struct PixelSize : IEquatable<PixelSize>
{
    /// <summary>Initializes a nonnegative pixel extent.</summary>
    /// <param name="width">The horizontal pixel count.</param>
    /// <param name="height">The vertical pixel count.</param>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is negative.</exception>
    public PixelSize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        Width = width;
        Height = height;
    }

    /// <summary>Gets the vertical pixel count.</summary>
    public int Height { get; }

    /// <summary>Gets the horizontal pixel count.</summary>
    public int Width { get; }

    /// <summary>Negates stored-extent equality.</summary>
    public static bool operator !=(PixelSize left, PixelSize right) => !left.Equals(right);

    /// <summary>Compares stored pixel extents.</summary>
    public static bool operator ==(PixelSize left, PixelSize right) => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(PixelSize other) => Width == other.Width && Height == other.Height;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PixelSize other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Width, Height);
}
