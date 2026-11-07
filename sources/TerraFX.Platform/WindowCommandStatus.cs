// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Platform;

/// <summary>Reports the application outcome of an owner-thread window command.</summary>
public enum WindowCommandStatus
{
    /// <summary>The native platform accepted the requested operation.</summary>
    Applied = 0,

    /// <summary>The target window was no longer active when the owner applied the command.</summary>
    Disposed = 1,

    /// <summary>The selected platform does not support the requested operation.</summary>
    Unsupported = 2,

    /// <summary>The native platform denied the requested operation.</summary>
    PermissionDenied = 3,

    /// <summary>The native platform has not confirmed the requested state transition.</summary>
    Unconfirmed = 4,

    /// <summary>The bounded owner mailbox had no capacity for the command.</summary>
    QueueFull = 5,
}
