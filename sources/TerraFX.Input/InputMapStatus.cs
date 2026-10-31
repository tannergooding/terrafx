// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Input;

/// <summary>Describes the result of publishing one complete input map.</summary>
public enum InputMapStatus
{
    /// <summary>A new map was published.</summary>
    Registered = 0,

    /// <summary>An existing map was atomically replaced.</summary>
    Replaced = 1,

    /// <summary>The candidate was rejected without changing the live map.</summary>
    Rejected = 2,

    /// <summary>The input-control operation could not be admitted within capacity.</summary>
    CapacityExceeded = 3,
}
