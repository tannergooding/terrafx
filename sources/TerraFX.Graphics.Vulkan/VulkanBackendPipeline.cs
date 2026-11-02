// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Interop.Vulkan;

namespace TerraFX.Graphics.Vulkan;

internal sealed class VulkanBackendPipeline : IGraphicsBackendPipeline
{
    private readonly VkDescriptorSetLayout[] _descriptorSetLayouts;
    private bool _isDisposed;
    private readonly VulkanBackendDevice _owner;
    private VkPipeline _pipeline;
    private VkPipelineLayout _pipelineLayout;

    internal VulkanBackendPipeline(
        VulkanBackendDevice owner,
        VkPipeline pipeline,
        VkPipelineLayout pipelineLayout,
        VkDescriptorSetLayout[] descriptorSetLayouts)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(descriptorSetLayouts);
        _owner = owner;
        _pipeline = pipeline;
        _pipelineLayout = pipelineLayout;
        _descriptorSetLayouts = descriptorSetLayouts;
    }

    internal ReadOnlySpan<VkDescriptorSetLayout> DescriptorSetLayouts => _descriptorSetLayouts;

    internal bool IsDisposed => _isDisposed;

    internal VulkanBackendDevice Owner => _owner;

    internal VkPipeline Pipeline => _pipeline;

    internal VkPipelineLayout PipelineLayout => _pipelineLayout;

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
}
