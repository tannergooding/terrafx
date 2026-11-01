// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Describes memory properties reported for a native graphics heap.</summary>
[Flags]
public enum GraphicsMemoryHeapProperties
{
    /// <summary>No recognized memory property.</summary>
    None = 0,

    /// <summary>The heap is device local.</summary>
    DeviceLocal = 1,

    /// <summary>The heap is host visible.</summary>
    HostVisible = 2,

    /// <summary>The heap is host coherent.</summary>
    HostCoherent = 4,

    /// <summary>The heap is host cached.</summary>
    HostCached = 8,
}
