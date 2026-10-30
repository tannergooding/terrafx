// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

/// <summary>Identifies the persistence role of one declared system source field.</summary>
public enum WorldSystemSourcePurpose
{
    /// <summary>No valid source-state purpose is selected.</summary>
    None = 0,

    /// <summary>The field contributes to authoritative system state.</summary>
    Authoritative = 1,

    /// <summary>The field is rebuilt as a derived cache.</summary>
    DerivedCache = 2,

    /// <summary>The field is immutable configuration excluded from canonical state.</summary>
    ImmutableConfiguration = 3,
}
