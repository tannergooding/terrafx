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
    internal VulkanBackendDescriptorSet CreateSampledImageSamplerDescriptorSet(
        VulkanBackendPipeline pipeline,
        ImmutableArray<VulkanBackendSampledImageBinding> imageBindings,
        ImmutableArray<VulkanBackendSamplerBinding> samplerBindings)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        EnsureActive();

        if (!ReferenceEquals(pipeline.Owner, this) || pipeline.IsDisposed)
        {
            throw new ArgumentException("The pipeline is not live on this Vulkan device.", nameof(pipeline));
        }

        if (imageBindings.IsDefault || samplerBindings.IsDefault)
        {
            throw new ArgumentException("Sampled-image and sampler descriptor bindings must be initialized.", nameof(imageBindings));
        }

        var resources = pipeline.Resources;
        var setIndex = -1;
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
                throw new NotSupportedException("Sampled-image and sampler descriptor arrays are not supported.");
            }

            switch (resource.Kind)
            {
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
                    throw new NotSupportedException("This descriptor-set path supports only Frame-group sampled images and samplers.");
                }
            }

            setIndex = checked((int)GetNativeDescriptorSetIndex(resource.Group));
        }

        if ((setIndex < 0)
            || (setIndex >= pipeline.DescriptorSetLayouts.Length)
            || (expectedImageCount != imageBindings.Length)
            || (expectedSamplerCount != samplerBindings.Length)
            || ((expectedImageCount == 0) && (expectedSamplerCount == 0)))
        {
            throw new ArgumentException("The pipeline does not have a complete Frame-group sampled-image/sampler set.");
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

        var poolSizes = new VkDescriptorPoolSize[(imageInfos.Length == 0 ? 0 : 1) + (samplerInfos.Length == 0 ? 0 : 1)];
        var poolSizeCount = 0;

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
                [],
                retainedTextures,
                retainedSamplers,
                imageViews);
            pipeline.RetainDescriptorSet();
            var retainedTextureCount = 0;
            var retainedSamplerCount = 0;

            try
            {
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
}
