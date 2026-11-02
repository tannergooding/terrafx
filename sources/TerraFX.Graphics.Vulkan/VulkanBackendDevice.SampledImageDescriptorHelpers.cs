// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX.Graphics;
using TerraFX.Interop.Vulkan;
using VulkanApi = TerraFX.Interop.Vulkan.Vulkan;

namespace TerraFX.Graphics.Vulkan;

internal sealed unsafe partial class VulkanBackendDevice
{
    private static VkImageView CreateDescriptorImageView(VkDevice device, VulkanBackendTexture texture)
    {
        var description = texture.Description;
        var createInfo = default(VkImageViewCreateInfo);
        createInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
        createInfo.image = texture.Image;
        createInfo.viewType = GetNativeDescriptorImageViewType(description);
        createInfo.format = VulkanNativeTypeMapper.GetVkFormat(description.Format);
        createInfo.subresourceRange.aspectMask = description.Format == TextureFormat.D32Float
            ? VkImageAspectFlags.VK_IMAGE_ASPECT_DEPTH_BIT
            : VkImageAspectFlags.VK_IMAGE_ASPECT_COLOR_BIT;
        createInfo.subresourceRange.baseMipLevel = 0;
        createInfo.subresourceRange.levelCount = checked((uint)description.MipLevels);
        createInfo.subresourceRange.baseArrayLayer = 0;
        createInfo.subresourceRange.layerCount = checked((uint)description.ArrayLayers);
        VkImageView imageView = default;
        ThrowIfFailed(VulkanApi.vkCreateImageView(device, &createInfo, null, &imageView), "vkCreateImageView");
        return imageView;
    }

    private static VkImageViewType GetNativeDescriptorImageViewType(TextureDescription description)
        => description.Dimension switch {
            TextureDimension.Dimension2D when description.ArrayLayers == 1 => VkImageViewType.VK_IMAGE_VIEW_TYPE_2D,
            TextureDimension.Dimension2D => VkImageViewType.VK_IMAGE_VIEW_TYPE_2D_ARRAY,
            TextureDimension.Dimension3D => VkImageViewType.VK_IMAGE_VIEW_TYPE_3D,
            _ => throw new ArgumentOutOfRangeException(nameof(description)),
        };

    private static VkImageLayout GetSampledImageLayout(VulkanBackendTexture texture)
    {
        var description = texture.Description;
        var aspect = description.Format == TextureFormat.D32Float
            ? TextureAspect.Depth
            : TextureAspect.Color;
        var layout = VkImageLayout.VK_IMAGE_LAYOUT_GENERAL;

        for (var arrayLayer = 0; arrayLayer < description.ArrayLayers; arrayLayer++)
        {
            for (var mipLevel = 0; mipLevel < description.MipLevels; mipLevel++)
            {
                var subresource = new TextureSubresource(mipLevel, arrayLayer, aspect);

                if (!texture.IsInitialized(subresource))
                {
                    throw new InvalidOperationException("Every sampled-image subresource must be initialized before descriptor creation.");
                }

                if (texture.GetLayout(subresource) != layout)
                {
                    throw new NotSupportedException("All sampled-image subresources must be in the Vulkan GENERAL layout.");
                }
            }
        }

        return layout;
    }

    private static VkImageLayout GetStorageImageLayout(VulkanBackendTexture texture)
    {
        var description = texture.Description;
        var aspect = description.Format == TextureFormat.D32Float
            ? TextureAspect.Depth
            : TextureAspect.Color;
        var layout = VkImageLayout.VK_IMAGE_LAYOUT_GENERAL;

        for (var arrayLayer = 0; arrayLayer < description.ArrayLayers; arrayLayer++)
        {
            for (var mipLevel = 0; mipLevel < description.MipLevels; mipLevel++)
            {
                var subresource = new TextureSubresource(mipLevel, arrayLayer, aspect);

                if (texture.GetLayout(subresource) != layout)
                {
                    throw new NotSupportedException("All storage-image subresources must be in the Vulkan GENERAL layout.");
                }
            }
        }

        return layout;
    }

    private static bool MatchesReflectedResource(
        ImmutableArray<ShaderResourceSignature> resources,
        ShaderBindingGroup group,
        int binding,
        ShaderResourceKind kind)
    {
        for (var index = 0; index < resources.Length; index++)
        {
            var resource = resources[index];

            if ((resource.Group == group)
                && (resource.Binding == binding)
                && (resource.Kind == kind)
                && (resource.ArrayCount == 1))
            {
                return true;
            }
        }

        return false;
    }
}
