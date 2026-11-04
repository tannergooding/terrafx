// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Hosting;

/// <summary>Describes whether an input router was bound to a host route.</summary>
public enum InputBindingStatus
{
    /// <summary>The router was bound to the requested host route.</summary>
    Bound = 0,

    /// <summary>The route was rejected without changing host state.</summary>
    Rejected = 1,
}
