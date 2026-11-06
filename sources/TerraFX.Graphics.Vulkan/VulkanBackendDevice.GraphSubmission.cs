// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using TerraFX.Graphics;
using TerraFX.Interop.Vulkan;
using VulkanApi = TerraFX.Interop.Vulkan.Vulkan;

namespace TerraFX.Graphics.Vulkan;

internal sealed unsafe partial class VulkanBackendDevice
{
    private static VkImageView CreateGraphColorImageView(VkDevice device, VulkanBackendTexture texture)
    {
        var createInfo = default(VkImageViewCreateInfo);
        createInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
        createInfo.image = texture.Image;
        createInfo.viewType = VkImageViewType.VK_IMAGE_VIEW_TYPE_2D;
        createInfo.format = VulkanNativeTypeMapper.GetVkFormat(texture.Description.Format);
        createInfo.subresourceRange.aspectMask = VkImageAspectFlags.VK_IMAGE_ASPECT_COLOR_BIT;
        createInfo.subresourceRange.baseMipLevel = 0;
        createInfo.subresourceRange.levelCount = 1;
        createInfo.subresourceRange.baseArrayLayer = 0;
        createInfo.subresourceRange.layerCount = 1;
        VkImageView imageView = default;
        ThrowIfFailed(VulkanApi.vkCreateImageView(device, &createInfo, null, &imageView), "vkCreateImageView");
        return imageView;
    }

