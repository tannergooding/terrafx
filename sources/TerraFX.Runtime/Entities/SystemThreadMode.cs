// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

/// <summary>Identifies whether a system is eligible for parallel execution.</summary>
public enum SystemThreadMode
{
    /// <summary>No thread mode; valid only as a default value.</summary>
    None = 0,

    /// <summary>Execute on the owning World thread.</summary>
    OwnerThread = 1,

    /// <summary>Execute as a synchronous system job on the CPU worker pool.</summary>
    Parallel = 2,
}
