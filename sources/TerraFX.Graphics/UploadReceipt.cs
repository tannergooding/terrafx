// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;

namespace TerraFX.Graphics;

/// <summary>Reports the terminal outcome and completion fence of one GPU upload.</summary>
public sealed class UploadReceipt
{
    /// <summary>Initializes an immutable upload result.</summary>
    /// <param name="status">The terminal upload state.</param>
    /// <param name="deviceEpoch">The nonzero device generation.</param>
    /// <param name="completedFence">The positive completed graphics fence, or null when rejected.</param>
    /// <param name="rejection">The rejection diagnostic, or null for a ready upload.</param>
    /// <exception cref="ArgumentException">The status, fence, or rejection fields are inconsistent.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The status or device epoch is invalid.</exception>
    internal UploadReceipt(UploadStatus status, uint deviceEpoch, long? completedFence, Diagnostic? rejection)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        ArgumentOutOfRangeException.ThrowIfZero(deviceEpoch);

        if (status == UploadStatus.Ready)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(completedFence ?? 0);

            if (rejection is not null)
            {
                throw new ArgumentException("A ready upload cannot contain a rejection diagnostic.", nameof(rejection));
            }
        }
        else if ((completedFence is not null) || (rejection is null))
        {
            throw new ArgumentException("A rejected upload requires a diagnostic and cannot contain a completion fence.");
        }

        CompletedFence = completedFence;
        DeviceEpoch = deviceEpoch;
        Rejection = rejection;
        Status = status;
    }

    /// <summary>Gets the completed graphics fence, or null when the upload was rejected.</summary>
    public long? CompletedFence { get; }

    /// <summary>Gets the device generation that admitted the upload.</summary>
    public uint DeviceEpoch { get; }

    /// <summary>Gets the diagnostic for a rejected upload, or null when it completed.</summary>
    public Diagnostic? Rejection { get; }

    /// <summary>Gets the terminal upload state.</summary>
    public UploadStatus Status { get; }
}
