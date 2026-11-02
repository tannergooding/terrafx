// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using TerraFX.Graphics;
using TerraFX.Interop.Vulkan;
using VulkanApi = TerraFX.Interop.Vulkan.Vulkan;

namespace TerraFX.Graphics.Vulkan;

internal sealed unsafe partial class VulkanBackendDevice
{
    private int _liveDescriptorSetLayoutCount;

    private VkDescriptorSetLayout CreateDescriptorSetLayout(VkDescriptorSetLayoutBinding[] bindings)
    {
        var createInfo = default(VkDescriptorSetLayoutCreateInfo);
        createInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO;
        createInfo.bindingCount = checked((uint)bindings.Length);

        fixed (VkDescriptorSetLayoutBinding* bindingsPointer = bindings)
        {
            createInfo.pBindings = bindingsPointer;
            VkDescriptorSetLayout descriptorSetLayout = default;
            ThrowIfFailed(
                VulkanApi.vkCreateDescriptorSetLayout(_device, &createInfo, null, &descriptorSetLayout),
                "vkCreateDescriptorSetLayout");

            try
            {
                _liveDescriptorSetLayoutCount = checked(_liveDescriptorSetLayoutCount + 1);
                return descriptorSetLayout;
            }
            catch
            {
                VulkanApi.vkDestroyDescriptorSetLayout(_device, descriptorSetLayout, null);
                throw;
            }
        }
    }

    private (VkPipelineLayout PipelineLayout, VkDescriptorSetLayout[] DescriptorSetLayouts) CreatePipelineLayout(
        ImmutableArray<ShaderBytecodeInput> shaderInputs)
    {
        var bindingsBySet = new List<VkDescriptorSetLayoutBinding>?[4];

        for (var shaderIndex = 0; shaderIndex < shaderInputs.Length; shaderIndex++)
        {
            var shader = shaderInputs[shaderIndex];
            var stageFlags = GetNativeShaderStage(shader.Stage);
            var resources = shader.Reflection.Resources;

            for (var resourceIndex = 0; resourceIndex < resources.Length; resourceIndex++)
            {
                var resource = resources[resourceIndex];
                var setIndex = checked((int)GetNativeDescriptorSetIndex(resource.Group));
                var setBindings = bindingsBySet[setIndex] ??= [];
                var nativeBinding = GetNativeDescriptorBinding(resource);
                var descriptorType = GetNativeDescriptorType(resource.Kind);
                var descriptorCount = checked((uint)resource.ArrayCount);
                var existingBindingIndex = -1;

                for (var bindingIndex = 0; bindingIndex < setBindings.Count; bindingIndex++)
                {
                    if (setBindings[bindingIndex].binding == nativeBinding)
                    {
                        existingBindingIndex = bindingIndex;
                        break;
                    }
                }

                if (existingBindingIndex >= 0)
                {
                    var existingBinding = setBindings[existingBindingIndex];

                    if ((existingBinding.descriptorType != descriptorType)
                        || (existingBinding.descriptorCount != descriptorCount))
                    {
                        throw new ArgumentException("Shader stages disagree on a Vulkan descriptor binding.", nameof(shaderInputs));
                    }

                    existingBinding.stageFlags |= stageFlags;
                    setBindings[existingBindingIndex] = existingBinding;
                }
                else
                {
                    var binding = default(VkDescriptorSetLayoutBinding);
                    binding.binding = nativeBinding;
                    binding.descriptorType = descriptorType;
                    binding.descriptorCount = descriptorCount;
                    binding.stageFlags = stageFlags;
                    setBindings.Add(binding);
                }
            }
        }

        var descriptorSetCount = 0;

        for (var setIndex = 0; setIndex < bindingsBySet.Length; setIndex++)
        {
            if (bindingsBySet[setIndex] is { Count: > 0 })
            {
                descriptorSetCount = setIndex + 1;
            }
        }

        var descriptorSetLayouts = new VkDescriptorSetLayout[descriptorSetCount];
        var createdDescriptorSetLayoutCount = 0;
        VkPipelineLayout pipelineLayout = default;
        var ownsPipelineLayout = false;

        try
        {
            for (var setIndex = 0; setIndex < descriptorSetCount; setIndex++)
            {
                var setBindings = bindingsBySet[setIndex];
                VkDescriptorSetLayoutBinding[] nativeBindings = setBindings is null
                    ? []
                    : [.. setBindings];
                Array.Sort(nativeBindings, static (left, right) => left.binding.CompareTo(right.binding));
                descriptorSetLayouts[setIndex] = CreateDescriptorSetLayout(nativeBindings);
                createdDescriptorSetLayoutCount++;
            }

            var createInfo = default(VkPipelineLayoutCreateInfo);
            createInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;
            createInfo.setLayoutCount = checked((uint)descriptorSetLayouts.Length);

            fixed (VkDescriptorSetLayout* descriptorSetLayoutsPointer = descriptorSetLayouts)
            {
                createInfo.pSetLayouts = descriptorSetLayoutsPointer;
                ThrowIfFailed(
                    VulkanApi.vkCreatePipelineLayout(_device, &createInfo, null, &pipelineLayout),
                    "vkCreatePipelineLayout");
                ownsPipelineLayout = true;
            }

            return (pipelineLayout, descriptorSetLayouts);
        }
        catch
        {
            if (ownsPipelineLayout)
            {
                VulkanApi.vkDestroyPipelineLayout(_device, pipelineLayout, null);
            }

            DestroyDescriptorSetLayouts(
                new ReadOnlySpan<VkDescriptorSetLayout>(descriptorSetLayouts, 0, createdDescriptorSetLayoutCount));

            throw;
        }
    }

    private void DestroyDescriptorSetLayouts(ReadOnlySpan<VkDescriptorSetLayout> descriptorSetLayouts)
    {
        for (var index = descriptorSetLayouts.Length - 1; index >= 0; index--)
        {
            VulkanApi.vkDestroyDescriptorSetLayout(_device, descriptorSetLayouts[index], null);
            _liveDescriptorSetLayoutCount = checked(_liveDescriptorSetLayoutCount - 1);
        }
    }
}
