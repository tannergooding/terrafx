// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
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

    private static VkAttachmentLoadOp GetGraphAttachmentLoadOp(AttachmentLoadAction loadAction)
        => loadAction switch {
            AttachmentLoadAction.Clear => VkAttachmentLoadOp.VK_ATTACHMENT_LOAD_OP_CLEAR,
            AttachmentLoadAction.Load => VkAttachmentLoadOp.VK_ATTACHMENT_LOAD_OP_LOAD,
            AttachmentLoadAction.DontCare => VkAttachmentLoadOp.VK_ATTACHMENT_LOAD_OP_DONT_CARE,
            _ => throw new ArgumentOutOfRangeException(nameof(loadAction)),
        };

    private static VkAttachmentStoreOp GetGraphAttachmentStoreOp(AttachmentStoreAction storeAction)
        => storeAction switch {
            AttachmentStoreAction.Store => VkAttachmentStoreOp.VK_ATTACHMENT_STORE_OP_STORE,
            AttachmentStoreAction.Discard => VkAttachmentStoreOp.VK_ATTACHMENT_STORE_OP_DONT_CARE,
            _ => throw new ArgumentOutOfRangeException(nameof(storeAction)),
        };

    private static VkImageView GetGraphDepthAttachmentImageView(VkDevice device, VulkanBackendTexture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ObjectDisposedException.ThrowIf(texture.IsDisposed, texture);

        if ((texture.Description.Format != TextureFormat.D32Float)
            || ((texture.Description.Usage & TextureUsage.DepthAttachment) == 0))
        {
            throw new ArgumentException("The graph depth attachment is not a D32Float texture.", nameof(texture));
        }

        if (texture.DepthAttachmentImageView.Value == 0)
        {
            var createInfo = default(VkImageViewCreateInfo);
            createInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
            createInfo.image = texture.Image;
            createInfo.viewType = VkImageViewType.VK_IMAGE_VIEW_TYPE_2D;
            createInfo.format = VulkanNativeTypeMapper.GetVkFormat(texture.Description.Format);
            createInfo.subresourceRange.aspectMask = VkImageAspectFlags.VK_IMAGE_ASPECT_DEPTH_BIT;
            createInfo.subresourceRange.baseMipLevel = 0;
            createInfo.subresourceRange.levelCount = 1;
            createInfo.subresourceRange.baseArrayLayer = 0;
            createInfo.subresourceRange.layerCount = 1;
            var imageView = default(VkImageView);
            ThrowIfFailed(VulkanApi.vkCreateImageView(device, &createInfo, null, &imageView), "vkCreateImageView");
            texture.DepthAttachmentImageView = imageView;
        }

        return texture.DepthAttachmentImageView;
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
        submission.ValidateNativeRasterSubmission();
        EnsureOwnerThread();
        EnsureActive();
        _graphSubmissionObserver?.Invoke(submission);
        var surfaceTarget = submission.SurfaceTarget;
        VulkanBackendSurface? surface = null;

        if (surfaceTarget is not null)
        {
            surface = surfaceTarget.BackendSurface as VulkanBackendSurface
                ?? throw new NotSupportedException("Vulkan graph submission requires a Vulkan-backed Surface.");
            var surfaceSnapshot = surfaceTarget.CaptureSnapshot();

            if (!surfaceTarget.IsLive
                || (surfaceSnapshot.Capabilities.SurfaceEpoch != submission.SurfaceEpoch)
                || (surfaceSnapshot.Capabilities.SwapchainEpoch != submission.SwapchainEpoch)
                || (surfaceSnapshot.ColorFormat != submission.OutputColorTarget.Description.Format))
            {
                throw new InvalidOperationException("The compiled graph targets a stale Vulkan Surface generation.");
            }
        }

        var target = submission.GetTexture(submission.TextureImports[0].Version.ResourceId) as VulkanBackendTexture
            ?? throw new ArgumentException("The graph output target is not a Vulkan texture.", nameof(submission));
        ObjectDisposedException.ThrowIf(target.IsDisposed, nameof(submission));

        if ((target.Description.Dimension != TextureDimension.Dimension2D)
            || (target.Description.ArrayLayers < 1)
            || (target.Description.Usage & TextureUsage.ColorAttachment) == 0)
        {
            throw new NotSupportedException("Vulkan graph submission requires a 2D color-attachment texture.");
        }

        if (surface is not null
            && (!SurfaceFormatCompatibility.IsSrgbPresentationSourceCompatible(
                    target.Description.Format,
                    surface.ColorFormat)
                || ((target.Description.Usage & TextureUsage.CopySource) == 0)))
        {
            throw new NotSupportedException("Vulkan Surface graph output requires a matching sRGB source format and CopySource usage.");
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
        var colorTextureScopes = new Dictionary<ulong, (
            VulkanBackendTexture Texture,
            TextureSubresource Subresource,
            VkImageLayout Layout,
            VkPipelineStageFlags2 LastStageMask,
            VkAccessFlags2 LastAccessMask,
            VkPipelineStageFlags2 StageMask,
            VkAccessFlags2 AccessMask)>(submission.TextureImports.Length - 1);
        var depthTextureScopes = new Dictionary<ulong, (
            VulkanBackendTexture Texture,
            TextureSubresource Subresource,
            VkImageLayout Layout,
            VkPipelineStageFlags2 LastStageMask,
            VkAccessFlags2 LastAccessMask,
            VkPipelineStageFlags2 StageMask,
            VkAccessFlags2 AccessMask)>(submission.TextureImports.Length - 1);
        var sampledTextureScopes = new Dictionary<ulong, (
            VulkanBackendTexture Texture,
            TextureSubresource Subresource,
            VkImageLayout Layout,
            VkPipelineStageFlags2 LastStageMask,
            VkAccessFlags2 LastAccessMask,
            VkPipelineStageFlags2 StageMask,
            VkAccessFlags2 AccessMask)>(submission.TextureImports.Length - 1);
        var storageTextureScopes = new Dictionary<ulong, (
            VulkanBackendTexture Texture,
            TextureSubresource Subresource,
            VkImageLayout Layout,
            VkPipelineStageFlags2 LastStageMask,
            VkAccessFlags2 LastAccessMask,
            VkPipelineStageFlags2 StageMask,
            VkAccessFlags2 AccessMask)>(submission.TextureImports.Length - 1);

        for (var index = 1; index < submission.TextureImports.Length; index++)
        {
            var import = submission.TextureImports[index];

            if ((import.Version.Description.Usage & TextureUsage.ColorAttachment) != 0)
            {
                var resourceId = import.Version.ResourceId;
                var texture = submission.GetTexture(resourceId) as VulkanBackendTexture
                    ?? throw new InvalidOperationException("The graph color attachment is not a Vulkan texture.");
                var colorRange = import.Version.Range;
                var hasColorAttachmentAccess = false;

                foreach (var access in submission.Accesses)
                {
                    if (access.IsTexture
                        && (access.ResourceId == resourceId)
                        && (access.RequiredState == GraphResourceState.ColorAttachment)
                        && access.IsWrite)
                    {
                        colorRange = access.TextureRange;
                        hasColorAttachmentAccess = true;
                        break;
                    }
                }

                if (hasColorAttachmentAccess)
                {
                    var colorSubresource = new TextureSubresource(
                        colorRange.FirstMip,
                        colorRange.FirstLayer,
                        colorRange.Aspect);
                    colorTextureScopes.Add(
                        resourceId,
                        (
                            texture,
                            colorSubresource,
                            texture.GetLayout(colorSubresource),
                            texture.GetLastStageMask(colorSubresource),
                            texture.GetLastAccessMask(colorSubresource),
                            VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_NONE,
                            VkAccessFlags2.VK_ACCESS_2_NONE));
                }
            }
            else if ((import.Version.Description.Usage & TextureUsage.DepthAttachment) != 0)
            {
                var resourceId = import.Version.ResourceId;
                var texture = submission.GetTexture(resourceId) as VulkanBackendTexture
                    ?? throw new InvalidOperationException("The graph depth attachment is not a Vulkan texture.");
                var depthRange = import.Version.Range;

                foreach (var access in submission.Accesses)
                {
                    if (access.IsTexture && (access.ResourceId == resourceId))
                    {
                        depthRange = access.TextureRange;
                        break;
                    }
                }

                var depthSubresource = new TextureSubresource(
                    depthRange.FirstMip,
                    depthRange.FirstLayer,
                    depthRange.Aspect);
                depthTextureScopes.Add(
                    resourceId,
                    (
                        texture,
                        depthSubresource,
                        texture.GetLayout(depthSubresource),
                        texture.GetLastStageMask(depthSubresource),
                        texture.GetLastAccessMask(depthSubresource),
                        VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_NONE,
                        VkAccessFlags2.VK_ACCESS_2_NONE));
            }
            else if ((import.Version.Description.Usage & TextureUsage.Sampled) != 0)
            {
                var resourceId = import.Version.ResourceId;
                var texture = submission.GetTexture(resourceId) as VulkanBackendTexture
                    ?? throw new InvalidOperationException("The graph sampled image is not a Vulkan texture.");
                var sampledRange = import.Version.Range;

                foreach (var access in submission.Accesses)
                {
                    if (access.IsTexture
                        && (access.ResourceId == resourceId)
                        && (access.RequiredState == GraphResourceState.ShaderSampledRead)
                        && !access.IsWrite)
                    {
                        sampledRange = access.TextureRange;
                        break;
                    }
                }

                var sampledSubresource = new TextureSubresource(
                    sampledRange.FirstMip,
                    sampledRange.FirstLayer,
                    sampledRange.Aspect);
                sampledTextureScopes.Add(
                    resourceId,
                    (
                        texture,
                        sampledSubresource,
                        texture.GetLayout(sampledSubresource),
                        texture.GetLastStageMask(sampledSubresource),
                        texture.GetLastAccessMask(sampledSubresource),
                        VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_NONE,
                        VkAccessFlags2.VK_ACCESS_2_NONE));
            }
            else if ((import.Version.Description.Usage & TextureUsage.Storage) != 0)
            {
                var resourceId = import.Version.ResourceId;
                var texture = submission.GetTexture(resourceId) as VulkanBackendTexture
                    ?? throw new InvalidOperationException("The graph storage image is not a Vulkan texture.");
                var storageRange = import.Version.Range;

                foreach (var access in submission.Accesses)
                {
                    if (access.IsTexture
                        && (access.ResourceId == resourceId)
                        && (access.RequiredState == GraphResourceState.StorageReadWrite)
                        && access.IsStorageReadWritePair)
                    {
                        storageRange = access.TextureRange;
                        break;
                    }
                }

                var storageSubresource = new TextureSubresource(
                    storageRange.FirstMip,
                    storageRange.FirstLayer,
                    storageRange.Aspect);
                storageTextureScopes.Add(
                    resourceId,
                    (
                        texture,
                        storageSubresource,
                        texture.GetLayout(storageSubresource),
                        texture.GetLastStageMask(storageSubresource),
                        texture.GetLastAccessMask(storageSubresource),
                        VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_NONE,
                        VkAccessFlags2.VK_ACCESS_2_NONE));
            }
        }

        var graphBufferScopes = new List<(
            VulkanBackendBuffer Buffer,
            VkPipelineStageFlags2 StageMask,
            VkAccessFlags2 AccessMask)>(submission.BufferImports.Length);
        var currentBufferScopes = new Dictionary<ulong, (
            VkPipelineStageFlags2 StageMask,
            VkAccessFlags2 AccessMask)>(submission.BufferImports.Length);
        var copyTextureScopes = new Dictionary<ulong, (
            VulkanBackendTexture Texture,
            TextureSubresource Subresource,
            VkImageLayout Layout,
            VkPipelineStageFlags2 StageMask,
            VkAccessFlags2 AccessMask,
            bool IsDestination)>(submission.TextureImports.Length - 1);

        foreach (var import in submission.BufferImports)
        {
            var buffer = submission.GetBuffer(import.Version.ResourceId) as VulkanBackendBuffer
                ?? throw new InvalidOperationException("The graph buffer import is not a Vulkan buffer.");
            currentBufferScopes.Add(
                import.Version.ResourceId,
                (buffer.LastStageMask, buffer.LastAccessMask));
        }

        foreach (var import in submission.TextureImports)
        {
            var isCopySource = false;
            var isCopyDestination = false;

            foreach (var access in submission.Accesses)
            {
                if (!access.IsTexture || (access.ResourceId != import.Version.ResourceId))
                {
                    continue;
                }

                isCopySource |= (access.RequiredState == GraphResourceState.CopySource) && !access.IsWrite;
                isCopyDestination |= (access.RequiredState == GraphResourceState.CopyDestination) && access.IsWrite;
            }

            if (!isCopySource && !isCopyDestination)
            {
                continue;
            }

            var texture = submission.GetTexture(import.Version.ResourceId) as VulkanBackendTexture
                ?? throw new InvalidOperationException("The graph copy texture import is not a Vulkan texture.");
            var copySubresource = new TextureSubresource(
                import.Version.Range.FirstMip,
                import.Version.Range.FirstLayer,
                import.Version.Range.Aspect);
            copyTextureScopes.Add(
                import.Version.ResourceId,
                (
                    texture,
                    copySubresource,
                    texture.GetLayout(copySubresource),
                    texture.GetLastStageMask(copySubresource),
                    texture.GetLastAccessMask(copySubresource),
                    isCopyDestination));
        }

        void RecordGraphCopyImageBarrier(ulong resourceId, bool isDestination)
        {
            var scopeTexture = copyTextureScopes[resourceId].Texture;
            var scopeSubresource = copyTextureScopes[resourceId].Subresource;
            var scopeLayout = copyTextureScopes[resourceId].Layout;
            var scopeStageMask = copyTextureScopes[resourceId].StageMask;
            var scopeAccessMask = copyTextureScopes[resourceId].AccessMask;
            var isScopeDestination = copyTextureScopes[resourceId].IsDestination;
            var state = isDestination ? GraphResourceState.CopyDestination : GraphResourceState.CopySource;
            var expectedLayout = VulkanGraphResourceStateMapper.GetImageLayout(state);

            if (scopeLayout != expectedLayout)
            {
                throw new InvalidOperationException("The graph texture copy does not match the Vulkan texture's tracked image layout.");
            }

            var barrier = default(VkImageMemoryBarrier2);
            barrier.sType = VkStructureType.VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
            barrier.srcStageMask = scopeStageMask;
            barrier.srcAccessMask = scopeAccessMask;
            barrier.dstStageMask = VulkanGraphResourceStateMapper.GetStageMask(state);
            barrier.dstAccessMask = VulkanGraphResourceStateMapper.GetAccessMask(state);
            barrier.oldLayout = expectedLayout;
            barrier.newLayout = expectedLayout;
            barrier.srcQueueFamilyIndex = uint.MaxValue;
            barrier.dstQueueFamilyIndex = uint.MaxValue;
            barrier.image = scopeTexture.Image;
            barrier.subresourceRange.aspectMask = VkImageAspectFlags.VK_IMAGE_ASPECT_COLOR_BIT;
            barrier.subresourceRange.baseMipLevel = checked((uint)scopeSubresource.MipLevel);
            barrier.subresourceRange.levelCount = 1;
            barrier.subresourceRange.baseArrayLayer = checked((uint)scopeSubresource.ArrayLayer);
            barrier.subresourceRange.layerCount = 1;
            var dependencyInfo = default(VkDependencyInfo);
            dependencyInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
            dependencyInfo.imageMemoryBarrierCount = 1;
            dependencyInfo.pImageMemoryBarriers = &barrier;
            VulkanApi.vkCmdPipelineBarrier2(_commandBuffer, &dependencyInfo);
            copyTextureScopes[resourceId] = (
                scopeTexture,
                scopeSubresource,
                expectedLayout,
                barrier.dstStageMask,
                barrier.dstAccessMask,
                isScopeDestination);

            if (colorTextureScopes.TryGetValue(resourceId, out var copyColorTextureScope))
            {
                colorTextureScopes[resourceId] = (
                    copyColorTextureScope.Texture,
                    copyColorTextureScope.Subresource,
                    expectedLayout,
                    barrier.dstStageMask,
                    barrier.dstAccessMask,
                    copyColorTextureScope.StageMask,
                    copyColorTextureScope.AccessMask);
            }
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
                    if (colorTextureScopes.TryGetValue(access.ResourceId, out var colorTextureScope))
                    {
                        colorTextureScope.StageMask |= VulkanGraphResourceStateMapper.GetStageMask(access.RequiredState, shaderStages);
                        colorTextureScope.AccessMask |= VulkanGraphResourceStateMapper.GetAccessMask(access.RequiredState);
                        colorTextureScopes[access.ResourceId] = colorTextureScope;
                    }
                    else if (depthTextureScopes.TryGetValue(access.ResourceId, out var depthTextureScope))
                    {
                        depthTextureScope.StageMask |= VulkanGraphResourceStateMapper.GetStageMask(access.RequiredState, shaderStages);
                        depthTextureScope.AccessMask |= VulkanGraphResourceStateMapper.GetAccessMask(access.RequiredState);
                        depthTextureScopes[access.ResourceId] = depthTextureScope;
                    }
                    else if (sampledTextureScopes.TryGetValue(access.ResourceId, out var sampledTextureScope))
                    {
                        sampledTextureScope.StageMask |= VulkanGraphResourceStateMapper.GetStageMask(access.RequiredState, shaderStages);
                        sampledTextureScope.AccessMask |= VulkanGraphResourceStateMapper.GetAccessMask(access.RequiredState);
                        sampledTextureScopes[access.ResourceId] = sampledTextureScope;
                    }
                    else if (storageTextureScopes.TryGetValue(access.ResourceId, out var storageTextureScope))
                    {
                        storageTextureScope.StageMask |= VulkanGraphResourceStateMapper.GetStageMask(access.RequiredState, shaderStages);
                        storageTextureScope.AccessMask |= VulkanGraphResourceStateMapper.GetAccessMask(access.RequiredState);
                        storageTextureScopes[access.ResourceId] = storageTextureScope;
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

        _ = GetCompletedGraphFence();
        VulkanGraphFrameSlot? availableGraphFrameSlot = null;
        var availableGraphFrameSlotIndex = -1;

        for (var index = 0; index < _graphFrameSlots.Length; index++)
        {
            var graphFrameSlot = _graphFrameSlots[index];

            if (graphFrameSlot.FenceValue == 0)
            {
                availableGraphFrameSlot = graphFrameSlot;
                availableGraphFrameSlotIndex = index;
                break;
            }
        }

        var selectedGraphFrameSlot = availableGraphFrameSlot
            ?? throw new InvalidOperationException("Vulkan graph command frame slots are exhausted before graphics admission.");
        var colorAttachmentResourceIds = new List<ulong>();
        var colorAttachmentResourceIdSet = new HashSet<ulong>();

        foreach (var pass in submission.Passes)
        {
            foreach (var attachment in pass.Attachments)
            {
                if (!attachment.IsDepth && colorAttachmentResourceIdSet.Add(attachment.Version.ResourceId))
                {
                    colorAttachmentResourceIds.Add(attachment.Version.ResourceId);
                }
            }
        }

        var graphColorImageViews = new Dictionary<ulong, VkImageView>(colorAttachmentResourceIds.Count);
        var graphImageViews = new List<VkImageView>(colorAttachmentResourceIds.Count);
        var acquiredImageIndex = 0u;
        var subresource = new TextureSubresource(0, 0, TextureAspect.Color);
        var currentLayout = target.GetLayout(subresource);
        var currentStageMask = target.GetLastStageMask(subresource);
        var currentAccessMask = target.GetLastAccessMask(subresource);
        var ownsImageViews = false;
        var hasAcquiredImage = false;
        var hasStartedRecording = false;
        var graphSubmissionAccepted = false;
        var colorAttachmentStoreActions = new Dictionary<ulong, AttachmentStoreAction>(colorAttachmentResourceIds.Count);
        var depthAttachmentStoreActions = new Dictionary<ulong, AttachmentStoreAction>(submission.TextureImports.Length - 1);
        var passIndices = new Dictionary<RenderPassId, int>(submission.Passes.Length);
        var textureContentsAfterSubmission = new Dictionary<ulong, (int PassIndex, bool IsValid)>(
            submission.TextureImports.Length - 1);

        for (var passIndex = 0; passIndex < submission.Passes.Length; passIndex++)
        {
            var pass = submission.Passes[passIndex];
            passIndices.Add(pass.PassId, passIndex);

            foreach (var attachment in pass.Attachments)
            {
                var storeActions = attachment.IsDepth ? depthAttachmentStoreActions : colorAttachmentStoreActions;
                storeActions[attachment.Version.ResourceId] = attachment.StoreAction;
                textureContentsAfterSubmission[attachment.Version.ResourceId] = (
                    passIndex,
                    attachment.StoreAction == AttachmentStoreAction.Store);
            }
        }

        foreach (var access in submission.Accesses)
        {
            if (!access.IsTexture
                || !access.IsWrite
                || (access.RequiredState != GraphResourceState.CopyDestination))
            {
                continue;
            }

            var passIndex = passIndices[access.PassId];

            if (!textureContentsAfterSubmission.TryGetValue(access.ResourceId, out var contentsState)
                || (passIndex >= contentsState.PassIndex))
            {
                textureContentsAfterSubmission[access.ResourceId] = (passIndex, true);
            }
        }

        _commandBuffer = selectedGraphFrameSlot.CommandBuffer;

        try
        {
            _ = _uncertainGraphImageViews.EnsureCapacity(
                checked(_uncertainGraphImageViews.Count + colorAttachmentResourceIds.Count));
            _ = _uncertainTextureOwners.EnsureCapacity(checked(_uncertainTextureOwners.Count + 1));
            _ = descriptorSets.EnsureCapacity(descriptorSetCapacity);

            if (surface is not null)
            {
                surface.AcquireNextImage(availableGraphFrameSlotIndex, out acquiredImageIndex);
                hasAcquiredImage = true;

                if ((surface.Capabilities.SurfaceEpoch != submission.SurfaceEpoch)
                    || (surface.Capabilities.SwapchainEpoch != submission.SwapchainEpoch))
                {
                    surface.ReleaseAcquiredImage(availableGraphFrameSlotIndex, acquiredImageIndex);
                    hasAcquiredImage = false;
                    surfaceTarget?.PublishBackendState();
                    throw new InvalidOperationException("Vulkan recreated a stale swapchain while acquiring a graph image.");
                }

                _surfaceImageAcquiredObserver?.Invoke();
            }

            ownsImageViews = true;

            foreach (var resourceId in colorAttachmentResourceIds)
            {
                var texture = submission.GetTexture(resourceId) as VulkanBackendTexture
                    ?? throw new InvalidOperationException("A graph color attachment is not a Vulkan texture.");
                var imageView = CreateGraphColorImageView(_device, texture);
                graphColorImageViews.Add(resourceId, imageView);
                graphImageViews.Add(imageView);
            }

            BeginOneTimeCommands();
            hasStartedRecording = true;
            var colorRenderingAttachments = stackalloc VkRenderingAttachmentInfo[
                GraphicsGraphSubmission.MaximumNativeColorAttachmentCount];

            foreach (var pass in submission.Passes)
            {
                foreach (var aliasingBarrier in submission.AliasingBarriers)
                {
                    if (aliasingBarrier.PassId != pass.PassId)
                    {
                        continue;
                    }

                    var barrier = default(VkMemoryBarrier2);
                    barrier.sType = VkStructureType.VK_STRUCTURE_TYPE_MEMORY_BARRIER_2;
                    barrier.srcStageMask = VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT;
                    barrier.srcAccessMask = VkAccessFlags2.VK_ACCESS_2_MEMORY_READ_BIT
                        | VkAccessFlags2.VK_ACCESS_2_MEMORY_WRITE_BIT;
                    barrier.dstStageMask = VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT;
                    barrier.dstAccessMask = VkAccessFlags2.VK_ACCESS_2_MEMORY_READ_BIT
                        | VkAccessFlags2.VK_ACCESS_2_MEMORY_WRITE_BIT;
                    var dependencyInfo = default(VkDependencyInfo);
                    dependencyInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
                    dependencyInfo.memoryBarrierCount = 1;
                    dependencyInfo.pMemoryBarriers = &barrier;
                    VulkanApi.vkCmdPipelineBarrier2(_commandBuffer, &dependencyInfo);
                }

                foreach (var aliasingBarrier in submission.TextureAliasingBarriers)
                {
                    if (aliasingBarrier.PassId != pass.PassId)
                    {
                        continue;
                    }

                    var barrier = default(VkMemoryBarrier2);
                    barrier.sType = VkStructureType.VK_STRUCTURE_TYPE_MEMORY_BARRIER_2;
                    barrier.srcStageMask = VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT;
                    barrier.srcAccessMask = VkAccessFlags2.VK_ACCESS_2_MEMORY_READ_BIT
                        | VkAccessFlags2.VK_ACCESS_2_MEMORY_WRITE_BIT;
                    barrier.dstStageMask = VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT;
                    barrier.dstAccessMask = VkAccessFlags2.VK_ACCESS_2_MEMORY_READ_BIT
                        | VkAccessFlags2.VK_ACCESS_2_MEMORY_WRITE_BIT;
                    var dependencyInfo = default(VkDependencyInfo);
                    dependencyInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
                    dependencyInfo.memoryBarrierCount = 1;
                    dependencyInfo.pMemoryBarriers = &barrier;
                    VulkanApi.vkCmdPipelineBarrier2(_commandBuffer, &dependencyInfo);
                }

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
                    var (_, _, storageLayout, storageLastStageMask, storageLastAccessMask, _, _)
                        = storageTextureScopes[resourceId];
                    var sourceStage = initialUseNeedsBarrier ? storageLastStageMask : shaderStages;
                    var sourceAccess = initialUseNeedsBarrier ? storageLastAccessMask : storageAccess;

                    if (storageLayout != VulkanGraphResourceStateMapper.GetImageLayout(import.InitialState))
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

                            if (copyTextureScopes.TryGetValue(targetResourceId, out var targetCopyTextureScope))
                            {
                                copyTextureScopes[targetResourceId] = (
                                    targetCopyTextureScope.Texture,
                                    targetCopyTextureScope.Subresource,
                                    currentLayout,
                                    currentStageMask,
                                    currentAccessMask,
                                    targetCopyTextureScope.IsDestination);
                            }
                        }
                        else if (colorTextureScopes.TryGetValue(transition.ResourceId, out var colorTextureScope))
                        {
                            var (
                                colorTexture,
                                colorSubresource,
                                colorLayout,
                                colorLastStageMask,
                                colorLastAccessMask,
                                colorStageMask,
                                colorAccessMask) = colorTextureScope;
                            RecordGraphImageBarrier(
                                pass,
                                colorTexture,
                                transition,
                                ref colorLayout,
                                ref colorLastStageMask,
                                ref colorLastAccessMask);
                            colorTextureScopes[transition.ResourceId] = (
                                colorTexture,
                                colorSubresource,
                                colorLayout,
                                colorLastStageMask,
                                colorLastAccessMask,
                                colorStageMask,
                                colorAccessMask);

                            if (copyTextureScopes.TryGetValue(transition.ResourceId, out var colorCopyTextureScope))
                            {
                                copyTextureScopes[transition.ResourceId] = (
                                    colorCopyTextureScope.Texture,
                                    colorCopyTextureScope.Subresource,
                                    colorLayout,
                                    colorLastStageMask,
                                    colorLastAccessMask,
                                    colorCopyTextureScope.IsDestination);
                            }
                        }
                        else if (depthTextureScopes.TryGetValue(transition.ResourceId, out var depthTextureScope))
                        {
                            var (
                                depthTexture,
                                depthSubresource,
                                depthLayout,
                                depthLastStageMask,
                                depthLastAccessMask,
                                depthStageMask,
                                depthAccessMask) = depthTextureScope;
                            RecordGraphImageBarrier(
                                pass,
                                depthTexture,
                                transition,
                                ref depthLayout,
                                ref depthLastStageMask,
                                ref depthLastAccessMask);
                            depthTextureScopes[transition.ResourceId] = (
                                depthTexture,
                                depthSubresource,
                                depthLayout,
                                depthLastStageMask,
                                depthLastAccessMask,
                                depthStageMask,
                                depthAccessMask);
                        }
                        else if (sampledTextureScopes.TryGetValue(transition.ResourceId, out var sampledTextureScope))
                        {
                            var (
                                sampledTexture,
                                sampledSubresource,
                                sampledLayout,
                                sampledLastStageMask,
                                sampledLastAccessMask,
                                sampledStageMask,
                                sampledAccessMask) = sampledTextureScope;
                            RecordGraphImageBarrier(
                                pass,
                                sampledTexture,
                                transition,
                                ref sampledLayout,
                                ref sampledLastStageMask,
                                ref sampledLastAccessMask);
                            sampledTextureScopes[transition.ResourceId] = (
                                sampledTexture,
                                sampledSubresource,
                                sampledLayout,
                                sampledLastStageMask,
                                sampledLastAccessMask,
                                sampledStageMask,
                                sampledAccessMask);
                        }
                        else if (storageTextureScopes.TryGetValue(transition.ResourceId, out var storageTextureScope))
                        {
                            var (
                                storageTexture,
                                storageSubresource,
                                storageLayout,
                                storageLastStageMask,
                                storageLastAccessMask,
                                storageStageMask,
                                storageAccessMask) = storageTextureScope;
                            RecordGraphImageBarrier(
                                pass,
                                storageTexture,
                                transition,
                                ref storageLayout,
                                ref storageLastStageMask,
                                ref storageLastAccessMask);
                            storageTextureScopes[transition.ResourceId] = (
                                storageTexture,
                                storageSubresource,
                                storageLayout,
                                storageLastStageMask,
                                storageLastAccessMask,
                                storageStageMask,
                                storageAccessMask);
                        }
                        else if (copyTextureScopes.TryGetValue(transition.ResourceId, out var copyTextureScope))
                        {
                            var copyTextureLayout = copyTextureScope.Layout;
                            var copyTextureStageMask = copyTextureScope.StageMask;
                            var copyTextureAccessMask = copyTextureScope.AccessMask;
                            RecordGraphImageBarrier(
                                pass,
                                copyTextureScope.Texture,
                                transition,
                                ref copyTextureLayout,
                                ref copyTextureStageMask,
                                ref copyTextureAccessMask);
                            copyTextureScopes[transition.ResourceId] = (
                                copyTextureScope.Texture,
                                copyTextureScope.Subresource,
                                copyTextureLayout,
                                copyTextureStageMask,
                                copyTextureAccessMask,
                                copyTextureScope.IsDestination);
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
                        if (command.Kind == GraphicsCommandKind.CopyBuffer)
                        {
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
                        else if (command.Kind == GraphicsCommandKind.CopyTexture)
                        {
                            var sourceVersion = command.CopySourceTexture
                                ?? throw new InvalidOperationException("The graph copy command has no source texture version.");
                            var destinationVersion = command.CopyDestinationTexture
                                ?? throw new InvalidOperationException("The graph copy command has no destination texture version.");
                            var sourceTexture = copyTextureScopes[sourceVersion.ResourceId].Texture;
                            var sourceSubresource = copyTextureScopes[sourceVersion.ResourceId].Subresource;
                            var destinationTexture = copyTextureScopes[destinationVersion.ResourceId].Texture;
                            var destinationSubresource = copyTextureScopes[destinationVersion.ResourceId].Subresource;
                            RecordGraphCopyImageBarrier(sourceVersion.ResourceId, isDestination: false);
                            RecordGraphCopyImageBarrier(destinationVersion.ResourceId, isDestination: true);

                            if (sourceVersion.ResourceId == targetResourceId)
                            {
                                var (
                                    _,
                                    _,
                                    targetCopyTextureLayout,
                                    targetCopyTextureStageMask,
                                    targetCopyTextureAccessMask,
                                    _) = copyTextureScopes[targetResourceId];
                                currentLayout = targetCopyTextureLayout;
                                currentStageMask = targetCopyTextureStageMask;
                                currentAccessMask = targetCopyTextureAccessMask;
                            }

                            var depth = GraphicsDevice.GetTextureSubresourceExtent(
                                sourceVersion.Description,
                                sourceSubresource).Depth;
                            var region = default(VkImageCopy);
                            region.srcSubresource.aspectMask = VkImageAspectFlags.VK_IMAGE_ASPECT_COLOR_BIT;
                            region.srcSubresource.mipLevel = checked((uint)sourceSubresource.MipLevel);
                            region.srcSubresource.baseArrayLayer = checked((uint)sourceSubresource.ArrayLayer);
                            region.srcSubresource.layerCount = 1;
                            region.dstSubresource.aspectMask = VkImageAspectFlags.VK_IMAGE_ASPECT_COLOR_BIT;
                            region.dstSubresource.mipLevel = checked((uint)destinationSubresource.MipLevel);
                            region.dstSubresource.baseArrayLayer = checked((uint)destinationSubresource.ArrayLayer);
                            region.dstSubresource.layerCount = 1;
                            var copyRegion = command.CopyTextureRegion;
                            region.srcOffset.x = copyRegion.SourceX;
                            region.srcOffset.y = copyRegion.SourceY;
                            region.extent.width = checked((uint)copyRegion.Width);
                            region.extent.height = checked((uint)copyRegion.Height);
                            region.extent.depth = checked((uint)depth);
                            VulkanApi.vkCmdCopyImage(
                                _commandBuffer,
                                sourceTexture.Image,
                                VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
                                destinationTexture.Image,
                                VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
                                1,
                                &region);
                        }
                        else
                        {
                            throw new NotSupportedException($"Native Vulkan graph copy pass does not support {command.Kind} commands.");
                        }
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
                                var storageImageBindings =
                                    ImmutableArray.CreateBuilder<VulkanBackendStorageImageBinding>(bindings.Textures.Length);

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

                                foreach (var binding in bindings.Textures)
                                {
                                    if (binding.Kind != ShaderResourceKind.StorageImage)
                                    {
                                        throw new NotSupportedException("Native Vulkan compute graphs currently support storage-image bindings only.");
                                    }

                                    var texture = submission.GetTexture(binding.Resource.ResourceId) as VulkanBackendTexture
                                        ?? throw new InvalidOperationException("A compute storage-image binding is not a Vulkan texture.");
                                    storageImageBindings.Add(new VulkanBackendStorageImageBinding(
                                        binding.Group,
                                        binding.Binding,
                                        texture,
                                        binding.Range,
                                        VkImageLayout.VK_IMAGE_LAYOUT_GENERAL));
                                }

                                var descriptorSet = CreateFrameDescriptorSet(
                                    pipeline,
                                    [],
                                    storageBufferBindings.ToImmutable(),
                                    [],
                                    storageImageBindings.ToImmutable(),
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

                var depthAttachment = default(GraphicsGraphSubmissionAttachment);
                var colorAttachmentCount = 0;
                var hasDepthAttachment = false;
                var remainingStorageBarrierCount = pass.Commands.Count(
                    static command => command.Kind == GraphicsCommandKind.StorageBarrier);

                foreach (var attachment in pass.Attachments)
                {
                    if (attachment.IsDepth)
                    {
                        depthAttachment = attachment;
                        hasDepthAttachment = true;
                    }
                    else
                    {
                        if (colorAttachmentCount >= GraphicsGraphSubmission.MaximumNativeColorAttachmentCount)
                        {
                            throw new NotSupportedException("The graph pass exceeds the native color-attachment view capacity.");
                        }

                        var renderingAttachment = default(VkRenderingAttachmentInfo);
                        renderingAttachment.sType = VkStructureType.VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO;
                        renderingAttachment.imageView = graphColorImageViews[attachment.Version.ResourceId];
                        renderingAttachment.imageLayout = VkImageLayout.VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
                        renderingAttachment.loadOp = GetGraphAttachmentLoadOp(attachment.LoadAction);
                        renderingAttachment.storeOp = (remainingStorageBarrierCount > 0)
                            ? VkAttachmentStoreOp.VK_ATTACHMENT_STORE_OP_STORE
                            : GetGraphAttachmentStoreOp(attachment.StoreAction);

                        if (attachment.LoadAction == AttachmentLoadAction.Clear)
                        {
                            renderingAttachment.clearValue.color.float32[0] = attachment.ClearColor.X;
                            renderingAttachment.clearValue.color.float32[1] = attachment.ClearColor.Y;
                            renderingAttachment.clearValue.color.float32[2] = attachment.ClearColor.Z;
                            renderingAttachment.clearValue.color.float32[3] = attachment.ClearColor.W;
                        }

                        colorRenderingAttachments[colorAttachmentCount++] = renderingAttachment;
                    }
                }

                if (colorAttachmentCount == 0)
                {
                    throw new InvalidOperationException("The raster graph pass has no color attachment.");
                }

                var finalDepthStoreOp = hasDepthAttachment
                    ? GetGraphAttachmentStoreOp(depthAttachment.StoreAction)
                    : default;

                var depthRenderingAttachment = default(VkRenderingAttachmentInfo);

                if (hasDepthAttachment)
                {
                    var depthTexture = submission.GetTexture(depthAttachment.Version.ResourceId) as VulkanBackendTexture
                        ?? throw new InvalidOperationException("The raster graph depth attachment is not a Vulkan texture.");
                    depthRenderingAttachment.sType = VkStructureType.VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO;
                    depthRenderingAttachment.imageView = GetGraphDepthAttachmentImageView(_device, depthTexture);
                    depthRenderingAttachment.imageLayout = VulkanGraphResourceStateMapper.GetImageLayout(GraphResourceState.DepthWrite);
                    depthRenderingAttachment.loadOp = GetGraphAttachmentLoadOp(depthAttachment.LoadAction);
                    depthRenderingAttachment.storeOp = (remainingStorageBarrierCount > 0)
                        ? VkAttachmentStoreOp.VK_ATTACHMENT_STORE_OP_STORE
                        : finalDepthStoreOp;

                    if (depthAttachment.LoadAction == AttachmentLoadAction.Clear)
                    {
                        depthRenderingAttachment.clearValue.depthStencil.depth = depthAttachment.ClearDepth;
                    }
                }

                var renderingInfo = default(VkRenderingInfo);
                renderingInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_RENDERING_INFO;
                renderingInfo.renderArea.extent.width = checked((uint)target.Description.Width);
                renderingInfo.renderArea.extent.height = checked((uint)target.Description.Height);
                renderingInfo.layerCount = 1;
                renderingInfo.colorAttachmentCount = checked((uint)colorAttachmentCount);
                renderingInfo.pColorAttachments = colorRenderingAttachments;
                renderingInfo.pDepthAttachment = hasDepthAttachment ? &depthRenderingAttachment : null;
                renderingInfo.pStencilAttachment = null;
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
                                        binding.Range,
                                        pipeline.Resources.Single(resource =>
                                            (resource.Kind == ShaderResourceKind.SampledImage)
                                            && (resource.Group == binding.Group)
                                            && (resource.Binding == binding.Binding)).ImageArrayed,
                                        VkImageLayout.VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL));
                                }
                                else
                                {
                                    storageImageBindings.Add(new VulkanBackendStorageImageBinding(
                                        binding.Group,
                                        binding.Binding,
                                        texture,
                                        binding.Range,
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
                            var colorAttachmentIndex = 0;

                            foreach (var attachment in pass.Attachments)
                            {
                                if (attachment.IsDepth)
                                {
                                    continue;
                                }

                                colorRenderingAttachments[colorAttachmentIndex].loadOp =
                                    VkAttachmentLoadOp.VK_ATTACHMENT_LOAD_OP_LOAD;
                                colorRenderingAttachments[colorAttachmentIndex].storeOp = (remainingStorageBarrierCount > 1)
                                    ? VkAttachmentStoreOp.VK_ATTACHMENT_STORE_OP_STORE
                                    : GetGraphAttachmentStoreOp(attachment.StoreAction);
                                colorAttachmentIndex++;
                            }

                            if (hasDepthAttachment)
                            {
                                depthRenderingAttachment.loadOp = VkAttachmentLoadOp.VK_ATTACHMENT_LOAD_OP_LOAD;
                            }

                            RecordGraphStorageBarrier(pass, submission, command);
                            remainingStorageBarrierCount--;

                            if (hasDepthAttachment)
                            {
                                depthRenderingAttachment.storeOp = (remainingStorageBarrierCount > 0)
                                    ? VkAttachmentStoreOp.VK_ATTACHMENT_STORE_OP_STORE
                                    : finalDepthStoreOp;
                            }

                            VulkanApi.vkCmdBeginRendering(_commandBuffer, &renderingInfo);
                            VulkanApi.vkCmdSetViewport(_commandBuffer, 0, 1, &viewport);
                            VulkanApi.vkCmdSetScissor(_commandBuffer, 0, 1, &scissor);
                            break;
                        }

                        case GraphicsCommandKind.SetVertexBuffer:
                        {
                            var binding = command.VertexBuffer;
                            var buffer = submission.GetBuffer(binding.Buffer.ResourceId) as VulkanBackendBuffer
                                ?? throw new InvalidOperationException("The graph vertex binding lost its Vulkan buffer.");
                            var nativeBuffer = buffer.Buffer;
                            var offset = checked((ulong)binding.Range.ByteOffset);
                            VulkanApi.vkCmdBindVertexBuffers(
                                _commandBuffer,
                                checked((uint)binding.Slot),
                                1,
                                &nativeBuffer,
                                &offset);
                            break;
                        }

                        case GraphicsCommandKind.SetIndexBuffer:
                        {
                            var binding = command.IndexBuffer;
                            var buffer = submission.GetBuffer(binding.Buffer.ResourceId) as VulkanBackendBuffer
                                ?? throw new InvalidOperationException("The graph index binding lost its Vulkan buffer.");
                            var indexType = binding.Format switch {
                                IndexFormat.UInt16 => VkIndexType.VK_INDEX_TYPE_UINT16,
                                IndexFormat.UInt32 => VkIndexType.VK_INDEX_TYPE_UINT32,
                                _ => throw new InvalidOperationException("The graph index binding has an invalid element format."),
                            };
                            VulkanApi.vkCmdBindIndexBuffer(
                                _commandBuffer,
                                buffer.Buffer,
                                checked((ulong)binding.Range.ByteOffset),
                                indexType);
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

                        case GraphicsCommandKind.DrawIndexed:
                        {
                            var draw = command.DrawIndexed;
                            VulkanApi.vkCmdDrawIndexed(
                                _commandBuffer,
                                checked((uint)draw.IndexCount),
                                checked((uint)draw.InstanceCount),
                                checked((uint)draw.FirstIndex),
                                draw.BaseVertex,
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

            var expectedOutputLayout = (surface is null)
                ? VkImageLayout.VK_IMAGE_LAYOUT_GENERAL
                : VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL;

            if (currentLayout != expectedOutputLayout)
            {
                throw new InvalidOperationException("The compiled graph did not transition its output target to the required terminal layout.");
            }

            if (surface is not null)
            {
                RecordSurfacePresentationCopy(surface, acquiredImageIndex, target);
            }

            foreach (var (_, _, sampledLayout, _, _, _, _) in sampledTextureScopes.Values)
            {
                if (sampledLayout != VulkanGraphResourceStateMapper.GetImageLayout(GraphResourceState.ShaderSampledRead))
                {
                    throw new InvalidOperationException("The compiled graph did not transition every Frame sampled image to the shader-read layout.");
                }
            }

            foreach (var (_, _, storageLayout, _, _, _, _) in storageTextureScopes.Values)
            {
                if (storageLayout != VulkanGraphResourceStateMapper.GetImageLayout(GraphResourceState.StorageReadWrite))
                {
                    throw new InvalidOperationException("The compiled graph did not transition every Frame storage image to GENERAL.");
                }
            }

            foreach (var (resourceId, colorTextureScope) in colorTextureScopes)
            {
                var isColorAttachmentLayout =
                    colorTextureScope.Layout == VulkanGraphResourceStateMapper.GetImageLayout(GraphResourceState.ColorAttachment);
                var isCopyLayout = copyTextureScopes.TryGetValue(resourceId, out var copyTextureScope)
                    && copyTextureScope.Layout is VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL
                        or VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;

                if (!isColorAttachmentLayout && !isCopyLayout)
                {
                    throw new InvalidOperationException("The compiled graph did not leave each imported color attachment in its tracked layout.");
                }
            }

            foreach (var (resourceId, copyTextureScope) in copyTextureScopes)
            {
                if (resourceId == targetResourceId)
                {
                    continue;
                }

                var copyTextureLayout = copyTextureScope.Layout;

                if (copyTextureLayout is not (
                    VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL
                    or VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL))
                {
                    if (!colorTextureScopes.ContainsKey(resourceId)
                        || (copyTextureLayout != VulkanGraphResourceStateMapper.GetImageLayout(GraphResourceState.ColorAttachment)))
                    {
                        throw new InvalidOperationException("The compiled graph did not leave each copy texture in a transfer or color-attachment layout.");
                    }
                }
            }

            var submittedFence = (surface is null)
                ? SubmitGraphWithoutWait()
                : SubmitGraphWithoutWait(
                    surface.GetAcquireSemaphore(availableGraphFrameSlotIndex),
                    VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COPY_BIT,
                    surface.GetRenderFinishedSemaphore(acquiredImageIndex));
            selectedGraphFrameSlot.FenceValue = checked((ulong)submittedFence);
            selectedGraphFrameSlot.ImageViews = graphImageViews;
            selectedGraphFrameSlot.DescriptorSets = descriptorSets;
            ownsImageViews = false;
            graphSubmissionAccepted = true;
            target.SetLayout(subresource, currentLayout);
            target.SetLastAccessScope(subresource, currentStageMask, currentAccessMask);

            if (colorAttachmentStoreActions.TryGetValue(targetResourceId, out var targetStoreAction)
                && (targetStoreAction == AttachmentStoreAction.Store))
            {
                target.MarkInitialized(subresource);
            }
            else
            {
                target.MarkUninitialized(subresource);
            }

            foreach (var colorTextureScope in colorTextureScopes)
            {
                var (
                    colorTexture,
                    colorSubresource,
                    colorLayout,
                    _,
                    _,
                    colorStageMask,
                    colorAccessMask) = colorTextureScope.Value;
                colorTexture.SetLayout(colorSubresource, colorLayout);
                colorTexture.SetLastAccessScope(
                    colorSubresource,
                    colorStageMask,
                    colorAccessMask);

                if (colorAttachmentStoreActions.TryGetValue(
                    colorTextureScope.Key,
                    out var colorStoreAction)
                    && (colorStoreAction == AttachmentStoreAction.Store))
                {
                    colorTexture.MarkInitialized(colorSubresource);
                }
                else
                {
                    colorTexture.MarkUninitialized(colorSubresource);
                }
            }

            foreach (var depthTextureScope in depthTextureScopes)
            {
                var (
                    depthTexture,
                    depthSubresource,
                    depthLayout,
                    _,
                    _,
                    depthStageMask,
                    depthAccessMask) = depthTextureScope.Value;
                depthTexture.SetLayout(depthSubresource, depthLayout);
                depthTexture.SetLastAccessScope(
                    depthSubresource,
                    depthStageMask,
                    depthAccessMask);

                if (depthAttachmentStoreActions.TryGetValue(
                        depthTextureScope.Key,
                        out var depthStoreAction)
                    && (depthStoreAction == AttachmentStoreAction.Store))
                {
                    depthTexture.MarkInitialized(depthSubresource);
                }
                else
                {
                    depthTexture.MarkUninitialized(depthSubresource);
                }
            }

            foreach (var (
                sampledTexture,
                sampledSubresource,
                sampledLayout,
                _,
                _,
                sampledStageMask,
                sampledAccessMask) in sampledTextureScopes.Values)
            {
                sampledTexture.SetLayout(sampledSubresource, sampledLayout);
                sampledTexture.SetLastAccessScope(
                    sampledSubresource,
                    sampledStageMask,
                    sampledAccessMask);
            }

            foreach (var (
                storageTexture,
                storageSubresource,
                storageLayout,
                _,
                _,
                storageStageMask,
                storageAccessMask) in storageTextureScopes.Values)
            {
                storageTexture.SetLayout(storageSubresource, storageLayout);
                storageTexture.SetLastAccessScope(
                    storageSubresource,
                    storageStageMask,
                    storageAccessMask);
                storageTexture.MarkInitialized(storageSubresource);
            }

            foreach (var (resourceId, copyTextureScope) in copyTextureScopes)
            {
                var (
                    copyTexture,
                    copyTextureSubresource,
                    copyTextureLayout,
                    copyTextureStageMask,
                    copyTextureAccessMask,
                    copyTextureIsDestination) = copyTextureScope;
                copyTexture.SetLayout(copyTextureSubresource, copyTextureLayout);
                copyTexture.SetLastAccessScope(
                    copyTextureSubresource,
                    copyTextureStageMask,
                    copyTextureAccessMask);

                if (copyTextureIsDestination)
                {
                    if (textureContentsAfterSubmission.TryGetValue(
                        resourceId,
                        out var finalContentsState)
                        && !finalContentsState.IsValid)
                    {
                        copyTexture.MarkUninitialized(copyTextureSubresource);
                    }
                    else
                    {
                        copyTexture.MarkInitialized(copyTextureSubresource);
                    }
                }
                else if (colorAttachmentStoreActions.TryGetValue(
                    resourceId,
                    out var copySourceStoreAction))
                {
                    if (copySourceStoreAction == AttachmentStoreAction.Store)
                    {
                        copyTexture.MarkInitialized(copyTextureSubresource);
                    }
                    else
                    {
                        copyTexture.MarkUninitialized(copyTextureSubresource);
                    }
                }
            }

            foreach (var graphBufferScope in graphBufferScopes)
            {
                graphBufferScope.Buffer.SetLastAccessScope(
                    graphBufferScope.StageMask,
                    graphBufferScope.AccessMask);
            }

            if (surface is not null)
            {
                var presentationSequence = surface.Present(
                    checked((uint)availableGraphFrameSlotIndex),
                    acquiredImageIndex,
                    submission.DeviceEpoch);
                submission.SetPresentationSequence(presentationSequence);
                hasAcquiredImage = false;
            }

            return submittedFence;
        }
        catch (Exception exception)
        {
            if (graphSubmissionAccepted)
            {
                if (hasAcquiredImage)
                {
                    MarkUncertainWsiOperation();
                }

                throw;
            }

            if (_hasUncertainSubmission)
            {
                throw;
            }

            var cleanupExceptions = new List<Exception>(2);

            if (hasStartedRecording)
            {
                try
                {
                    StopRecording();
                }
                catch (Exception stopException)
                {
                    MarkUncertainWsiOperation();
                    cleanupExceptions.Add(stopException);
                }
            }

            if (hasAcquiredImage && (surface is not null))
            {
                try
                {
                    surface.ReleaseAcquiredImage(availableGraphFrameSlotIndex, acquiredImageIndex);
                    hasAcquiredImage = false;
                }
                catch (Exception releaseException)
                {
                    cleanupExceptions.Add(releaseException);
                }
            }

            if (cleanupExceptions.Count != 0)
            {
                cleanupExceptions.Insert(0, exception);
                throw new AggregateException(cleanupExceptions);
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

                    foreach (var imageView in graphImageViews)
                    {
                        _uncertainGraphImageViews.Add(imageView);
                    }

                    ownsImageViews = false;

                    foreach (var descriptorSet in descriptorSets)
                    {
                        _uncertainGraphDescriptorSets.Add(descriptorSet);
                    }
                }
                else if (!graphSubmissionAccepted)
                {
                    for (var index = descriptorSets.Count - 1; index >= 0; index--)
                    {
                        descriptorSets[index].Dispose();
                    }
                }
            }
            finally
            {
                if (ownsImageViews)
                {
                    for (var index = graphImageViews.Count - 1; index >= 0; index--)
                    {
                        VulkanApi.vkDestroyImageView(_device, graphImageViews[index], null);
                    }
                }

                _commandBuffer = _transferCommandBuffer;
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
            GraphResourceState.VertexRead
            or GraphResourceState.IndexRead
            or GraphResourceState.UniformRead
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
            or GraphResourceState.VertexRead
            or GraphResourceState.IndexRead
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
            or GraphResourceState.CopySource
            or GraphResourceState.CopyDestination
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
        barrier.subresourceRange.aspectMask = transition.TextureRange.Aspect switch {
            TextureAspect.Color => VkImageAspectFlags.VK_IMAGE_ASPECT_COLOR_BIT,
            TextureAspect.Depth => VkImageAspectFlags.VK_IMAGE_ASPECT_DEPTH_BIT,
            _ => throw new ArgumentOutOfRangeException(nameof(transition)),
        };
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

            case GraphResourceState.CopySource:
            case GraphResourceState.CopyDestination:
            {
                barrier.dstStageMask = VulkanGraphResourceStateMapper.GetStageMask(transition.AfterState);
                barrier.dstAccessMask = VulkanGraphResourceStateMapper.GetAccessMask(transition.AfterState);
                break;
            }

            case GraphResourceState.ColorAttachment:
            {
                barrier.dstStageMask = VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT;
                barrier.dstAccessMask = VkAccessFlags2.VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT;
                break;
            }

            case GraphResourceState.DepthRead:
            case GraphResourceState.DepthWrite:
            {
                barrier.dstStageMask = VulkanGraphResourceStateMapper.GetStageMask(transition.AfterState);
                barrier.dstAccessMask = VulkanGraphResourceStateMapper.GetAccessMask(transition.AfterState);
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
            or GraphResourceState.DepthRead
            or GraphResourceState.DepthWrite
            or GraphResourceState.CopySource
            or GraphResourceState.CopyDestination
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

    private void RecordSurfacePresentationCopy(
        VulkanBackendSurface surface,
        uint imageIndex,
        VulkanBackendTexture source)
    {
        var image = surface.GetSwapchainImage(imageIndex);
        var oldLayout = surface.HasImageBeenPresented(imageIndex)
            ? VkImageLayout.VK_IMAGE_LAYOUT_PRESENT_SRC_KHR
            : VkImageLayout.VK_IMAGE_LAYOUT_UNDEFINED;
        var toCopy = default(VkImageMemoryBarrier2);
        toCopy.sType = VkStructureType.VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
        toCopy.srcStageMask = VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_NONE;
        toCopy.srcAccessMask = VkAccessFlags2.VK_ACCESS_2_NONE;
        toCopy.dstStageMask = VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COPY_BIT;
        toCopy.dstAccessMask = VkAccessFlags2.VK_ACCESS_2_TRANSFER_WRITE_BIT;
        toCopy.oldLayout = oldLayout;
        toCopy.newLayout = VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
        toCopy.srcQueueFamilyIndex = uint.MaxValue;
        toCopy.dstQueueFamilyIndex = uint.MaxValue;
        toCopy.image = image;
        toCopy.subresourceRange.aspectMask = VkImageAspectFlags.VK_IMAGE_ASPECT_COLOR_BIT;
        toCopy.subresourceRange.levelCount = 1;
        toCopy.subresourceRange.layerCount = 1;
        var acquireDependency = default(VkDependencyInfo);
        acquireDependency.sType = VkStructureType.VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
        acquireDependency.imageMemoryBarrierCount = 1;
        acquireDependency.pImageMemoryBarriers = &toCopy;
        VulkanApi.vkCmdPipelineBarrier2(_commandBuffer, &acquireDependency);

        var copy = default(VkImageCopy);
        copy.srcSubresource.aspectMask = VkImageAspectFlags.VK_IMAGE_ASPECT_COLOR_BIT;
        copy.srcSubresource.layerCount = 1;
        copy.dstSubresource.aspectMask = VkImageAspectFlags.VK_IMAGE_ASPECT_COLOR_BIT;
        copy.dstSubresource.layerCount = 1;
        copy.extent.width = checked((uint)source.Description.Width);
        copy.extent.height = checked((uint)source.Description.Height);
        copy.extent.depth = 1;
        VulkanApi.vkCmdCopyImage(
            _commandBuffer,
            source.Image,
            VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
            image,
            VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
            1,
            &copy);

        var toPresent = default(VkImageMemoryBarrier2);
        toPresent.sType = VkStructureType.VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
        toPresent.srcStageMask = VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COPY_BIT;
        toPresent.srcAccessMask = VkAccessFlags2.VK_ACCESS_2_TRANSFER_WRITE_BIT;
        toPresent.dstStageMask = VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_NONE;
        toPresent.dstAccessMask = VkAccessFlags2.VK_ACCESS_2_NONE;
        toPresent.oldLayout = VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
        toPresent.newLayout = VkImageLayout.VK_IMAGE_LAYOUT_PRESENT_SRC_KHR;
        toPresent.srcQueueFamilyIndex = uint.MaxValue;
        toPresent.dstQueueFamilyIndex = uint.MaxValue;
        toPresent.image = image;
        toPresent.subresourceRange.aspectMask = VkImageAspectFlags.VK_IMAGE_ASPECT_COLOR_BIT;
        toPresent.subresourceRange.levelCount = 1;
        toPresent.subresourceRange.layerCount = 1;
        var presentDependency = default(VkDependencyInfo);
        presentDependency.sType = VkStructureType.VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
        presentDependency.imageMemoryBarrierCount = 1;
        presentDependency.pImageMemoryBarriers = &toPresent;
        VulkanApi.vkCmdPipelineBarrier2(_commandBuffer, &presentDependency);
    }

    private void RetireCompletedGraphFrameSlots(ulong completedValue)
    {
        foreach (var graphFrameSlot in _graphFrameSlots)
        {
            if ((graphFrameSlot.FenceValue == 0) || (graphFrameSlot.FenceValue > completedValue))
            {
                continue;
            }

            if (graphFrameSlot.DescriptorSets is List<VulkanBackendDescriptorSet> descriptorSets)
            {
                for (var index = descriptorSets.Count - 1; index >= 0; index--)
                {
                    descriptorSets[index].Dispose();
                }

                descriptorSets.Clear();
                graphFrameSlot.DescriptorSets = null;
            }

            if (graphFrameSlot.ImageViews is List<VkImageView> imageViews)
            {
                for (var index = imageViews.Count - 1; index >= 0; index--)
                {
                    VulkanApi.vkDestroyImageView(_device, imageViews[index], null);
                }

                imageViews.Clear();
                graphFrameSlot.ImageViews = null;
            }

            graphFrameSlot.FenceValue = 0;
        }
    }
}
