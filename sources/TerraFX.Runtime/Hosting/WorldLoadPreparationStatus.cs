// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Hosting;

/// <summary>Identifies whether a World checkpoint preparation was admitted.</summary>
public enum WorldLoadPreparationStatus
{
    /// <summary>A complete, disposable replacement plan is available.</summary>
    Prepared,

    /// <summary>The checkpoint or target binding was refused with diagnostics.</summary>
    Rejected,
}
