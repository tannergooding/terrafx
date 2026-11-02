// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Hosting;

/// <summary>Defines the required durability guarantee for one save.</summary>
public enum SaveDurabilityPolicy
{
    /// <summary>No valid policy; rejected by save options.</summary>
    None = 0,

    /// <summary>Require the platform's supported durable replacement guarantee.</summary>
    RequireDurable = 1,

    /// <summary>Permit an explicit best-effort replacement when durable replacement is unavailable.</summary>
    BestEffort = 2,
}
