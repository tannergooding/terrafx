// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Input;

/// <summary>Identifies one derived action transition.</summary>
public enum InputTransitionKind
{
    /// <summary>The effective axis value changed.</summary>
    ValueChanged = 0,

    /// <summary>The action became down.</summary>
    Pressed = 1,

    /// <summary>The action became up.</summary>
    Released = 2,

    /// <summary>The action was canceled by an input-control boundary.</summary>
    Canceled = 3,
}
