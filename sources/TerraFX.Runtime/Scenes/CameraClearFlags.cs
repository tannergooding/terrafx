// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;

namespace TerraFX.Scenes;

/// <summary>Specifies which camera target channels are cleared before rendering.</summary>
[Flags]
[SuppressMessage("Design", "CA1028:Enum Storage should be Int32", Justification = "The bit values are fixed by the reviewed camera wire contract.")]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "CameraClearFlags is the reviewed public API name.")]
public enum CameraClearFlags : byte
{
    /// <summary>Preserves all target contents.</summary>
    None = 0,

    /// <summary>Clears the color target.</summary>
    Color = 1,

    /// <summary>Clears the depth target.</summary>
    Depth = 2,
}
