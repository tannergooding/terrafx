// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Graphics.Vulkan;

internal enum VulkanBackendStartupStage
{
    None = 0,
    InstanceCreated = 1,
    LogicalDeviceCreated = 2,
    CommandPoolCreated = 3,
    CommandBufferCreated = 4,
    TimelineSemaphoreCreated = 5,
}
