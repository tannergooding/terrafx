// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

/// <summary>Identifies the system cleanup owner operation.</summary>
public enum WorldSystemCleanupOperation
{
    /// <summary>No cleanup operation is selected.</summary>
    None = 0,

    /// <summary>Unwinds system instances after World construction failed.</summary>
    ConstructionUnwind = 1,

    /// <summary>Releases system instances during World disposal.</summary>
    Disposal = 2,
}
