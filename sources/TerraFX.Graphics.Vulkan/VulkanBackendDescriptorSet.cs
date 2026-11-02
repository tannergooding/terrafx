// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Interop.Vulkan;

namespace TerraFX.Graphics.Vulkan;

internal sealed class VulkanBackendDescriptorSet : IDisposable
{
    private readonly VulkanBackendBuffer[] _buffers;
    private VkDescriptorPool _descriptorPool;
    private VkDescriptorSet _descriptorSet;
    private readonly VkImageView[] _imageViews;
    private bool _isDisposed;
    private readonly VulkanBackendDevice _owner;
    private readonly VulkanBackendPipeline _pipeline;
    private readonly VulkanBackendSampler[] _samplers;
    private readonly VulkanBackendTexture[] _textures;

    internal VulkanBackendDescriptorSet(
        VulkanBackendDevice owner,
        VulkanBackendPipeline pipeline,
        VkDescriptorPool descriptorPool,
        VkDescriptorSet descriptorSet,
        VulkanBackendBuffer[] buffers,
        VulkanBackendTexture[] textures,
        VulkanBackendSampler[] samplers,
        VkImageView[] imageViews)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(buffers);
        ArgumentNullException.ThrowIfNull(textures);
        ArgumentNullException.ThrowIfNull(samplers);
        ArgumentNullException.ThrowIfNull(imageViews);
        _owner = owner;
        _pipeline = pipeline;
        _descriptorPool = descriptorPool;
        _descriptorSet = descriptorSet;
        _buffers = buffers;
        _textures = textures;
        _samplers = samplers;
        _imageViews = imageViews;
    }

    internal ReadOnlySpan<VulkanBackendBuffer> Buffers => _buffers;

    internal VkDescriptorPool DescriptorPool => _descriptorPool;

    internal VkDescriptorSet DescriptorSet => _descriptorSet;

    internal ReadOnlySpan<VkImageView> ImageViews => _imageViews;

    internal bool IsDisposed => _isDisposed;

    internal VulkanBackendDevice Owner => _owner;

    internal VulkanBackendPipeline Pipeline => _pipeline;

    internal ReadOnlySpan<VulkanBackendSampler> Samplers => _samplers;

    internal ReadOnlySpan<VulkanBackendTexture> Textures => _textures;

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _owner.DestroyDescriptorSet(this);
        }
    }

    internal void MarkDisposed()
    {
        Array.Clear(_buffers);
        Array.Clear(_textures);
        Array.Clear(_samplers);
        Array.Clear(_imageViews);
        _descriptorPool = default;
        _descriptorSet = default;
        _isDisposed = true;
    }
}
