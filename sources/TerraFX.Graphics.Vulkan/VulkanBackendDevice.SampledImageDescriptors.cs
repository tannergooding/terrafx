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
    internal VulkanBackendDescriptorSet CreateFrameDescriptorSet(
        VulkanBackendPipeline pipeline,
        ImmutableArray<VulkanBackendUniformBufferBinding> uniformBufferBindings,
        ImmutableArray<VulkanBackendSampledImageBinding> imageBindings,
        ImmutableArray<VulkanBackendSamplerBinding> samplerBindings)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        EnsureActive();

        if (!ReferenceEquals(pipeline.Owner, this) || pipeline.IsDisposed)
        {
            throw new ArgumentException("The pipeline is not live on this Vulkan device.", nameof(pipeline));
        }

        if (uniformBufferBindings.IsDefault || imageBindings.IsDefault || samplerBindings.IsDefault)
        {
            throw new ArgumentException("Frame descriptor bindings must be initialized.", nameof(uniformBufferBindings));
        }

        var resources = pipeline.Resources;
        var setIndex = -1;
        var expectedUniformBufferCount = 0;
        var expectedImageCount = 0;
        var expectedSamplerCount = 0;

        for (var index = 0; index < resources.Length; index++)
        {
            var resource = resources[index];

            if (resource.Group != ShaderBindingGroup.Frame)
            {
                continue;
            }

            if (resource.ArrayCount != 1)
            {
                throw new NotSupportedException("Frame descriptor arrays are not supported.");
            }

            switch (resource.Kind)
            {
                case ShaderResourceKind.UniformBuffer:
                {
                    expectedUniformBufferCount++;
                    break;
                }

                case ShaderResourceKind.SampledImage:
                {
                    expectedImageCount++;
                    break;
                }

                case ShaderResourceKind.Sampler:
                {
                    expectedSamplerCount++;
                    break;
                }

                default:
                {
                    throw new NotSupportedException("This descriptor-set path supports only Frame-group uniform buffers, sampled images, and samplers.");
                }
            }

            setIndex = checked((int)GetNativeDescriptorSetIndex(resource.Group));
        }

        if ((setIndex < 0)
            || (setIndex >= pipeline.DescriptorSetLayouts.Length)
            || (expectedUniformBufferCount != uniformBufferBindings.Length)
            || (expectedImageCount != imageBindings.Length)
            || (expectedSamplerCount != samplerBindings.Length)
            || ((expectedUniformBufferCount == 0) && (expectedImageCount == 0) && (expectedSamplerCount == 0)))
        {
            throw new ArgumentException("The pipeline does not have a complete Frame-group descriptor set.");
        }

        var sortedUniformBufferBindings = uniformBufferBindings.ToArray();
        Array.Sort(sortedUniformBufferBindings, static (left, right) => left.Binding.CompareTo(right.Binding));
        var retainedBuffers = new VulkanBackendBuffer[sortedUniformBufferBindings.Length];
        var bufferInfos = new VkDescriptorBufferInfo[sortedUniformBufferBindings.Length];
        var bufferWrites = new VkWriteDescriptorSet[sortedUniformBufferBindings.Length];

        for (var index = 0; index < sortedUniformBufferBindings.Length; index++)
        {
            var binding = sortedUniformBufferBindings[index];
            ArgumentNullException.ThrowIfNull(binding.Buffer);

            if ((binding.Group != ShaderBindingGroup.Frame)
                || (binding.Binding < 0)
                || binding.Buffer.IsDisposed
                || !ReferenceEquals(binding.Buffer.Owner, this))
            {
                throw new ArgumentException("A uniform-buffer descriptor binding has an invalid group, binding, or buffer owner.", nameof(uniformBufferBindings));
            }

            if (!MatchesReflectedResource(
                    resources,
                    binding.Group,
                    binding.Binding,
                    ShaderResourceKind.UniformBuffer)
                || ((index != 0) && (sortedUniformBufferBindings[index - 1].Binding == binding.Binding)))
            {
                throw new ArgumentException("Uniform-buffer descriptors must match the pipeline's unique reflected bindings.", nameof(uniformBufferBindings));
            }

            if ((binding.Buffer.Description.Usage & BufferUsage.Uniform) == 0)
            {
                throw new ArgumentException("A bound buffer does not declare uniform-buffer usage.", nameof(uniformBufferBindings));
            }

            ArgumentOutOfRangeException.ThrowIfNegative(binding.Offset);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(binding.Range);

            var rangeEnd = checked(binding.Offset + binding.Range);

            if (rangeEnd > binding.Buffer.Description.ByteLength)
            {
                throw new ArgumentException("The uniform-buffer descriptor range exceeds its buffer.", nameof(uniformBufferBindings));
            }

            if ((binding.Offset % Capabilities.Limits.UniformBufferOffsetAlignment) != 0)
            {
                throw new ArgumentException("The uniform-buffer descriptor offset does not satisfy device alignment.", nameof(uniformBufferBindings));
            }

            ArgumentOutOfRangeException.ThrowIfGreaterThan(
                binding.Range,
                Capabilities.Limits.MaximumUniformBufferRangeBytes,
                nameof(uniformBufferBindings));

            var write = default(VkWriteDescriptorSet);
            write.sType = VkStructureType.VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
            write.dstBinding = GetNativeDescriptorBinding(
                new ShaderResourceSignature(
                    binding.Group,
                    binding.Binding,
                    ShaderResourceKind.UniformBuffer,
                    arrayCount: 1));
            write.descriptorCount = 1;
            write.descriptorType = VkDescriptorType.VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;
            bufferWrites[index] = write;
            retainedBuffers[index] = binding.Buffer;
            bufferInfos[index].buffer = binding.Buffer.Buffer;
            bufferInfos[index].offset = checked((ulong)binding.Offset);
            bufferInfos[index].range = checked((ulong)binding.Range);
        }

        var sortedImageBindings = imageBindings.ToArray();
        Array.Sort(sortedImageBindings, static (left, right) => left.Binding.CompareTo(right.Binding));
        var retainedTextures = new VulkanBackendTexture[sortedImageBindings.Length];
        var imageViews = new VkImageView[sortedImageBindings.Length];
        var imageInfos = new VkDescriptorImageInfo[sortedImageBindings.Length];
        var imageWrites = new VkWriteDescriptorSet[sortedImageBindings.Length];

        for (var index = 0; index < sortedImageBindings.Length; index++)
        {
            var binding = sortedImageBindings[index];
            ArgumentNullException.ThrowIfNull(binding.Texture);

            if ((binding.Group != ShaderBindingGroup.Frame)
                || (binding.Binding < 0)
                || binding.Texture.IsDisposed
                || !ReferenceEquals(binding.Texture.Owner, this))
            {
                throw new ArgumentException("A sampled-image descriptor binding has an invalid group, binding, or texture owner.", nameof(imageBindings));
            }

            if (!MatchesReflectedResource(
                    resources,
                    binding.Group,
                    binding.Binding,
                    ShaderResourceKind.SampledImage))
            {
                throw new ArgumentException("Sampled-image descriptors must match the pipeline's unique reflected bindings.", nameof(imageBindings));
            }

            if ((index != 0) && (sortedImageBindings[index - 1].Binding == binding.Binding))
            {
                throw new ArgumentException("Sampled-image descriptors contain a duplicate binding.", nameof(imageBindings));
            }

            if ((binding.Texture.Description.Usage & TextureUsage.Sampled) == 0)
            {
                throw new ArgumentException("A bound texture does not declare sampled usage.", nameof(imageBindings));
            }

            imageInfos[index].imageLayout = GetSampledImageLayout(binding.Texture);
            retainedTextures[index] = binding.Texture;

            var write = default(VkWriteDescriptorSet);
            write.sType = VkStructureType.VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
            write.dstBinding = GetNativeDescriptorBinding(
                new ShaderResourceSignature(
                    binding.Group,
                    binding.Binding,
                    ShaderResourceKind.SampledImage,
                    arrayCount: 1));
            write.descriptorCount = 1;
            write.descriptorType = VkDescriptorType.VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;
            imageWrites[index] = write;
        }

        var sortedSamplerBindings = samplerBindings.ToArray();
        Array.Sort(sortedSamplerBindings, static (left, right) => left.Binding.CompareTo(right.Binding));
        var retainedSamplers = new VulkanBackendSampler[sortedSamplerBindings.Length];
        var samplerInfos = new VkDescriptorImageInfo[sortedSamplerBindings.Length];
        var samplerWrites = new VkWriteDescriptorSet[sortedSamplerBindings.Length];

        for (var index = 0; index < sortedSamplerBindings.Length; index++)
        {
            var binding = sortedSamplerBindings[index];
            ArgumentNullException.ThrowIfNull(binding.Sampler);

            if ((binding.Group != ShaderBindingGroup.Frame)
                || (binding.Binding < 0)
                || binding.Sampler.IsDisposed
                || !ReferenceEquals(binding.Sampler.Owner, this))
            {
                throw new ArgumentException("A sampler descriptor binding has an invalid group, binding, or sampler owner.", nameof(samplerBindings));
            }

            if (!MatchesReflectedResource(
                    resources,
                    binding.Group,
                    binding.Binding,
                    ShaderResourceKind.Sampler))
            {
                throw new ArgumentException("Sampler descriptors must match the pipeline's unique reflected bindings.", nameof(samplerBindings));
            }

            if ((index != 0) && (sortedSamplerBindings[index - 1].Binding == binding.Binding))
            {
                throw new ArgumentException("Sampler descriptors contain a duplicate binding.", nameof(samplerBindings));
            }

            retainedSamplers[index] = binding.Sampler;
            samplerInfos[index].sampler = binding.Sampler.Sampler;

            var write = default(VkWriteDescriptorSet);
            write.sType = VkStructureType.VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
            write.dstBinding = GetNativeDescriptorBinding(
                new ShaderResourceSignature(
                    binding.Group,
                    binding.Binding,
                    ShaderResourceKind.Sampler,
                    arrayCount: 1));
            write.descriptorCount = 1;
            write.descriptorType = VkDescriptorType.VK_DESCRIPTOR_TYPE_SAMPLER;
            samplerWrites[index] = write;
        }

        var poolSizes = new VkDescriptorPoolSize[
            (bufferInfos.Length == 0 ? 0 : 1)
            + (imageInfos.Length == 0 ? 0 : 1)
            + (samplerInfos.Length == 0 ? 0 : 1)];
        var poolSizeCount = 0;

        if (bufferInfos.Length != 0)
        {
            var poolSize = default(VkDescriptorPoolSize);
            poolSize.type = VkDescriptorType.VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;
            poolSize.descriptorCount = checked((uint)bufferInfos.Length);
            poolSizes[poolSizeCount++] = poolSize;
        }

        if (imageInfos.Length != 0)
        {
            var poolSize = default(VkDescriptorPoolSize);
            poolSize.type = VkDescriptorType.VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;
            poolSize.descriptorCount = checked((uint)imageInfos.Length);
            poolSizes[poolSizeCount++] = poolSize;
        }

        if (samplerInfos.Length != 0)
        {
            var poolSize = default(VkDescriptorPoolSize);
            poolSize.type = VkDescriptorType.VK_DESCRIPTOR_TYPE_SAMPLER;
            poolSize.descriptorCount = checked((uint)samplerInfos.Length);
            poolSizes[poolSizeCount++] = poolSize;
        }

        var poolCreateInfo = default(VkDescriptorPoolCreateInfo);
        poolCreateInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO;
        poolCreateInfo.maxSets = 1;
        poolCreateInfo.poolSizeCount = checked((uint)poolSizes.Length);
        VkDescriptorPool descriptorPool = default;
        fixed (VkDescriptorPoolSize* poolSizesPointer = poolSizes)
        {
            poolCreateInfo.pPoolSizes = poolSizesPointer;
            ThrowIfFailed(
                VulkanApi.vkCreateDescriptorPool(_device, &poolCreateInfo, null, &descriptorPool),
                "vkCreateDescriptorPool");
        }

        var ownsDescriptorPool = true;
        var createdImageViewCount = 0;
        var ownsImageViews = true;

        try
        {
            for (; createdImageViewCount < retainedTextures.Length; createdImageViewCount++)
            {
                var imageView = CreateSampledImageView(_device, retainedTextures[createdImageViewCount]);
                imageViews[createdImageViewCount] = imageView;
                imageInfos[createdImageViewCount].imageView = imageView;
            }

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

            if (bufferWrites.Length != 0)
            {
                fixed (VkDescriptorBufferInfo* bufferInfosPointer = bufferInfos)
                fixed (VkWriteDescriptorSet* bufferWritesPointer = bufferWrites)
                {
                    for (var index = 0; index < bufferWrites.Length; index++)
                    {
                        bufferWritesPointer[index].dstSet = descriptorSet;
                        bufferWritesPointer[index].pBufferInfo = bufferInfosPointer + index;
                    }

                    VulkanApi.vkUpdateDescriptorSets(
                        _device,
                        checked((uint)bufferWrites.Length),
                        bufferWritesPointer,
                        0,
                        null);
                }
            }

            if (imageWrites.Length != 0)
            {
                fixed (VkDescriptorImageInfo* imageInfosPointer = imageInfos)
                fixed (VkWriteDescriptorSet* imageWritesPointer = imageWrites)
                {
                    for (var index = 0; index < imageWrites.Length; index++)
                    {
                        imageWritesPointer[index].dstSet = descriptorSet;
                        imageWritesPointer[index].pImageInfo = imageInfosPointer + index;
                    }

                    VulkanApi.vkUpdateDescriptorSets(
                        _device,
                        checked((uint)imageWrites.Length),
                        imageWritesPointer,
                        0,
                        null);
                }
            }

            if (samplerWrites.Length != 0)
            {
                fixed (VkDescriptorImageInfo* samplerInfosPointer = samplerInfos)
                fixed (VkWriteDescriptorSet* samplerWritesPointer = samplerWrites)
                {
                    for (var index = 0; index < samplerWrites.Length; index++)
                    {
                        samplerWritesPointer[index].dstSet = descriptorSet;
                        samplerWritesPointer[index].pImageInfo = samplerInfosPointer + index;
                    }

                    VulkanApi.vkUpdateDescriptorSets(
                        _device,
                        checked((uint)samplerWrites.Length),
                        samplerWritesPointer,
                        0,
                        null);
                }
            }

            var result = new VulkanBackendDescriptorSet(
                this,
                pipeline,
                descriptorPool,
                descriptorSet,
                retainedBuffers,
                retainedTextures,
                retainedSamplers,
                imageViews);
            pipeline.RetainDescriptorSet();
            var retainedBufferCount = 0;
            var retainedTextureCount = 0;
            var retainedSamplerCount = 0;

            try
            {
                for (; retainedBufferCount < retainedBuffers.Length; retainedBufferCount++)
                {
                    retainedBuffers[retainedBufferCount].RetainDescriptorSet();
                }

                for (; retainedTextureCount < retainedTextures.Length; retainedTextureCount++)
                {
                    retainedTextures[retainedTextureCount].RetainDescriptorSet();
                }

                for (; retainedSamplerCount < retainedSamplers.Length; retainedSamplerCount++)
                {
                    retainedSamplers[retainedSamplerCount].RetainDescriptorSet();
                }

                _liveDescriptorSetCount = checked(_liveDescriptorSetCount + 1);
            }
            catch
            {
                for (var index = retainedSamplerCount - 1; index >= 0; index--)
                {
                    retainedSamplers[index].ReleaseDescriptorSet();
                }

                for (var index = retainedTextureCount - 1; index >= 0; index--)
                {
                    retainedTextures[index].ReleaseDescriptorSet();
                }

                for (var index = retainedBufferCount - 1; index >= 0; index--)
                {
                    retainedBuffers[index].ReleaseDescriptorSet();
                }

                pipeline.ReleaseDescriptorSet();
                throw;
            }

            ownsDescriptorPool = false;
            ownsImageViews = false;
            return result;
        }
        finally
        {
            if (ownsDescriptorPool)
            {
                VulkanApi.vkDestroyDescriptorPool(_device, descriptorPool, null);
            }

            if (ownsImageViews)
            {
                for (var index = createdImageViewCount - 1; index >= 0; index--)
                {
                    VulkanApi.vkDestroyImageView(_device, imageViews[index], null);
                }
            }
        }
    }

    internal VulkanBackendDescriptorSet CreateSampledImageSamplerDescriptorSet(
        VulkanBackendPipeline pipeline,
        ImmutableArray<VulkanBackendSampledImageBinding> imageBindings,
        ImmutableArray<VulkanBackendSamplerBinding> samplerBindings)
        => CreateFrameDescriptorSet(
            pipeline,
            [],
            imageBindings,
            samplerBindings);
}
