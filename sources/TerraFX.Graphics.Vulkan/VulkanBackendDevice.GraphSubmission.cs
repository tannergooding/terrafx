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
    internal static int CountGraphDescriptorGroups(uint groupMask)
    {
        var count = 0;

        while (groupMask != 0)
        {
            count++;
            groupMask &= groupMask - 1;
        }

        return count;
    }

    internal static int CountGraphDescriptorSets(BindingSet bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        uint groupMask = 0;

        foreach (var binding in bindings.Buffers)
        {
            groupMask |= GetGraphDescriptorGroupBit(binding.Group);
        }

        foreach (var binding in bindings.Textures)
        {
            groupMask |= GetGraphDescriptorGroupBit(binding.Group);
        }

        foreach (var binding in bindings.Samplers)
        {
            groupMask |= GetGraphDescriptorGroupBit(binding.Group);
        }

        return CountGraphDescriptorGroups(groupMask);
    }

    internal static uint GetGraphDescriptorGroupBit(ShaderBindingGroup group)
        => group switch {
            ShaderBindingGroup.Frame => 1u << 0,
            ShaderBindingGroup.Material => 1u << 1,
            ShaderBindingGroup.ObjectData => 1u << 2,
            ShaderBindingGroup.Pass => 1u << 3,
            _ => throw new ArgumentOutOfRangeException(nameof(group)),
        };

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

    private static VkPipelineStageFlags2 GetShaderStageMask(
        ReadOnlySpan<RecordedGraphicsCommand> commands,
        ulong resourceId)
    {
        var stageMask = VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_NONE;
        GpuPipeline? pipeline = null;

        foreach (var command in commands)
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
            var commandReadState = 0;
            using var commandRead = pass.ReadCommands(ref commandReadState);

            foreach (var command in commandRead.Commands)
            {
                if (command.Kind == GraphicsCommandKind.SetBindings)
                {
                    var bindings = command.Bindings
                        ?? throw new InvalidOperationException("A recorded binding command lost its binding set.");
                    descriptorSetCapacity = checked(
                        descriptorSetCapacity + CountGraphDescriptorSets(bindings));
                }
            }
        }

        var descriptorSets = new VulkanBackendDescriptorSet[descriptorSetCapacity];
        var descriptorSetCount = 0;
        _ = _uncertainGraphDescriptorSets.EnsureCapacity(
            checked(_uncertainGraphDescriptorSets.Count + descriptorSetCapacity));
        var targetResourceId = submission.TextureImports[0].Version.ResourceId;
        var textureScopes = new VulkanGraphTextureScope[submission.TextureImports.Length];

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
                var hasSampledAccess = false;

                foreach (var access in submission.Accesses)
                {
                    if (!access.IsTexture || (access.ResourceId != resourceId))
                    {
                        continue;
                    }

                    if ((access.RequiredState == GraphResourceState.ColorAttachment) && access.IsWrite)
                    {
                        colorRange = access.TextureRange;
                        hasColorAttachmentAccess = true;
                    }

                    hasSampledAccess |= (access.RequiredState == GraphResourceState.ShaderSampledRead) && !access.IsWrite;
                }

                if (hasColorAttachmentAccess)
                {
                    var colorSubresource = new TextureSubresource(
                        colorRange.FirstMip,
                        colorRange.FirstLayer,
                        colorRange.Aspect);
                    textureScopes[index] = new VulkanGraphTextureScope {
                        Kind = VulkanGraphTextureScopeKind.Color,
                        Texture = texture,
                        Subresource = colorSubresource,
                        Layout = texture.GetLayout(colorSubresource),
                        LastStageMask = texture.GetLastStageMask(colorSubresource),
                        LastAccessMask = texture.GetLastAccessMask(colorSubresource),
                        StageMask = VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_NONE,
                        AccessMask = VkAccessFlags2.VK_ACCESS_2_NONE,
                        HasSampledAccess = hasSampledAccess,
                    };
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
                textureScopes[index] = new VulkanGraphTextureScope {
                    Kind = VulkanGraphTextureScopeKind.Depth,
                    Texture = texture,
                    Subresource = depthSubresource,
                    Layout = texture.GetLayout(depthSubresource),
                    LastStageMask = texture.GetLastStageMask(depthSubresource),
                    LastAccessMask = texture.GetLastAccessMask(depthSubresource),
                    StageMask = VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_NONE,
                    AccessMask = VkAccessFlags2.VK_ACCESS_2_NONE,
                };
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
                textureScopes[index] = new VulkanGraphTextureScope {
                    Kind = VulkanGraphTextureScopeKind.Sampled,
                    Texture = texture,
                    Subresource = sampledSubresource,
                    Layout = texture.GetLayout(sampledSubresource),
                    LastStageMask = texture.GetLastStageMask(sampledSubresource),
                    LastAccessMask = texture.GetLastAccessMask(sampledSubresource),
                    StageMask = VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_NONE,
                    AccessMask = VkAccessFlags2.VK_ACCESS_2_NONE,
                };
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
                textureScopes[index] = new VulkanGraphTextureScope {
                    Kind = VulkanGraphTextureScopeKind.Storage,
                    Texture = texture,
                    Subresource = storageSubresource,
                    Layout = texture.GetLayout(storageSubresource),
                    LastStageMask = texture.GetLastStageMask(storageSubresource),
                    LastAccessMask = texture.GetLastAccessMask(storageSubresource),
                    StageMask = VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_NONE,
                    AccessMask = VkAccessFlags2.VK_ACCESS_2_NONE,
                };
            }
        }

        var graphBufferScopes = new (
            VulkanBackendBuffer Buffer,
            VkPipelineStageFlags2 StageMask,
            VkAccessFlags2 AccessMask)[submission.BufferImports.Length];
        var graphBufferScopeCount = 0;
        var currentBufferScopes = new (
            VkPipelineStageFlags2 StageMask,
            VkAccessFlags2 AccessMask)[submission.BufferImports.Length];

        for (var index = 0; index < submission.BufferImports.Length; index++)
        {
            var import = submission.BufferImports[index];
            var buffer = submission.GetBuffer(import.Version.ResourceId) as VulkanBackendBuffer
                ?? throw new InvalidOperationException("The graph buffer import is not a Vulkan buffer.");
            currentBufferScopes[index] = (buffer.LastStageMask, buffer.LastAccessMask);
        }

        for (var index = 0; index < submission.TextureImports.Length; index++)
        {
            var import = submission.TextureImports[index];
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
            var scope = textureScopes[index];
            scope.Texture = texture;
            scope.HasCopyScope = true;
            scope.CopySubresource = copySubresource;
            scope.CopyLayout = texture.GetLayout(copySubresource);
            scope.CopyStageMask = texture.GetLastStageMask(copySubresource);
            scope.CopyAccessMask = texture.GetLastAccessMask(copySubresource);
            scope.IsCopyDestination = isCopyDestination;
            textureScopes[index] = scope;
        }

        void RecordGraphCopyImageBarrier(ulong resourceId, bool isDestination)
        {
            var scopeIndex = submission.GetTextureImportIndex(resourceId);
            var scope = textureScopes[scopeIndex];

            if (!scope.HasCopyScope)
            {
                throw new KeyNotFoundException("The graph texture is not in the Vulkan copy scope index.");
            }

            var scopeTexture = scope.Texture
                ?? throw new InvalidOperationException("The graph copy texture scope lost its Vulkan texture.");
            var scopeSubresource = scope.CopySubresource;
            var scopeLayout = scope.CopyLayout;
            var scopeStageMask = scope.CopyStageMask;
            var scopeAccessMask = scope.CopyAccessMask;
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
            scope.CopyLayout = expectedLayout;
            scope.CopyStageMask = barrier.dstStageMask;
            scope.CopyAccessMask = barrier.dstAccessMask;

            if (scope.Kind == VulkanGraphTextureScopeKind.Color)
            {
                scope.Layout = expectedLayout;
                scope.LastStageMask = barrier.dstStageMask;
                scope.LastAccessMask = barrier.dstAccessMask;
            }

            textureScopes[scopeIndex] = scope;
        }

        foreach (var pass in submission.Passes)
        {
            var commandReadState = 0;
            using var commandRead = pass.ReadCommands(ref commandReadState);

            foreach (var access in submission.Accesses)
            {
                if (access.PassId != pass.PassId)
                {
                    continue;
                }

                var shaderStages = GetShaderStageMask(commandRead.Commands, access.ResourceId);

                if (access.IsTexture)
                {
                    var textureIndex = submission.GetTextureImportIndex(access.ResourceId);
                    var textureScope = textureScopes[textureIndex];

                    if (textureScope.Kind != VulkanGraphTextureScopeKind.None)
                    {
                        textureScope.StageMask |= VulkanGraphResourceStateMapper.GetStageMask(access.RequiredState, shaderStages);
                        textureScope.AccessMask |= VulkanGraphResourceStateMapper.GetAccessMask(access.RequiredState);
                        textureScopes[textureIndex] = textureScope;
                    }

                    continue;
                }

                var graphBuffer = submission.GetBuffer(access.ResourceId) as VulkanBackendBuffer
                    ?? throw new InvalidOperationException("The graph uniform buffer is not a Vulkan buffer.");
                var graphBufferStageMask = VulkanGraphResourceStateMapper.GetStageMask(access.RequiredState, shaderStages);
                var graphBufferAccessMask = VulkanGraphResourceStateMapper.GetAccessMask(access.RequiredState);
                var existingScopeIndex = -1;

                for (var index = 0; index < graphBufferScopeCount; index++)
                {
                    if (ReferenceEquals(graphBufferScopes[index].Buffer, graphBuffer))
                    {
                        existingScopeIndex = index;
                        break;
                    }
                }

                if (existingScopeIndex < 0)
                {
                    graphBufferScopes[graphBufferScopeCount] = (
                        graphBuffer,
                        graphBufferStageMask,
                        graphBufferAccessMask);
                    graphBufferScopeCount++;
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
        var colorAttachmentResourceCount = submission.ColorAttachmentResourceCount;

        var graphColorImageViews = new VkImageView[colorAttachmentResourceCount];
        var graphImageViews = new VkImageView[colorAttachmentResourceCount];
        var graphImageViewCount = 0;
        var acquiredImageIndex = 0u;
        var subresource = new TextureSubresource(0, 0, TextureAspect.Color);
        var currentLayout = target.GetLayout(subresource);
        var currentStageMask = target.GetLastStageMask(subresource);
        var currentAccessMask = target.GetLastAccessMask(subresource);
        var ownsImageViews = false;
        var hasAcquiredImage = false;
        var hasStartedRecording = false;
        var graphSubmissionAccepted = false;

        for (var passIndex = 0; passIndex < submission.Passes.Length; passIndex++)
        {
            var pass = submission.Passes[passIndex];

            foreach (var attachment in pass.Attachments)
            {
                var textureIndex = submission.GetTextureImportIndex(attachment.Version.ResourceId);
                var textureScope = textureScopes[textureIndex];

                if (attachment.IsDepth)
                {
                    textureScope.DepthStoreAction = attachment.StoreAction;
                    textureScope.HasDepthAttachmentStoreAction = true;
                }
                else
                {
                    textureScope.ColorStoreAction = attachment.StoreAction;
                    textureScope.HasColorAttachmentStoreAction = true;
                }

                textureScope.HasContentsState = true;
                textureScope.ContentsPassIndex = passIndex;
                textureScope.ContentsValid = attachment.StoreAction == AttachmentStoreAction.Store;
                textureScopes[textureIndex] = textureScope;
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

            var passIndex = submission.GetSubmissionPassIndex(access.PassId);
            var textureIndex = submission.GetTextureImportIndex(access.ResourceId);
            var contentsScope = textureScopes[textureIndex];

            if (!contentsScope.HasContentsState || (passIndex >= contentsScope.ContentsPassIndex))
            {
                contentsScope.HasContentsState = true;
                contentsScope.ContentsPassIndex = passIndex;
                contentsScope.ContentsValid = true;
                textureScopes[textureIndex] = contentsScope;
            }
        }

        _commandBuffer = selectedGraphFrameSlot.CommandBuffer;

        try
        {
            _ = _uncertainGraphImageViews.EnsureCapacity(
                checked(_uncertainGraphImageViews.Count + colorAttachmentResourceCount));
            _ = _uncertainTextureOwners.EnsureCapacity(checked(_uncertainTextureOwners.Count + 1));

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

            for (var index = 0; index < colorAttachmentResourceCount; index++)
            {
                var resourceId = submission.GetColorAttachmentResourceId(index);
                var texture = submission.GetTexture(resourceId) as VulkanBackendTexture
                    ?? throw new InvalidOperationException("A graph color attachment is not a Vulkan texture.");
                var imageView = CreateGraphColorImageView(_device, texture);
                graphColorImageViews[index] = imageView;
                graphImageViews[graphImageViewCount] = imageView;
                graphImageViewCount++;
            }

            BeginOneTimeCommands();
            hasStartedRecording = true;
            var colorRenderingAttachments = stackalloc VkRenderingAttachmentInfo[
                GraphicsGraphSubmission.MaximumNativeColorAttachmentCount];

            foreach (var pass in submission.Passes)
            {
                var commandReadState = 0;
                using var commandRead = pass.ReadCommands(ref commandReadState);
                var commands = commandRead.Commands;

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
                    var shaderStages = GetShaderStageMask(commands, resourceId);
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
                    currentBufferScopes[submission.GetBufferImportIndex(resourceId)] = (shaderStages, storageAccess);
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
                    var shaderStages = GetShaderStageMask(commands, resourceId);
                    var storageAccess = VulkanGraphResourceStateMapper.GetAccessMask(GraphResourceState.StorageReadWrite);
                    var textureIndex = submission.GetTextureImportIndex(resourceId);
                    var textureScope = textureScopes[textureIndex];

                    if (textureScope.Kind != VulkanGraphTextureScopeKind.Storage)
                    {
                        throw new KeyNotFoundException("The graph texture is not in the Vulkan storage scope index.");
                    }

                    var sourceStage = initialUseNeedsBarrier ? textureScope.LastStageMask : shaderStages;
                    var sourceAccess = initialUseNeedsBarrier ? textureScope.LastAccessMask : storageAccess;

                    if (textureScope.Layout != VulkanGraphResourceStateMapper.GetImageLayout(import.InitialState))
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
                                commands,
                                target,
                                transition,
                                ref currentLayout,
                                ref currentStageMask,
                                ref currentAccessMask);

                            var targetScopeIndex = submission.GetTextureImportIndex(targetResourceId);
                            var targetScope = textureScopes[targetScopeIndex];

                            if (targetScope.HasCopyScope)
                            {
                                targetScope.CopyLayout = currentLayout;
                                targetScope.CopyStageMask = currentStageMask;
                                targetScope.CopyAccessMask = currentAccessMask;
                                textureScopes[targetScopeIndex] = targetScope;
                            }
                        }
                        else
                        {
                            var textureIndex = submission.GetTextureImportIndex(transition.ResourceId);
                            var textureScope = textureScopes[textureIndex];

                            if (textureScope.Kind != VulkanGraphTextureScopeKind.None)
                            {
                                var texture = textureScope.Texture
                                    ?? throw new InvalidOperationException("The graph texture scope lost its Vulkan texture.");
                                RecordGraphImageBarrier(
                                    commands,
                                    texture,
                                    transition,
                                    ref textureScope.Layout,
                                    ref textureScope.LastStageMask,
                                    ref textureScope.LastAccessMask);

                                if (textureScope.Kind == VulkanGraphTextureScopeKind.Color
                                    && textureScope.HasCopyScope)
                                {
                                    textureScope.CopyLayout = textureScope.Layout;
                                    textureScope.CopyStageMask = textureScope.LastStageMask;
                                    textureScope.CopyAccessMask = textureScope.LastAccessMask;
                                }

                                textureScopes[textureIndex] = textureScope;
                            }
                            else if (textureScope.HasCopyScope)
                            {
                                var texture = textureScope.Texture
                                    ?? throw new InvalidOperationException("The graph copy texture scope lost its Vulkan texture.");
                                RecordGraphImageBarrier(
                                    commands,
                                    texture,
                                    transition,
                                    ref textureScope.CopyLayout,
                                    ref textureScope.CopyStageMask,
                                    ref textureScope.CopyAccessMask);
                                textureScopes[textureIndex] = textureScope;
                            }
                            else
                            {
                                throw new NotSupportedException("The graph transition references an unsupported Vulkan texture.");
                            }
                        }
                    }
                    else
                    {
                        var buffer = submission.GetBuffer(transition.ResourceId) as VulkanBackendBuffer
                            ?? throw new InvalidOperationException("The graph transition lost its Vulkan buffer.");
                        var bufferImportIndex = submission.GetBufferImportIndex(transition.ResourceId);
                        var (bufferScopeStageMask, bufferScopeAccessMask) = currentBufferScopes[bufferImportIndex];
                        currentBufferScopes[bufferImportIndex] = RecordGraphBufferBarrier(
                            commands,
                            buffer,
                            transition,
                            bufferScopeStageMask,
                            bufferScopeAccessMask);
                    }
                }

                if (pass.Kind == CommandRecorderPassKind.Copy)
                {
                    foreach (var command in commands)
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
                            var sourceScopeIndex = submission.GetTextureImportIndex(sourceVersion.ResourceId);
                            var sourceScope = textureScopes[sourceScopeIndex];

                            if (!sourceScope.HasCopyScope)
                            {
                                throw new KeyNotFoundException("The graph copy source is not in the Vulkan copy scope index.");
                            }

                            var sourceTexture = sourceScope.Texture
                                ?? throw new InvalidOperationException("The graph copy source scope lost its Vulkan texture.");
                            var sourceSubresource = sourceScope.CopySubresource;
                            var destinationScopeIndex = submission.GetTextureImportIndex(destinationVersion.ResourceId);
                            var destinationScope = textureScopes[destinationScopeIndex];

                            if (!destinationScope.HasCopyScope)
                            {
                                throw new KeyNotFoundException("The graph copy destination is not in the Vulkan copy scope index.");
                            }

                            var destinationTexture = destinationScope.Texture
                                ?? throw new InvalidOperationException("The graph copy destination scope lost its Vulkan texture.");
                            var destinationSubresource = destinationScope.CopySubresource;
                            RecordGraphCopyImageBarrier(sourceVersion.ResourceId, isDestination: false);
                            RecordGraphCopyImageBarrier(destinationVersion.ResourceId, isDestination: true);

                            if (sourceVersion.ResourceId == targetResourceId)
                            {
                                var targetScope = textureScopes[sourceScopeIndex];
                                currentLayout = targetScope.CopyLayout;
                                currentStageMask = targetScope.CopyStageMask;
                                currentAccessMask = targetScope.CopyAccessMask;
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

                    foreach (var command in commands)
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
                                        ?? throw new InvalidOperationException("A compute graph storage-buffer binding is not a Vulkan buffer.");
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
                                        ?? throw new InvalidOperationException("A compute graph storage-image binding is not a Vulkan texture.");
                                    storageImageBindings.Add(new VulkanBackendStorageImageBinding(
                                        binding.Group,
                                        binding.Binding,
                                        texture,
                                        binding.Range,
                                        VkImageLayout.VK_IMAGE_LAYOUT_GENERAL));
                                }

                                BindGraphDescriptorSets(
                                    pipeline,
                                    VkPipelineBindPoint.VK_PIPELINE_BIND_POINT_COMPUTE,
                                    [],
                                    storageBufferBindings.ToImmutable(),
                                    [],
                                    storageImageBindings.ToImmutable(),
                                    [],
                                    descriptorSets,
                                    ref descriptorSetCount);
                                break;
                            }

                            case GraphicsCommandKind.StorageBarrier:
                            {
                                RecordGraphStorageBarrier(commands, submission, command);
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
                var remainingStorageBarrierCount = 0;

                foreach (var command in commands)
                {
                    if (command.Kind == GraphicsCommandKind.StorageBarrier)
                    {
                        remainingStorageBarrierCount++;
                    }
                }

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
                        renderingAttachment.imageView =
                            graphColorImageViews[submission.GetColorAttachmentResourceIndex(attachment.Version.ResourceId)];
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
                var renderAreaX = pass.RenderArea?.X ?? 0;
                var renderAreaY = pass.RenderArea?.Y ?? 0;
                var renderAreaWidth = pass.RenderArea?.Width ?? target.Description.Width;
                var renderAreaHeight = pass.RenderArea?.Height ?? target.Description.Height;
                renderingInfo.renderArea.offset.x = renderAreaX;
                renderingInfo.renderArea.offset.y = renderAreaY;
                renderingInfo.renderArea.extent.width = checked((uint)renderAreaWidth);
                renderingInfo.renderArea.extent.height = checked((uint)renderAreaHeight);
                renderingInfo.layerCount = 1;
                renderingInfo.colorAttachmentCount = checked((uint)colorAttachmentCount);
                renderingInfo.pColorAttachments = colorRenderingAttachments;
                renderingInfo.pDepthAttachment = hasDepthAttachment ? &depthRenderingAttachment : null;
                renderingInfo.pStencilAttachment = null;
                VulkanApi.vkCmdBeginRendering(_commandBuffer, &renderingInfo);

                var viewport = new VkViewport {
                    x = renderAreaX,
                    // A negative height keeps Vulkan's front-face convention aligned with D3D12's +Y-up projection.
                    y = renderAreaY + renderAreaHeight,
                    width = renderAreaWidth,
                    height = -renderAreaHeight,
                    minDepth = 0.0f,
                    maxDepth = 1.0f,
                };
                var scissor = default(VkRect2D);
                scissor.offset.x = renderAreaX;
                scissor.offset.y = renderAreaY;
                scissor.extent.width = checked((uint)renderAreaWidth);
                scissor.extent.height = checked((uint)renderAreaHeight);
                VulkanApi.vkCmdSetViewport(_commandBuffer, 0, 1, &viewport);
                VulkanApi.vkCmdSetScissor(_commandBuffer, 0, 1, &scissor);

                VulkanBackendPipeline? currentPipeline = null;

                foreach (var command in commands)
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
                                    ?? throw new InvalidOperationException("A graph buffer binding is not a Vulkan buffer.");

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
                                    ?? throw new InvalidOperationException("A graph image binding is not a Vulkan texture.");

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
                                        VulkanGraphResourceStateMapper.GetTextureImageLayout(
                                            GraphResourceState.ShaderSampledRead,
                                            texture.Description.Format,
                                            binding.Range.Aspect)));
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
                                    ?? throw new InvalidOperationException("A graph sampler lost its Vulkan backend resource.");
                                samplerBindings.Add(new VulkanBackendSamplerBinding(
                                    binding.Group,
                                    binding.Binding,
                                    sampler));
                            }

                            BindGraphDescriptorSets(
                                pipeline,
                                VkPipelineBindPoint.VK_PIPELINE_BIND_POINT_GRAPHICS,
                                uniformBufferBindings.ToImmutable(),
                                storageBufferBindings.ToImmutable(),
                                sampledImageBindings.ToImmutable(),
                                storageImageBindings.ToImmutable(),
                                samplerBindings.ToImmutable(),
                                descriptorSets,
                                ref descriptorSetCount);
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

                            RecordGraphStorageBarrier(commands, submission, command);
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

            for (var index = 1; index < textureScopes.Length; index++)
            {
                var textureScope = textureScopes[index];

                if ((textureScope.Kind == VulkanGraphTextureScopeKind.Sampled)
                    && (textureScope.Layout != VulkanGraphResourceStateMapper.GetImageLayout(GraphResourceState.ShaderSampledRead)))
                {
                    throw new InvalidOperationException("The compiled graph did not transition every sampled image to the shader-read layout.");
                }
            }

            for (var index = 1; index < textureScopes.Length; index++)
            {
                var textureScope = textureScopes[index];

                if ((textureScope.Kind == VulkanGraphTextureScopeKind.Storage)
                    && (textureScope.Layout != VulkanGraphResourceStateMapper.GetImageLayout(GraphResourceState.StorageReadWrite)))
                {
                    throw new InvalidOperationException("The compiled graph did not transition every storage image to GENERAL.");
                }
            }

            for (var index = 1; index < textureScopes.Length; index++)
            {
                var textureScope = textureScopes[index];

                if (textureScope.Kind != VulkanGraphTextureScopeKind.Color)
                {
                    continue;
                }

                var isColorAttachmentLayout =
                    textureScope.Layout == VulkanGraphResourceStateMapper.GetImageLayout(GraphResourceState.ColorAttachment);
                // A tone-map pass may sample a transient HDR color attachment after its final color write.
                var isSampledLayout = textureScope.HasSampledAccess
                    && (textureScope.Layout == VulkanGraphResourceStateMapper.GetImageLayout(GraphResourceState.ShaderSampledRead));
                var isCopyLayout = textureScope.HasCopyScope
                    && textureScope.CopyLayout is VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL
                        or VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;

                if (!isColorAttachmentLayout && !isSampledLayout && !isCopyLayout)
                {
                    throw new InvalidOperationException("The compiled graph did not leave each imported color attachment in its tracked layout.");
                }
            }

            for (var index = 0; index < textureScopes.Length; index++)
            {
                var textureScope = textureScopes[index];
                var resourceId = submission.TextureImports[index].Version.ResourceId;

                if (!textureScope.HasCopyScope)
                {
                    continue;
                }

                if (resourceId == targetResourceId)
                {
                    continue;
                }

                var copyTextureLayout = textureScope.CopyLayout;

                if (copyTextureLayout is not (
                    VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL
                    or VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL))
                {
                    if ((textureScope.Kind != VulkanGraphTextureScopeKind.Color)
                        || (copyTextureLayout != VulkanGraphResourceStateMapper.GetImageLayout(GraphResourceState.ColorAttachment)))
                    {
                        throw new InvalidOperationException("The compiled graph did not leave each copy texture in a transfer or color-attachment layout.");
                    }
                }
            }

            if (descriptorSetCount != descriptorSetCapacity)
            {
                throw new InvalidOperationException("The graph descriptor-set preflight count did not match binding emission.");
            }

            var submittedFence = (surface is null)
                ? SubmitGraphWithoutWait()
                : SubmitGraphWithoutWait(
                    surface.GetAcquireSemaphore(availableGraphFrameSlotIndex),
                    VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COPY_BIT,
                    surface.GetRenderFinishedSemaphore(acquiredImageIndex));
            selectedGraphFrameSlot.FenceValue = checked((ulong)submittedFence);
            selectedGraphFrameSlot.ImageViews = graphImageViews;
            selectedGraphFrameSlot.ImageViewCount = graphImageViewCount;
            selectedGraphFrameSlot.DescriptorSets = descriptorSets;
            selectedGraphFrameSlot.DescriptorSetCount = descriptorSetCount;
            ownsImageViews = false;
            graphSubmissionAccepted = true;
            target.SetLayout(subresource, currentLayout);
            target.SetLastAccessScope(subresource, currentStageMask, currentAccessMask);

            var targetContentsScope = textureScopes[submission.GetTextureImportIndex(targetResourceId)];

            if (targetContentsScope.HasColorAttachmentStoreAction
                && (targetContentsScope.ColorStoreAction == AttachmentStoreAction.Store))
            {
                target.MarkInitialized(subresource);
            }
            else
            {
                target.MarkUninitialized(subresource);
            }

            for (var index = 1; index < textureScopes.Length; index++)
            {
                var textureScope = textureScopes[index];

                if (textureScope.Kind != VulkanGraphTextureScopeKind.Color)
                {
                    continue;
                }

                var texture = textureScope.Texture
                    ?? throw new InvalidOperationException("The graph color scope lost its Vulkan texture.");
                texture.SetLayout(textureScope.Subresource, textureScope.Layout);
                texture.SetLastAccessScope(
                    textureScope.Subresource,
                    textureScope.StageMask,
                    textureScope.AccessMask);

                if (textureScope.HasColorAttachmentStoreAction
                    && (textureScope.ColorStoreAction == AttachmentStoreAction.Store))
                {
                    texture.MarkInitialized(textureScope.Subresource);
                }
                else
                {
                    texture.MarkUninitialized(textureScope.Subresource);
                }
            }

            for (var index = 1; index < textureScopes.Length; index++)
            {
                var textureScope = textureScopes[index];

                if (textureScope.Kind != VulkanGraphTextureScopeKind.Depth)
                {
                    continue;
                }

                var texture = textureScope.Texture
                    ?? throw new InvalidOperationException("The graph depth scope lost its Vulkan texture.");
                texture.SetLayout(textureScope.Subresource, textureScope.Layout);
                texture.SetLastAccessScope(
                    textureScope.Subresource,
                    textureScope.StageMask,
                    textureScope.AccessMask);

                if (textureScope.HasDepthAttachmentStoreAction
                    && (textureScope.DepthStoreAction == AttachmentStoreAction.Store))
                {
                    texture.MarkInitialized(textureScope.Subresource);
                }
                else
                {
                    texture.MarkUninitialized(textureScope.Subresource);
                }
            }

            for (var index = 1; index < textureScopes.Length; index++)
            {
                var textureScope = textureScopes[index];

                if (textureScope.Kind != VulkanGraphTextureScopeKind.Sampled)
                {
                    continue;
                }

                var texture = textureScope.Texture
                    ?? throw new InvalidOperationException("The graph sampled scope lost its Vulkan texture.");
                texture.SetLayout(textureScope.Subresource, textureScope.Layout);
                texture.SetLastAccessScope(
                    textureScope.Subresource,
                    textureScope.StageMask,
                    textureScope.AccessMask);
            }

            for (var index = 1; index < textureScopes.Length; index++)
            {
                var textureScope = textureScopes[index];

                if (textureScope.Kind != VulkanGraphTextureScopeKind.Storage)
                {
                    continue;
                }

                var texture = textureScope.Texture
                    ?? throw new InvalidOperationException("The graph storage scope lost its Vulkan texture.");
                texture.SetLayout(textureScope.Subresource, textureScope.Layout);
                texture.SetLastAccessScope(
                    textureScope.Subresource,
                    textureScope.StageMask,
                    textureScope.AccessMask);
                texture.MarkInitialized(textureScope.Subresource);
            }

            for (var index = 0; index < textureScopes.Length; index++)
            {
                var textureScope = textureScopes[index];

                if (!textureScope.HasCopyScope)
                {
                    continue;
                }

                var texture = textureScope.Texture
                    ?? throw new InvalidOperationException("The graph copy texture scope lost its Vulkan texture.");
                texture.SetLayout(textureScope.CopySubresource, textureScope.CopyLayout);
                texture.SetLastAccessScope(
                    textureScope.CopySubresource,
                    textureScope.CopyStageMask,
                    textureScope.CopyAccessMask);

                if (textureScope.IsCopyDestination)
                {
                    if (textureScope.HasContentsState && !textureScope.ContentsValid)
                    {
                        texture.MarkUninitialized(textureScope.CopySubresource);
                    }
                    else
                    {
                        texture.MarkInitialized(textureScope.CopySubresource);
                    }
                }
                else if (textureScope.HasColorAttachmentStoreAction)
                {
                    if (textureScope.ColorStoreAction == AttachmentStoreAction.Store)
                    {
                        texture.MarkInitialized(textureScope.CopySubresource);
                    }
                    else
                    {
                        texture.MarkUninitialized(textureScope.CopySubresource);
                    }
                }
            }

            for (var index = 0; index < graphBufferScopeCount; index++)
            {
                var graphBufferScope = graphBufferScopes[index];
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

            Exception? stopException = null;
            Exception? releaseException = null;

            if (hasStartedRecording)
            {
                try
                {
                    StopRecording();
                }
                catch (Exception caughtStopException)
                {
                    MarkUncertainWsiOperation();
                    stopException = caughtStopException;
                }
            }

            if (hasAcquiredImage && (surface is not null))
            {
                try
                {
                    surface.ReleaseAcquiredImage(availableGraphFrameSlotIndex, acquiredImageIndex);
                    hasAcquiredImage = false;
                }
                catch (Exception caughtReleaseException)
                {
                    releaseException = caughtReleaseException;
                }
            }

            if (stopException is not null)
            {
                if (releaseException is not null)
                {
                    throw new AggregateException(exception, stopException, releaseException);
                }

                throw new AggregateException(exception, stopException);
            }

            if (releaseException is not null)
            {
                throw new AggregateException(exception, releaseException);
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

                    for (var index = 0; index < graphImageViewCount; index++)
                    {
                        _uncertainGraphImageViews.Add(graphImageViews[index]);
                    }

                    ownsImageViews = false;

                    for (var index = 0; index < descriptorSetCount; index++)
                    {
                        _uncertainGraphDescriptorSets.Add(descriptorSets[index]);
                    }
                }
                else if (!graphSubmissionAccepted)
                {
                    for (var index = descriptorSetCount - 1; index >= 0; index--)
                    {
                        descriptorSets[index].Dispose();
                    }
                }
            }
            finally
            {
                if (ownsImageViews)
                {
                    for (var index = graphImageViewCount - 1; index >= 0; index--)
                    {
                        VulkanApi.vkDestroyImageView(_device, graphImageViews[index], null);
                    }
                }

                _commandBuffer = _transferCommandBuffer;
            }
        }
    }

    private void BindGraphDescriptorSets(
        VulkanBackendPipeline pipeline,
        VkPipelineBindPoint bindPoint,
        ImmutableArray<VulkanBackendUniformBufferBinding> uniformBufferBindings,
        ImmutableArray<VulkanBackendStorageBufferBinding> storageBufferBindings,
        ImmutableArray<VulkanBackendSampledImageBinding> imageBindings,
        ImmutableArray<VulkanBackendStorageImageBinding> storageImageBindings,
        ImmutableArray<VulkanBackendSamplerBinding> samplerBindings,
        VulkanBackendDescriptorSet[] descriptorSets,
        ref int descriptorSetCount)
    {
        ReadOnlySpan<ShaderBindingGroup> groups = [
            ShaderBindingGroup.Frame,
            ShaderBindingGroup.Material,
            ShaderBindingGroup.ObjectData,
            ShaderBindingGroup.Pass,
        ];

        foreach (var group in groups)
        {
            var groupUniformBufferBindingCount = 0;
            var groupStorageBufferBindingCount = 0;
            var groupImageBindingCount = 0;
            var groupStorageImageBindingCount = 0;
            var groupSamplerBindingCount = 0;

            foreach (var binding in uniformBufferBindings)
            {
                if (binding.Group == group)
                {
                    groupUniformBufferBindingCount++;
                }
            }

            foreach (var binding in storageBufferBindings)
            {
                if (binding.Group == group)
                {
                    groupStorageBufferBindingCount++;
                }
            }

            foreach (var binding in imageBindings)
            {
                if (binding.Group == group)
                {
                    groupImageBindingCount++;
                }
            }

            foreach (var binding in storageImageBindings)
            {
                if (binding.Group == group)
                {
                    groupStorageImageBindingCount++;
                }
            }

            foreach (var binding in samplerBindings)
            {
                if (binding.Group == group)
                {
                    groupSamplerBindingCount++;
                }
            }

            if ((groupUniformBufferBindingCount == 0)
                && (groupStorageBufferBindingCount == 0)
                && (groupImageBindingCount == 0)
                && (groupStorageImageBindingCount == 0)
                && (groupSamplerBindingCount == 0))
            {
                continue;
            }

            var groupUniformBufferBindings = new VulkanBackendUniformBufferBinding[groupUniformBufferBindingCount];
            var groupStorageBufferBindings = new VulkanBackendStorageBufferBinding[groupStorageBufferBindingCount];
            var groupImageBindings = new VulkanBackendSampledImageBinding[groupImageBindingCount];
            var groupStorageImageBindings = new VulkanBackendStorageImageBinding[groupStorageImageBindingCount];
            var groupSamplerBindings = new VulkanBackendSamplerBinding[groupSamplerBindingCount];
            var groupUniformBufferBindingIndex = 0;
            var groupStorageBufferBindingIndex = 0;
            var groupImageBindingIndex = 0;
            var groupStorageImageBindingIndex = 0;
            var groupSamplerBindingIndex = 0;

            foreach (var binding in uniformBufferBindings)
            {
                if (binding.Group == group)
                {
                    groupUniformBufferBindings[groupUniformBufferBindingIndex] = binding;
                    groupUniformBufferBindingIndex++;
                }
            }

            foreach (var binding in storageBufferBindings)
            {
                if (binding.Group == group)
                {
                    groupStorageBufferBindings[groupStorageBufferBindingIndex] = binding;
                    groupStorageBufferBindingIndex++;
                }
            }

            foreach (var binding in imageBindings)
            {
                if (binding.Group == group)
                {
                    groupImageBindings[groupImageBindingIndex] = binding;
                    groupImageBindingIndex++;
                }
            }

            foreach (var binding in storageImageBindings)
            {
                if (binding.Group == group)
                {
                    groupStorageImageBindings[groupStorageImageBindingIndex] = binding;
                    groupStorageImageBindingIndex++;
                }
            }

            foreach (var binding in samplerBindings)
            {
                if (binding.Group == group)
                {
                    groupSamplerBindings[groupSamplerBindingIndex] = binding;
                    groupSamplerBindingIndex++;
                }
            }

            if (descriptorSetCount >= descriptorSets.Length)
            {
                throw new InvalidOperationException("The graph descriptor-set preflight capacity was exceeded.");
            }

            var descriptorSet = CreateGraphDescriptorSet(
                pipeline,
                groupUniformBufferBindings,
                groupStorageBufferBindings,
                groupImageBindings,
                groupStorageImageBindings,
                groupSamplerBindings,
                group);
            descriptorSets[descriptorSetCount++] = descriptorSet;
            var nativeDescriptorSet = descriptorSet.DescriptorSet;
            VulkanApi.vkCmdBindDescriptorSets(
                _commandBuffer,
                bindPoint,
                pipeline.PipelineLayout,
                GetNativeDescriptorSetIndex(group),
                1,
                &nativeDescriptorSet,
                0,
                null);
        }
    }

    private (VkPipelineStageFlags2 StageMask, VkAccessFlags2 AccessMask) RecordGraphBufferBarrier(
        ReadOnlySpan<RecordedGraphicsCommand> commands,
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

        var shaderStages = GetShaderStageMask(commands, transition.ResourceId);
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
        ReadOnlySpan<RecordedGraphicsCommand> commands,
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
            or GraphResourceState.DepthRead
            or GraphResourceState.DepthWrite
            or GraphResourceState.OffscreenOutput
            or GraphResourceState.CopySource
            or GraphResourceState.CopyDestination
            or GraphResourceState.StorageRead
            or GraphResourceState.StorageWrite
            or GraphResourceState.StorageReadWrite))
        {
            throw new NotSupportedException($"Vulkan graph submission does not support a transition from {transition.BeforeState}.");
        }

        var expectedBeforeLayout = VulkanGraphResourceStateMapper.GetTextureImageLayout(
            transition.BeforeState,
            texture.Description.Format,
            transition.TextureRange.Aspect);

        if (currentLayout != expectedBeforeLayout)
        {
            throw new InvalidOperationException("The graph transition does not match the Vulkan texture's tracked image layout.");
        }

        var afterLayout = VulkanGraphResourceStateMapper.GetTextureImageLayout(
            transition.AfterState,
            texture.Description.Format,
            transition.TextureRange.Aspect);
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
                var shaderStages = GetShaderStageMask(commands, transition.ResourceId);
                barrier.dstStageMask = VulkanGraphResourceStateMapper.GetStageMask(
                    transition.AfterState,
                    shaderStages);
                barrier.dstAccessMask = VulkanGraphResourceStateMapper.GetAccessMask(transition.AfterState);
                break;
            }

            case GraphResourceState.ShaderSampledRead:
            {
                var shaderStages = GetShaderStageMask(commands, transition.ResourceId);
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
        ReadOnlySpan<RecordedGraphicsCommand> commands,
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
            var shaderStages = GetShaderStageMask(commands, bufferVersion.ResourceId);
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
        var textureStages = GetShaderStageMask(commands, textureVersion.ResourceId);
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

            if (graphFrameSlot.DescriptorSets is VulkanBackendDescriptorSet[] descriptorSets)
            {
                for (var index = graphFrameSlot.DescriptorSetCount - 1; index >= 0; index--)
                {
                    descriptorSets[index].Dispose();
                }

                Array.Clear(descriptorSets, 0, graphFrameSlot.DescriptorSetCount);
                graphFrameSlot.DescriptorSets = null;
                graphFrameSlot.DescriptorSetCount = 0;
            }

            if (graphFrameSlot.ImageViews is VkImageView[] imageViews)
            {
                for (var index = graphFrameSlot.ImageViewCount - 1; index >= 0; index--)
                {
                    VulkanApi.vkDestroyImageView(_device, imageViews[index], null);
                }

                Array.Clear(imageViews, 0, graphFrameSlot.ImageViewCount);
                graphFrameSlot.ImageViews = null;
                graphFrameSlot.ImageViewCount = 0;
            }

            graphFrameSlot.FenceValue = 0;
        }
    }

    private struct VulkanGraphTextureScope
    {
        internal VkAccessFlags2 AccessMask;
        internal AttachmentStoreAction ColorStoreAction;
        internal int ContentsPassIndex;
        internal bool ContentsValid;
        internal VkAccessFlags2 CopyAccessMask;
        internal VkImageLayout CopyLayout;
        internal VkPipelineStageFlags2 CopyStageMask;
        internal TextureSubresource CopySubresource;
        internal AttachmentStoreAction DepthStoreAction;
        internal bool HasColorAttachmentStoreAction;
        internal bool HasContentsState;
        internal bool HasCopyScope;
        internal bool HasDepthAttachmentStoreAction;
        internal bool HasSampledAccess;
        internal bool IsCopyDestination;
        internal VulkanGraphTextureScopeKind Kind;
        internal VkAccessFlags2 LastAccessMask;
        internal VkPipelineStageFlags2 LastStageMask;
        internal VkImageLayout Layout;
        internal VkPipelineStageFlags2 StageMask;
        internal TextureSubresource Subresource;
        internal VulkanBackendTexture? Texture;
    }

    private enum VulkanGraphTextureScopeKind : byte
    {
        None,
        Color,
        Depth,
        Sampled,
        Storage,
    }
}
