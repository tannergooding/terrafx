// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Input;

/// <summary>Flags that modify input binding behavior.</summary>
[Flags]
public enum InputBindingBehavior
{
    /// <summary>No additional behavior.</summary>
    None = 0,

    /// <summary>Lease matched physical controls against lower-ranked contexts.</summary>
    ConsumePhysicalEvent = 1,
}
