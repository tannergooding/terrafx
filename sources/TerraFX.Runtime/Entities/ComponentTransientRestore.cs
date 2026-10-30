// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

/// <summary>Identifies how one transient component is restored.</summary>
public enum ComponentTransientRestore
{
    /// <summary>The component is persistent and has no transient restore policy.</summary>
    None = 0,

    /// <summary>Reset transient component values to their declared defaults.</summary>
    Reset = 1,

    /// <summary>Rebuild transient component values as exact derived state.</summary>
    Derived = 2,
}
