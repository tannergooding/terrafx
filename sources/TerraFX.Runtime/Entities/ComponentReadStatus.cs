// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

/// <summary>Identifies one component read result status.</summary>
public enum ComponentReadStatus
{
    /// <summary>No valid read result was issued.</summary>
    None = 0,

    /// <summary>A complete symbolic author value was read.</summary>
    Read = 1,

    /// <summary>The source record was rejected with diagnostics.</summary>
    Rejected = 2,
}
