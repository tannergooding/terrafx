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
        ImmutableArray<VulkanBackendStorageBufferBinding> storageBufferBindings,
        ImmutableArray<VulkanBackendSampledImageBinding> imageBindings,
        ImmutableArray<VulkanBackendStorageImageBinding> storageImageBindings,
        ImmutableArray<VulkanBackendSamplerBinding> samplerBindings)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        EnsureActive();

        if (!ReferenceEquals(pipeline.Owner, this) || pipeline.IsDisposed)
        {
            throw new ArgumentException("The pipeline is not live on this Vulkan device.", nameof(pipeline));
        }

        if (uniformBufferBindings.IsDefault
            || storageBufferBindings.IsDefault
            || imageBindings.IsDefault
            || storageImageBindings.IsDefault
            || samplerBindings.IsDefault)
        {
            throw new ArgumentException("All Frame descriptor binding arrays must be initialized.");
        }

        var resources = pipeline.Resources;
        var setIndex = -1;
        var expectedUniformBufferCount = 0;
        var expectedStorageBufferCount = 0;
        var expectedImageCount = 0;
        var expectedStorageImageCount = 0;
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

                case ShaderResourceKind.StorageBuffer:
                {
                    expectedStorageBufferCount++;
                    break;
                }

                case ShaderResourceKind.SampledImage:
                {
                    expectedImageCount++;
                    break;
                }

                case ShaderResourceKind.StorageImage:
                {
                    expectedStorageImageCount++;
                    break;
                }

                case ShaderResourceKind.Sampler:
                {
                    expectedSamplerCount++;
                    break;
                }

                default:
                {
                    throw new NotSupportedException("This descriptor-set path supports only Frame-group uniform buffers, storage buffers, sampled images, storage images, and samplers.");
                }
            }

            setIndex = checked((int)GetNativeDescriptorSetIndex(resource.Group));
        }

        if ((setIndex < 0)
            || (setIndex >= pipeline.DescriptorSetLayouts.Length)
            || (expectedUniformBufferCount != uniformBufferBindings.Length)
            || (expectedStorageBufferCount != storageBufferBindings.Length)
            || (expectedImageCount != imageBindings.Length)
            || (expectedStorageImageCount != storageImageBindings.Length)
            || (expectedSamplerCount != samplerBindings.Length)
            || ((expectedUniformBufferCount == 0)
                && (expectedStorageBufferCount == 0)
                && (expectedImageCount == 0)
                && (expectedStorageImageCount == 0)
                && (expectedSamplerCount == 0)))
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

        var sortedStorageBufferBindings = storageBufferBindings.ToArray();
        Array.Sort(sortedStorageBufferBindings, static (left, right) => left.Binding.CompareTo(right.Binding));
        var retainedStorageBuffers = new VulkanBackendBuffer[sortedStorageBufferBindings.Length];
        var storageBufferInfos = new VkDescriptorBufferInfo[sortedStorageBufferBindings.Length];
        var storageBufferWrites = new VkWriteDescriptorSet[sortedStorageBufferBindings.Length];
        var maximumStorageBufferRange = sortedStorageBufferBindings.Length == 0
            ? 0
            : GetMaximumStorageBufferRangeBytes();

        for (var index = 0; index < sortedStorageBufferBindings.Length; index++)
        {
            var binding = sortedStorageBufferBindings[index];
            ArgumentNullException.ThrowIfNull(binding.Buffer);

            if ((binding.Group != ShaderBindingGroup.Frame)
                || (binding.Binding < 0)
                || binding.Buffer.IsDisposed
                || !ReferenceEquals(binding.Buffer.Owner, this))
            {
                throw new ArgumentException("A storage-buffer descriptor binding has an invalid group, binding, or buffer owner.", nameof(storageBufferBindings));
            }

            if (!MatchesReflectedResource(
                    resources,
                    binding.Group,
                    binding.Binding,
                    ShaderResourceKind.StorageBuffer)
                || ((index != 0) && (sortedStorageBufferBindings[index - 1].Binding == binding.Binding)))
            {
                throw new ArgumentException("Storage-buffer descriptors must match the pipeline's unique reflected bindings.", nameof(storageBufferBindings));
            }

            if ((binding.Buffer.Description.Usage & BufferUsage.Storage) == 0)
            {
                throw new ArgumentException("A bound buffer does not declare storage-buffer usage.", nameof(storageBufferBindings));
            }

            ArgumentOutOfRangeException.ThrowIfNegative(binding.Offset);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(binding.Range);

            var rangeEnd = checked(binding.Offset + binding.Range);

            if (rangeEnd > binding.Buffer.Description.ByteLength)
            {
                throw new ArgumentException("The storage-buffer descriptor range exceeds its buffer.", nameof(storageBufferBindings));
            }

            if ((binding.Offset % Capabilities.Limits.StorageBufferOffsetAlignment) != 0)
            {
                throw new ArgumentException("The storage-buffer descriptor offset does not satisfy device alignment.", nameof(storageBufferBindings));
            }

            ArgumentOutOfRangeException.ThrowIfGreaterThan(
                binding.Range,
                maximumStorageBufferRange,
                nameof(storageBufferBindings));

            var write = default(VkWriteDescriptorSet);
            write.sType = VkStructureType.VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
            write.dstBinding = GetNativeDescriptorBinding(
                new ShaderResourceSignature(
                    binding.Group,
                    binding.Binding,
                    ShaderResourceKind.StorageBuffer,
                    arrayCount: 1));
            write.descriptorCount = 1;
            write.descriptorType = VkDescriptorType.VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
            storageBufferWrites[index] = write;
            retainedStorageBuffers[index] = binding.Buffer;
            storageBufferInfos[index].buffer = binding.Buffer.Buffer;
            storageBufferInfos[index].offset = checked((ulong)binding.Offset);
            storageBufferInfos[index].range = checked((ulong)binding.Range);
        }

        var allRetainedBuffers = new VulkanBackendBuffer[retainedBuffers.Length + retainedStorageBuffers.Length];
        Array.Copy(retainedBuffers, 0, allRetainedBuffers, 0, retainedBuffers.Length);
        Array.Copy(retainedStorageBuffers, 0, allRetainedBuffers, retainedBuffers.Length, retainedStorageBuffers.Length);

        var sortedImageBindings = imageBindings.ToArray();
        Array.Sort(sortedImageBindings, static (left, right) => left.Binding.CompareTo(right.Binding));
        var retainedSampledTextures = new VulkanBackendTexture[sortedImageBindings.Length];
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
            retainedSampledTextures[index] = binding.Texture;

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

        var sortedStorageImageBindings = storageImageBindings.ToArray();
        Array.Sort(sortedStorageImageBindings, static (left, right) => left.Binding.CompareTo(right.Binding));
        var retainedStorageTextures = new VulkanBackendTexture[sortedStorageImageBindings.Length];
        var storageImageInfos = new VkDescriptorImageInfo[sortedStorageImageBindings.Length];
        var storageImageWrites = new VkWriteDescriptorSet[sortedStorageImageBindings.Length];

        for (var index = 0; index < sortedStorageImageBindings.Length; index++)
        {
            var binding = sortedStorageImageBindings[index];
            ArgumentNullException.ThrowIfNull(binding.Texture);

            if ((binding.Group != ShaderBindingGroup.Frame)
                || (binding.Binding < 0)
                || binding.Texture.IsDisposed
                || !ReferenceEquals(binding.Texture.Owner, this))
            {
                throw new ArgumentException("A storage-image descriptor binding has an invalid group, binding, or texture owner.", nameof(storageImageBindings));
            }

            if (!MatchesReflectedResource(
                    resources,
                    binding.Group,
                    binding.Binding,
                    ShaderResourceKind.StorageImage)
                || ((index != 0) && (sortedStorageImageBindings[index - 1].Binding == binding.Binding)))
            {
                throw new ArgumentException("Storage-image descriptors must match the pipeline's unique reflected bindings.", nameof(storageImageBindings));
            }

            if ((binding.Texture.Description.Usage & TextureUsage.Storage) == 0)
            {
                throw new ArgumentException("A bound texture does not declare storage-image usage.", nameof(storageImageBindings));
            }

            storageImageInfos[index].imageLayout = GetStorageImageLayout(binding.Texture);
            retainedStorageTextures[index] = binding.Texture;

            var write = default(VkWriteDescriptorSet);
            write.sType = VkStructureType.VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
            write.dstBinding = GetNativeDescriptorBinding(
                new ShaderResourceSignature(
                    binding.Group,
                    binding.Binding,
                    ShaderResourceKind.StorageImage,
                    arrayCount: 1));
            write.descriptorCount = 1;
            write.descriptorType = VkDescriptorType.VK_DESCRIPTOR_TYPE_STORAGE_IMAGE;
            storageImageWrites[index] = write;
        }

        var retainedTextures = new VulkanBackendTexture[retainedSampledTextures.Length + retainedStorageTextures.Length];
        Array.Copy(retainedSampledTextures, 0, retainedTextures, 0, retainedSampledTextures.Length);
        Array.Copy(retainedStorageTextures, 0, retainedTextures, retainedSampledTextures.Length, retainedStorageTextures.Length);
        var imageViews = new VkImageView[retainedTextures.Length];

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
            + (storageBufferInfos.Length == 0 ? 0 : 1)
            + (imageInfos.Length == 0 ? 0 : 1)
            + (storageImageInfos.Length == 0 ? 0 : 1)
            + (samplerInfos.Length == 0 ? 0 : 1)];
        var poolSizeCount = 0;

        if (bufferInfos.Length != 0)
        {
            var poolSize = default(VkDescriptorPoolSize);
            poolSize.type = VkDescriptorType.VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;
            poolSize.descriptorCount = checked((uint)bufferInfos.Length);
            poolSizes[poolSizeCount++] = poolSize;
        }

        if (storageBufferInfos.Length != 0)
        {
            var poolSize = default(VkDescriptorPoolSize);
            poolSize.type = VkDescriptorType.VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
            poolSize.descriptorCount = checked((uint)storageBufferInfos.Length);
            poolSizes[poolSizeCount++] = poolSize;
        }

        if (imageInfos.Length != 0)
        {
            var poolSize = default(VkDescriptorPoolSize);
            poolSize.type = VkDescriptorType.VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;
            poolSize.descriptorCount = checked((uint)imageInfos.Length);
            poolSizes[poolSizeCount++] = poolSize;
        }

        if (storageImageInfos.Length != 0)
        {
            var poolSize = default(VkDescriptorPoolSize);
            poolSize.type = VkDescriptorType.VK_DESCRIPTOR_TYPE_STORAGE_IMAGE;
            poolSize.descriptorCount = checked((uint)storageImageInfos.Length);
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
            for (var index = 0; index < retainedSampledTextures.Length; index++)
            {
                var imageView = CreateDescriptorImageView(_device, retainedSampledTextures[index]);
                imageViews[createdImageViewCount++] = imageView;
                imageInfos[index].imageView = imageView;
            }

            for (var index = 0; index < retainedStorageTextures.Length; index++)
            {
                var imageView = CreateDescriptorImageView(_device, retainedStorageTextures[index]);
                imageViews[createdImageViewCount++] = imageView;
                storageImageInfos[index].imageView = imageView;
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

            if (storageBufferWrites.Length != 0)
            {
                fixed (VkDescriptorBufferInfo* storageBufferInfosPointer = storageBufferInfos)
                fixed (VkWriteDescriptorSet* storageBufferWritesPointer = storageBufferWrites)
                {
                    for (var index = 0; index < storageBufferWrites.Length; index++)
                    {
                        storageBufferWritesPointer[index].dstSet = descriptorSet;
                        storageBufferWritesPointer[index].pBufferInfo = storageBufferInfosPointer + index;
                    }

                    VulkanApi.vkUpdateDescriptorSets(
                        _device,
                        checked((uint)storageBufferWrites.Length),
                        storageBufferWritesPointer,
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

            if (storageImageWrites.Length != 0)
            {
                fixed (VkDescriptorImageInfo* storageImageInfosPointer = storageImageInfos)
                fixed (VkWriteDescriptorSet* storageImageWritesPointer = storageImageWrites)
                {
                    for (var index = 0; index < storageImageWrites.Length; index++)
                    {
                        storageImageWritesPointer[index].dstSet = descriptorSet;
                        storageImageWritesPointer[index].pImageInfo = storageImageInfosPointer + index;
                    }

                    VulkanApi.vkUpdateDescriptorSets(
                        _device,
                        checked((uint)storageImageWrites.Length),
                        storageImageWritesPointer,
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
                allRetainedBuffers,
                retainedTextures,
                retainedSamplers,
                imageViews);
            pipeline.RetainDescriptorSet();
            var retainedBufferCount = 0;
            var retainedTextureCount = 0;
            var retainedSamplerCount = 0;

            try
            {
                for (; retainedBufferCount < allRetainedBuffers.Length; retainedBufferCount++)
                {
                    allRetainedBuffers[retainedBufferCount].RetainDescriptorSet();
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
                    allRetainedBuffers[index].ReleaseDescriptorSet();
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
            [],
            imageBindings,
            [],
            samplerBindings);

    private long GetMaximumStorageBufferRangeBytes()
    {
        var properties2 = default(VkPhysicalDeviceProperties2);
        properties2.sType = VkStructureType.VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PROPERTIES_2;
        VulkanApi.vkGetPhysicalDeviceProperties2(_physicalDevice, &properties2);
        return checked((long)properties2.properties.limits.maxStorageBufferRange);
    }
}
