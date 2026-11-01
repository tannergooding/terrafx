// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Graphics;

/// <summary>Selects the native device profile admitted by a host.</summary>
public enum GraphicsDeviceProfile
{
    /// <summary>No selected native graphics profile.</summary>
    None = 0,

    /// <summary>Creates a real device without a presentation surface or swapchain.</summary>
    OffscreenNative = 1,

    /// <summary>Creates a real device for one explicitly selected native presentation platform.</summary>
    Interactive = 2,
}
