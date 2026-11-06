// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace TerraFX.Graphics;

internal sealed class GraphicsGraphSubmission
{
    internal GraphicsGraphSubmission(
        uint deviceEpoch,
        GpuTexture offscreenColorTarget,
        ImmutableArray<RenderPassId> orderedPasses,
        ImmutableArray<GraphicsGraphSubmissionPass> passes,
        ImmutableArray<GraphResourceStateTransition> stateTransitions,
        ImmutableArray<GraphResourceAccess> accesses,
        ImmutableArray<GraphBufferImportRecord> bufferImports,
        ImmutableArray<GraphTextureImportRecord> textureImports,
        ImmutableArray<RecordedPassCommandList> recordedPassCommandLists)
    {
        ArgumentOutOfRangeException.ThrowIfZero(deviceEpoch);
        ArgumentNullException.ThrowIfNull(offscreenColorTarget);

        if (orderedPasses.IsDefault
            || passes.IsDefault
            || stateTransitions.IsDefault
            || accesses.IsDefault
            || bufferImports.IsDefault
            || textureImports.IsDefault
            || recordedPassCommandLists.IsDefault)
        {
            throw new ArgumentException("A graphics graph submission requires initialized immutable data.");
        }

        if (offscreenColorTarget.DeviceEpoch != deviceEpoch)
        {
            throw new ArgumentException("The graph output target belongs to another device epoch.", nameof(offscreenColorTarget));
        }

        DeviceEpoch = deviceEpoch;
        OffscreenColorTarget = offscreenColorTarget;
        OrderedPasses = [.. orderedPasses];
        Passes = [.. passes];
        StateTransitions = [.. stateTransitions];
        Accesses = [.. accesses];
        BufferImports = [.. bufferImports];
        TextureImports = [.. textureImports];
        RecordedPassCommandLists = [.. recordedPassCommandLists];
    }

    internal ImmutableArray<GraphResourceAccess> Accesses { get; }

    internal ImmutableArray<GraphBufferImportRecord> BufferImports { get; }

    internal uint DeviceEpoch { get; }

    internal GpuTexture OffscreenColorTarget { get; }

    internal ImmutableArray<RenderPassId> OrderedPasses { get; }

    internal ImmutableArray<GraphicsGraphSubmissionPass> Passes { get; }

    internal ImmutableArray<RecordedPassCommandList> RecordedPassCommandLists { get; }

    internal ImmutableArray<GraphResourceStateTransition> StateTransitions { get; }

    internal ImmutableArray<GraphTextureImportRecord> TextureImports { get; }

    internal static IGraphicsBackendPipeline GetPipeline(RecordedGraphicsCommand command)
    {
        var pipeline = command.PipelineOwner
            ?? throw new InvalidOperationException("A recorded pipeline command lost its owner.");
        return pipeline.ResourceOwner.BackendResource as IGraphicsBackendPipeline
            ?? throw new InvalidOperationException("The recorded pipeline lost its backend resource.");
    }

    internal void DisposeRecordedPassCommandLists()
    {
        foreach (var commandList in RecordedPassCommandLists)
        {
            commandList.Dispose();
        }
    }

    internal IGraphicsBackendBuffer GetBuffer(ulong resourceId)
    {
        var buffer = GetBufferImport(resourceId).Buffer;
        return buffer.ResourceOwner.BackendResource as IGraphicsBackendBuffer
            ?? throw new InvalidOperationException("The imported buffer lost its backend resource.");
    }

    internal IGraphicsBackendTexture GetTexture(ulong resourceId)
    {
        var texture = GetTextureImport(resourceId).Texture;
        return texture.ResourceOwner.BackendResource as IGraphicsBackendTexture
            ?? throw new InvalidOperationException("The imported texture lost its backend resource.");
    }

    internal void RecordCompletedOwnerStates()
    {
        foreach (var passId in OrderedPasses)
        {
            foreach (var access in Accesses)
            {
                if (access.PassId == passId)
                {
                    if (access.IsTexture)
                    {
                        GetTextureImport(access.ResourceId).Texture.ResourceOwner.RecordGraphAccess(access);
                    }
                    else
                    {
                        GetBufferImport(access.ResourceId).Buffer.ResourceOwner.RecordGraphAccess(access);
                    }
                }
            }
        }
    }

