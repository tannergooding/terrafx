// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Graphics;

/// <summary>Reports the lifecycle state of one host-owned graphics device.</summary>
public enum GraphicsDeviceStatus
{
    /// <summary>No initialized device state.</summary>
    None = 0,

    /// <summary>The native device passed profile admission and accepts requests.</summary>
    Ready = 1,

    /// <summary>The native API reported device loss and new requests are refused.</summary>
    Lost = 2,

    /// <summary>Host shutdown closed admissions and is joining device consumers.</summary>
    Stopping = 3,

    /// <summary>Device consumers joined and native device ownership was released.</summary>
    Disposed = 4,
}
