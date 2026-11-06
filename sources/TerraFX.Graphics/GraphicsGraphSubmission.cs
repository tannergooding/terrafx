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

    internal bool HasFirstStorageUse(RenderPassId passId, ulong resourceId)
    {
        var hasPriorStorageWrite = false;

        foreach (var orderedPass in OrderedPasses)
        {
            if (orderedPass == passId)
            {
                foreach (var access in Accesses)
                {
                    if ((access.PassId == passId)
                        && (access.ResourceId == resourceId)
                        && access.IsStorageReadWritePair)
                    {
                        return !hasPriorStorageWrite;
                    }
                }

                return false;
            }

            foreach (var access in Accesses)
            {
                if ((access.PassId == orderedPass)
                    && (access.ResourceId == resourceId)
                    && access.IsStorageReadWritePair
                    && access.IsWrite)
                {
                    hasPriorStorageWrite = true;
                }
            }
        }

        throw new ArgumentException("The pass is not part of this ordered graph.", nameof(passId));
    }

    internal bool HasPriorStorageWriteHazard(RenderPassId passId, ulong resourceId)
    {
        var priorWrites = new List<GraphResourceAccess>();

        foreach (var orderedPass in OrderedPasses)
        {
            if (orderedPass == passId)
            {
                foreach (var currentAccess in Accesses)
                {
                    if ((currentAccess.PassId != passId)
                        || (currentAccess.ResourceId != resourceId)
                        || !currentAccess.IsStorageReadWritePair)
                    {
                        continue;
                    }

                    foreach (var priorWrite in priorWrites)
                    {
                        if (priorWrite.Overlaps(currentAccess))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            foreach (var access in Accesses)
            {
                if ((access.PassId == orderedPass)
                    && (access.ResourceId == resourceId)
                    && access.IsStorageReadWritePair
                    && access.IsWrite)
                {
                    priorWrites.Add(access);
                }
            }
        }

        throw new ArgumentException("The pass is not part of this ordered graph.", nameof(passId));
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
                        var import = GetBufferImport(access.ResourceId);

                        if (!import.IsTransient)
                        {
                            import.Buffer.ResourceOwner.RecordGraphAccess(access);
                        }
                    }
                }
            }
        }
    }

    internal void ValidateOffscreenRasterSubmission()
    {
        if ((BufferImports.Length > 4)
            || (TextureImports.Length is < 1 or > 3)
            || !ReferenceEquals(TextureImports[0].Texture, OffscreenColorTarget))
        {
            throw new NotSupportedException("Native graph submission supports one offscreen color target, Frame uniform/storage buffers, and sampled/storage images.");
        }

        var targetResourceId = TextureImports[0].Version.ResourceId;
        GraphBufferImportRecord? uniformBufferImport = null;
        var storageBufferImports = new Dictionary<ulong, GraphBufferImportRecord>();
        var copySourceBufferImports = new Dictionary<ulong, GraphBufferImportRecord>();
        var copyDestinationBufferImports = new Dictionary<ulong, GraphBufferImportRecord>();

        foreach (var import in BufferImports)
        {
            var usage = import.Version.Description.Usage;
            var hasUniformUsage = (usage & BufferUsage.Uniform) != 0;
            var hasStorageUsage = (usage & BufferUsage.Storage) != 0;
            var hasCopySourceImportAccess = false;
            var hasCopyDestinationImportAccess = false;

            foreach (var access in Accesses)
            {
                if (access.IsTexture || (access.ResourceId != import.Version.ResourceId))
                {
                    continue;
                }

                hasCopySourceImportAccess |= (access.RequiredState == GraphResourceState.CopySource) && !access.IsWrite;
                hasCopyDestinationImportAccess |= (access.RequiredState == GraphResourceState.CopyDestination) && access.IsWrite;
            }

            if (hasUniformUsage || hasStorageUsage)
            {
                if (hasUniformUsage == hasStorageUsage)
                {
                    throw new NotSupportedException("Native graph submission supports separately imported uniform and storage buffers.");
                }

                if (hasUniformUsage)
                {
                    if (uniformBufferImport is not null)
                    {
                        throw new NotSupportedException("Native graph submission supports one Frame uniform buffer.");
                    }

                    uniformBufferImport = import;
                }
                else
                {
                    if (!storageBufferImports.TryAdd(import.Version.ResourceId, import))
                    {
                        throw new NotSupportedException("Native graph submission requires one import per Frame storage buffer resource.");
                    }
                }

                if ((hasCopySourceImportAccess || hasCopyDestinationImportAccess) && !hasStorageUsage)
                {
                    throw new NotSupportedException("Native graph copy buffers with additional usages must also be storage buffers.");
                }
            }

            if (hasCopySourceImportAccess)
            {
                if (((usage & BufferUsage.CopySource) == 0)
                    || !copySourceBufferImports.TryAdd(import.Version.ResourceId, import))
                {
                    throw new NotSupportedException("Native graph submission requires each copy-source resource to have copy-source usage and one import.");
                }
            }

            if (hasCopyDestinationImportAccess)
            {
                if (((usage & BufferUsage.CopyDestination) == 0)
                    || !copyDestinationBufferImports.TryAdd(import.Version.ResourceId, import))
                {
                    throw new NotSupportedException("Native graph submission requires each copy-destination resource to have copy-destination usage and one import.");
                }
            }

            if (!hasUniformUsage
                && !hasStorageUsage
                && !hasCopySourceImportAccess
                && !hasCopyDestinationImportAccess)
            {
                throw new NotSupportedException("Native graph submission requires each imported buffer to have a supported declared usage.");
            }
        }

        if (uniformBufferImport is not null
            && ((uniformBufferImport.Version.Range != new GraphBufferRange(0, uniformBufferImport.Buffer.ByteLength))
                || !uniformBufferImport.InitialContentsValid
                || (uniformBufferImport.InitialState is not (GraphResourceState.General or GraphResourceState.UniformRead))))
        {
            throw new NotSupportedException("Native graph submission requires an initialized full-range Frame uniform buffer.");
        }

        GraphTextureImportRecord? sampledTextureImport = null;
        GraphTextureImportRecord? storageTextureImport = null;

        for (var index = 1; index < TextureImports.Length; index++)
        {
            var import = TextureImports[index];
            var usage = import.Version.Description.Usage;
            var hasSampledUsage = (usage & TextureUsage.Sampled) != 0;
            var hasStorageUsage = (usage & TextureUsage.Storage) != 0;

            if (ReferenceEquals(import.Texture, OffscreenColorTarget)
                || (import.Version.ResourceId == targetResourceId)
                || (hasSampledUsage == hasStorageUsage)
                || (import.Version.Description.Dimension != TextureDimension.Dimension2D)
                || (import.Version.Description.MipLevels != 1)
                || (import.Version.Description.ArrayLayers != 1)
                || (import.Version.Range != new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color)))
            {
                throw new NotSupportedException("Native graph submission supports distinct single-subresource sampled and storage textures.");
            }

            if (hasSampledUsage)
            {
                if (sampledTextureImport is not null
                    || !import.InitialContentsValid
                    || (import.InitialState is not (GraphResourceState.General or GraphResourceState.ShaderSampledRead)))
                {
                    throw new NotSupportedException("Native graph submission requires one initialized Frame sampled image.");
                }

                sampledTextureImport = import;
            }
            else
            {
                if ((import.Version.Description.Format != TextureFormat.R32UInt)
                    || (storageTextureImport is not null)
                    || (import.InitialContentsValid && import.InitialState == GraphResourceState.Undefined)
                    || (import.InitialState is not (
                        GraphResourceState.Undefined
                        or GraphResourceState.General
                        or GraphResourceState.StorageWrite
                        or GraphResourceState.StorageReadWrite)))
                {
                    throw new NotSupportedException("Native graph submission supports one R32UInt Frame storage image in an initialized or undefined state.");
                }

                storageTextureImport = import;
            }
        }

        foreach (var import in storageBufferImports.Values)
        {
            if ((import.Version.Range != new GraphBufferRange(0, import.Buffer.ByteLength))
                || (import.InitialContentsValid && (import.InitialState == GraphResourceState.Undefined))
                || (import.InitialState is not (
                    GraphResourceState.Undefined
                    or GraphResourceState.General
                    or GraphResourceState.CopySource
                    or GraphResourceState.CopyDestination
                    or GraphResourceState.StorageWrite
                    or GraphResourceState.StorageReadWrite)))
            {
                throw new NotSupportedException("Native graph submission requires full-range Frame storage buffers in supported initial states.");
            }
        }

        foreach (var import in copySourceBufferImports.Values)
        {
            if ((import.Version.Range != new GraphBufferRange(0, import.Buffer.ByteLength))
                || (import.IsTransient
                    ? (import.InitialContentsValid || (import.InitialState != GraphResourceState.Undefined))
                    : (!import.InitialContentsValid
                        || (import.InitialState is not (
                            GraphResourceState.General
                            or GraphResourceState.CopySource
                            or GraphResourceState.CopyDestination
                            or GraphResourceState.StorageRead
                            or GraphResourceState.StorageWrite
                            or GraphResourceState.StorageReadWrite)))))
            {
                throw new NotSupportedException("Native graph submission requires full-range copy-source buffers with valid imported or transient state.");
            }
        }

        foreach (var import in copyDestinationBufferImports.Values)
        {
            if ((import.Version.Range != new GraphBufferRange(0, import.Buffer.ByteLength))
                || (import.InitialContentsValid && (import.InitialState == GraphResourceState.Undefined))
                || (import.InitialState is not (
                    GraphResourceState.Undefined
                    or GraphResourceState.General
                    or GraphResourceState.CopySource
                    or GraphResourceState.CopyDestination
                    or GraphResourceState.StorageRead
                    or GraphResourceState.StorageWrite
                    or GraphResourceState.StorageReadWrite)))
            {
                throw new NotSupportedException("Native graph submission requires full-range copy-destination buffers with a supported initial state.");
            }
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
        var hasStorageBufferPipeline = false;
        var hasStorageBufferBinding = false;
        var hasStorageBufferAccess = false;
        var hasStorageImagePipeline = false;
        var hasStorageImageBinding = false;
        var hasStorageImageAccess = false;
        var copySourceResourcesUsed = new HashSet<ulong>();
        var copyDestinationResourcesUsed = new HashSet<ulong>();
        var uniformBufferPasses = new HashSet<RenderPassId>();
        var sampledTexturePasses = new HashSet<RenderPassId>();
        var storageBufferBindingPairs = new HashSet<(RenderPassId PassId, ulong ResourceId)>();
        var storageBufferResourcesUsed = new HashSet<ulong>();
        var storageTexturePasses = new HashSet<RenderPassId>();
        var copySourcePasses = new HashSet<RenderPassId>();
        var copyDestinationPasses = new HashSet<RenderPassId>();

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

            if (pass.Kind == CommandRecorderPassKind.Copy)
            {
                if (!pass.Attachments.IsEmpty)
                {
                    throw new NotSupportedException("A native graph copy pass cannot declare raster attachments.");
                }
            }
            else if (pass.Kind == CommandRecorderPassKind.Compute)
            {
                if (!pass.Attachments.IsEmpty)
                {
                    throw new NotSupportedException("A native graph compute pass cannot declare raster attachments.");
                }
            }
            else if ((pass.Kind != CommandRecorderPassKind.Raster) || (pass.Attachments.Length != 1))
            {
                throw new NotSupportedException("Native graph submission currently supports copy, compute, and raster color-clear passes.");
            }
            else
            {
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
            }

            GpuPipeline? currentPipeline = null;
            var currentPipelineRequiresUniformBuffer = false;
            var currentPipelineRequiresSampledImage = false;
            var currentPipelineRequiresSampler = false;
            var currentPipelineRequiresStorageBuffer = false;
            var currentPipelineStorageBufferBindingCount = 0;
            var currentPipelineStorageBufferWriteOnlyBindings = new HashSet<int>();
            var currentPipelineRequiresStorageImage = false;
            var currentPipelineBindingsSet = false;
            var hasDraw = false;
            var hasDispatch = false;
            var hasBufferCopy = false;

            foreach (var command in pass.Commands)
            {
                switch (command.Kind)
                {
                    case GraphicsCommandKind.SetPipeline:
                    {
                        if (pass.Kind is not (CommandRecorderPassKind.Raster or CommandRecorderPassKind.Compute))
                        {
                            throw new NotSupportedException("Native graph copy passes cannot bind pipelines.");
                        }

                        var pipeline = command.PipelineOwner
                            ?? throw new InvalidOperationException("A recorded pipeline command lost its pipeline owner.");
                        var layout = pipeline.BindingLayout;
                        var isComputePass = pass.Kind == CommandRecorderPassKind.Compute;

                        if ((pipeline.DeviceEpoch != DeviceEpoch)
                            || !ReferenceEquals(pipeline.ResourceOwner.Device, OffscreenColorTarget.ResourceOwner.Device)
                            || (layout.IsCompute != isComputePass)
                            || (layout.VertexStride != 0)
                            || !layout.VertexElements.IsEmpty)
                        {
                            throw new NotSupportedException("Native graph submission requires a same-device pipeline matching the pass kind.");
                        }

                        if (isComputePass
                            ? (layout.DepthTargetFormat.HasValue || !layout.ColorTargetFormats.IsEmpty)
                            : (layout.DepthTargetFormat.HasValue
                                || (layout.Topology != PrimitiveTopology.TriangleList)
                                || (layout.ColorTargetFormats.Length != 1)
                                || (layout.ColorTargetFormats[0] != TextureImports[0].Version.Description.Format)))
                        {
                            throw new NotSupportedException("Native graph submission requires compute pipelines without attachments and vertex-ID graphics pipelines for its offscreen color format.");
                        }

                        var reflectedResources = new HashSet<ShaderResourceSignature>();
                        currentPipelineStorageBufferBindingCount = 0;
                        currentPipelineStorageBufferWriteOnlyBindings.Clear();

                        foreach (var reflection in layout.ShaderReflections)
                        {
                            var supportedStage = isComputePass
                                ? reflection.Stage == ShaderStage.Compute
                                : reflection.Stage is ShaderStage.Vertex or ShaderStage.Pixel;

                            foreach (var resource in reflection.Resources)
                            {
                                var supportedKind = isComputePass
                                    ? resource.Kind is ShaderResourceKind.StorageBuffer or ShaderResourceKind.StorageImage
                                    : resource.Kind is ShaderResourceKind.UniformBuffer
                                        or ShaderResourceKind.SampledImage
                                        or ShaderResourceKind.Sampler
                                        or ShaderResourceKind.StorageBuffer
                                        or ShaderResourceKind.StorageImage;
                                var isWriteOnlyComputeBuffer = isComputePass
                                    && (resource.Kind == ShaderResourceKind.StorageBuffer)
                                    && (resource.Access == ShaderResourceAccess.Write);

                                if ((resource.Group != ShaderBindingGroup.Frame)
                                    || !supportedKind
                                    || ((resource.Kind is ShaderResourceKind.UniformBuffer
                                            or ShaderResourceKind.SampledImage
                                            or ShaderResourceKind.Sampler)
                                        && (resource.Binding != 0))
                                    || ((resource.Kind == ShaderResourceKind.StorageImage)
                                        && (resource.Binding != 1))
                                    || (resource.ArrayCount != 1)
                                    || ((resource.Kind is ShaderResourceKind.UniformBuffer
                                            or ShaderResourceKind.SampledImage
                                            or ShaderResourceKind.Sampler)
                                        && (resource.Access != ShaderResourceAccess.Read))
                                    || ((resource.Kind is ShaderResourceKind.StorageBuffer or ShaderResourceKind.StorageImage)
                                        && (resource.Access != ShaderResourceAccess.ReadWrite)
                                        && !isWriteOnlyComputeBuffer)
                                    || !supportedStage)
                                {
                                    throw new NotSupportedException(
                                        "Native graph submission supports Frame graphics resources and compute-stage storage bindings.");
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

                                case ShaderResourceKind.StorageBuffer:
                                {
                                    currentPipelineRequiresStorageBuffer = true;
                                    currentPipelineStorageBufferBindingCount++;

                                    if (resource.Access == ShaderResourceAccess.Write)
                                    {
                                        _ = currentPipelineStorageBufferWriteOnlyBindings.Add(resource.Binding);
                                    }

                                    break;
                                }

                                case ShaderResourceKind.StorageImage:
                                {
                                    currentPipelineRequiresStorageImage = true;
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
                        hasStorageBufferPipeline |= currentPipelineRequiresStorageBuffer;
                        hasStorageImagePipeline |= currentPipelineRequiresStorageImage;
                        break;
                    }

                    case GraphicsCommandKind.SetBindings:
                    {
                        if ((currentPipeline is null)
                            || (!currentPipelineRequiresUniformBuffer
                                && !currentPipelineRequiresSampledImage
                                && !currentPipelineRequiresSampler
                                && !currentPipelineRequiresStorageBuffer
                                && !currentPipelineRequiresStorageImage)
                            || currentPipelineBindingsSet)
                        {
                            throw new NotSupportedException("Native graph submission requires one complete Frame binding set after each resource-bearing pipeline.");
                        }

                        var bindings = command.Bindings
                            ?? throw new InvalidOperationException("A recorded binding command lost its binding set.");

                        var expectedBufferBindingCount = (currentPipelineRequiresUniformBuffer ? 1 : 0)
                            + currentPipelineStorageBufferBindingCount;

                        if ((bindings.Textures.Length != ((currentPipelineRequiresSampledImage ? 1 : 0)
                                + (currentPipelineRequiresStorageImage ? 1 : 0)))
                            || (bindings.Buffers.Length != expectedBufferBindingCount)
                            || (bindings.Samplers.Length != (currentPipelineRequiresSampler ? 1 : 0)))
                        {
                            throw new NotSupportedException("The Frame binding set must provide every reflected uniform, sampled, storage, and sampler resource exactly once.");
                        }

                        foreach (var binding in bindings.Buffers)
                        {
                            switch (binding.Kind)
                            {
                                case ShaderResourceKind.UniformBuffer:
                                {
                                    if ((uniformBufferImport is null)
                                        || (binding.Group != ShaderBindingGroup.Frame)
                                        || (binding.Binding != 0)
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
                                    break;
                                }

                                case ShaderResourceKind.StorageBuffer:
                                {
                                    if (!storageBufferImports.TryGetValue(binding.Resource.ResourceId, out var storageBufferImport)
                                        || (binding.Group != ShaderBindingGroup.Frame)
                                        || (binding.Range != storageBufferImport.Version.Range))
                                    {
                                        throw new NotSupportedException("The Frame storage-buffer binding must exactly match the imported graph buffer range.");
                                    }

                                    if (currentPipelineStorageBufferWriteOnlyBindings.Contains(binding.Binding))
                                    {
                                        var writeAccessDeclared = false;

                                        foreach (var access in Accesses)
                                        {
                                            if (!access.IsTexture
                                                && (access.PassId == pass.PassId)
                                                && (access.ResourceId == binding.Resource.ResourceId)
                                                && (access.RequiredState == GraphResourceState.StorageWrite)
                                                && access.IsWrite
                                                && ReferenceEquals(access.BufferVersion, binding.Resource)
                                                && binding.Range.IsWithin(access.BufferRange))
                                            {
                                                writeAccessDeclared = true;
                                                break;
                                            }
                                        }

                                        if (!writeAccessDeclared)
                                        {
                                            throw new InvalidOperationException("The compute Frame storage-buffer binding has no matching declared StorageWrite access.");
                                        }
                                    }
                                    else
                                    {
                                        GraphResourceAccess? storageReadAccess = null;
                                        GraphResourceAccess? storageWriteAccess = null;

                                        foreach (var access in Accesses)
                                        {
                                            if (!access.IsTexture
                                                && (access.PassId == pass.PassId)
                                                && (access.ResourceId == binding.Resource.ResourceId)
                                                && (access.RequiredState == GraphResourceState.StorageReadWrite)
                                                && access.IsStorageReadWritePair
                                                && binding.Range.IsWithin(access.BufferRange))
                                            {
                                                if (access.IsWrite
                                                    && ReferenceEquals(access.BufferVersion, binding.Resource))
                                                {
                                                    storageWriteAccess = access;
                                                }
                                                else if (!access.IsWrite)
                                                {
                                                    storageReadAccess = access;
                                                }
                                            }
                                        }

                                        if ((storageReadAccess is not GraphResourceAccess readAccess)
                                            || (storageWriteAccess is not GraphResourceAccess writeAccess)
                                            || !readAccess.ContentsValid
                                            || !readAccess.IsStorageReadWritePairOf(writeAccess))
                                        {
                                            throw new InvalidOperationException("The Frame storage-buffer binding has no matching initialized StorageReadWrite version pair.");
                                        }
                                    }

                                    hasStorageBufferBinding = true;
                                    _ = storageBufferBindingPairs.Add((pass.PassId, binding.Resource.ResourceId));
                                    break;
                                }

                                default:
                                {
                                    throw new NotSupportedException($"Native graph submission does not support {binding.Kind} buffer bindings.");
                                }
                            }
                        }

                        foreach (var binding in bindings.Textures)
                        {
                            switch (binding.Kind)
                            {
                                case ShaderResourceKind.SampledImage:
                                {
                                    if ((sampledTextureImport is null)
                                        || (binding.Group != ShaderBindingGroup.Frame)
                                        || (binding.Binding != 0)
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
                                    break;
                                }

                                case ShaderResourceKind.StorageImage:
                                {
                                    if ((storageTextureImport is null)
                                        || (binding.Group != ShaderBindingGroup.Frame)
                                        || (binding.Binding != 1)
                                        || (binding.Resource.ResourceId != storageTextureImport.Version.ResourceId)
                                        || (binding.Range != storageTextureImport.Version.Range))
                                    {
                                        throw new NotSupportedException("The Frame storage-image binding must exactly match the imported graph texture range.");
                                    }

                                    GraphResourceAccess? storageReadAccess = null;
                                    GraphResourceAccess? storageWriteAccess = null;

                                    foreach (var access in Accesses)
                                    {
                                        if (access.IsTexture
                                            && (access.PassId == pass.PassId)
                                            && (access.ResourceId == binding.Resource.ResourceId)
                                            && (access.RequiredState == GraphResourceState.StorageReadWrite)
                                            && access.IsStorageReadWritePair
                                            && binding.Range.IsWithin(access.TextureRange))
                                        {
                                            if (access.IsWrite
                                                && ReferenceEquals(access.TextureVersion, binding.Resource))
                                            {
                                                storageWriteAccess = access;
                                            }
                                            else if (!access.IsWrite)
                                            {
                                                storageReadAccess = access;
                                            }
                                        }
                                    }

                                    if ((storageReadAccess is not GraphResourceAccess readAccess)
                                        || (storageWriteAccess is not GraphResourceAccess writeAccess)
                                        || !readAccess.ContentsValid
                                        || !readAccess.IsStorageReadWritePairOf(writeAccess))
                                    {
                                        throw new InvalidOperationException("The Frame storage-image binding has no matching initialized StorageReadWrite version pair.");
                                    }

                                    hasStorageImageBinding = true;
                                    _ = storageTexturePasses.Add(pass.PassId);
                                    break;
                                }

                                default:
                                {
                                    throw new NotSupportedException($"Native graph submission does not support {binding.Kind} texture bindings.");
                                }
                            }
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

                    case GraphicsCommandKind.StorageBarrier:
                    {
                        if ((currentPipeline is null)
                            || !currentPipelineBindingsSet
                            || !command.StorageBarrierSourceWrites
                            || (!command.StorageBarrierDestinationReads
                                && !command.StorageBarrierDestinationWrites))
                        {
                            throw new InvalidOperationException("A storage barrier requires a bound storage pipeline and a prior write hazard.");
                        }

                        if (command.StorageBarrierBuffer is GraphBufferVersion barrierBuffer)
                        {
                            if (!storageBufferImports.TryGetValue(barrierBuffer.ResourceId, out var storageBufferImport)
                                || (command.StorageBarrierBufferRange != storageBufferImport.Version.Range)
                                || !storageBufferBindingPairs.Contains((pass.PassId, barrierBuffer.ResourceId)))
                            {
                                throw new NotSupportedException("A storage-buffer barrier must target the bound Frame storage buffer range.");
                            }
                        }
                        else if (command.StorageBarrierTexture is GraphTextureVersion barrierTexture)
                        {
                            if ((storageTextureImport is null)
                                || (barrierTexture.ResourceId != storageTextureImport.Version.ResourceId)
                                || (command.StorageBarrierTextureRange != storageTextureImport.Version.Range)
                                || !storageTexturePasses.Contains(pass.PassId))
                            {
                                throw new NotSupportedException("A storage-image barrier must target the bound Frame storage image range.");
                            }
                        }
                        else
                        {
                            throw new InvalidOperationException("A storage barrier has no graph resource.");
                        }

                        break;
                    }

                    case GraphicsCommandKind.Draw:
                    {
                        if (pass.Kind != CommandRecorderPassKind.Raster)
                        {
                            throw new NotSupportedException("Native graph non-raster passes cannot record draws.");
                        }

                        if (currentPipeline is null)
                        {
                            throw new InvalidOperationException("A recorded draw has no preceding graphics pipeline.");
                        }

                        if ((currentPipelineRequiresUniformBuffer
                                || currentPipelineRequiresSampledImage
                                || currentPipelineRequiresSampler
                                || currentPipelineRequiresStorageBuffer
                                || currentPipelineRequiresStorageImage)
                            && !currentPipelineBindingsSet)
                        {
                            throw new InvalidOperationException("A resource-bearing graph draw has no recorded binding set.");
                        }

                        hasDraw |= (command.Draw.VertexCount > 0) && (command.Draw.InstanceCount > 0);
                        break;
                    }

                    case GraphicsCommandKind.Dispatch:
                    {
                        if (pass.Kind != CommandRecorderPassKind.Compute)
                        {
                            throw new NotSupportedException("Native graph dispatches must be recorded by a compute pass.");
                        }

                        if ((currentPipeline is null) || !currentPipeline.BindingLayout.IsCompute)
                        {
                            throw new InvalidOperationException("A recorded dispatch has no preceding compute pipeline.");
                        }

                        if (currentPipelineRequiresStorageBuffer && !currentPipelineBindingsSet)
                        {
                            throw new InvalidOperationException("A resource-bearing graph dispatch has no recorded binding set.");
                        }

                        hasDispatch |= (command.Dispatch.GroupCountX > 0)
                            && (command.Dispatch.GroupCountY > 0)
                            && (command.Dispatch.GroupCountZ > 0);
                        break;
                    }

                    case GraphicsCommandKind.CopyBuffer:
                    {
                        if (pass.Kind != CommandRecorderPassKind.Copy)
                        {
                            throw new NotSupportedException("Native graph buffer copies must be recorded by a copy pass.");
                        }

                        var source = command.CopySource
                            ?? throw new InvalidOperationException("The graph copy command has no source buffer version.");
                        var destination = command.CopyDestination
                            ?? throw new InvalidOperationException("The graph copy command has no destination buffer version.");

                        if (!copySourceBufferImports.TryGetValue(source.ResourceId, out var copySourceBufferImport)
                            || !copyDestinationBufferImports.TryGetValue(destination.ResourceId, out var copyDestinationBufferImport)
                            || (source.ResourceId == destination.ResourceId))
                        {
                            throw new NotSupportedException("Native graph buffer copies require distinct imported copy-source and copy-destination buffers.");
                        }

                        var sourceRange = new GraphBufferRange(command.SourceOffset, command.ByteLength);
                        var destinationRange = new GraphBufferRange(command.DestinationOffset, command.ByteLength);

                        if (!sourceRange.IsWithin(copySourceBufferImport.Version.Range)
                            || !destinationRange.IsWithin(copyDestinationBufferImport.Version.Range))
                        {
                            throw new ArgumentOutOfRangeException(nameof(command), "The graph copy range exceeds an imported buffer.");
                        }

                        var sourceAccessDeclared = false;
                        var destinationAccessDeclared = false;

                        foreach (var access in Accesses)
                        {
                            if (access.IsTexture || (access.PassId != pass.PassId))
                            {
                                continue;
                            }

                            sourceAccessDeclared |= (access.ResourceId == source.ResourceId)
                                && (access.RequiredState == GraphResourceState.CopySource)
                                && !access.IsWrite
                                && access.ContentsValid
                                && ReferenceEquals(access.BufferVersion, source)
                                && sourceRange.IsWithin(access.BufferRange);
                            destinationAccessDeclared |= (access.ResourceId == destination.ResourceId)
                                && (access.RequiredState == GraphResourceState.CopyDestination)
                                && access.IsWrite
                                && ReferenceEquals(access.BufferVersion, destination)
                                && destinationRange.IsWithin(access.BufferRange);
                        }

                        if (!sourceAccessDeclared || !destinationAccessDeclared)
                        {
                            throw new InvalidOperationException("A native graph buffer copy has no matching declared source and destination ranges.");
                        }

                        _ = copySourcePasses.Add(pass.PassId);
                        _ = copyDestinationPasses.Add(pass.PassId);
                        hasBufferCopy = true;
                        break;
                    }

                    default:
                    {
                        throw new NotSupportedException($"Native graph submission does not yet support {command.Kind} commands.");
                    }
                }
            }

            if ((pass.Kind == CommandRecorderPassKind.Raster) && !pass.Commands.IsEmpty && !hasDraw)
            {
                throw new NotSupportedException("Native graph submission requires at least one nonempty draw in a command-bearing raster pass.");
            }

            if ((pass.Kind == CommandRecorderPassKind.Compute) && !pass.Commands.IsEmpty && !hasDispatch)
            {
                throw new NotSupportedException("Native graph compute passes require a nonempty dispatch.");
            }

            if ((pass.Kind == CommandRecorderPassKind.Copy) && !pass.Commands.IsEmpty && !hasBufferCopy)
            {
                throw new NotSupportedException("Native graph copy passes require a supported buffer copy command.");
            }
        }

        foreach (var access in Accesses)
        {
            if (access.IsTexture && (access.ResourceId == targetResourceId))
            {
                continue;
            }

            if (!access.IsTexture
                && copySourceBufferImports.ContainsKey(access.ResourceId)
                && (access.RequiredState == GraphResourceState.CopySource)
                && !access.IsWrite)
            {
                if (!copySourcePasses.Contains(access.PassId))
                {
                    throw new InvalidOperationException("The imported copy-source buffer is declared by a pass without a matching copy command.");
                }

                _ = copySourceResourcesUsed.Add(access.ResourceId);
                continue;
            }

            if (!access.IsTexture
                && copyDestinationBufferImports.ContainsKey(access.ResourceId)
                && (access.RequiredState == GraphResourceState.CopyDestination)
                && access.IsWrite)
            {
                if (!copyDestinationPasses.Contains(access.PassId))
                {
                    throw new InvalidOperationException("The imported copy-destination buffer is declared by a pass without a matching copy command.");
                }

                _ = copyDestinationResourcesUsed.Add(access.ResourceId);
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

            if (!access.IsTexture
                && storageBufferImports.ContainsKey(access.ResourceId)
                && (access.RequiredState == GraphResourceState.StorageWrite)
                && access.IsWrite)
            {
                if (!storageBufferBindingPairs.Contains((access.PassId, access.ResourceId)))
                {
                    throw new InvalidOperationException("The imported Frame storage buffer is declared by a pass without a matching binding set.");
                }

                hasStorageBufferAccess = true;
                _ = storageBufferResourcesUsed.Add(access.ResourceId);
                continue;
            }

            if (!access.IsTexture
                && storageBufferImports.ContainsKey(access.ResourceId)
                && (access.RequiredState == GraphResourceState.StorageReadWrite)
                && access.IsStorageReadWritePair)
            {
                if (!storageBufferBindingPairs.Contains((access.PassId, access.ResourceId)))
                {
                    throw new InvalidOperationException("The imported Frame storage buffer is declared by a pass without a matching binding set.");
                }

                hasStorageBufferAccess = true;
                _ = storageBufferResourcesUsed.Add(access.ResourceId);
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

            if (access.IsTexture
                && (storageTextureImport is not null)
                && (access.ResourceId == storageTextureImport.Version.ResourceId)
                && (access.RequiredState == GraphResourceState.StorageReadWrite)
                && access.IsStorageReadWritePair)
            {
                if (!storageTexturePasses.Contains(access.PassId))
                {
                    throw new InvalidOperationException("The imported Frame storage image is declared by a pass without a matching binding set.");
                }

                hasStorageImageAccess = true;
                continue;
            }

            if (access.IsTexture || (uniformBufferImport is null) || (access.ResourceId != uniformBufferImport.Version.ResourceId))
            {
                throw new NotSupportedException("Native graph submission does not support imported resources outside its Frame resource set.");
            }

            throw new NotSupportedException("Native graph submission supports only its declared Frame resource access states.");
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

            if (!transition.IsTexture
                && copySourceBufferImports.ContainsKey(transition.ResourceId)
                && (transition.AfterState == GraphResourceState.CopySource)
                && (transition.BeforeState is
                    GraphResourceState.General
                    or GraphResourceState.CopySource
                    or GraphResourceState.CopyDestination
                    or GraphResourceState.StorageRead
                    or GraphResourceState.StorageWrite
                    or GraphResourceState.StorageReadWrite)
                && copySourcePasses.Contains(transition.PassId))
            {
                continue;
            }

            if (!transition.IsTexture
                && copyDestinationBufferImports.ContainsKey(transition.ResourceId)
                && (transition.AfterState == GraphResourceState.CopyDestination)
                && (transition.BeforeState is
                    GraphResourceState.Undefined
                    or GraphResourceState.General
                    or GraphResourceState.CopySource
                    or GraphResourceState.CopyDestination
                    or GraphResourceState.StorageRead
                    or GraphResourceState.StorageWrite
                    or GraphResourceState.StorageReadWrite)
                && copyDestinationPasses.Contains(transition.PassId))
            {
                continue;
            }

            if (!transition.IsTexture
                && storageBufferImports.ContainsKey(transition.ResourceId)
                && (transition.AfterState is GraphResourceState.StorageWrite or GraphResourceState.StorageReadWrite)
                && (transition.BeforeState is
                    GraphResourceState.Undefined
                    or GraphResourceState.General
                    or GraphResourceState.CopySource
                    or GraphResourceState.CopyDestination
                    or GraphResourceState.StorageRead
                    or GraphResourceState.StorageWrite
                    or GraphResourceState.StorageReadWrite)
                && storageBufferBindingPairs.Contains((transition.PassId, transition.ResourceId)))
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

            if (transition.IsTexture
                && (storageTextureImport is not null)
                && (transition.ResourceId == storageTextureImport.Version.ResourceId)
                && (transition.AfterState == GraphResourceState.StorageReadWrite)
                && (transition.BeforeState is
                    GraphResourceState.General
                    or GraphResourceState.StorageRead
                    or GraphResourceState.StorageWrite
                    or GraphResourceState.StorageReadWrite)
                && storageTexturePasses.Contains(transition.PassId))
            {
                continue;
            }

            if ((uniformBufferImport is null || transition.ResourceId != uniformBufferImport.Version.ResourceId)
                && !storageBufferImports.ContainsKey(transition.ResourceId)
                && (sampledTextureImport is null || transition.ResourceId != sampledTextureImport.Version.ResourceId)
                && (storageTextureImport is null || transition.ResourceId != storageTextureImport.Version.ResourceId))
            {
                throw new NotSupportedException("Native graph submission does not support state transitions for other imported resources.");
            }

            throw new NotSupportedException("Native graph submission supports only declared Frame resource transitions.");
        }

        if ((uniformBufferImport is not null)
            && (!hasUniformBufferPipeline || !hasUniformBufferBinding || !hasUniformBufferAccess))
        {
            throw new NotSupportedException("An imported Frame uniform buffer must be reflected, bound, and declared by a graph pass.");
        }

        if (copySourceResourcesUsed.Count != copySourceBufferImports.Count)
        {
            throw new NotSupportedException("Every imported copy-source buffer must be read by a graph copy command.");
        }

        if (copyDestinationResourcesUsed.Count != copyDestinationBufferImports.Count)
        {
            throw new NotSupportedException("Every imported copy-destination buffer must be written by a graph copy command.");
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

        if ((storageBufferImports.Count != 0)
            && (!hasStorageBufferPipeline
                || !hasStorageBufferBinding
                || !hasStorageBufferAccess
                || (storageBufferResourcesUsed.Count != storageBufferImports.Count)))
        {
            throw new NotSupportedException("Every imported Frame storage buffer must be reflected, bound, and declared by a graph pass.");
        }

        if ((storageTextureImport is not null)
            && (!hasStorageImagePipeline || !hasStorageImageBinding || !hasStorageImageAccess))
        {
            throw new NotSupportedException("An imported Frame storage image must be reflected, bound, and declared by a graph pass.");
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
