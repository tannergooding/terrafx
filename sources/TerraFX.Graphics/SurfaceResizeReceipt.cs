// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Contains the result of an owner-applied surface resize.</summary>
public sealed class SurfaceResizeReceipt
{
    internal SurfaceResizeReceipt(
        SurfaceResizeStatus status,
        PixelSize clientPixelSize,
        uint surfaceEpoch,
        uint swapchainEpoch,
        Diagnostic? rejection)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        ArgumentOutOfRangeException.ThrowIfZero(surfaceEpoch);
        var hasDrawableExtent = (clientPixelSize.Width > 0) && (clientPixelSize.Height > 0);

        if (status == SurfaceResizeStatus.Resized)
        {
            if (!hasDrawableExtent || (swapchainEpoch == 0) || (rejection is not null))
            {
                throw new ArgumentException("A resized receipt requires a positive drawable extent, active swapchain epoch, and no rejection.");
            }
        }
        else if (status == SurfaceResizeStatus.Suspended)
        {
            if (hasDrawableExtent || (swapchainEpoch != 0) || (rejection is not null))
            {
                throw new ArgumentException("A suspended receipt requires a zero drawable extent, no swapchain epoch, and no rejection.");
            }
        }
        else if (rejection is null)
        {
            throw new ArgumentException("A rejected receipt requires a diagnostic and other statuses must not include one.", nameof(rejection));
        }

        Status = status;
        ClientPixelSize = clientPixelSize;
        SurfaceEpoch = surfaceEpoch;
        SwapchainEpoch = swapchainEpoch;
        Rejection = rejection;
    }

    /// <summary>Gets the observed physical client extent.</summary>
    public PixelSize ClientPixelSize { get; }

    /// <summary>Gets the diagnostic for a rejected request, or null when it was applied.</summary>
    public Diagnostic? Rejection { get; }

    /// <summary>Gets the native surface generation.</summary>
    /// <summary>Gets the resize result.</summary>
    public SurfaceResizeStatus Status { get; }

    /// <summary>Gets the native surface generation.</summary>
    public uint SurfaceEpoch { get; }

    /// <summary>Gets the active swapchain generation, or zero when suspended.</summary>
    public uint SwapchainEpoch { get; }
}