    private static VkPipelineStageFlags2 GetShaderStageMask(GraphicsGraphSubmissionPass pass, ulong resourceId)
    {
        var stageMask = VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_NONE;
        GpuPipeline? pipeline = null;

        foreach (var command in pass.Commands)
        {
            if (command.Kind == GraphicsCommandKind.SetPipeline)
            {
                pipeline = command.PipelineOwner
                    ?? throw new InvalidOperationException("A recorded pipeline command lost its owner.");
                continue;
            }

            if ((command.Kind != GraphicsCommandKind.SetBindings) || (command.Bindings is not BindingSet bindings))
            {
                continue;
            }

            var selectedPipeline = pipeline
                ?? throw new InvalidOperationException("A recorded binding set has no preceding graphics pipeline.");

            foreach (var binding in bindings.Buffers)
            {
                if (binding.Resource.ResourceId != resourceId)
                {
                    continue;
                }

                AddStages(selectedPipeline, binding.Group, binding.Binding, binding.Kind);
            }

            foreach (var binding in bindings.Textures)
            {
                if (binding.Resource.ResourceId != resourceId)
                {
                    continue;
                }

                AddStages(selectedPipeline, binding.Group, binding.Binding, binding.Kind);
            }
        }

        return stageMask;

        void AddStages(GpuPipeline selectedPipeline, ShaderBindingGroup group, int binding, ShaderResourceKind kind)
        {
            foreach (var reflection in selectedPipeline.BindingLayout.ShaderReflections)
            {
                foreach (var resource in reflection.Resources)
                {
                    if ((resource.Group == group)
                        && (resource.Binding == binding)
                        && (resource.Kind == kind))
                    {
                        stageMask |= reflection.Stage switch {
                            ShaderStage.Vertex => VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_VERTEX_SHADER_BIT,
                            ShaderStage.Pixel => VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT,
                            ShaderStage.Compute => VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT,
                            _ => throw new NotSupportedException(
                                $"Vulkan graph submission does not support {reflection.Stage} resource stages."),
                        };
                    }
                }
            }
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "A failed graph recording must reset the unsubmitted command buffer before releasing its retained owners.")]
    internal long SubmitGraph(GraphicsGraphSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
        submission.ValidateOffscreenRasterSubmission();
        EnsureOwnerThread();
        EnsureActive();

        var target = submission.GetTexture(submission.TextureImports[0].Version.ResourceId) as VulkanBackendTexture
            ?? throw new ArgumentException("The graph output target is not a Vulkan texture.", nameof(submission));
        ObjectDisposedException.ThrowIf(target.IsDisposed, nameof(submission));

        if ((target.Description.Dimension != TextureDimension.Dimension2D)
            || (target.Description.ArrayLayers < 1)
            || (target.Description.Usage & TextureUsage.ColorAttachment) == 0)
        {
            throw new NotSupportedException("Vulkan graph submission requires a 2D color-attachment texture.");
        }

        var descriptorSetCapacity = 0;

        foreach (var pass in submission.Passes)
        {
            foreach (var command in pass.Commands)
            {
                if (command.Kind == GraphicsCommandKind.SetBindings)
                {
                    descriptorSetCapacity = checked(descriptorSetCapacity + 1);
                }
            }
        }

        var descriptorSets = new List<VulkanBackendDescriptorSet>(descriptorSetCapacity);
        _ = _uncertainGraphDescriptorSets.EnsureCapacity(
            checked(_uncertainGraphDescriptorSets.Count + descriptorSetCapacity));
        var targetResourceId = submission.TextureImports[0].Version.ResourceId;
        ulong sampledTextureResourceId = 0;
        ulong storageTextureResourceId = 0;

        for (var index = 1; index < submission.TextureImports.Length; index++)
        {
            var import = submission.TextureImports[index];

            if ((import.Version.Description.Usage & TextureUsage.Sampled) != 0)
            {
                sampledTextureResourceId = import.Version.ResourceId;
            }
            else if ((import.Version.Description.Usage & TextureUsage.Storage) != 0)
            {
                storageTextureResourceId = import.Version.ResourceId;
            }
        }

        VulkanBackendTexture? sampledTexture = null;
        var sampledTextureStageMask = VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_NONE;
        var sampledTextureAccessMask = VkAccessFlags2.VK_ACCESS_2_NONE;
        VulkanBackendTexture? storageTexture = null;
        var storageTextureStageMask = VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_NONE;
        var storageTextureAccessMask = VkAccessFlags2.VK_ACCESS_2_NONE;
        var graphBufferScopes = new List<(
            VulkanBackendBuffer Buffer,
            VkPipelineStageFlags2 StageMask,
            VkAccessFlags2 AccessMask)>(submission.BufferImports.Length);
        var currentBufferScopes = new Dictionary<ulong, (
            VkPipelineStageFlags2 StageMask,
            VkAccessFlags2 AccessMask)>(submission.BufferImports.Length);

        foreach (var import in submission.BufferImports)
        {
            var buffer = submission.GetBuffer(import.Version.ResourceId) as VulkanBackendBuffer
                ?? throw new InvalidOperationException("The graph buffer import is not a Vulkan buffer.");
            currentBufferScopes.Add(
                import.Version.ResourceId,
                (buffer.LastStageMask, buffer.LastAccessMask));
        }

        foreach (var pass in submission.Passes)
        {
            foreach (var access in submission.Accesses)
            {
                if (access.PassId != pass.PassId)
                {
                    continue;
                }

                var shaderStages = GetShaderStageMask(pass, access.ResourceId);

                if (access.IsTexture)
                {
                    if (access.ResourceId == sampledTextureResourceId)
                    {
                        sampledTexture = submission.GetTexture(access.ResourceId) as VulkanBackendTexture
                            ?? throw new InvalidOperationException("The Frame sampled image is not a Vulkan texture.");
                        sampledTextureStageMask |= VulkanGraphResourceStateMapper.GetStageMask(access.RequiredState, shaderStages);
                        sampledTextureAccessMask |= VulkanGraphResourceStateMapper.GetAccessMask(access.RequiredState);
                    }
                    else if (access.ResourceId == storageTextureResourceId)
                    {
                        storageTexture = submission.GetTexture(access.ResourceId) as VulkanBackendTexture
                            ?? throw new InvalidOperationException("The Frame storage image is not a Vulkan texture.");
                        storageTextureStageMask |= VulkanGraphResourceStateMapper.GetStageMask(access.RequiredState, shaderStages);
                        storageTextureAccessMask |= VulkanGraphResourceStateMapper.GetAccessMask(access.RequiredState);
                    }

                    continue;
                }

                var graphBuffer = submission.GetBuffer(access.ResourceId) as VulkanBackendBuffer
                    ?? throw new InvalidOperationException("The graph uniform buffer is not a Vulkan buffer.");
                var graphBufferStageMask = VulkanGraphResourceStateMapper.GetStageMask(access.RequiredState, shaderStages);
                var graphBufferAccessMask = VulkanGraphResourceStateMapper.GetAccessMask(access.RequiredState);
                var existingScopeIndex = -1;

                for (var index = 0; index < graphBufferScopes.Count; index++)
                {
                    if (ReferenceEquals(graphBufferScopes[index].Buffer, graphBuffer))
                    {
                        existingScopeIndex = index;
                        break;
                    }
                }

                if (existingScopeIndex < 0)
                {
                    graphBufferScopes.Add((graphBuffer, graphBufferStageMask, graphBufferAccessMask));
                }
                else
                {
                    var existingScope = graphBufferScopes[existingScopeIndex];
                    graphBufferScopes[existingScopeIndex] = (
                        graphBuffer,
                        existingScope.StageMask | graphBufferStageMask,
                        existingScope.AccessMask | graphBufferAccessMask);
                }
            }
        }

        var imageView = CreateGraphColorImageView(_device, target);
        var subresource = new TextureSubresource(0, 0, TextureAspect.Color);
        var currentLayout = target.GetLayout(subresource);
        var currentStageMask = target.GetLastStageMask(subresource);
        var currentAccessMask = target.GetLastAccessMask(subresource);
        var sampledSubresource = new TextureSubresource(0, 0, TextureAspect.Color);
        var sampledCurrentLayout = sampledTexture?.GetLayout(sampledSubresource) ?? default;
        var sampledCurrentStageMask = sampledTexture?.GetLastStageMask(sampledSubresource)
            ?? VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_NONE;
        var sampledCurrentAccessMask = sampledTexture?.GetLastAccessMask(sampledSubresource)
            ?? VkAccessFlags2.VK_ACCESS_2_NONE;
        var storageSubresource = new TextureSubresource(0, 0, TextureAspect.Color);
        var storageCurrentLayout = storageTexture?.GetLayout(storageSubresource) ?? default;
        var storageCurrentStageMask = storageTexture?.GetLastStageMask(storageSubresource)
            ?? VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_NONE;
        var storageCurrentAccessMask = storageTexture?.GetLastAccessMask(storageSubresource)
            ?? VkAccessFlags2.VK_ACCESS_2_NONE;
        var ownsImageView = true;

        try
        {
            _ = _uncertainGraphImageViews.EnsureCapacity(checked(_uncertainGraphImageViews.Count + 1));
            _ = _uncertainTextureOwners.EnsureCapacity(checked(_uncertainTextureOwners.Count + 1));
            BeginOneTimeCommands();

            foreach (var pass in submission.Passes)
            {
                foreach (var import in submission.BufferImports)
                {
                    if ((import.Version.Description.Usage & BufferUsage.Storage) == 0)
                    {
                        continue;
                    }

                    var resourceId = import.Version.ResourceId;
                    var initialUseNeedsBarrier = (import.InitialState == GraphResourceState.StorageReadWrite)
                        && submission.HasFirstStorageUse(pass.PassId, resourceId);
                    var priorPassNeedsBarrier = submission.HasPriorStorageWriteHazard(pass.PassId, resourceId);

                    if (!initialUseNeedsBarrier && !priorPassNeedsBarrier)
                    {
                        continue;
                    }

                    var buffer = submission.GetBuffer(resourceId) as VulkanBackendBuffer
                        ?? throw new InvalidOperationException("The graph storage hazard lost its Vulkan buffer.");
                    var shaderStages = GetShaderStageMask(pass, resourceId);
                    var storageAccess = VulkanGraphResourceStateMapper.GetAccessMask(GraphResourceState.StorageReadWrite);
                    var sourceStage = initialUseNeedsBarrier ? buffer.LastStageMask : shaderStages;
                    var sourceAccess = initialUseNeedsBarrier ? buffer.LastAccessMask : storageAccess;
                    RecordBufferBarrier(
                        buffer,
                        import.Version.Range.ByteOffset,
                        checked((nint)import.Version.Range.ByteLength),
                        sourceStage,
                        sourceAccess,
                        shaderStages,
                        storageAccess);
                    currentBufferScopes[resourceId] = (shaderStages, storageAccess);
                }

                for (var importIndex = 1; importIndex < submission.TextureImports.Length; importIndex++)
                {
                    var import = submission.TextureImports[importIndex];

                    if ((import.Version.Description.Usage & TextureUsage.Storage) == 0)
                    {
                        continue;
                    }

                    var resourceId = import.Version.ResourceId;
                    var initialUseNeedsBarrier = (import.InitialState == GraphResourceState.StorageReadWrite)
                        && submission.HasFirstStorageUse(pass.PassId, resourceId);
                    var priorPassNeedsBarrier = submission.HasPriorStorageWriteHazard(pass.PassId, resourceId);

                    if (!initialUseNeedsBarrier && !priorPassNeedsBarrier)
                    {
                        continue;
                    }

                    var texture = submission.GetTexture(resourceId) as VulkanBackendTexture
                        ?? throw new InvalidOperationException("The graph storage hazard lost its Vulkan texture.");
                    var shaderStages = GetShaderStageMask(pass, resourceId);
                    var storageAccess = VulkanGraphResourceStateMapper.GetAccessMask(GraphResourceState.StorageReadWrite);
                    var sourceStage = initialUseNeedsBarrier ? storageCurrentStageMask : shaderStages;
                    var sourceAccess = initialUseNeedsBarrier ? storageCurrentAccessMask : storageAccess;

                    if (storageCurrentLayout != VulkanGraphResourceStateMapper.GetImageLayout(import.InitialState))
                    {
                        throw new InvalidOperationException("The storage barrier does not match the Vulkan texture's tracked image layout.");
                    }

                    var barrier = default(VkImageMemoryBarrier2);
                    barrier.sType = VkStructureType.VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
                    barrier.srcStageMask = sourceStage;
                    barrier.srcAccessMask = sourceAccess;
                    barrier.dstStageMask = shaderStages;
                    barrier.dstAccessMask = storageAccess;
                    barrier.oldLayout = VkImageLayout.VK_IMAGE_LAYOUT_GENERAL;
                    barrier.newLayout = VkImageLayout.VK_IMAGE_LAYOUT_GENERAL;
                    barrier.srcQueueFamilyIndex = uint.MaxValue;
                    barrier.dstQueueFamilyIndex = uint.MaxValue;
                    barrier.image = texture.Image;
                    barrier.subresourceRange.aspectMask = VkImageAspectFlags.VK_IMAGE_ASPECT_COLOR_BIT;
                    barrier.subresourceRange.baseMipLevel = checked((uint)import.Version.Range.FirstMip);
                    barrier.subresourceRange.levelCount = checked((uint)import.Version.Range.MipCount);
                    barrier.subresourceRange.baseArrayLayer = checked((uint)import.Version.Range.FirstLayer);
                    barrier.subresourceRange.layerCount = checked((uint)import.Version.Range.LayerCount);
                    var dependencyInfo = default(VkDependencyInfo);
                    dependencyInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
                    dependencyInfo.imageMemoryBarrierCount = 1;
                    dependencyInfo.pImageMemoryBarriers = &barrier;
                    VulkanApi.vkCmdPipelineBarrier2(_commandBuffer, &dependencyInfo);
                }

                foreach (var transition in submission.StateTransitions)
                {
                    if (transition.PassId != pass.PassId)
                    {
                        continue;
                    }

                    if (transition.IsTexture)
                    {
                        if (transition.ResourceId == targetResourceId)
                        {
                            RecordGraphImageBarrier(
                                pass,
                                target,
                                transition,
                                ref currentLayout,
                                ref currentStageMask,
                                ref currentAccessMask);
                        }
                        else if ((sampledTexture is not null)
                            && (transition.ResourceId == sampledTextureResourceId))
                        {
                            RecordGraphImageBarrier(
                                pass,
                                sampledTexture,
                                transition,
                                ref sampledCurrentLayout,
                                ref sampledCurrentStageMask,
                                ref sampledCurrentAccessMask);
                        }
                        else if ((storageTexture is not null)
                            && (transition.ResourceId == storageTextureResourceId))
                        {
                            RecordGraphImageBarrier(
                                pass,
                                storageTexture,
                                transition,
                                ref storageCurrentLayout,
                                ref storageCurrentStageMask,
                                ref storageCurrentAccessMask);
                        }
                        else
                        {
                            throw new NotSupportedException("The graph transition references an unsupported Vulkan texture.");
                        }
                    }
                    else
                    {
                        var buffer = submission.GetBuffer(transition.ResourceId) as VulkanBackendBuffer
                            ?? throw new InvalidOperationException("The graph transition lost its Vulkan buffer.");
                        var (bufferScopeStageMask, bufferScopeAccessMask) = currentBufferScopes[transition.ResourceId];
                        currentBufferScopes[transition.ResourceId] = RecordGraphBufferBarrier(
                            pass,
                            buffer,
                            transition,
                            bufferScopeStageMask,
                            bufferScopeAccessMask);
                    }
                }

                if (pass.Kind == CommandRecorderPassKind.Copy)
                {
                    foreach (var command in pass.Commands)
                    {
                        if (command.Kind != GraphicsCommandKind.CopyBuffer)
                        {
                            throw new NotSupportedException($"Native Vulkan graph copy pass does not support {command.Kind} commands.");
                        }

                        var sourceVersion = command.CopySource
                            ?? throw new InvalidOperationException("The graph copy command has no source buffer version.");
                        var destinationVersion = command.CopyDestination
                            ?? throw new InvalidOperationException("The graph copy command has no destination buffer version.");
                        var sourceBuffer = submission.GetBuffer(sourceVersion.ResourceId) as VulkanBackendBuffer
                            ?? throw new InvalidOperationException("The graph copy source is not a Vulkan buffer.");
                        var destinationBuffer = submission.GetBuffer(destinationVersion.ResourceId) as VulkanBackendBuffer
                            ?? throw new InvalidOperationException("The graph copy destination is not a Vulkan buffer.");
                        var region = default(VkBufferCopy);
                        region.srcOffset = checked((ulong)command.SourceOffset);
                        region.dstOffset = checked((ulong)command.DestinationOffset);
                        region.size = checked((ulong)command.ByteLength);
                        VulkanApi.vkCmdCopyBuffer(
                            _commandBuffer,
                            sourceBuffer.Buffer,
                            destinationBuffer.Buffer,
                            1,
                            &region);
                    }

                    continue;
                }

                if (pass.Kind == CommandRecorderPassKind.Compute)
                {
                    VulkanBackendPipeline? currentComputePipeline = null;

                    foreach (var command in pass.Commands)
                    {
                        switch (command.Kind)
                        {
                            case GraphicsCommandKind.SetPipeline:
                            {
                                var pipeline = GraphicsGraphSubmission.GetPipeline(command) as VulkanBackendPipeline
                                    ?? throw new InvalidOperationException("The graph compute pipeline is not a Vulkan pipeline.");

                                VulkanApi.vkCmdBindPipeline(
                                    _commandBuffer,
                                    VkPipelineBindPoint.VK_PIPELINE_BIND_POINT_COMPUTE,
                                    pipeline.Pipeline);
                                currentComputePipeline = pipeline;
                                break;
                            }

                            case GraphicsCommandKind.SetBindings:
                            {
                                var pipeline = currentComputePipeline
                                    ?? throw new InvalidOperationException("A recorded binding set has no preceding Vulkan compute pipeline.");
                                var bindings = command.Bindings
                                    ?? throw new InvalidOperationException("A recorded binding command lost its binding set.");
                                var storageBufferBindings =
                                    ImmutableArray.CreateBuilder<VulkanBackendStorageBufferBinding>(bindings.Buffers.Length);

                                foreach (var binding in bindings.Buffers)
                                {
                                    if (binding.Kind != ShaderResourceKind.StorageBuffer)
                                    {
                                        throw new NotSupportedException("Native Vulkan compute graphs currently support storage-buffer bindings only.");
                                    }

                                    var buffer = submission.GetBuffer(binding.Resource.ResourceId) as VulkanBackendBuffer
                                        ?? throw new InvalidOperationException("A compute storage-buffer binding is not a Vulkan buffer.");
                                    storageBufferBindings.Add(new VulkanBackendStorageBufferBinding(
                                        binding.Group,
                                        binding.Binding,
                                        buffer,
                                        binding.Range.ByteOffset,
                                        binding.Range.ByteLength));
                                }

                                var descriptorSet = CreateFrameDescriptorSet(
                                    pipeline,
                                    [],
                                    storageBufferBindings.ToImmutable(),
                                    [],
                                    [],
                                    []);
                                descriptorSets.Add(descriptorSet);
                                var nativeDescriptorSet = descriptorSet.DescriptorSet;
                                VulkanApi.vkCmdBindDescriptorSets(
                                    _commandBuffer,
                                    VkPipelineBindPoint.VK_PIPELINE_BIND_POINT_COMPUTE,
                                    pipeline.PipelineLayout,
                                    GetNativeDescriptorSetIndex(ShaderBindingGroup.Frame),
                                    1,
                                    &nativeDescriptorSet,
                                    0,
                                    null);
                                break;
                            }

                            case GraphicsCommandKind.StorageBarrier:
                            {
                                RecordGraphStorageBarrier(pass, submission, command);
                                break;
                            }

                            case GraphicsCommandKind.Dispatch:
                            {
                                var dispatch = command.Dispatch;
                                VulkanApi.vkCmdDispatch(
                                    _commandBuffer,
                                    checked((uint)dispatch.GroupCountX),
                                    checked((uint)dispatch.GroupCountY),
                                    checked((uint)dispatch.GroupCountZ));
                                break;
                            }

                            default:
                            {
                                throw new NotSupportedException($"Native Vulkan graph compute passes do not support {command.Kind} commands.");
                            }
                        }
                    }

                    continue;
                }

                if (pass.Kind != CommandRecorderPassKind.Raster)
                {
                    continue;
                }

                var attachment = pass.Attachments[0];
                var renderingAttachment = default(VkRenderingAttachmentInfo);
                renderingAttachment.sType = VkStructureType.VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO;
                renderingAttachment.imageView = imageView;
                renderingAttachment.imageLayout = VkImageLayout.VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
                renderingAttachment.loadOp = VkAttachmentLoadOp.VK_ATTACHMENT_LOAD_OP_CLEAR;
                renderingAttachment.storeOp = VkAttachmentStoreOp.VK_ATTACHMENT_STORE_OP_STORE;
                renderingAttachment.clearValue.color.float32[0] = attachment.ClearColor.X;
                renderingAttachment.clearValue.color.float32[1] = attachment.ClearColor.Y;
                renderingAttachment.clearValue.color.float32[2] = attachment.ClearColor.Z;
                renderingAttachment.clearValue.color.float32[3] = attachment.ClearColor.W;
                var renderingInfo = default(VkRenderingInfo);
                renderingInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_RENDERING_INFO;
                renderingInfo.renderArea.extent.width = checked((uint)target.Description.Width);
                renderingInfo.renderArea.extent.height = checked((uint)target.Description.Height);
                renderingInfo.layerCount = 1;
                renderingInfo.colorAttachmentCount = 1;
                renderingInfo.pColorAttachments = &renderingAttachment;
                VulkanApi.vkCmdBeginRendering(_commandBuffer, &renderingInfo);

                var viewport = new VkViewport {
                    x = 0.0f,
                    y = 0.0f,
                    width = target.Description.Width,
                    height = target.Description.Height,
                    minDepth = 0.0f,
                    maxDepth = 1.0f,
                };
                var scissor = default(VkRect2D);
                scissor.extent.width = checked((uint)target.Description.Width);
                scissor.extent.height = checked((uint)target.Description.Height);
                VulkanApi.vkCmdSetViewport(_commandBuffer, 0, 1, &viewport);
                VulkanApi.vkCmdSetScissor(_commandBuffer, 0, 1, &scissor);

                VulkanBackendPipeline? currentPipeline = null;

                foreach (var command in pass.Commands)
                {
                    switch (command.Kind)
                    {
                        case GraphicsCommandKind.SetPipeline:
                        {
                            var pipeline = GraphicsGraphSubmission.GetPipeline(command) as VulkanBackendPipeline
                                ?? throw new InvalidOperationException("The graph pipeline is not a Vulkan pipeline.");
                            VulkanApi.vkCmdBindPipeline(
                                _commandBuffer,
                                VkPipelineBindPoint.VK_PIPELINE_BIND_POINT_GRAPHICS,
                                pipeline.Pipeline);
                            currentPipeline = pipeline;
                            break;
                        }

                        case GraphicsCommandKind.SetBindings:
                        {
                            var pipeline = currentPipeline
                                ?? throw new InvalidOperationException("A recorded binding set has no preceding Vulkan pipeline.");
                            var bindings = command.Bindings
                                ?? throw new InvalidOperationException("A recorded binding command lost its binding set.");
                            var uniformBufferBindings =
                                ImmutableArray.CreateBuilder<VulkanBackendUniformBufferBinding>(bindings.Buffers.Length);
                            var storageBufferBindings =
                                ImmutableArray.CreateBuilder<VulkanBackendStorageBufferBinding>(bindings.Buffers.Length);

                            foreach (var binding in bindings.Buffers)
                            {
                                var buffer = submission.GetBuffer(binding.Resource.ResourceId) as VulkanBackendBuffer
                                    ?? throw new InvalidOperationException("A Frame buffer binding is not a Vulkan buffer.");

                                if (binding.Kind == ShaderResourceKind.UniformBuffer)
                                {
                                    uniformBufferBindings.Add(new VulkanBackendUniformBufferBinding(
                                        binding.Group,
                                        binding.Binding,
                                        buffer,
                                        binding.Range.ByteOffset,
                                        binding.Range.ByteLength));
                                }
                                else
                                {
                                    storageBufferBindings.Add(new VulkanBackendStorageBufferBinding(
                                        binding.Group,
                                        binding.Binding,
                                        buffer,
                                        binding.Range.ByteOffset,
                                        binding.Range.ByteLength));
                                }
                            }

                            var sampledImageBindings =
                                ImmutableArray.CreateBuilder<VulkanBackendSampledImageBinding>(bindings.Textures.Length);
                            var storageImageBindings =
                                ImmutableArray.CreateBuilder<VulkanBackendStorageImageBinding>(bindings.Textures.Length);

                            foreach (var binding in bindings.Textures)
                            {
                                var texture = submission.GetTexture(binding.Resource.ResourceId) as VulkanBackendTexture
                                    ?? throw new InvalidOperationException("A Frame image binding is not a Vulkan texture.");

                                if (binding.Kind == ShaderResourceKind.SampledImage)
                                {
                                    sampledImageBindings.Add(new VulkanBackendSampledImageBinding(
                                        binding.Group,
                                        binding.Binding,
                                        texture,
                                        VkImageLayout.VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL));
                                }
                                else
                                {
                                    storageImageBindings.Add(new VulkanBackendStorageImageBinding(
                                        binding.Group,
                                        binding.Binding,
                                        texture,
                                        VkImageLayout.VK_IMAGE_LAYOUT_GENERAL));
                                }
                            }

                            var samplerBindings =
                                ImmutableArray.CreateBuilder<VulkanBackendSamplerBinding>(bindings.Samplers.Length);

                            foreach (var binding in bindings.Samplers)
                            {
                                var sampler = binding.Sampler.ResourceOwner.BackendResource as VulkanBackendSampler
                                    ?? throw new InvalidOperationException("A Frame sampler lost its Vulkan backend resource.");
                                samplerBindings.Add(new VulkanBackendSamplerBinding(
                                    binding.Group,
                                    binding.Binding,
                                    sampler));
                            }

                            var descriptorSet = CreateFrameDescriptorSet(
                                pipeline,
                                uniformBufferBindings.ToImmutable(),
                                storageBufferBindings.ToImmutable(),
                                sampledImageBindings.ToImmutable(),
                                storageImageBindings.ToImmutable(),
                                samplerBindings.ToImmutable());
                            descriptorSets.Add(descriptorSet);
                            var nativeDescriptorSet = descriptorSet.DescriptorSet;
                            VulkanApi.vkCmdBindDescriptorSets(
                                _commandBuffer,
                                VkPipelineBindPoint.VK_PIPELINE_BIND_POINT_GRAPHICS,
                                pipeline.PipelineLayout,
                                GetNativeDescriptorSetIndex(ShaderBindingGroup.Frame),
                                1,
                                &nativeDescriptorSet,
                                0,
                                null);
                            break;
                        }

                        case GraphicsCommandKind.StorageBarrier:
                        {
                            VulkanApi.vkCmdEndRendering(_commandBuffer);
                            renderingAttachment.loadOp = VkAttachmentLoadOp.VK_ATTACHMENT_LOAD_OP_LOAD;
                            RecordGraphStorageBarrier(pass, submission, command);
                            VulkanApi.vkCmdBeginRendering(_commandBuffer, &renderingInfo);
                            VulkanApi.vkCmdSetViewport(_commandBuffer, 0, 1, &viewport);
                            VulkanApi.vkCmdSetScissor(_commandBuffer, 0, 1, &scissor);
                            break;
                        }

                        case GraphicsCommandKind.Draw:
                        {
                            var draw = command.Draw;
                            VulkanApi.vkCmdDraw(
                                _commandBuffer,
                                checked((uint)draw.VertexCount),
                                checked((uint)draw.InstanceCount),
                                checked((uint)draw.FirstVertex),
                                checked((uint)draw.FirstInstance));
                            break;
                        }

                        default:
                        {
                            throw new NotSupportedException($"Native Vulkan graph submission does not support {command.Kind} commands.");
                        }
                    }
                }

                VulkanApi.vkCmdEndRendering(_commandBuffer);
            }

            if (currentLayout != VkImageLayout.VK_IMAGE_LAYOUT_GENERAL)
            {
                throw new InvalidOperationException("The compiled graph did not transition its offscreen target to the output layout.");
            }

            if (sampledTexture is not null
                && (sampledCurrentLayout != VulkanGraphResourceStateMapper.GetImageLayout(GraphResourceState.ShaderSampledRead)))
            {
                throw new InvalidOperationException("The compiled graph did not transition its Frame sampled image to the shader-read layout.");
            }

            if (storageTexture is not null
                && (storageCurrentLayout != VulkanGraphResourceStateMapper.GetImageLayout(GraphResourceState.StorageReadWrite)))
            {
                throw new InvalidOperationException("The compiled graph did not transition its Frame storage image to GENERAL.");
            }

            var completedFence = SubmitAndWaitCore(retainedBuffer: null, retainedTexture: null);
            target.SetLayout(subresource, currentLayout);
            target.SetLastAccessScope(subresource, currentStageMask, currentAccessMask);
            target.MarkInitialized(subresource);

            if (sampledTexture is not null)
            {
                sampledTexture.SetLayout(sampledSubresource, sampledCurrentLayout);
                sampledTexture.SetLastAccessScope(
                    sampledSubresource,
                    sampledTextureStageMask,
                    sampledTextureAccessMask);
            }

            if (storageTexture is not null)
            {
                storageTexture.SetLayout(storageSubresource, storageCurrentLayout);
                storageTexture.SetLastAccessScope(
                    storageSubresource,
                    storageTextureStageMask,
                    storageTextureAccessMask);
                storageTexture.MarkInitialized(storageSubresource);
            }

            foreach (var graphBufferScope in graphBufferScopes)
            {
                graphBufferScope.Buffer.SetLastAccessScope(
                    graphBufferScope.StageMask,
                    graphBufferScope.AccessMask);
            }

            return completedFence;
        }
        catch (Exception exception)
        {
            if (_hasUncertainSubmission)
            {
                throw;
            }

            try
            {
                StopRecording();
            }
            catch (Exception stopException)
            {
                throw new AggregateException(exception, stopException);
            }

            throw;
        }
        finally
        {
            try
            {
                if (_hasUncertainSubmission)
                {
                    RetainUncertainTexture(target);
                    _uncertainGraphImageViews.Add(imageView);
                    ownsImageView = false;

                    foreach (var descriptorSet in descriptorSets)
                    {
                        _uncertainGraphDescriptorSets.Add(descriptorSet);
                    }
                }
                else
                {
                    for (var index = descriptorSets.Count - 1; index >= 0; index--)
                    {
                        descriptorSets[index].Dispose();
                    }
                }
            }
            finally
            {
                if (ownsImageView)
                {
                    VulkanApi.vkDestroyImageView(_device, imageView, null);
                }
            }
        }
    }

