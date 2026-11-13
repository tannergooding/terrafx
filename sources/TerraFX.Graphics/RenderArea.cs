// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Defines a top-left-origin half-open pixel rectangle for one raster pass.</summary>
public readonly struct RenderArea : IEquatable<RenderArea>
{
    /// <summary>Initializes a nonempty render area.</summary>
    /// <param name="x">The nonnegative pixel X coordinate.</param>
    /// <param name="y">The nonnegative pixel Y coordinate.</param>
    /// <param name="width">The positive pixel width.</param>
    /// <param name="height">The positive pixel height.</param>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is negative, an extent is not positive, or an end coordinate overflows.</exception>
    public RenderArea(int x, int y, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, int.MaxValue - x);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, int.MaxValue - y);

        Height = height;
        Width = width;
        X = x;
        Y = y;
    }

    /// <summary>Gets the positive pixel height.</summary>
    public int Height { get; }

    /// <summary>Gets the positive pixel width.</summary>
    public int Width { get; }

    /// <summary>Gets the nonnegative pixel X coordinate.</summary>
    public int X { get; }

    /// <summary>Gets the nonnegative pixel Y coordinate.</summary>
    public int Y { get; }

    internal bool IsValid
        => (X >= 0)
            && (Y >= 0)
            && (Width > 0)
            && (Height > 0)
            && (Width <= (int.MaxValue - X))
            && (Height <= (int.MaxValue - Y));

    /// <summary>Tests whether two render areas differ.</summary>
    public static bool operator !=(RenderArea left, RenderArea right)
        => !left.Equals(right);

    /// <summary>Tests whether two render areas are equal.</summary>
    public static bool operator ==(RenderArea left, RenderArea right)
        => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(RenderArea other)
        => (X == other.X)
            && (Y == other.Y)
            && (Width == other.Width)
            && (Height == other.Height);

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => (obj is RenderArea other) && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(X, Y, Width, Height);
}
