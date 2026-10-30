// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

/// <summary>Identifies the standalone simulation admission policy.</summary>
public enum SimulationProfile
{
    /// <summary>Admits the default standalone behavior and declared reset factories.</summary>
    Standard = 0,

    /// <summary>Requires exact local restoration for selected derived state.</summary>
    StrictLocal = 1,
}
