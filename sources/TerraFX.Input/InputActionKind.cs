// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Input;

/// <summary>Identifies the value shape produced by an input action.</summary>
public enum InputActionKind
{
    /// <summary>A digital pressed/released state.</summary>
    Digital = 0,

    /// <summary>A signed scalar value.</summary>
    Axis1 = 1,

    /// <summary>A two-dimensional vector value.</summary>
    Axis2 = 2,
}
