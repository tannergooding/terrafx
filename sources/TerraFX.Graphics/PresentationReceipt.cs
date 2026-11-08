// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;

namespace TerraFX.Graphics;

/// <summary>Reports an independently observed presentation outcome and its native generations.</summary>
public sealed class PresentationReceipt
{
    internal PresentationReceipt(
        PresentationStatus status,
        long presentationSequence,
        uint deviceEpoch,
        uint surfaceEpoch,
        uint swapchainEpoch,
        Diagnostic? diagnostic)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(presentationSequence);
        ArgumentOutOfRangeException.ThrowIfZero(deviceEpoch);
        ArgumentOutOfRangeException.ThrowIfZero(surfaceEpoch);
        ArgumentOutOfRangeException.ThrowIfZero(swapchainEpoch);

        Diagnostic = diagnostic;
        DeviceEpoch = deviceEpoch;
        PresentationSequence = presentationSequence;
        Status = status;
        SurfaceEpoch = surfaceEpoch;
        SwapchainEpoch = swapchainEpoch;
    }

    /// <summary>Gets the device generation that owned the presentation.</summary>
    public uint DeviceEpoch { get; }

    /// <summary>Gets the diagnostic associated with a non-presented terminal outcome.</summary>
    public Diagnostic? Diagnostic { get; }

    /// <summary>Gets the monotonic presentation sequence.</summary>
    public long PresentationSequence { get; }

    /// <summary>Gets the terminal presentation outcome.</summary>
    public PresentationStatus Status { get; }

    /// <summary>Gets the native Surface generation.</summary>
    public uint SurfaceEpoch { get; }

    /// <summary>Gets the swapchain generation.</summary>
    public uint SwapchainEpoch { get; }
}
