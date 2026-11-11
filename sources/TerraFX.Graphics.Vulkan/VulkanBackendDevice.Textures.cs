// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Graphics;
using TerraFX.Interop.Vulkan;
using VulkanApi = TerraFX.Interop.Vulkan.Vulkan;

namespace TerraFX.Graphics.Vulkan;

internal sealed unsafe partial class VulkanBackendDevice
{
    private int _liveTextureCount;

    private static VkImage CreateNativeImage(VkDevice device, TextureDescription description, bool aliasable = false)
    {
        var createInfo = default(VkImageCreateInfo);
        createInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
        createInfo.flags = aliasable
            ? VkImageCreateFlags.VK_IMAGE_CREATE_ALIAS_BIT
            : default;
        createInfo.imageType = description.Dimension == TextureDimension.Dimension2D
            ? VkImageType.VK_IMAGE_TYPE_2D
            : VkImageType.VK_IMAGE_TYPE_3D;
        createInfo.format = VulkanNativeTypeMapper.GetVkFormat(description.Format);
        createInfo.extent.width = checked((uint)description.Width);
        createInfo.extent.height = checked((uint)description.Height);
        createInfo.extent.depth = checked((uint)description.Depth);
        createInfo.mipLevels = checked((uint)description.MipLevels);
        createInfo.arrayLayers = checked((uint)description.ArrayLayers);
        createInfo.samples = VkSampleCountFlags.VK_SAMPLE_COUNT_1_BIT;
        createInfo.tiling = VkImageTiling.VK_IMAGE_TILING_OPTIMAL;
        createInfo.usage = GetNativeImageUsage(description.Usage);
        createInfo.sharingMode = VkSharingMode.VK_SHARING_MODE_EXCLUSIVE;
        createInfo.initialLayout = VkImageLayout.VK_IMAGE_LAYOUT_UNDEFINED;
        VkImage image = default;
        ThrowIfFailed(VulkanApi.vkCreateImage(device, &createInfo, null, &image), "vkCreateImage");
        return image;
    }

    internal VulkanBackendTexture CreateTexture(
        TextureDescription description,
        VulkanBackendTextureAllocation allocation)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(allocation);
        EnsureActive();
        ValidateTextureDescriptionSupport(description);

        if (!ReferenceEquals(allocation.Owner, this))
        {
            throw new ArgumentException("The transient texture allocation belongs to a different Vulkan device.", nameof(allocation));
        }

        ObjectDisposedException.ThrowIf(allocation.IsDisposed, allocation);
        var image = CreateNativeImage(_device, description, aliasable: true);
        var ownsImage = true;

