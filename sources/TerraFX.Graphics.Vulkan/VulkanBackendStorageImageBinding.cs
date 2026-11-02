// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using TerraFX.Graphics;

namespace TerraFX.Graphics.Vulkan;

internal readonly record struct VulkanBackendStorageImageBinding(
    ShaderBindingGroup Group,
    int Binding,
    VulkanBackendTexture Texture);
