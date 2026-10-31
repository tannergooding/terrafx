// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Input;

/// <summary>Identifies a normalized keyboard transition.</summary>
public enum InputKeyEventKind
{
    /// <summary>The key became down.</summary>
    Down = 0,

    /// <summary>The key became up.</summary>
    Up = 1,

    /// <summary>The operating system repeated a held key.</summary>
    Repeat = 2,
}
