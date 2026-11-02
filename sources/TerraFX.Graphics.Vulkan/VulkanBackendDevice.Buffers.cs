// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Graphics;
using TerraFX.Interop.Vulkan;
using VulkanApi = TerraFX.Interop.Vulkan.Vulkan;

namespace TerraFX.Graphics.Vulkan;

internal sealed unsafe partial class VulkanBackendDevice
{
    private int _liveBufferCount;

    private static VkBuffer CreateNativeBuffer(VkDevice device, BufferDescription description)
    {
        var createInfo = default(VkBufferCreateInfo);
        createInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
        createInfo.size = checked((ulong)description.ByteLength);
        createInfo.usage = GetNativeBufferUsage(description.Usage);
        createInfo.sharingMode = VkSharingMode.VK_SHARING_MODE_EXCLUSIVE;
        VkBuffer buffer = default;
        ThrowIfFailed(VulkanApi.vkCreateBuffer(device, &createInfo, null, &buffer), "vkCreateBuffer");
        return buffer;
    }

    private static void GetMemoryTypeIndex(
        VkPhysicalDeviceMemoryProperties memoryProperties,
        uint compatibleMemoryTypeBits,
        GpuMemoryKind memoryKind,
        out uint selectedMemoryTypeIndex,
        out VkMemoryPropertyFlags selectedProperties,
        out int heapIndex)
    {
        var requiredProperties = memoryKind switch {
            GpuMemoryKind.DeviceLocal => VkMemoryPropertyFlags.VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT,
            GpuMemoryKind.Upload or GpuMemoryKind.Readback => VkMemoryPropertyFlags.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT,
            _ => throw new ArgumentOutOfRangeException(nameof(memoryKind)),
        };
        var preferredProperties = memoryKind switch {
            GpuMemoryKind.DeviceLocal => VkMemoryPropertyFlags.VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT,
            GpuMemoryKind.Upload => VkMemoryPropertyFlags.VK_MEMORY_PROPERTY_HOST_COHERENT_BIT,
            GpuMemoryKind.Readback => VkMemoryPropertyFlags.VK_MEMORY_PROPERTY_HOST_CACHED_BIT
                | VkMemoryPropertyFlags.VK_MEMORY_PROPERTY_HOST_COHERENT_BIT,
            _ => throw new ArgumentOutOfRangeException(nameof(memoryKind)),
        };
        var memoryTypePointer = (VkMemoryType*)((byte*)&memoryProperties
            + checked((int)System.Runtime.InteropServices.Marshal.OffsetOf<VkPhysicalDeviceMemoryProperties>("memoryTypes")));
        var fallbackIndex = uint.MaxValue;
        VkMemoryPropertyFlags fallbackProperties = default;
        uint fallbackHeapIndex = 0;

        for (uint index = 0; index < memoryProperties.memoryTypeCount; index++)
        {
            if ((compatibleMemoryTypeBits & (1u << checked((int)index))) == 0)
            {
                continue;
            }

            var memoryType = memoryTypePointer[index];
            var properties = memoryType.propertyFlags;

            if ((properties & requiredProperties) != requiredProperties)
            {
                continue;
            }

            if (fallbackIndex == uint.MaxValue)
            {
                fallbackIndex = index;
                fallbackProperties = properties;
                fallbackHeapIndex = memoryType.heapIndex;
            }

            if ((properties & preferredProperties) == preferredProperties)
            {
                selectedMemoryTypeIndex = index;
                selectedProperties = properties;
                heapIndex = checked((int)memoryType.heapIndex);
                return;
            }
        }

        if (fallbackIndex != uint.MaxValue)
        {
            selectedMemoryTypeIndex = fallbackIndex;
            selectedProperties = fallbackProperties;
            heapIndex = checked((int)fallbackHeapIndex);
            return;
        }

        throw new NotSupportedException($"No Vulkan memory type supports {memoryKind} buffer storage.");
    }