    private (VkPipelineStageFlags2 StageMask, VkAccessFlags2 AccessMask) RecordGraphBufferBarrier(
        GraphicsGraphSubmissionPass pass,
        VulkanBackendBuffer buffer,
        GraphResourceStateTransition transition,
        VkPipelineStageFlags2 currentStageMask,
        VkAccessFlags2 currentAccessMask)
    {
        if (transition.AfterState is not (
            GraphResourceState.UniformRead
            or GraphResourceState.CopySource
            or GraphResourceState.CopyDestination
            or GraphResourceState.StorageWrite
            or GraphResourceState.StorageReadWrite))
        {
            throw new NotSupportedException($"Vulkan graph submission does not support a transition to {transition.AfterState} for buffers.");
        }

        VkPipelineStageFlags2 sourceStage;
        VkAccessFlags2 sourceAccess;

        if (transition.BeforeState == GraphResourceState.Undefined)
        {
            sourceStage = VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_NONE;
            sourceAccess = VkAccessFlags2.VK_ACCESS_2_NONE;
        }
        else if (transition.BeforeState is
            GraphResourceState.General
            or GraphResourceState.UniformRead
            or GraphResourceState.CopySource
            or GraphResourceState.CopyDestination
            or GraphResourceState.StorageRead
            or GraphResourceState.StorageWrite
            or GraphResourceState.StorageReadWrite)
        {
            sourceStage = currentStageMask;
            sourceAccess = currentAccessMask;
        }
        else
        {
            throw new NotSupportedException($"Vulkan graph submission does not support a transition from {transition.BeforeState} for buffers.");
        }

        var shaderStages = GetShaderStageMask(pass, transition.ResourceId);
        var destinationStage = VulkanGraphResourceStateMapper.GetStageMask(
            transition.AfterState,
            shaderStages);
        var destinationAccess = VulkanGraphResourceStateMapper.GetAccessMask(transition.AfterState);
        RecordBufferBarrier(
            buffer,
            transition.BufferRange.ByteOffset,
            checked((nint)transition.BufferRange.ByteLength),
            sourceStage,
            sourceAccess,
            destinationStage,
            destinationAccess);
        return (destinationStage, destinationAccess);
    }

