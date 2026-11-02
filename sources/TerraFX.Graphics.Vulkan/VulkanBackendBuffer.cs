// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Graphics;
using TerraFX.Interop.Vulkan;

namespace TerraFX.Graphics.Vulkan;

internal sealed class VulkanBackendBuffer : IGraphicsBackendBuffer
{
    private VkBuffer _buffer;
    private BufferDescription _description;
    private bool _isDisposed;
    private VkDeviceMemory _memory;
    private VkMemoryPropertyFlags _memoryProperties;
    private uint _memoryTypeIndex;
    private readonly VulkanBackendDevice _owner;

    internal VulkanBackendBuffer(
        VulkanBackendDevice owner,
        VkBuffer buffer,
        BufferDescription description,
        VkDeviceMemory memory,
        VkMemoryPropertyFlags memoryProperties,
        uint memoryTypeIndex)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(description);
        _owner = owner;
        _buffer = buffer;
        _description = description;
        _memory = memory;
        _memoryProperties = memoryProperties;
        _memoryTypeIndex = memoryTypeIndex;
    }

    internal VkBuffer Buffer => _buffer;

    internal BufferDescription Description => _description;

    internal bool IsDisposed => _isDisposed;

    internal VkDeviceMemory Memory => _memory;

    internal VkMemoryPropertyFlags MemoryProperties => _memoryProperties;

    internal uint MemoryTypeIndex => _memoryTypeIndex;

    internal VulkanBackendDevice Owner => _owner;

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _owner.DestroyBuffer(this);
        }
    }

    internal void MarkDisposed()
    {
        _buffer = default;
        _description = null!;
        _isDisposed = true;
        _memory = default;
        _memoryProperties = default;
        _memoryTypeIndex = 0;
    }
}
