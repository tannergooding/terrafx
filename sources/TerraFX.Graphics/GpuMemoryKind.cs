// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Graphics;

/// <summary>Selects the required CPU/GPU visibility for one buffer allocation.</summary>
public enum GpuMemoryKind
{
    /// <summary>No selected memory kind.</summary>
    None = 0,

    /// <summary>Device-local storage that is not exposed as a CPU span.</summary>
    DeviceLocal = 1,

    /// <summary>Host-written storage that the GPU may read.</summary>
    Upload = 2,

    /// <summary>Host-readable storage after the required copy completion.</summary>
    Readback = 3,
}
