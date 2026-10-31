// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Input;

/// <summary>Describes the result of one recipient-scoped input-control operation.</summary>
public enum InputRoutingStatus
{
    /// <summary>The operation was applied at its ordered host boundary.</summary>
    Applied = 0,

    /// <summary>The operation was rejected without mutation.</summary>
    Rejected = 1,

    /// <summary>The input-control operation could not be admitted within capacity.</summary>
    CapacityExceeded = 2,
}
