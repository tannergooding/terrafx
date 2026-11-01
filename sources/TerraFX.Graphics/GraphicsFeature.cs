// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Graphics;

/// <summary>Identifies a reported hardware or backend feature.</summary>
public enum GraphicsFeature
{
    /// <summary>No reported graphics feature.</summary>
    None = 0,

    /// <summary>Direct3D feature level 12.0.</summary>
    Direct3DFeatureLevel12 = 1,

    /// <summary>Shader Model 6.0.</summary>
    ShaderModel6 = 2,

    /// <summary>Vulkan timeline semaphores.</summary>
    TimelineSemaphore = 3,

    /// <summary>Vulkan synchronization2.</summary>
    Synchronization2 = 4,

    /// <summary>Vulkan dynamic rendering.</summary>
    DynamicRendering = 5,

    /// <summary>Vulkan swapchain maintenance1 for interactive presentation.</summary>
    SwapchainMaintenance1 = 6,

    /// <summary>Sampler anisotropy.</summary>
    SamplerAnisotropy = 7,
}
