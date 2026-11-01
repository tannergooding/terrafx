// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Numerics;

namespace TerraFX.Graphics;

/// <summary>Describes the immutable extent, format, and allowed uses of one GPU texture.</summary>
public sealed class TextureDescription
{
    private const TextureUsage KnownUsages =
        TextureUsage.Sampled
        | TextureUsage.Storage
        | TextureUsage.ColorAttachment
        | TextureUsage.DepthAttachment
        | TextureUsage.CopySource
        | TextureUsage.CopyDestination;

    /// <summary>Initializes a validated single-sample GPU texture description.</summary>
    /// <param name="dimension">The 2D or 3D texture dimension.</param>
    /// <param name="width">The positive width in texels.</param>
    /// <param name="height">The positive height in texels.</param>
    /// <param name="depth">The positive depth in texels, or one for a 2D texture.</param>
    /// <param name="arrayLayers">The positive array-layer count, or one for a 3D texture.</param>
    /// <param name="mipLevels">The positive mip count within the complete extent's chain.</param>
    /// <param name="format">The non-sentinel texture format.</param>
    /// <param name="usage">The nonempty set of requested texture usages.</param>
    /// <param name="debugName">The optional diagnostic name; an empty string is permitted.</param>
    /// <exception cref="ArgumentException">The dimensions or format-specific usages are inconsistent.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A dimension, format, usage, or mip count is invalid.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="debugName"/> is <see langword="null"/>.</exception>
    /// <remarks>Device-specific format and usage support is checked when the description is admitted to a device.</remarks>
    public TextureDescription(
        TextureDimension dimension,
        int width,
        int height,
        int depth,
        int arrayLayers,
        int mipLevels,
        TextureFormat format,
        TextureUsage usage,
        string debugName)
    {
        if ((dimension == TextureDimension.None) || !Enum.IsDefined(dimension))
        {
            throw new ArgumentOutOfRangeException(nameof(dimension));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(arrayLayers);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mipLevels);

        if (dimension == TextureDimension.Dimension2D)
        {
            if (depth != 1)
            {
                throw new ArgumentOutOfRangeException(nameof(depth), "A 2D texture has depth one.");
            }
        }
        else if (arrayLayers != 1)
        {
            throw new ArgumentOutOfRangeException(nameof(arrayLayers), "A 3D texture has one array layer.");
        }

        if ((format == TextureFormat.None) || !Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format));
        }

        if ((usage == TextureUsage.None) || ((usage & ~KnownUsages) != 0))
        {
            throw new ArgumentOutOfRangeException(nameof(usage));
        }

        if ((format == TextureFormat.D32Float)
            ? ((usage & (TextureUsage.Storage | TextureUsage.ColorAttachment)) != 0)
            : ((usage & TextureUsage.DepthAttachment) != 0))
        {
            throw new ArgumentException("Depth-attachment usage must match the D32Float format, which cannot use storage or color-attachment access.", nameof(usage));
        }

        var maximumDimension = (dimension == TextureDimension.Dimension2D)
            ? Math.Max(width, height)
            : Math.Max(Math.Max(width, height), depth);
        var maximumMipLevels = BitOperations.Log2((uint)maximumDimension) + 1;

        if (mipLevels > maximumMipLevels)
        {
            throw new ArgumentOutOfRangeException(nameof(mipLevels), "Mip levels must fit the complete extent's mip chain.");
        }

        ArgumentNullException.ThrowIfNull(debugName);

        ArrayLayers = arrayLayers;
        DebugName = debugName;
        Depth = depth;
        Dimension = dimension;
        Format = format;
        Height = height;
        MipLevels = mipLevels;
        Usage = usage;
        Width = width;
    }

    /// <summary>Gets the positive array-layer count.</summary>
    public int ArrayLayers { get; }

    /// <summary>Gets the optional diagnostic name.</summary>
    public string DebugName { get; }

    /// <summary>Gets the positive depth in texels.</summary>
    public int Depth { get; }

    /// <summary>Gets the texture dimension.</summary>
    public TextureDimension Dimension { get; }

    /// <summary>Gets the texture format.</summary>
    public TextureFormat Format { get; }

    /// <summary>Gets the positive height in texels.</summary>
    public int Height { get; }

    /// <summary>Gets the positive mip-level count.</summary>
    public int MipLevels { get; }

    /// <summary>Gets the nonempty set of requested texture usages.</summary>
    public TextureUsage Usage { get; }

    /// <summary>Gets the positive width in texels.</summary>
    public int Width { get; }
}
