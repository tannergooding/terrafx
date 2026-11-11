// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using TerraFX.Graphics;
using TerraFX.Interop.Vulkan;

namespace TerraFX.Graphics.Vulkan;

internal readonly record struct VulkanBackendSampledImageBinding(
    ShaderBindingGroup Group,
    int Binding,
    VulkanBackendTexture Texture,
    GraphTextureRange Range,
    bool ImageArrayed = false,
    VkImageLayout? ImageLayout = null);
