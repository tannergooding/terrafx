// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Hosting;

/// <summary>Identifies the fused multiply-add policy stored in the State2 numerics profile.</summary>
public enum WorldPersistenceFmaPolicy
{
    /// <summary>Fused multiply-add is disallowed.</summary>
    Disallowed = 0,

    /// <summary>Fused multiply-add is permitted.</summary>
    Permitted = 1,
}
