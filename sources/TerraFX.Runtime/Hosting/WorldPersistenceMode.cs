// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Hosting;

/// <summary>Defines the compatibility contract for a persisted World.</summary>
public enum WorldPersistenceMode
{
    /// <summary>Persist portable schema state and resolve assets against the selected current package.</summary>
    Standard = 0,

    /// <summary>Persist exact package and runtime identity for same-profile continuation.</summary>
    StrictLocal = 1,
}
