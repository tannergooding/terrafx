// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Hosting;

/// <summary>Defines how the host handles retained simulation debt after an overrun.</summary>
public enum SimulationOverrunPolicy
{
    /// <summary>Pause simulation and retain debt until an explicit host-control change.</summary>
    Pause = 0,

    /// <summary>Discard excess debt and record a replay discontinuity.</summary>
    DropDebt = 1,
}
