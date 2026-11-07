// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Platform;

/// <summary>Identifies a window command operation.</summary>
public enum WindowCommandKind
{
    /// <summary>Changes the presentation title.</summary>
    SetTitle = 0,

    /// <summary>Requests a window presentation mode.</summary>
    SetMode = 1,

    /// <summary>Closes the window.</summary>
    Close = 2,

    /// <summary>Requests a logical client-size change.</summary>
    SetClientSize = 3,
}