    internal void ValidateOffscreenRasterSubmission()
    {
        if ((BufferImports.Length > 1)
            || (TextureImports.Length is < 1 or > 2)
            || !ReferenceEquals(TextureImports[0].Texture, OffscreenColorTarget))
        {
            throw new NotSupportedException("Native graph submission supports one offscreen color target, one Frame uniform buffer, and one sampled image.");
        }

        var targetResourceId = TextureImports[0].Version.ResourceId;
        var uniformBufferImport = (BufferImports.Length == 1) ? BufferImports[0] : null;
        var sampledTextureImport = (TextureImports.Length == 2) ? TextureImports[1] : null;

        if (uniformBufferImport is not null
            && (((uniformBufferImport.Version.Description.Usage & BufferUsage.Uniform) == 0)
                || (uniformBufferImport.Version.Range != new GraphBufferRange(0, uniformBufferImport.Buffer.ByteLength))
                || !uniformBufferImport.InitialContentsValid
                || (uniformBufferImport.InitialState is not (GraphResourceState.General or GraphResourceState.UniformRead))))
        {
            throw new NotSupportedException("Native graph submission requires imported uniform buffers to declare uniform usage.");
        }

        if (sampledTextureImport is not null
            && (ReferenceEquals(sampledTextureImport.Texture, OffscreenColorTarget)
                || (sampledTextureImport.Version.ResourceId == targetResourceId)
                || ((sampledTextureImport.Version.Description.Usage & TextureUsage.Sampled) == 0)
                || (sampledTextureImport.Version.Description.Dimension != TextureDimension.Dimension2D)
                || (sampledTextureImport.Version.Description.MipLevels != 1)
                || (sampledTextureImport.Version.Description.ArrayLayers != 1)
                || (sampledTextureImport.Version.Range != new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color))
                || !sampledTextureImport.InitialContentsValid
                || (sampledTextureImport.InitialState is not (GraphResourceState.General or GraphResourceState.ShaderSampledRead))))
        {
            throw new NotSupportedException("Native graph submission requires one initialized, single-subresource Frame sampled image.");
        }

        var hasColorClear = false;
        var hasUniformBufferPipeline = false;
        var hasUniformBufferBinding = false;
        var hasUniformBufferAccess = false;
        var hasSampledImagePipeline = false;
        var hasSampledImageBinding = false;
        var hasSampledImageAccess = false;
        var hasSamplerPipeline = false;
        var hasSamplerBinding = false;
        var uniformBufferPasses = new HashSet<RenderPassId>();
        var sampledTexturePasses = new HashSet<RenderPassId>();

        foreach (var pass in Passes)
        {
            if (pass.Kind == CommandRecorderPassKind.Presentation)
            {
                if (!pass.Commands.IsEmpty || !pass.Attachments.IsEmpty)
                {
                    throw new InvalidOperationException("The synthetic offscreen output pass cannot record commands or attachments.");
                }

                continue;
            }

            if ((pass.Kind != CommandRecorderPassKind.Raster) || (pass.Attachments.Length != 1))
            {
                throw new NotSupportedException("Native graph submission currently supports raster color-clear passes.");
            }

            var attachment = pass.Attachments[0];

            if (attachment.IsDepth
                || (attachment.Version.ResourceId != targetResourceId)
                || (attachment.Range != new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color))
                || (attachment.LoadAction != AttachmentLoadAction.Clear)
                || (attachment.StoreAction != AttachmentStoreAction.Store))
            {
                throw new NotSupportedException("Native graph submission currently supports only stored mip-0/layer-0 color clears to its offscreen target.");
            }

            hasColorClear = true;
            GpuPipeline? currentPipeline = null;
            var currentPipelineRequiresUniformBuffer = false;
            var currentPipelineRequiresSampledImage = false;
            var currentPipelineRequiresSampler = false;
            var currentPipelineBindingsSet = false;
            var hasDraw = false;

