// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Hosting;

/// <summary>Identifies whether a prepared World replacement was published.</summary>
public enum WorldLoadStatus
{
    /// <summary>The replacement World was published.</summary>
    Loaded,

    /// <summary>The target binding or checkpoint was refused before publication.</summary>
    Rejected,
}
