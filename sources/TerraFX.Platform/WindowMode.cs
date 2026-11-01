// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Platform;

/// <summary>Defines the requested native window presentation mode.</summary>
public enum WindowMode
{
    /// <summary>Use a normal decorated window.</summary>
    Windowed = 0,

    /// <summary>Cover the selected display without requesting exclusive ownership.</summary>
    BorderlessFullscreen = 1,

    /// <summary>Request exclusive display mode where the selected platform supports it.</summary>
    ExclusiveFullscreen = 2,
}
