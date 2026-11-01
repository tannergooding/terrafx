// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Identifies one native surface format and color-space pair.</summary>
public readonly struct SurfaceFormat : IEquatable<SurfaceFormat>
{
    internal SurfaceFormat(TextureFormat format, SurfaceColorSpace colorSpace)
    {
        if ((format == TextureFormat.None) || !Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format));
        }

        if ((colorSpace == SurfaceColorSpace.None) || !Enum.IsDefined(colorSpace))
        {
            throw new ArgumentOutOfRangeException(nameof(colorSpace));
        }

        Format = format;
        ColorSpace = colorSpace;
    }

    /// <summary>Gets the color space.</summary>
    public SurfaceColorSpace ColorSpace { get; }

    /// <summary>Gets the texture format.</summary>
    public TextureFormat Format { get; }

    /// <inheritdoc />
    /// <inheritdoc />
    public static bool operator !=(SurfaceFormat left, SurfaceFormat right)
        => !left.Equals(right);

    /// <inheritdoc />
    public static bool operator ==(SurfaceFormat left, SurfaceFormat right)
        => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(SurfaceFormat other)
        => (Format == other.Format) && (ColorSpace == other.ColorSpace);

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => (obj is SurfaceFormat other) && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(Format, ColorSpace);
}
