// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Graphics;
using TerraFX.Interop.Vulkan;

namespace TerraFX.Graphics.Vulkan;

internal static class VulkanNativeTypeMapper
{
    internal static GraphicsApiVersion GetGraphicsApiVersion(uint nativeVersion)
    {
        var variant = nativeVersion >> 29;
        var major = (nativeVersion >> 22) & 0x7f;
        var minor = (nativeVersion >> 12) & 0x3ff;
        var patch = nativeVersion & 0xfff;
        return new GraphicsApiVersion(major, minor, patch, variant);
    }

    internal static GraphicsAdapterId GetNativeAdapterId(ReadOnlySpan<byte> deviceUuid)
    {
        if (deviceUuid.Length != 16)
        {
            throw new ArgumentException("A Vulkan deviceUUID must contain exactly 16 bytes.", nameof(deviceUuid));
        }

        var adapterGuid = new Guid(deviceUuid, bigEndian: true);

        if (adapterGuid == Guid.Empty)
        {
            throw new ArgumentException("A Vulkan deviceUUID cannot be all zero.", nameof(deviceUuid));
        }

        return new GraphicsAdapterId(GraphicsBackend.Vulkan, adapterGuid.ToByteArray(bigEndian: true));
    }

    internal static TextureFormat GetTextureFormat(VkFormat format)
        => format switch {
            VkFormat.VK_FORMAT_R8_UNORM => TextureFormat.R8Unorm,
            VkFormat.VK_FORMAT_R8G8B8A8_UNORM => TextureFormat.Rgba8Unorm,
            VkFormat.VK_FORMAT_R8G8B8A8_SRGB => TextureFormat.Rgba8Srgb,
            VkFormat.VK_FORMAT_B8G8R8A8_UNORM => TextureFormat.Bgra8Unorm,
            VkFormat.VK_FORMAT_B8G8R8A8_SRGB => TextureFormat.Bgra8Srgb,
            VkFormat.VK_FORMAT_R16G16B16A16_SFLOAT => TextureFormat.Rgba16Float,
            VkFormat.VK_FORMAT_R32_SFLOAT => TextureFormat.R32Float,
            VkFormat.VK_FORMAT_R32_UINT => TextureFormat.R32UInt,
            VkFormat.VK_FORMAT_D32_SFLOAT => TextureFormat.D32Float,
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

    internal static VkFormat GetVkFormat(TextureFormat format)
        => format switch {
            TextureFormat.R8Unorm => VkFormat.VK_FORMAT_R8_UNORM,
            TextureFormat.Rgba8Unorm => VkFormat.VK_FORMAT_R8G8B8A8_UNORM,
            TextureFormat.Rgba8Srgb => VkFormat.VK_FORMAT_R8G8B8A8_SRGB,
            TextureFormat.Bgra8Unorm => VkFormat.VK_FORMAT_B8G8R8A8_UNORM,
            TextureFormat.Bgra8Srgb => VkFormat.VK_FORMAT_B8G8R8A8_SRGB,
            TextureFormat.Rgba16Float => VkFormat.VK_FORMAT_R16G16B16A16_SFLOAT,
            TextureFormat.R32Float => VkFormat.VK_FORMAT_R32_SFLOAT,
            TextureFormat.R32UInt => VkFormat.VK_FORMAT_R32_UINT,
            TextureFormat.D32Float => VkFormat.VK_FORMAT_D32_SFLOAT,
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
}
