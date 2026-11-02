// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;
using TerraFX.Graphics;
using TerraFX.Interop.Vulkan;
using VulkanApi = TerraFX.Interop.Vulkan.Vulkan;

namespace TerraFX.Graphics.Vulkan;

internal sealed unsafe partial class VulkanBackendDevice
{
    private int _liveDescriptorSetCount;

    internal VulkanBackendDescriptorSet CreateUniformBufferDescriptorSet(
        VulkanBackendPipeline pipeline,
        ImmutableArray<VulkanBackendUniformBufferBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        EnsureActive();

        if (!ReferenceEquals(pipeline.Owner, this) || pipeline.IsDisposed)
        {
            throw new ArgumentException("The pipeline is not live on this Vulkan device.", nameof(pipeline));
        }

        if (bindings.IsDefault || bindings.IsEmpty)
        {
            throw new ArgumentException("Uniform-buffer descriptor bindings must be initialized and nonempty.", nameof(bindings));
        }

        var resources = pipeline.Resources;
        var setIndex = -1;
        var expectedBindingCount = 0;

        for (var index = 0; index < resources.Length; index++)
        {
            var resource = resources[index];

            if (resource.Group != ShaderBindingGroup.Frame)
            {
                continue;
            }

            if (resource.Kind != ShaderResourceKind.UniformBuffer)
            {
                throw new NotSupportedException("This descriptor-set path supports only Frame-group uniform buffers.");
            }

            if (resource.ArrayCount != 1)
            {
                throw new NotSupportedException("Uniform-buffer descriptor arrays are not supported.");
            }

            setIndex = checked((int)GetNativeDescriptorSetIndex(resource.Group));
            expectedBindingCount++;
        }

        if ((setIndex < 0)
            || (setIndex >= pipeline.DescriptorSetLayouts.Length)
            || (expectedBindingCount != bindings.Length))
        {
            throw new ArgumentException("The pipeline does not have a complete Frame-group uniform-buffer set.", nameof(bindings));
        }

        var sortedBindings = bindings.ToArray();
        Array.Sort(sortedBindings, static (left, right) => left.Binding.CompareTo(right.Binding));
        var nativeBindings = new uint[sortedBindings.Length];
        var retainedBuffers = new VulkanBackendBuffer[sortedBindings.Length];
        var bufferInfos = new VkDescriptorBufferInfo[sortedBindings.Length];

        for (var index = 0; index < sortedBindings.Length; index++)
        {
            var binding = sortedBindings[index];
            ArgumentNullException.ThrowIfNull(binding.Buffer);

            if ((binding.Group != ShaderBindingGroup.Frame)
                || (binding.Binding < 0)
                || binding.Buffer.IsDisposed
                || !ReferenceEquals(binding.Buffer.Owner, this))
            {
                throw new ArgumentException("A uniform-buffer descriptor binding has an invalid group, binding, or buffer owner.", nameof(bindings));
            }

            var expectedResourceFound = false;

            for (var resourceIndex = 0; resourceIndex < resources.Length; resourceIndex++)
            {
                var resource = resources[resourceIndex];

                if ((resource.Group == binding.Group)
                    && (resource.Binding == binding.Binding)
                    && (resource.Kind == ShaderResourceKind.UniformBuffer)
                    && (resource.ArrayCount == 1))
                {
                    expectedResourceFound = true;
                    break;
                }
            }

            if (!expectedResourceFound
                || ((index != 0) && (sortedBindings[index - 1].Binding == binding.Binding)))
            {
                throw new ArgumentException("Uniform-buffer descriptors must match the pipeline's unique reflected bindings.", nameof(bindings));
            }

            if ((binding.Buffer.Description.Usage & BufferUsage.Uniform) == 0)
            {
                throw new ArgumentException("A bound buffer does not declare uniform-buffer usage.", nameof(bindings));
            }

            ArgumentOutOfRangeException.ThrowIfNegative(binding.Offset);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(binding.Range);

            var rangeEnd = checked(binding.Offset + binding.Range);

            if (rangeEnd > binding.Buffer.Description.ByteLength)
            {
                throw new ArgumentException("The uniform-buffer descriptor range exceeds its buffer.", nameof(bindings));
            }

            if ((binding.Offset % Capabilities.Limits.UniformBufferOffsetAlignment) != 0)
            {
                throw new ArgumentException("The uniform-buffer descriptor offset does not satisfy device alignment.", nameof(bindings));
            }

            ArgumentOutOfRangeException.ThrowIfGreaterThan(
                binding.Range,
                Capabilities.Limits.MaximumUniformBufferRangeBytes,
                nameof(bindings));

            nativeBindings[index] = GetNativeDescriptorBinding(
                new ShaderResourceSignature(
                    binding.Group,
                    binding.Binding,
                    ShaderResourceKind.UniformBuffer,
                    arrayCount: 1));
            retainedBuffers[index] = binding.Buffer;
            bufferInfos[index].buffer = binding.Buffer.Buffer;
            bufferInfos[index].offset = checked((ulong)binding.Offset);
            bufferInfos[index].range = checked((ulong)binding.Range);
        }

        var poolSize = default(VkDescriptorPoolSize);
        poolSize.type = VkDescriptorType.VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;
        poolSize.descriptorCount = checked((uint)sortedBindings.Length);
        var poolCreateInfo = default(VkDescriptorPoolCreateInfo);
        poolCreateInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO;
        poolCreateInfo.maxSets = 1;
        poolCreateInfo.poolSizeCount = 1;
        poolCreateInfo.pPoolSizes = &poolSize;
        VkDescriptorPool descriptorPool = default;
        ThrowIfFailed(
            VulkanApi.vkCreateDescriptorPool(_device, &poolCreateInfo, null, &descriptorPool),
            "vkCreateDescriptorPool");
        var ownsDescriptorPool = true;

        try
        {
            var setLayout = pipeline.DescriptorSetLayouts[setIndex];
            var allocateInfo = default(VkDescriptorSetAllocateInfo);
            allocateInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO;
            allocateInfo.descriptorPool = descriptorPool;
            allocateInfo.descriptorSetCount = 1;
            allocateInfo.pSetLayouts = &setLayout;
            VkDescriptorSet descriptorSet = default;
            ThrowIfFailed(
                VulkanApi.vkAllocateDescriptorSets(_device, &allocateInfo, &descriptorSet),
                "vkAllocateDescriptorSets");

            var writes = new VkWriteDescriptorSet[sortedBindings.Length];

            for (var index = 0; index < writes.Length; index++)
            {
                var write = default(VkWriteDescriptorSet);
                write.sType = VkStructureType.VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
                write.dstSet = descriptorSet;
                write.dstBinding = nativeBindings[index];
                write.descriptorCount = 1;
                write.descriptorType = VkDescriptorType.VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;
                writes[index] = write;
            }

            fixed (VkDescriptorBufferInfo* bufferInfosPointer = bufferInfos)
            fixed (VkWriteDescriptorSet* writesPointer = writes)
            {
                for (var index = 0; index < writes.Length; index++)
                {
                    writesPointer[index].pBufferInfo = bufferInfosPointer + index;
                }

                VulkanApi.vkUpdateDescriptorSets(
                    _device,
                    checked((uint)writes.Length),
                    writesPointer,
                    0,
                    null);
            }

            var result = new VulkanBackendDescriptorSet(
                this,
                pipeline,
                descriptorPool,
                descriptorSet,
                retainedBuffers);
            pipeline.RetainDescriptorSet();
            var retainedBufferCount = 0;

            try
            {
                for (; retainedBufferCount < retainedBuffers.Length; retainedBufferCount++)
                {
                    retainedBuffers[retainedBufferCount].RetainDescriptorSet();
                }

                _liveDescriptorSetCount = checked(_liveDescriptorSetCount + 1);
            }
            catch
            {
                for (var index = retainedBufferCount - 1; index >= 0; index--)
                {
                    retainedBuffers[index].ReleaseDescriptorSet();
                }

                pipeline.ReleaseDescriptorSet();
                throw;
            }

            ownsDescriptorPool = false;
            return result;
        }
        finally
        {
            if (ownsDescriptorPool)
            {
                VulkanApi.vkDestroyDescriptorPool(_device, descriptorPool, null);
            }
        }
    }

    internal void DestroyDescriptorSet(VulkanBackendDescriptorSet descriptorSet)
    {
        ArgumentNullException.ThrowIfNull(descriptorSet);
        EnsureOwnerThread();

        if (!ReferenceEquals(descriptorSet.Owner, this))
        {
            throw new ArgumentException("The descriptor set belongs to a different Vulkan device.", nameof(descriptorSet));
        }

        if (descriptorSet.IsDisposed)
        {
            return;
        }

        if (_hasUncertainSubmission)
        {
            throw new InvalidOperationException("Vulkan descriptor-set retirement cannot release storage while submission completion is uncertain.");
        }

        ObjectDisposedException.ThrowIf(_isDisposed, this);
        VulkanApi.vkDestroyDescriptorPool(_device, descriptorSet.DescriptorPool, null);

        foreach (var buffer in descriptorSet.Buffers)
        {
            buffer.ReleaseDescriptorSet();
        }

        descriptorSet.Pipeline.ReleaseDescriptorSet();
        descriptorSet.MarkDisposed();
        _liveDescriptorSetCount = checked(_liveDescriptorSetCount - 1);
    }
}
