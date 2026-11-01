// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Graphics;

/// <summary>Selects one explicitly registered native graphics backend.</summary>
public enum GraphicsBackend
{
    /// <summary>No selected graphics backend.</summary>
    None = 0,

    /// <summary>Direct3D 12 through DXGI.</summary>
    D3D12 = 1,

    /// <summary>Vulkan.</summary>
    Vulkan = 2,
}
