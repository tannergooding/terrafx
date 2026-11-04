// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Hosting;

/// <summary>Identifies the terminal outcome of one checkpoint save.</summary>
public enum SaveStatus
{
    /// <summary>The checkpoint and its replacement were durably committed.</summary>
    DurableCommitted,

    /// <summary>The checkpoint was committed using an explicitly permitted best-effort replacement.</summary>
    BestEffortCommitted,

    /// <summary>The requested durable replacement guarantee is unavailable.</summary>
    DurabilityUnsupported,

    /// <summary>The checkpoint could not be captured or committed.</summary>
    Rejected,
}