            foreach (var command in pass.Commands)
            {
                switch (command.Kind)
                {
                    case GraphicsCommandKind.SetPipeline:
                    {
                        var pipeline = command.PipelineOwner
                            ?? throw new InvalidOperationException("A recorded pipeline command lost its pipeline owner.");
                        var layout = pipeline.BindingLayout;

                        if ((pipeline.DeviceEpoch != DeviceEpoch)
                            || !ReferenceEquals(pipeline.ResourceOwner.Device, OffscreenColorTarget.ResourceOwner.Device)
                            || layout.IsCompute
                            || (layout.VertexStride != 0)
                            || !layout.VertexElements.IsEmpty
                            || layout.DepthTargetFormat.HasValue
                            || (layout.Topology != PrimitiveTopology.TriangleList)
                            || (layout.ColorTargetFormats.Length != 1)
                            || (layout.ColorTargetFormats[0] != TextureImports[0].Version.Description.Format))
                        {
                            throw new NotSupportedException("Native graph submission requires a same-device vertex-ID graphics pipeline for its offscreen color format.");
                        }

                        var reflectedResources = new HashSet<ShaderResourceSignature>();

                        foreach (var reflection in layout.ShaderReflections)
                        {
                            foreach (var resource in reflection.Resources)
                            {
                                if ((resource.Group != ShaderBindingGroup.Frame)
                                    || (resource.Kind is not (
                                        ShaderResourceKind.UniformBuffer
                                        or ShaderResourceKind.SampledImage
                                        or ShaderResourceKind.Sampler))
                                    || (resource.Binding != 0)
                                    || (resource.ArrayCount != 1)
                                    || (resource.Access != ShaderResourceAccess.Read)
                                    || (reflection.Stage is not (ShaderStage.Vertex or ShaderStage.Pixel)))
                                {
                                    throw new NotSupportedException(
                                        "Native graph submission supports only Frame-group uniform, sampled-image, and sampler bindings at binding 0.");
                                }

                                _ = reflectedResources.Add(resource);
                            }
                        }

                        foreach (var resource in reflectedResources)
                        {
                            switch (resource.Kind)
                            {
                                case ShaderResourceKind.UniformBuffer:
                                {
                                    currentPipelineRequiresUniformBuffer = true;
                                    break;
                                }

                                case ShaderResourceKind.SampledImage:
                                {
                                    currentPipelineRequiresSampledImage = true;
                                    break;
                                }

                                case ShaderResourceKind.Sampler:
                                {
                                    currentPipelineRequiresSampler = true;
                                    break;
                                }
                            }
                        }

                        if (currentPipelineRequiresSampledImage != currentPipelineRequiresSampler)
                        {
                            throw new NotSupportedException("A Frame sampled-image binding requires a matching sampler binding.");
                        }

                        currentPipeline = pipeline;
                        currentPipelineBindingsSet = false;
                        hasUniformBufferPipeline |= currentPipelineRequiresUniformBuffer;
                        hasSampledImagePipeline |= currentPipelineRequiresSampledImage;
                        hasSamplerPipeline |= currentPipelineRequiresSampler;
                        break;
                    }

                    case GraphicsCommandKind.SetBindings:
                    {
                        if ((currentPipeline is null)
                            || (!currentPipelineRequiresUniformBuffer
                                && !currentPipelineRequiresSampledImage
                                && !currentPipelineRequiresSampler)
                            || currentPipelineBindingsSet)
                        {
                            throw new NotSupportedException("Native graph submission requires one complete Frame binding set after each resource-bearing pipeline.");
                        }

                        var bindings = command.Bindings
                            ?? throw new InvalidOperationException("A recorded binding command lost its binding set.");

                        if ((bindings.Buffers.Length != (currentPipelineRequiresUniformBuffer ? 1 : 0))
                            || (bindings.Textures.Length != (currentPipelineRequiresSampledImage ? 1 : 0))
                            || (bindings.Samplers.Length != (currentPipelineRequiresSampler ? 1 : 0)))
                        {
                            throw new NotSupportedException("The Frame binding set must provide every reflected uniform, sampled-image, and sampler resource exactly once.");
                        }

                        if (currentPipelineRequiresUniformBuffer)
                        {
                            var binding = bindings.Buffers[0];

                            if ((uniformBufferImport is null)
                                || (binding.Group != ShaderBindingGroup.Frame)
                                || (binding.Binding != 0)
                                || (binding.Kind != ShaderResourceKind.UniformBuffer)
                                || (binding.Resource.ResourceId != uniformBufferImport.Version.ResourceId)
                                || (binding.Range != uniformBufferImport.Version.Range))
                            {
                                throw new NotSupportedException("The Frame uniform-buffer binding must exactly match the imported graph buffer range.");
                            }

                            var accessDeclared = false;

                            foreach (var access in Accesses)
                            {
                                if (!access.IsTexture
                                    && (access.PassId == pass.PassId)
                                    && (access.ResourceId == binding.Resource.ResourceId)
                                    && (access.RequiredState == GraphResourceState.UniformRead)
                                    && !access.IsWrite
                                    && binding.Range.IsWithin(access.BufferRange))
                                {
                                    accessDeclared = true;
                                    break;
                                }
                            }

                            if (!accessDeclared)
                            {
                                throw new InvalidOperationException("The Frame uniform-buffer binding has no matching declared UniformRead access.");
                            }

                            hasUniformBufferBinding = true;
                            _ = uniformBufferPasses.Add(pass.PassId);
                        }

                        if (currentPipelineRequiresSampledImage)
                        {
                            var binding = bindings.Textures[0];

                            if ((sampledTextureImport is null)
                                || (binding.Group != ShaderBindingGroup.Frame)
                                || (binding.Binding != 0)
                                || (binding.Kind != ShaderResourceKind.SampledImage)
                                || (binding.Resource.ResourceId != sampledTextureImport.Version.ResourceId)
                                || (binding.Range != sampledTextureImport.Version.Range))
                            {
                                throw new NotSupportedException("The Frame sampled-image binding must exactly match the imported graph texture range.");
                            }

                            var accessDeclared = false;

                            foreach (var access in Accesses)
                            {
                                if (access.IsTexture
                                    && (access.PassId == pass.PassId)
                                    && (access.ResourceId == binding.Resource.ResourceId)
                                    && (access.RequiredState == GraphResourceState.ShaderSampledRead)
                                    && !access.IsWrite
                                    && binding.Range.IsWithin(access.TextureRange))
                                {
                                    accessDeclared = true;
                                    break;
                                }
                            }

                            if (!accessDeclared)
                            {
                                throw new InvalidOperationException("The Frame sampled-image binding has no matching declared SampledRead access.");
                            }

                            hasSampledImageBinding = true;
                            _ = sampledTexturePasses.Add(pass.PassId);
                        }

                        if (currentPipelineRequiresSampler)
                        {
                            var binding = bindings.Samplers[0];

                            if ((binding.Group != ShaderBindingGroup.Frame)
                                || (binding.Binding != 0)
                                || !ReferenceEquals(binding.Sampler.ResourceOwner.Device, OffscreenColorTarget.ResourceOwner.Device)
                                || (binding.Sampler.DeviceEpoch != DeviceEpoch))
                            {
                                throw new NotSupportedException("The Frame sampler binding must identify a live same-device sampler at binding 0.");
                            }

                            hasSamplerBinding = true;
                        }

                        currentPipelineBindingsSet = true;
                        break;
                    }

                    case GraphicsCommandKind.Draw:
                    {
                        if (currentPipeline is null)
                        {
                            throw new InvalidOperationException("A recorded draw has no preceding graphics pipeline.");
                        }

                        if ((currentPipelineRequiresUniformBuffer
                                || currentPipelineRequiresSampledImage
                                || currentPipelineRequiresSampler)
                            && !currentPipelineBindingsSet)
                        {
                            throw new InvalidOperationException("A resource-bearing graph draw has no recorded binding set.");
                        }

                        hasDraw |= (command.Draw.VertexCount > 0) && (command.Draw.InstanceCount > 0);
                        break;
                    }

                    default:
                    {
                        throw new NotSupportedException($"Native graph submission does not yet support {command.Kind} commands.");
                    }
                }
            }

