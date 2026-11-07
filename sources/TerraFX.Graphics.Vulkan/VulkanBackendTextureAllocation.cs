// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Graphics;
using TerraFX.Interop.Vulkan;

namespace TerraFX.Graphics.Vulkan;

internal sealed class VulkanBackendTextureAllocation : IGraphicsBackendTextureAllocation
{
    private readonly long _allocationSizeBytes;
    private bool _isDisposed;
    private VkDeviceMemory _memory;
    private readonly VkMemoryPropertyFlags _memoryProperties;
    private uint _memoryTypeIndex;
    private readonly VulkanBackendDevice _owner;

    internal VulkanBackendTextureAllocation(
        VulkanBackendDevice owner,
        VkDeviceMemory memory,
        VkMemoryPropertyFlags memoryProperties,
        uint memoryTypeIndex,
        long allocationSizeBytes)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(allocationSizeBytes);
        _owner = owner;
        _memory = memory;
        _memoryProperties = memoryProperties;
        _memoryTypeIndex = memoryTypeIndex;
        _allocationSizeBytes = allocationSizeBytes;
    }

    internal long AllocationSizeBytes => _allocationSizeBytes;

    internal bool IsDisposed => _isDisposed;

    internal VkDeviceMemory Memory => _memory;

    internal VkMemoryPropertyFlags MemoryProperties => _memoryProperties;

    internal uint MemoryTypeIndex => _memoryTypeIndex;

    internal VulkanBackendDevice Owner => _owner;

    public void Dispose()
        => _owner.DestroyTextureAllocation(this);

    internal void MarkDisposed()
    {
        _memory = default;
        _memoryTypeIndex = 0;
        _isDisposed = true;
    }
}
