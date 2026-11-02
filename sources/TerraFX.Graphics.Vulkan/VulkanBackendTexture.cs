// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Graphics;
using TerraFX.Interop.Vulkan;

namespace TerraFX.Graphics.Vulkan;

internal sealed class VulkanBackendTexture : IGraphicsBackendTexture
{
    private TextureDescription _description;
    private int _descriptorSetReferenceCount;
    private VkImage _image;
    private bool[] _initialized;
    private bool _isDisposed;
    private VkImageLayout[] _layouts;
    private VkDeviceMemory _memory;
    private uint _memoryTypeIndex;
    private readonly VulkanBackendDevice _owner;

    internal VulkanBackendTexture(
        VulkanBackendDevice owner,
        VkImage image,
        TextureDescription description,
        VkDeviceMemory memory,
        uint memoryTypeIndex)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(description);
        _owner = owner;
        _image = image;
        _description = description;
        _initialized = new bool[checked(description.MipLevels * description.ArrayLayers)];
        _memory = memory;
        _memoryTypeIndex = memoryTypeIndex;
        _layouts = new VkImageLayout[_initialized.Length];
        Array.Fill(_layouts, VkImageLayout.VK_IMAGE_LAYOUT_UNDEFINED);
    }

    internal TextureDescription Description => _description;

    internal int DescriptorSetReferenceCount => _descriptorSetReferenceCount;

    internal VkImage Image => _image;

    internal bool IsDisposed => _isDisposed;

    internal VkDeviceMemory Memory => _memory;

    internal uint MemoryTypeIndex => _memoryTypeIndex;

    internal VulkanBackendDevice Owner => _owner;

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _owner.DestroyTexture(this);
        }
    }

    internal VkImageLayout GetLayout(TextureSubresource subresource)
        => _layouts[GetSubresourceIndex(subresource)];

    internal bool IsInitialized(TextureSubresource subresource)
        => _initialized[GetSubresourceIndex(subresource)];

    internal void MarkDisposed()
    {
        _description = null!;
        _image = default;
        _isDisposed = true;
        _descriptorSetReferenceCount = 0;
        _initialized = [];
        _layouts = [];
        _memory = default;
        _memoryTypeIndex = 0;
    }

    internal void MarkInitialized(TextureSubresource subresource)
        => _initialized[GetSubresourceIndex(subresource)] = true;

    internal void ReleaseDescriptorSet()
    {
        if (_descriptorSetReferenceCount <= 0)
        {
            throw new InvalidOperationException("The Vulkan texture has no retained descriptor sets.");
        }

        _descriptorSetReferenceCount--;
    }

    internal void RetainDescriptorSet()
        => _descriptorSetReferenceCount = checked(_descriptorSetReferenceCount + 1);

    internal void SetLayout(TextureSubresource subresource, VkImageLayout layout)
        => _layouts[GetSubresourceIndex(subresource)] = layout;

    private int GetSubresourceIndex(TextureSubresource subresource)
    {
        _ = GraphicsDevice.GetTextureSubresourceExtent(_description, subresource);
        return checked((subresource.ArrayLayer * _description.MipLevels) + subresource.MipLevel);
    }
}
