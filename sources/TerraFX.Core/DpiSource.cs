// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX;

/// <summary>Identifies the source used for a window's DPI conversion value.</summary>
public enum DpiSource
{
    /// <summary>No platform or application source was available.</summary>
    Unknown = 0,

    /// <summary>The Windows Per-Monitor-V2 window DPI.</summary>
    WindowsPerMonitorV2 = 1,

    /// <summary>The Wayland fractional-scale protocol value.</summary>
    WaylandFractionalScale = 2,

    /// <summary>The Wayland integer surface buffer scale.</summary>
    WaylandIntegerBufferScale = 3,

    /// <summary>A recognized X11 settings value.</summary>
    X11Settings = 4,

    /// <summary>An explicit application-provided scale.</summary>
    ApplicationOverride = 5,
}