    private void RecordGraphImageBarrier(
        GraphicsGraphSubmissionPass pass,
        VulkanBackendTexture texture,
        GraphResourceStateTransition transition,
        ref VkImageLayout currentLayout,
        ref VkPipelineStageFlags2 currentStageMask,
        ref VkAccessFlags2 currentAccessMask)
    {
        if (transition.BeforeState is not (
            GraphResourceState.Undefined
            or GraphResourceState.General
            or GraphResourceState.ShaderSampledRead
            or GraphResourceState.ColorAttachment
            or GraphResourceState.OffscreenOutput
            or GraphResourceState.StorageRead
            or GraphResourceState.StorageWrite
            or GraphResourceState.StorageReadWrite))
        {
            throw new NotSupportedException($"Vulkan graph submission does not support a transition from {transition.BeforeState}.");
        }

        var expectedBeforeLayout = VulkanGraphResourceStateMapper.GetImageLayout(transition.BeforeState);

        if (currentLayout != expectedBeforeLayout)
        {
            throw new InvalidOperationException("The graph transition does not match the Vulkan texture's tracked image layout.");
        }

        var afterLayout = VulkanGraphResourceStateMapper.GetImageLayout(transition.AfterState);
        var barrier = default(VkImageMemoryBarrier2);
        barrier.sType = VkStructureType.VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
        barrier.srcQueueFamilyIndex = uint.MaxValue;
        barrier.dstQueueFamilyIndex = uint.MaxValue;
        barrier.oldLayout = currentLayout;
        barrier.newLayout = afterLayout;
        barrier.image = texture.Image;
        barrier.subresourceRange.aspectMask = VkImageAspectFlags.VK_IMAGE_ASPECT_COLOR_BIT;
        barrier.subresourceRange.baseMipLevel = checked((uint)transition.TextureRange.FirstMip);
        barrier.subresourceRange.levelCount = checked((uint)transition.TextureRange.MipCount);
        barrier.subresourceRange.baseArrayLayer = checked((uint)transition.TextureRange.FirstLayer);
        barrier.subresourceRange.layerCount = checked((uint)transition.TextureRange.LayerCount);

        barrier.srcStageMask = currentStageMask;
        barrier.srcAccessMask = currentAccessMask;

        switch (transition.AfterState)
        {
            case GraphResourceState.StorageWrite:
            case GraphResourceState.StorageReadWrite:
            {
                var shaderStages = GetShaderStageMask(pass, transition.ResourceId);
                barrier.dstStageMask = VulkanGraphResourceStateMapper.GetStageMask(
                    transition.AfterState,
                    shaderStages);
                barrier.dstAccessMask = VulkanGraphResourceStateMapper.GetAccessMask(transition.AfterState);
                break;
            }

            case GraphResourceState.ShaderSampledRead:
            {
                var shaderStages = GetShaderStageMask(pass, transition.ResourceId);
                barrier.dstStageMask = VulkanGraphResourceStateMapper.GetStageMask(
                    transition.AfterState,
                    shaderStages);
                barrier.dstAccessMask = VulkanGraphResourceStateMapper.GetAccessMask(transition.AfterState);
                break;
            }

            case GraphResourceState.ColorAttachment:
            {
                barrier.dstStageMask = VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT;
                barrier.dstAccessMask = VkAccessFlags2.VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT;
                break;
            }

            case GraphResourceState.OffscreenOutput:
            {
                barrier.dstStageMask = VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_NONE;
                barrier.dstAccessMask = VkAccessFlags2.VK_ACCESS_2_NONE;
                break;
            }

            default:
            {
                throw new NotSupportedException($"Vulkan graph submission does not support a transition to {transition.AfterState}.");
            }
        }

        var dependencyInfo = default(VkDependencyInfo);
        dependencyInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
        dependencyInfo.imageMemoryBarrierCount = 1;
        dependencyInfo.pImageMemoryBarriers = &barrier;
        VulkanApi.vkCmdPipelineBarrier2(_commandBuffer, &dependencyInfo);
        currentLayout = afterLayout;

        if (transition.AfterState is
            GraphResourceState.ColorAttachment
            or GraphResourceState.ShaderSampledRead
            or GraphResourceState.StorageWrite
            or GraphResourceState.StorageReadWrite)
        {
            currentStageMask = barrier.dstStageMask;
            currentAccessMask = barrier.dstAccessMask;
        }
    }

