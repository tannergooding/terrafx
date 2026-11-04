// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Hosting;

/// <summary>Defines the applied or rejected outcome of a hosted time-scale request.</summary>
public enum TimeScaleChangeStatus
{
    /// <summary>The World applied the requested scale at an owner-control boundary.</summary>
    Applied = 0,

    /// <summary>The World refused the request without changing its current time scale.</summary>
    Rejected = 1,
}
