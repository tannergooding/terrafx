// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX;

/// <summary>Identifies how transient schema state is reconstructed.</summary>
public enum ContentTransientRestore
{
    /// <summary>The schema does not define transient component state.</summary>
    NotApplicable = 0,

    /// <summary>Transient values are reset to declared defaults.</summary>
    ResetDeclaredDefaults = 1,

    /// <summary>Transient values are rebuilt as exact derived state.</summary>
    ExactDerivedRehydration = 2,
}
