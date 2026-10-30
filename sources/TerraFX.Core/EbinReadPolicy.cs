// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX;

/// <summary>Selects the structural requirements applied to an EBIN record.</summary>
public enum EbinReadPolicy
{
    /// <summary>Accepts any structurally valid encoding.</summary>
    Structural = 0,

    /// <summary>Requires canonical field and nested-record packing.</summary>
    Canonical = 1,
}
