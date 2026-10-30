// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Input;

/// <summary>Identifies a keyboard control representation.</summary>
public enum KeyboardControlKind
{
    /// <summary>A physical USB HID usage.</summary>
    KeyboardHid = 0,

    /// <summary>A layout-dependent logical key name.</summary>
    LogicalKey = 1,
}
