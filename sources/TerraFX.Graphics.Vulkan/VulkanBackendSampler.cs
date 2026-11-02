// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Interop.Vulkan;

namespace TerraFX.Graphics.Vulkan;

internal sealed class VulkanBackendSampler : IGraphicsBackendSampler
{
    private int _descriptorSetReferenceCount;
    private bool _isDisposed;
    private readonly VulkanBackendDevice _owner;
    private VkSampler _sampler;

    internal VulkanBackendSampler(VulkanBackendDevice owner, VkSampler sampler)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _owner = owner;
        _sampler = sampler;
    }

    internal int DescriptorSetReferenceCount => _descriptorSetReferenceCount;

    internal bool IsDisposed => _isDisposed;

    internal VulkanBackendDevice Owner => _owner;

    internal VkSampler Sampler => _sampler;

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _owner.DestroySampler(this);
        }
    }

    internal void MarkDisposed()
    {
        _isDisposed = true;
        _descriptorSetReferenceCount = 0;
        _sampler = default;
    }

    internal void ReleaseDescriptorSet()
    {
        if (_descriptorSetReferenceCount <= 0)
        {
            throw new InvalidOperationException("The Vulkan sampler has no retained descriptor sets.");
        }

        _descriptorSetReferenceCount--;
    }

    internal void RetainDescriptorSet()
        => _descriptorSetReferenceCount = checked(_descriptorSetReferenceCount + 1);
}
