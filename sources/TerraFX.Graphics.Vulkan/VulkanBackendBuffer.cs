// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Graphics;
using TerraFX.Interop.Vulkan;

namespace TerraFX.Graphics.Vulkan;

internal sealed class VulkanBackendBuffer : IGraphicsBackendBuffer
{
    private long _allocationSizeBytes;
    private VkBuffer _buffer;
    private BufferDescription _description;
    private int _descriptorSetReferenceCount;
    private bool _isDisposed;
    private bool _isStaging;
    private VkAccessFlags2 _lastAccessMask;
    private VkPipelineStageFlags2 _lastStageMask;
    private VkDeviceMemory _memory;
    private VkMemoryPropertyFlags _memoryProperties;
    private uint _memoryTypeIndex;
    private readonly VulkanBackendDevice _owner;
    private bool _ownsMemory;

    internal VulkanBackendBuffer(
        VulkanBackendDevice owner,
        VkBuffer buffer,
        BufferDescription description,
        VkDeviceMemory memory,
        VkMemoryPropertyFlags memoryProperties,
        uint memoryTypeIndex,
        long allocationSizeBytes,
        bool isStaging,
        bool ownsMemory = true)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(description);
        _owner = owner;
        _buffer = buffer;
        _description = description;
        _memory = memory;
        _memoryProperties = memoryProperties;
        _memoryTypeIndex = memoryTypeIndex;
        _allocationSizeBytes = allocationSizeBytes;
        _isStaging = isStaging;
        _ownsMemory = ownsMemory;
        _lastAccessMask = VkAccessFlags2.VK_ACCESS_2_NONE;
        _lastStageMask = VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_NONE;
    }

    internal long AllocationSizeBytes => _allocationSizeBytes;

    internal VkBuffer Buffer => _buffer;

    internal BufferDescription Description => _description;

    internal int DescriptorSetReferenceCount => _descriptorSetReferenceCount;

    internal bool IsDisposed => _isDisposed;

    internal bool IsStaging => _isStaging;

    internal VkAccessFlags2 LastAccessMask => _lastAccessMask;

    internal VkPipelineStageFlags2 LastStageMask => _lastStageMask;

    internal VkDeviceMemory Memory => _memory;

    internal VkMemoryPropertyFlags MemoryProperties => _memoryProperties;

    internal uint MemoryTypeIndex => _memoryTypeIndex;

    internal VulkanBackendDevice Owner => _owner;

    internal bool OwnsMemory => _ownsMemory;

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _owner.DestroyBuffer(this);
        }
    }

    internal void MarkDisposed()
    {
        _allocationSizeBytes = 0;
        _buffer = default;
        _description = null!;
        _isDisposed = true;
        _isStaging = false;
        _ownsMemory = false;
        _lastAccessMask = VkAccessFlags2.VK_ACCESS_2_NONE;
        _lastStageMask = VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_NONE;
        _memory = default;
        _memoryProperties = default;
        _memoryTypeIndex = 0;
    }

    internal void ReleaseDescriptorSet()
    {
        if (_descriptorSetReferenceCount <= 0)
        {
            throw new InvalidOperationException("The Vulkan buffer has no retained descriptor sets.");
        }

        _descriptorSetReferenceCount--;
    }

    internal void RetainDescriptorSet()
        => _descriptorSetReferenceCount = checked(_descriptorSetReferenceCount + 1);

    internal void SetLastAccessScope(VkPipelineStageFlags2 stageMask, VkAccessFlags2 accessMask)
    {
        // A buffer-wide scope union keeps range-specific barriers from omitting earlier accesses on other partitions.
        _lastStageMask |= stageMask;
        _lastAccessMask |= accessMask;
    }
}
