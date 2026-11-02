// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX.Interop.Vulkan;

namespace TerraFX.Graphics.Vulkan;

internal sealed class VulkanBackendPipeline : IGraphicsBackendPipeline
{
    private readonly VkDescriptorSetLayout[] _descriptorSetLayouts;
    private int _descriptorSetReferenceCount;
    private bool _isDisposed;
    private readonly VulkanBackendDevice _owner;
    private VkPipeline _pipeline;
    private VkPipelineLayout _pipelineLayout;
    private readonly ImmutableArray<ShaderResourceSignature> _resources;

    internal VulkanBackendPipeline(
        VulkanBackendDevice owner,
        VkPipeline pipeline,
        VkPipelineLayout pipelineLayout,
        VkDescriptorSetLayout[] descriptorSetLayouts,
        ImmutableArray<ShaderResourceSignature> resources)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(descriptorSetLayouts);
        _owner = owner;
        _pipeline = pipeline;
        _pipelineLayout = pipelineLayout;
        _descriptorSetLayouts = descriptorSetLayouts;
        _resources = resources;
    }

    internal ReadOnlySpan<VkDescriptorSetLayout> DescriptorSetLayouts => _descriptorSetLayouts;

    internal int DescriptorSetReferenceCount => _descriptorSetReferenceCount;

    internal bool IsDisposed => _isDisposed;

    internal VulkanBackendDevice Owner => _owner;

    internal VkPipeline Pipeline => _pipeline;

    internal VkPipelineLayout PipelineLayout => _pipelineLayout;

    internal ImmutableArray<ShaderResourceSignature> Resources => _resources;

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _owner.DestroyPipeline(this);
        }
    }

    internal void MarkDisposed()
    {
        _isDisposed = true;
        Array.Clear(_descriptorSetLayouts);
        _pipeline = default;
        _pipelineLayout = default;
    }

    internal void ReleaseDescriptorSet()
    {
        if (_descriptorSetReferenceCount <= 0)
        {
            throw new InvalidOperationException("The Vulkan pipeline has no retained descriptor sets.");
        }

        _descriptorSetReferenceCount--;
    }

    internal void RetainDescriptorSet()
        => _descriptorSetReferenceCount = checked(_descriptorSetReferenceCount + 1);
}
