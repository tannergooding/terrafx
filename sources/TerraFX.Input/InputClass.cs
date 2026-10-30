// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Input;

/// <summary>Identifies physical classes that an input context can capture.</summary>
[Flags]
public enum InputClass
{
    /// <summary>No physical class is captured.</summary>
    None = 0,

    /// <summary>Keyboard controls.</summary>
    Keyboard = 1,

    /// <summary>Pointer controls.</summary>
    Pointing = 2,

    /// <summary>Gamepad controls.</summary>
    Gamepad = 4,
}
