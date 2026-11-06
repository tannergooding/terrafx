// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
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

        var imageView = CreateGraphColorImageView(_device, target);
        var subresource = new TextureSubresource(0, 0, TextureAspect.Color);
        var currentLayout = target.GetLayout(subresource);
        var currentStageMask = target.GetLastStageMask(subresource);
        var currentAccessMask = target.GetLastAccessMask(subresource);
        var ownsImageView = true;

        try
        {
            _ = _uncertainGraphImageViews.EnsureCapacity(checked(_uncertainGraphImageViews.Count + 1));
            _ = _uncertainTextureOwners.EnsureCapacity(checked(_uncertainTextureOwners.Count + 1));
            BeginOneTimeCommands();

            foreach (var pass in submission.Passes)
            {
                foreach (var transition in submission.StateTransitions)
                {
                    if ((transition.PassId != pass.PassId) || !transition.IsTexture)
                    {
                        continue;
                    }

                    RecordGraphImageBarrier(
                        target,
                        transition,
                        ref currentLayout,
                        ref currentStageMask,
                        ref currentAccessMask);
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

            var completedFence = SubmitAndWaitCore(retainedBuffer: null, retainedTexture: null);
            target.SetLayout(subresource, currentLayout);
            target.SetLastAccessScope(subresource, currentStageMask, currentAccessMask);
            target.MarkInitialized(subresource);
            return completedFence;
        }
        finally
        {
            if (_hasUncertainSubmission)
            {
                RetainUncertainTexture(target);
                _uncertainGraphImageViews.Add(imageView);
                ownsImageView = false;
            }

            if (ownsImageView)
            {
                VulkanApi.vkDestroyImageView(_device, imageView, null);
            }
        }
    }

    private void RecordGraphImageBarrier(
        VulkanBackendTexture texture,
        GraphResourceStateTransition transition,
        ref VkImageLayout currentLayout,
        ref VkPipelineStageFlags2 currentStageMask,
        ref VkAccessFlags2 currentAccessMask)
    {
        if (transition.BeforeState is not (
            GraphResourceState.Undefined
            or GraphResourceState.General
            or GraphResourceState.ColorAttachment
            or GraphResourceState.OffscreenOutput))
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

        if (transition.AfterState == GraphResourceState.ColorAttachment)
        {
            currentStageMask = barrier.dstStageMask;
            currentAccessMask = barrier.dstAccessMask;
        }
    }
}
