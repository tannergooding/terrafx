// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;

namespace TerraFX.Entities;

/// <summary>Identifies the selected persistence class of a component schema.</summary>
[Flags]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "The public M03 API contract names this storage selection ComponentStorageFlags.")]
public enum ComponentStorageFlags
{
    /// <summary>No valid component storage class is selected.</summary>
    None = 0,

    /// <summary>The component is persistent.</summary>
    Persistent = 1,

    /// <summary>The component is transient.</summary>
    Transient = 2,
}
