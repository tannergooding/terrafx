// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Platform;

/// <summary>Selects the explicitly requested native platform adapter.</summary>
public enum PlatformBackend
{
    /// <summary>No platform backend is selected.</summary>
    None = 0,

    /// <summary>Do not open display, physical input, or audio services.</summary>
    Headless = 1,

    /// <summary>Use the Windows native window and input APIs.</summary>
    WindowsNative = 2,

    /// <summary>Require the Wayland adapter.</summary>
    WaylandOnly = 3,

    /// <summary>Try Wayland, then explicitly fall back to X11.</summary>
    WaylandThenX11 = 4,

    /// <summary>Require the X11 adapter.</summary>
    X11Only = 5,
}