            if (!pass.Commands.IsEmpty && !hasDraw)
            {
                throw new NotSupportedException("Native graph submission requires at least one nonempty draw in a command-bearing raster pass.");
            }
        }

        foreach (var access in Accesses)
        {
            if (access.IsTexture && (access.ResourceId == targetResourceId))
            {
                continue;
            }

            if (!access.IsTexture
                && (uniformBufferImport is not null)
                && (access.ResourceId == uniformBufferImport.Version.ResourceId)
                && (access.RequiredState == GraphResourceState.UniformRead)
                && !access.IsWrite)
            {
                if (!uniformBufferPasses.Contains(access.PassId))
                {
                    throw new InvalidOperationException("The imported Frame uniform buffer is declared by a pass without a matching binding set.");
                }

                hasUniformBufferAccess = true;
                continue;
            }

            if (access.IsTexture
                && (sampledTextureImport is not null)
                && (access.ResourceId == sampledTextureImport.Version.ResourceId)
                && (access.RequiredState == GraphResourceState.ShaderSampledRead)
                && !access.IsWrite)
            {
                if (!sampledTexturePasses.Contains(access.PassId))
                {
                    throw new InvalidOperationException("The imported Frame sampled image is declared by a pass without a matching binding set.");
                }

                hasSampledImageAccess = true;
                continue;
            }

            if (access.IsTexture || (uniformBufferImport is null) || (access.ResourceId != uniformBufferImport.Version.ResourceId))
            {
                throw new NotSupportedException("Native graph submission does not support imported resources other than its target and Frame uniform buffer.");
            }

            throw new NotSupportedException("Native graph submission supports only UniformRead accesses for its imported Frame buffer.");
        }

        foreach (var transition in StateTransitions)
        {
            if (transition.IsTexture && (transition.ResourceId == targetResourceId))
            {
                continue;
            }

            if (!transition.IsTexture
                && (uniformBufferImport is not null)
                && (transition.ResourceId == uniformBufferImport.Version.ResourceId)
                && (transition.AfterState == GraphResourceState.UniformRead)
                && (transition.BeforeState == GraphResourceState.General)
                && uniformBufferPasses.Contains(transition.PassId))
            {
                continue;
            }

            if (transition.IsTexture
                && (sampledTextureImport is not null)
                && (transition.ResourceId == sampledTextureImport.Version.ResourceId)
                && (transition.AfterState == GraphResourceState.ShaderSampledRead)
                && (transition.BeforeState == GraphResourceState.General)
                && sampledTexturePasses.Contains(transition.PassId))
            {
                continue;
            }

            if (transition.IsTexture || (uniformBufferImport is null) || (transition.ResourceId != uniformBufferImport.Version.ResourceId))
            {
                throw new NotSupportedException("Native graph submission does not support state transitions for other imported resources.");
            }

            throw new NotSupportedException("Native graph submission supports only transitions into UniformRead for its imported Frame buffer.");
        }

        if ((uniformBufferImport is not null)
            && (!hasUniformBufferPipeline || !hasUniformBufferBinding || !hasUniformBufferAccess))
        {
            throw new NotSupportedException("An imported Frame uniform buffer must be reflected, bound, and declared by a graph pass.");
        }

        if ((sampledTextureImport is not null)
            && (!hasSampledImagePipeline
                || !hasSampledImageBinding
                || !hasSampledImageAccess
                || !hasSamplerPipeline
                || !hasSamplerBinding))
        {
            throw new NotSupportedException("An imported Frame sampled image must be reflected, bound with a sampler, and declared by a graph pass.");
        }

        if (!hasColorClear)
        {
            throw new NotSupportedException("Native graph submission requires at least one stored offscreen color clear.");
        }
    }

    private GraphBufferImportRecord GetBufferImport(ulong resourceId)
    {
        foreach (var import in BufferImports)
        {
            if (import.Version.ResourceId == resourceId)
            {
                return import;
            }
        }

        throw new NotSupportedException("Native graph submission does not yet materialize transient buffers.");
    }

    private GraphTextureImportRecord GetTextureImport(ulong resourceId)
    {
        foreach (var import in TextureImports)
        {
            if (import.Version.ResourceId == resourceId)
            {
                return import;
            }
        }

        throw new NotSupportedException("Native graph submission does not yet materialize transient textures.");
    }
}
