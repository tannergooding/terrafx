// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;

namespace TerraFX.Graphics;

/// <summary>Contains copied capabilities for one native surface and swapchain generation.</summary>
public sealed class SurfaceCapabilities
{
    private readonly ImmutableArray<SurfaceFormat> _formats;
    private readonly ImmutableArray<GraphicsPresentMode> _presentModes;

    internal SurfaceCapabilities(
        ImmutableArray<SurfaceFormat> formats,
        ImmutableArray<GraphicsPresentMode> presentModes,
        PixelSize minimumExtent,
        PixelSize maximumExtent,
        int minimumImageCount,
        int? maximumImageCount,
        bool supportsTearing,
        uint surfaceEpoch,
        uint swapchainEpoch)
    {
        if (formats.IsDefaultOrEmpty)
        {
            throw new ArgumentException("Surface formats must be initialized and nonempty.", nameof(formats));
        }

        if (presentModes.IsDefaultOrEmpty)
        {
            throw new ArgumentException("Surface present modes must be initialized and nonempty.", nameof(presentModes));
        }

        if ((minimumExtent.Width <= 0)
            || (minimumExtent.Height <= 0)
            || (maximumExtent.Width < minimumExtent.Width)
            || (maximumExtent.Height < minimumExtent.Height))
        {
            throw new ArgumentOutOfRangeException(nameof(minimumExtent));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumImageCount);

        if ((maximumImageCount is <= 0)
            || (maximumImageCount is int maximumCount && (maximumCount < minimumImageCount)))
        {
            throw new ArgumentOutOfRangeException(nameof(maximumImageCount));
        }

        ArgumentOutOfRangeException.ThrowIfZero(surfaceEpoch);

        var sortedFormats = formats.ToArray();
        Array.Sort(sortedFormats, static (left, right) => {
            var formatComparison = left.Format.CompareTo(right.Format);
            return (formatComparison != 0) ? formatComparison : left.ColorSpace.CompareTo(right.ColorSpace);
        });

        for (var index = 0; index < sortedFormats.Length; index++)
        {
            if ((sortedFormats[index].Format == TextureFormat.None)
                || !Enum.IsDefined(sortedFormats[index].Format)
                || (sortedFormats[index].ColorSpace == SurfaceColorSpace.None)
                || !Enum.IsDefined(sortedFormats[index].ColorSpace))
            {
                throw new ArgumentOutOfRangeException(nameof(formats));
            }

            if ((index > 0)
                && (sortedFormats[index].Format == sortedFormats[index - 1].Format)
                && (sortedFormats[index].ColorSpace == sortedFormats[index - 1].ColorSpace))
            {
                throw new ArgumentException("Surface format/color-space pairs must be unique.", nameof(formats));
            }
        }

        var sortedPresentModes = presentModes.ToArray();
        Array.Sort(sortedPresentModes);

        for (var index = 0; index < sortedPresentModes.Length; index++)
        {
            if ((sortedPresentModes[index] == GraphicsPresentMode.None) || !Enum.IsDefined(sortedPresentModes[index]))
            {
                throw new ArgumentOutOfRangeException(nameof(presentModes));
            }

            if ((index > 0) && (sortedPresentModes[index] == sortedPresentModes[index - 1]))
            {
                throw new ArgumentException("Surface present modes must be unique.", nameof(presentModes));
            }
        }

        if (Array.BinarySearch(sortedPresentModes, GraphicsPresentMode.Fifo) < 0)
        {
            throw new ArgumentException("A native surface must support FIFO presentation.", nameof(presentModes));
        }

        _formats = [.. sortedFormats];
        _presentModes = [.. sortedPresentModes];
        MinimumExtent = minimumExtent;
        MaximumExtent = maximumExtent;
        MinimumImageCount = minimumImageCount;
        MaximumImageCount = maximumImageCount;
        SupportsTearing = supportsTearing;
        SurfaceEpoch = surfaceEpoch;
        SwapchainEpoch = swapchainEpoch;
    }

    /// <summary>Gets the actual supported format/color-space pairs in canonical order.</summary>
    public ImmutableArray<SurfaceFormat> Formats => _formats;

    /// <summary>Gets the maximum supported physical client extent.</summary>
    public PixelSize MaximumExtent { get; }

    /// <summary>Gets the optional maximum image count; <see langword="null"/> means no finite maximum.</summary>
    public int? MaximumImageCount { get; }

    /// <summary>Gets the minimum supported physical client extent.</summary>
    public PixelSize MinimumExtent { get; }

    /// <summary>Gets the minimum image count.</summary>
    public int MinimumImageCount { get; }

    /// <summary>Gets the actual supported present modes in numeric order.</summary>
    public ImmutableArray<GraphicsPresentMode> PresentModes => _presentModes;

    /// <summary>Gets whether this surface supports tearing.</summary>
    public bool SupportsTearing { get; }

    /// <summary>Gets the native surface generation.</summary>
    public uint SurfaceEpoch { get; }

    /// <summary>Gets the native swapchain generation, or zero when no swapchain is active.</summary>
    public uint SwapchainEpoch { get; }
}
