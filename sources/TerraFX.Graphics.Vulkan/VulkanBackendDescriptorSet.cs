// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Interop.Vulkan;

namespace TerraFX.Graphics.Vulkan;

internal sealed class VulkanBackendDescriptorSet : IDisposable
{
    private readonly VulkanBackendBuffer[] _buffers;
    private VkDescriptorPool _descriptorPool;
    private VkDescriptorSet _descriptorSet;
    private bool _isDisposed;
    private readonly VulkanBackendDevice _owner;
    private readonly VulkanBackendPipeline _pipeline;

    internal VulkanBackendDescriptorSet(
        VulkanBackendDevice owner,
        VulkanBackendPipeline pipeline,
        VkDescriptorPool descriptorPool,
        VkDescriptorSet descriptorSet,
        VulkanBackendBuffer[] buffers)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(buffers);
        _owner = owner;
        _pipeline = pipeline;
        _descriptorPool = descriptorPool;
        _descriptorSet = descriptorSet;
        _buffers = buffers;
    }

    internal ReadOnlySpan<VulkanBackendBuffer> Buffers => _buffers;

    internal VkDescriptorPool DescriptorPool => _descriptorPool;

    internal VkDescriptorSet DescriptorSet => _descriptorSet;

    internal bool IsDisposed => _isDisposed;

    internal VulkanBackendDevice Owner => _owner;

    internal VulkanBackendPipeline Pipeline => _pipeline;

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
        _descriptorPool = default;
        _descriptorSet = default;
        _isDisposed = true;
    }
}