    private void RecordGraphStorageBarrier(
        GraphicsGraphSubmissionPass pass,
        GraphicsGraphSubmission submission,
        RecordedGraphicsCommand command)
    {
        var sourceAccessMask = VkAccessFlags2.VK_ACCESS_2_NONE;

        if (command.StorageBarrierSourceReads)
        {
            sourceAccessMask |= VkAccessFlags2.VK_ACCESS_2_SHADER_STORAGE_READ_BIT;
        }

        if (command.StorageBarrierSourceWrites)
        {
            sourceAccessMask |= VkAccessFlags2.VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT;
        }

        var destinationAccessMask = VkAccessFlags2.VK_ACCESS_2_NONE;

        if (command.StorageBarrierDestinationReads)
        {
            destinationAccessMask |= VkAccessFlags2.VK_ACCESS_2_SHADER_STORAGE_READ_BIT;
        }

        if (command.StorageBarrierDestinationWrites)
        {
            destinationAccessMask |= VkAccessFlags2.VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT;
        }

        if (command.StorageBarrierBuffer is GraphBufferVersion bufferVersion)
        {
            var buffer = submission.GetBuffer(bufferVersion.ResourceId) as VulkanBackendBuffer
                ?? throw new InvalidOperationException("The graph storage barrier lost its Vulkan buffer.");
            var shaderStages = GetShaderStageMask(pass, bufferVersion.ResourceId);
            var barrier = default(VkBufferMemoryBarrier2);
            barrier.sType = VkStructureType.VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER_2;
            barrier.srcStageMask = shaderStages;
            barrier.srcAccessMask = sourceAccessMask;
            barrier.dstStageMask = shaderStages;
            barrier.dstAccessMask = destinationAccessMask;
            barrier.srcQueueFamilyIndex = uint.MaxValue;
            barrier.dstQueueFamilyIndex = uint.MaxValue;
            barrier.buffer = buffer.Buffer;
            barrier.offset = checked((ulong)command.StorageBarrierBufferRange.ByteOffset);
            barrier.size = checked((ulong)command.StorageBarrierBufferRange.ByteLength);
            var dependencyInfo = default(VkDependencyInfo);
            dependencyInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
            dependencyInfo.dependencyFlags = default;
            dependencyInfo.bufferMemoryBarrierCount = 1;
            dependencyInfo.pBufferMemoryBarriers = &barrier;
            VulkanApi.vkCmdPipelineBarrier2(_commandBuffer, &dependencyInfo);
            return;
        }

        var textureVersion = command.StorageBarrierTexture
            ?? throw new InvalidOperationException("The graph storage barrier has no Vulkan texture.");
        var texture = submission.GetTexture(textureVersion.ResourceId) as VulkanBackendTexture
            ?? throw new InvalidOperationException("The graph storage barrier lost its Vulkan texture.");
        var textureStages = GetShaderStageMask(pass, textureVersion.ResourceId);
        var imageBarrier = default(VkImageMemoryBarrier2);
        imageBarrier.sType = VkStructureType.VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
        imageBarrier.srcStageMask = textureStages;
        imageBarrier.srcAccessMask = sourceAccessMask;
        imageBarrier.dstStageMask = textureStages;
        imageBarrier.dstAccessMask = destinationAccessMask;
        imageBarrier.oldLayout = VkImageLayout.VK_IMAGE_LAYOUT_GENERAL;
        imageBarrier.newLayout = VkImageLayout.VK_IMAGE_LAYOUT_GENERAL;
        imageBarrier.srcQueueFamilyIndex = uint.MaxValue;
        imageBarrier.dstQueueFamilyIndex = uint.MaxValue;
        imageBarrier.image = texture.Image;
        imageBarrier.subresourceRange.aspectMask = VkImageAspectFlags.VK_IMAGE_ASPECT_COLOR_BIT;
        imageBarrier.subresourceRange.baseMipLevel = checked((uint)command.StorageBarrierTextureRange.FirstMip);
        imageBarrier.subresourceRange.levelCount = checked((uint)command.StorageBarrierTextureRange.MipCount);
        imageBarrier.subresourceRange.baseArrayLayer = checked((uint)command.StorageBarrierTextureRange.FirstLayer);
        imageBarrier.subresourceRange.layerCount = checked((uint)command.StorageBarrierTextureRange.LayerCount);
        var imageDependencyInfo = default(VkDependencyInfo);
        imageDependencyInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
        imageDependencyInfo.dependencyFlags = default;
        imageDependencyInfo.imageMemoryBarrierCount = 1;
        imageDependencyInfo.pImageMemoryBarriers = &imageBarrier;
        VulkanApi.vkCmdPipelineBarrier2(_commandBuffer, &imageDependencyInfo);
    }
}