        try
        {
            var requirements = default(VkMemoryRequirements);
            VulkanApi.vkGetImageMemoryRequirements(_device, image, &requirements);

            if ((requirements.size > checked((ulong)allocation.AllocationSizeBytes))
                || ((requirements.memoryTypeBits & (1u << checked((int)allocation.MemoryTypeIndex))) == 0))
            {
                throw new NotSupportedException("The transient Vulkan texture is incompatible with its shared memory allocation.");
            }

            ThrowIfFailed(VulkanApi.vkBindImageMemory(_device, image, allocation.Memory, 0), "vkBindImageMemory");
            var result = new VulkanBackendTexture(
                this,
                image,
                description,
                allocation.Memory,
                allocation.MemoryTypeIndex,
                ownsMemory: false);
            _liveTextureCount = checked(_liveTextureCount + 1);
            ownsImage = false;
            return result;
        }
        finally
        {
            if (ownsImage)
            {
                VulkanApi.vkDestroyImage(_device, image, null);
            }
        }
    }

    internal VulkanBackendTexture CreateTexture(TextureDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        EnsureActive();
        ValidateTextureDescriptionSupport(description);
        var image = CreateNativeImage(_device, description);
        var ownsImage = true;
        var memory = default(VkDeviceMemory);
        var ownsMemory = false;

        try
        {
            var requirements = default(VkMemoryRequirements);
            VulkanApi.vkGetImageMemoryRequirements(_device, image, &requirements);
            GetMemoryTypeIndex(
                _physicalDeviceMemoryProperties,
                requirements.memoryTypeBits,
                GpuMemoryKind.DeviceLocal,
                out var memoryTypeIndex,
                out _,
                out _);
            var allocateInfo = default(VkMemoryAllocateInfo);
            allocateInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
            allocateInfo.allocationSize = requirements.size;
            allocateInfo.memoryTypeIndex = memoryTypeIndex;
            ThrowIfFailed(VulkanApi.vkAllocateMemory(_device, &allocateInfo, null, &memory), "vkAllocateMemory");
            ownsMemory = true;
            ThrowIfFailed(VulkanApi.vkBindImageMemory(_device, image, memory, 0), "vkBindImageMemory");

            var result = new VulkanBackendTexture(this, image, description, memory, memoryTypeIndex, ownsMemory: true);
            _liveTextureCount = checked(_liveTextureCount + 1);
            ownsImage = false;
            ownsMemory = false;
            return result;
        }
        finally
        {
            if (ownsMemory)
            {
                VulkanApi.vkFreeMemory(_device, memory, null);
            }

            if (ownsImage)
            {
                VulkanApi.vkDestroyImage(_device, image, null);
            }
        }
    }

    internal VulkanBackendTextureAllocation CreateTextureAllocation(
        TextureDescription description,
        GraphicsResourceAllocationInfo allocationInfo)
    {
        ArgumentNullException.ThrowIfNull(description);
        EnsureActive();
        ValidateTextureDescriptionSupport(description);
        var image = CreateNativeImage(_device, description, aliasable: true);

        try
        {
            var requirements = default(VkMemoryRequirements);
            VulkanApi.vkGetImageMemoryRequirements(_device, image, &requirements);

            if (requirements.size > checked((ulong)allocationInfo.SizeBytes))
            {
                throw new ArgumentException("The shared Vulkan allocation is smaller than the image requirements.", nameof(allocationInfo));
            }

            GetMemoryTypeIndex(
                _physicalDeviceMemoryProperties,
                requirements.memoryTypeBits,
                GpuMemoryKind.DeviceLocal,
                out var memoryTypeIndex,
                out var memoryProperties,
                out _);
            var allocateInfo = default(VkMemoryAllocateInfo);
            allocateInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
            allocateInfo.allocationSize = checked((ulong)allocationInfo.SizeBytes);
            allocateInfo.memoryTypeIndex = memoryTypeIndex;
            VkDeviceMemory memory = default;
            ThrowIfFailed(VulkanApi.vkAllocateMemory(_device, &allocateInfo, null, &memory), "vkAllocateMemory");

            try
            {
                return new VulkanBackendTextureAllocation(
                    this,
                    memory,
                    memoryProperties,
                    memoryTypeIndex,
                    allocationInfo.SizeBytes);
            }
            catch
            {
                VulkanApi.vkFreeMemory(_device, memory, null);
                throw;
            }
        }
        finally
        {
            VulkanApi.vkDestroyImage(_device, image, null);
        }
    }

    internal void DestroyTexture(VulkanBackendTexture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        EnsureOwnerThread();

        if (!ReferenceEquals(texture.Owner, this))
        {
            throw new ArgumentException("The texture belongs to a different Vulkan device.", nameof(texture));
        }

        if (texture.IsDisposed)
        {
            return;
        }

        if (texture.DescriptorSetReferenceCount != 0)
        {
            throw new InvalidOperationException("Vulkan texture retirement cannot release storage while descriptor sets remain live.");
        }

        if (_hasUncertainSubmission)
        {
            throw new InvalidOperationException("Vulkan texture retirement cannot release storage while submission completion is uncertain.");
        }

        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (texture.DepthAttachmentImageView.Value != 0)
        {
            VulkanApi.vkDestroyImageView(_device, texture.DepthAttachmentImageView, null);
        }

        VulkanApi.vkDestroyImage(_device, texture.Image, null);

        if (texture.OwnsMemory)
        {
            VulkanApi.vkFreeMemory(_device, texture.Memory, null);
        }

        texture.MarkDisposed();
        _liveTextureCount = checked(_liveTextureCount - 1);
    }

    internal void DestroyTextureAllocation(VulkanBackendTextureAllocation allocation)
    {
        ArgumentNullException.ThrowIfNull(allocation);
        EnsureOwnerThread();

        if (!ReferenceEquals(allocation.Owner, this))
        {
            throw new ArgumentException("The transient texture allocation belongs to a different Vulkan device.", nameof(allocation));
        }

        if (allocation.IsDisposed)
        {
            return;
        }

        if (_hasUncertainSubmission)
        {
            throw new InvalidOperationException("Vulkan transient texture allocation retirement cannot release memory while submission completion is uncertain.");
        }

        ObjectDisposedException.ThrowIf(_isDisposed, this);
        VulkanApi.vkFreeMemory(_device, allocation.Memory, null);
        allocation.MarkDisposed();
    }

    internal GraphicsResourceAllocationInfo GetTextureAllocationInfo(TextureDescription description)
        => GetTextureAllocationInfoCore(description, aliasable: false);

    internal GraphicsResourceAllocationInfo GetTransientTextureAllocationInfo(TextureDescription description)
        => GetTextureAllocationInfoCore(description, aliasable: true);

    private GraphicsResourceAllocationInfo GetTextureAllocationInfoCore(
        TextureDescription description,
        bool aliasable)
    {
        ArgumentNullException.ThrowIfNull(description);
        EnsureActive();
        ValidateTextureDescriptionSupport(description);
        var image = CreateNativeImage(_device, description, aliasable);

        try
        {
            var requirements = default(VkMemoryRequirements);
            VulkanApi.vkGetImageMemoryRequirements(_device, image, &requirements);
            GetMemoryTypeIndex(
                _physicalDeviceMemoryProperties,
                requirements.memoryTypeBits,
                GpuMemoryKind.DeviceLocal,
                out _,
                out _,
                out var heapIndex);
            return new GraphicsResourceAllocationInfo(
                ConvertPositiveLong(requirements.size, nameof(requirements)),
                ConvertPositiveLong(requirements.alignment, nameof(requirements)),
                heapIndex,
                budgetBytes: null);
        }
        finally
        {
            VulkanApi.vkDestroyImage(_device, image, null);
        }
    }

    private void ValidateTextureDescriptionSupport(TextureDescription description)
    {
        if (description.Dimension == TextureDimension.Dimension2D)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(
                description.Width,
                Capabilities.Limits.MaximumTextureDimension2D,
                nameof(description));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(
                description.Height,
                Capabilities.Limits.MaximumTextureDimension2D,
                nameof(description));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(
                description.ArrayLayers,
                Capabilities.Limits.MaximumArrayLayers,
                nameof(description));
        }
        else
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(
                description.Width,
                Capabilities.Limits.MaximumTextureDimension3D,
                nameof(description));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(
                description.Height,
                Capabilities.Limits.MaximumTextureDimension3D,
                nameof(description));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(
                description.Depth,
                Capabilities.Limits.MaximumTextureDimension3D,
                nameof(description));
        }

        GraphicsFormatCapabilities? formatCapabilities = null;

        foreach (var candidate in Capabilities.Formats)
        {
            if (candidate.Format == description.Format)
            {
                formatCapabilities = candidate;
                break;
            }
        }

        if (formatCapabilities is null)
        {
            throw new NotSupportedException($"Vulkan does not support texture format {description.Format}.");
        }

        if ((description.Usage & ~formatCapabilities.SupportedUsages) != 0)
        {
            throw new NotSupportedException(
                $"Vulkan format {description.Format} does not support all requested texture usages.");
        }

        var imageType = description.Dimension == TextureDimension.Dimension2D
            ? VkImageType.VK_IMAGE_TYPE_2D
            : VkImageType.VK_IMAGE_TYPE_3D;
        var imageFormatProperties = default(VkImageFormatProperties);
        var imagePropertiesResult = VulkanApi.vkGetPhysicalDeviceImageFormatProperties(
            _physicalDevice,
            VulkanNativeTypeMapper.GetVkFormat(description.Format),
            imageType,
            VkImageTiling.VK_IMAGE_TILING_OPTIMAL,
            GetNativeImageUsage(description.Usage),
            default,
            &imageFormatProperties);

        if (imagePropertiesResult == VkResult.VK_ERROR_FORMAT_NOT_SUPPORTED)
        {
            throw new NotSupportedException(
                $"Vulkan does not support the requested {description.Dimension} {description.Format} usage combination.");
        }

        ThrowIfFailed(imagePropertiesResult, "vkGetPhysicalDeviceImageFormatProperties");

        if ((description.Width > imageFormatProperties.maxExtent.width)
            || (description.Height > imageFormatProperties.maxExtent.height)
            || (description.Depth > imageFormatProperties.maxExtent.depth)
            || (description.ArrayLayers > imageFormatProperties.maxArrayLayers))
        {
            throw new ArgumentOutOfRangeException(nameof(description), "The texture extent exceeds native format support.");
        }

        if ((imageFormatProperties.sampleCounts & VkSampleCountFlags.VK_SAMPLE_COUNT_1_BIT) == 0)
        {
            throw new NotSupportedException($"Vulkan format {description.Format} does not support single-sample images.");
        }
    }
}
