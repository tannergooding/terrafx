// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Hosting;

/// <summary>Defines the requested host execution profile.</summary>
public enum HostProfile
{
    /// <summary>No host profile is selected.</summary>
    None = 0,

    /// <summary>Run simulation and CPU extraction without native window or graphics services.</summary>
    Headless = 1,

    /// <summary>Require native graphics without a display or presentation surface.</summary>
    OffscreenNative = 2,

    /// <summary>Require a native display, window, and graphics presentation path.</summary>
    Interactive = 3,
}