    private static VkBufferUsageFlags GetNativeBufferUsage(BufferUsage usage)
    {
        var nativeUsage = default(VkBufferUsageFlags);

        if ((usage & BufferUsage.Vertex) != 0)
        {
            nativeUsage |= VkBufferUsageFlags.VK_BUFFER_USAGE_VERTEX_BUFFER_BIT;
        }

        if ((usage & BufferUsage.Index) != 0)
        {
            nativeUsage |= VkBufferUsageFlags.VK_BUFFER_USAGE_INDEX_BUFFER_BIT;
        }

        if ((usage & BufferUsage.Uniform) != 0)
        {
            nativeUsage |= VkBufferUsageFlags.VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT;
        }

        if ((usage & BufferUsage.Storage) != 0)
        {
            nativeUsage |= VkBufferUsageFlags.VK_BUFFER_USAGE_STORAGE_BUFFER_BIT;
        }

        if ((usage & BufferUsage.CopySource) != 0)
        {
            nativeUsage |= VkBufferUsageFlags.VK_BUFFER_USAGE_TRANSFER_SRC_BIT;
        }

        if ((usage & BufferUsage.CopyDestination) != 0)
        {
            nativeUsage |= VkBufferUsageFlags.VK_BUFFER_USAGE_TRANSFER_DST_BIT;
        }

        return nativeUsage;
    }

    internal VulkanBackendBuffer CreateBuffer(BufferDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        EnsureActive();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            description.ByteLength,
            Capabilities.Limits.MaximumBufferByteLength,
            nameof(description));

        var buffer = CreateNativeBuffer(_device, description);
        var ownsBuffer = true;
        var memory = default(VkDeviceMemory);
        var ownsMemory = false;

        try
        {
            var requirements = default(VkMemoryRequirements);
            VulkanApi.vkGetBufferMemoryRequirements(_device, buffer, &requirements);
            GetMemoryTypeIndex(
                _physicalDeviceMemoryProperties,
                requirements.memoryTypeBits,
                description.MemoryKind,
                out var memoryTypeIndex,
                out var memoryProperties,
                out _);
            var allocateInfo = default(VkMemoryAllocateInfo);
            allocateInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
            allocateInfo.allocationSize = requirements.size;
            allocateInfo.memoryTypeIndex = memoryTypeIndex;
            ThrowIfFailed(VulkanApi.vkAllocateMemory(_device, &allocateInfo, null, &memory), "vkAllocateMemory");
            ownsMemory = true;
            ThrowIfFailed(VulkanApi.vkBindBufferMemory(_device, buffer, memory, 0), "vkBindBufferMemory");

            var result = new VulkanBackendBuffer(
                this,
                buffer,
                description,
                memory,
                memoryProperties,
                memoryTypeIndex);
            _liveBufferCount = checked(_liveBufferCount + 1);
            ownsBuffer = false;
            ownsMemory = false;
            return result;
        }
        finally
        {
            if (ownsMemory)
            {
                VulkanApi.vkFreeMemory(_device, memory, null);
            }

            if (ownsBuffer)
            {
                VulkanApi.vkDestroyBuffer(_device, buffer, null);
            }
        }
    }

    internal void DestroyBuffer(VulkanBackendBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        EnsureOwnerThread();

        if (!ReferenceEquals(buffer.Owner, this))
        {
            throw new ArgumentException("The buffer belongs to a different Vulkan device.", nameof(buffer));
        }

        if (buffer.IsDisposed)
        {
            return;
        }

        if (_hasUncertainSubmission)
        {
            throw new InvalidOperationException("Vulkan buffer retirement cannot release storage while submission completion is uncertain.");
        }

        ObjectDisposedException.ThrowIf(_isDisposed, this);
        VulkanApi.vkDestroyBuffer(_device, buffer.Buffer, null);
        VulkanApi.vkFreeMemory(_device, buffer.Memory, null);
        buffer.MarkDisposed();
        _liveBufferCount = checked(_liveBufferCount - 1);
    }

    internal GraphicsResourceAllocationInfo GetBufferAllocationInfo(BufferDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        EnsureActive();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            description.ByteLength,
            Capabilities.Limits.MaximumBufferByteLength,
            nameof(description));

        var buffer = CreateNativeBuffer(_device, description);

        try
        {
            var requirements = default(VkMemoryRequirements);
            VulkanApi.vkGetBufferMemoryRequirements(_device, buffer, &requirements);
            GetMemoryTypeIndex(
                _physicalDeviceMemoryProperties,
                requirements.memoryTypeBits,
                description.MemoryKind,
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
            VulkanApi.vkDestroyBuffer(_device, buffer, null);
        }
    }

    private void EnsureActive()
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_hasUncertainSubmission)
        {
            throw new InvalidOperationException("Vulkan device operations are blocked while submission completion is uncertain.");
        }
    }
}
