// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Rendering;

/// <summary>Confirms completion of one graphics submission and its device/surface generation.</summary>
public sealed class RenderSubmissionReceipt
{
    internal RenderSubmissionReceipt(
        long submissionSequence,
        long completedFence,
        long? presentationSequence,
        uint deviceEpoch,
        uint surfaceEpoch,
        uint swapchainEpoch)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(submissionSequence);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(completedFence);
        ArgumentOutOfRangeException.ThrowIfZero(deviceEpoch);

        if (presentationSequence is long sequence)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
            ArgumentOutOfRangeException.ThrowIfZero(surfaceEpoch);
            ArgumentOutOfRangeException.ThrowIfZero(swapchainEpoch);
        }
        else if ((surfaceEpoch != 0) || (swapchainEpoch != 0))
        {
            throw new ArgumentException("An offscreen submission cannot carry surface or swapchain epochs.");
        }

        CompletedFence = completedFence;
        DeviceEpoch = deviceEpoch;
        PresentationSequence = presentationSequence;
        SubmissionSequence = submissionSequence;
        SurfaceEpoch = surfaceEpoch;
        SwapchainEpoch = swapchainEpoch;
    }

    /// <summary>Gets the completed backend fence value.</summary>
    public long CompletedFence { get; }

    /// <summary>Gets the device generation that completed the submission.</summary>
    public uint DeviceEpoch { get; }

    /// <summary>Gets the distinct presentation sequence, or null for offscreen output.</summary>
    public long? PresentationSequence { get; }

    /// <summary>Gets the monotonic graphics submission sequence.</summary>
    public long SubmissionSequence { get; }

    /// <summary>Gets the surface generation, or zero for offscreen output.</summary>
    public uint SurfaceEpoch { get; }

    /// <summary>Gets the swapchain generation, or zero for offscreen output.</summary>
    public uint SwapchainEpoch { get; }
}
