// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace TerraFX.Graphics;

internal sealed class GraphicsGraphSubmission
{
    internal GraphicsGraphSubmission(
        uint deviceEpoch,
        GpuTexture outputColorTarget,
        Surface? surfaceTarget,
        GraphTextureVersion presentationSource,
        uint surfaceEpoch,
        uint swapchainEpoch,
        ImmutableArray<RenderPassId> orderedPasses,
        ImmutableArray<GraphicsGraphSubmissionPass> passes,
        ImmutableArray<GraphResourceStateTransition> stateTransitions,
        ImmutableArray<GraphResourceAliasingBarrier> aliasingBarriers,
        ImmutableArray<GraphTextureAliasingBarrier> textureAliasingBarriers,
        ImmutableArray<GraphResourceAccess> accesses,
        ImmutableArray<GraphBufferImportRecord> bufferImports,
        ImmutableArray<GraphTextureImportRecord> textureImports,
        ImmutableArray<RecordedPassCommandList> recordedPassCommandLists)
    {
        ArgumentOutOfRangeException.ThrowIfZero(deviceEpoch);
        ArgumentNullException.ThrowIfNull(outputColorTarget);
        ArgumentNullException.ThrowIfNull(presentationSource);

        if (orderedPasses.IsDefault
            || passes.IsDefault
            || stateTransitions.IsDefault
            || aliasingBarriers.IsDefault
            || textureAliasingBarriers.IsDefault
            || accesses.IsDefault
            || bufferImports.IsDefault
            || textureImports.IsDefault
            || recordedPassCommandLists.IsDefault)
        {
            throw new ArgumentException("A graphics graph submission requires initialized immutable data.");
        }

        if (outputColorTarget.DeviceEpoch != deviceEpoch)
        {
            throw new ArgumentException("The graph output target belongs to another device epoch.", nameof(outputColorTarget));
        }

        if (presentationSource.DeviceEpoch != deviceEpoch)
        {
            throw new ArgumentException("The presentation source belongs to another device epoch.", nameof(presentationSource));
        }

        if (surfaceTarget is null)
        {
            if ((surfaceEpoch != 0) || (swapchainEpoch != 0))
            {
                throw new ArgumentException("An offscreen graph cannot carry Surface or swapchain epochs.");
            }
        }
        else
        {
            ArgumentOutOfRangeException.ThrowIfZero(surfaceEpoch);
            ArgumentOutOfRangeException.ThrowIfZero(swapchainEpoch);
            ObjectDisposedException.ThrowIf(!surfaceTarget.IsLive, surfaceTarget);
            var surfaceSnapshot = surfaceTarget.CaptureSnapshot();

            if (!ReferenceEquals(surfaceTarget.Device, outputColorTarget.ResourceOwner.Device)
                || (surfaceSnapshot.Capabilities.SurfaceEpoch != surfaceEpoch)
                || (surfaceSnapshot.Capabilities.SwapchainEpoch != swapchainEpoch)
                || (surfaceSnapshot.ClientPixelSize.Width != outputColorTarget.Description.Width)
                || (surfaceSnapshot.ClientPixelSize.Height != outputColorTarget.Description.Height)
                || !SurfaceFormatCompatibility.IsSrgbPresentationSourceCompatible(
                    outputColorTarget.Description.Format,
                    surfaceSnapshot.ColorFormat))
            {
                throw new ArgumentException("The Surface presentation target does not match the output texture and captured epochs.", nameof(surfaceTarget));
            }
        }

        if (textureImports.IsDefaultOrEmpty
            || !ReferenceEquals(textureImports[0].Texture, outputColorTarget)
            || (textureImports[0].Version.ResourceId != presentationSource.ResourceId))
        {
            throw new ArgumentException("The graph output texture must be the first texture import and contain the selected presentation source.");
        }

        DeviceEpoch = deviceEpoch;
        OutputColorTarget = outputColorTarget;
        SurfaceTarget = surfaceTarget;
        PresentationSource = presentationSource;
        SurfaceEpoch = surfaceEpoch;
        SwapchainEpoch = swapchainEpoch;
        OrderedPasses = [.. orderedPasses];
        Passes = [.. passes];
        StateTransitions = [.. stateTransitions];
        AliasingBarriers = [.. aliasingBarriers];
        TextureAliasingBarriers = [.. textureAliasingBarriers];
        Accesses = [.. accesses];
        BufferImports = [.. bufferImports];
        TextureImports = [.. textureImports];
        RecordedPassCommandLists = [.. recordedPassCommandLists];
    }

    internal ImmutableArray<GraphResourceAccess> Accesses { get; }

    internal ImmutableArray<GraphResourceAliasingBarrier> AliasingBarriers { get; }

    internal ImmutableArray<GraphBufferImportRecord> BufferImports { get; }

    internal uint DeviceEpoch { get; }

    internal ImmutableArray<RenderPassId> OrderedPasses { get; }

    internal GpuTexture OutputColorTarget { get; }

    internal ImmutableArray<GraphicsGraphSubmissionPass> Passes { get; }

    internal long? PresentationSequence { get; private set; }

    internal GraphTextureVersion PresentationSource { get; }

    internal ImmutableArray<RecordedPassCommandList> RecordedPassCommandLists { get; }

    internal ImmutableArray<GraphResourceStateTransition> StateTransitions { get; }

    internal uint SurfaceEpoch { get; }

    internal Surface? SurfaceTarget { get; }

    internal uint SwapchainEpoch { get; }

    internal ImmutableArray<GraphTextureAliasingBarrier> TextureAliasingBarriers { get; }

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

    internal void RecordSubmittedOwnerStates()
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

    internal void RecordSubmittedQueueFence(long fenceValue)
    {
        foreach (var commandList in RecordedPassCommandLists)
        {
            commandList.RecordSubmittedQueueFence(fenceValue);
        }
    }

    internal void SetPresentationSequence(long presentationSequence)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(presentationSequence);

        if (SurfaceTarget is null)
        {
            throw new InvalidOperationException("An offscreen graph cannot report a presentation sequence.");
        }

        if (PresentationSequence.HasValue)
        {
            throw new InvalidOperationException("A Surface graph submission cannot present more than once.");
        }

        PresentationSequence = presentationSequence;
    }

    internal void ValidateNativeRasterSubmission()
    {
        if ((BufferImports.Length > 8)
            || (TextureImports.Length is < 1 or > 8)
            || !ReferenceEquals(TextureImports[0].Texture, OutputColorTarget))
        {
            throw new NotSupportedException("Native graph submission supports one graph output color target and up to seven imported or transient Frame resource textures.");
        }

        var targetResourceId = TextureImports[0].Version.ResourceId;

        if (PresentationSource.ResourceId != targetResourceId)
        {
            throw new NotSupportedException("Native graph submission requires the selected terminal color version to share its output texture.");
        }

        if (SurfaceTarget is Surface surface)
        {
            var surfaceSnapshot = surface.CaptureSnapshot();
            var sourceDescription = PresentationSource.Description;

            if (surface.IsRetired
                || surface.IsQuarantined
                || (surface.Device.DeviceEpoch != DeviceEpoch)
                || (surface.Device.Status != GraphicsDeviceStatus.Ready)
                || (surfaceSnapshot.Capabilities.SurfaceEpoch != SurfaceEpoch)
                || (surfaceSnapshot.Capabilities.SwapchainEpoch != SwapchainEpoch)
                || (surfaceSnapshot.ClientPixelSize.Width != sourceDescription.Width)
                || (surfaceSnapshot.ClientPixelSize.Height != sourceDescription.Height)
                || !SurfaceFormatCompatibility.IsSrgbPresentationSourceCompatible(
                    sourceDescription.Format,
                    surfaceSnapshot.ColorFormat)
                || ((sourceDescription.Usage & (TextureUsage.ColorAttachment | TextureUsage.CopySource))
                    != (TextureUsage.ColorAttachment | TextureUsage.CopySource)))
            {
                throw new InvalidOperationException("The graph output texture no longer matches the active sRGB Surface generation.");
            }
        }

        GraphBufferImportRecord? uniformBufferImport = null;
        var vertexBufferImports = new Dictionary<ulong, GraphBufferImportRecord>();
        var indexBufferImports = new Dictionary<ulong, GraphBufferImportRecord>();
        var storageBufferImports = new Dictionary<ulong, GraphBufferImportRecord>();
        var copySourceBufferImports = new Dictionary<ulong, GraphBufferImportRecord>();
        var copyDestinationBufferImports = new Dictionary<ulong, GraphBufferImportRecord>();
        var copySourceTextureImports = new Dictionary<ulong, GraphTextureImportRecord>();
        var copyDestinationTextureImports = new Dictionary<ulong, GraphTextureImportRecord>();
        var bufferImportsByResourceId = new Dictionary<ulong, GraphBufferImportRecord>(BufferImports.Length);
        var textureImportsByResourceId = new Dictionary<ulong, GraphTextureImportRecord>(TextureImports.Length);
        var orderedPassIndices = new Dictionary<RenderPassId, int>(OrderedPasses.Length);

        for (var index = 0; index < OrderedPasses.Length; index++)
        {
            orderedPassIndices.Add(OrderedPasses[index], index);
        }

        foreach (var import in BufferImports)
        {
            if (!bufferImportsByResourceId.TryAdd(import.Version.ResourceId, import))
            {
                throw new NotSupportedException("Native graph submission requires one import per graph buffer resource.");
            }

            var usage = import.Version.Description.Usage;
            var hasUniformUsage = (usage & BufferUsage.Uniform) != 0;
            var hasStorageUsage = (usage & BufferUsage.Storage) != 0;
            var hasVertexReadImportAccess = false;
            var hasIndexReadImportAccess = false;
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
                hasVertexReadImportAccess |= (access.RequiredState == GraphResourceState.VertexRead) && !access.IsWrite;
                hasIndexReadImportAccess |= (access.RequiredState == GraphResourceState.IndexRead) && !access.IsWrite;
            }

            if (hasVertexReadImportAccess)
            {
                if (((usage & BufferUsage.Vertex) == 0)
                    || !vertexBufferImports.TryAdd(import.Version.ResourceId, import))
                {
                    throw new NotSupportedException("Native graph vertex reads require each imported vertex resource to have vertex usage and one import.");
                }
            }

            if (hasIndexReadImportAccess)
            {
                if (((usage & BufferUsage.Index) == 0)
                    || !indexBufferImports.TryAdd(import.Version.ResourceId, import))
                {
                    throw new NotSupportedException("Native graph index reads require each imported index resource to have index usage and one import.");
                }
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
                && !hasVertexReadImportAccess
                && !hasIndexReadImportAccess
                && !hasCopySourceImportAccess
                && !hasCopyDestinationImportAccess)
            {
                throw new NotSupportedException("Native graph submission requires each imported buffer to have a supported declared usage.");
            }
        }

        foreach (var import in TextureImports)
        {
            if (import.IsTransient && !textureImportsByResourceId.TryAdd(import.Version.ResourceId, import))
            {
                throw new NotSupportedException("Native graph submission requires one transient import per graph texture resource.");
            }
        }

        var outputTargetCopySourceAccess = false;

        foreach (var access in Accesses)
        {
            if (access.IsTexture
                && (access.ResourceId == targetResourceId)
                && (access.RequiredState == GraphResourceState.CopySource)
                && !access.IsWrite)
            {
                if (!access.ContentsValid)
                {
                    throw new NotSupportedException("The graph output color target must have initialized contents before serving as a graph copy source.");
                }

                outputTargetCopySourceAccess = true;
            }
        }

        if (outputTargetCopySourceAccess)
        {
            var outputTargetImport = TextureImports[0];

            if (((outputTargetImport.Version.Description.Usage & TextureUsage.CopySource) == 0)
                || !copySourceTextureImports.TryAdd(targetResourceId, outputTargetImport))
            {
                throw new NotSupportedException("The graph output color target requires copy-source usage before it can be copied.");
            }
        }

        var aliasedResources = new HashSet<ulong>();

        foreach (var aliasingBarrier in AliasingBarriers)
        {
            if (!bufferImportsByResourceId.TryGetValue(aliasingBarrier.BeforeResourceId, out var beforeImport)
                || !bufferImportsByResourceId.TryGetValue(aliasingBarrier.AfterResourceId, out var afterImport)
                || !beforeImport.IsTransient
                || !afterImport.IsTransient
                || !orderedPassIndices.TryGetValue(aliasingBarrier.PassId, out var aliasPassIndex)
                || !aliasedResources.Add(aliasingBarrier.AfterResourceId))
            {
                throw new InvalidOperationException("A transient aliasing barrier does not match distinct materialized graph buffers and a scheduled pass.");
            }

            var beforeLastPassIndex = -1;
            var afterFirstPassIndex = int.MaxValue;

            foreach (var access in Accesses)
            {
                if (access.IsTexture)
                {
                    continue;
                }

                if (access.ResourceId == aliasingBarrier.BeforeResourceId)
                {
                    beforeLastPassIndex = Math.Max(beforeLastPassIndex, orderedPassIndices[access.PassId]);
                }

                if (access.ResourceId == aliasingBarrier.AfterResourceId)
                {
                    afterFirstPassIndex = Math.Min(afterFirstPassIndex, orderedPassIndices[access.PassId]);
                }
            }

            if ((beforeLastPassIndex >= afterFirstPassIndex)
                || (afterFirstPassIndex != aliasPassIndex))
            {
                throw new InvalidOperationException("Transient alias lifetimes overlap or the alias barrier is not at the new resource's first pass.");
            }
        }

        if (uniformBufferImport is not null
            && (!uniformBufferImport.InitialContentsValid
                || (uniformBufferImport.InitialState is not (GraphResourceState.General or GraphResourceState.UniformRead))))
        {
            throw new NotSupportedException("Native graph submission requires an initialized Frame uniform buffer in a supported state.");
        }

        var sampledTextureImports = new Dictionary<ulong, GraphTextureImportRecord>();
        var storageTextureImports = new Dictionary<ulong, GraphTextureImportRecord>();

        for (var index = 1; index < TextureImports.Length; index++)
        {
            var import = TextureImports[index];
            var usage = import.Version.Description.Usage;
            var hasSampledUsage = (usage & TextureUsage.Sampled) != 0;
            var hasStorageUsage = (usage & TextureUsage.Storage) != 0;
            var hasCopySourceImportAccess = false;
            var hasCopyDestinationImportAccess = false;

            if (ReferenceEquals(import.Texture, OutputColorTarget)
                || (import.Version.ResourceId == targetResourceId)
                || (import.Version.Description.Dimension != TextureDimension.Dimension2D)
                || ((import.Version.Range.MipCount != 1) && !(hasSampledUsage || hasStorageUsage))
                || (import.Version.Range.LayerCount != 1)
                || (import.Version.Range.Aspect != TextureAspect.Color))
            {
                throw new NotSupportedException("Native graph submission requires distinct non-target 2D color texture ranges with single-layer selections.");
            }

            foreach (var access in Accesses)
            {
                if (!access.IsTexture || (access.ResourceId != import.Version.ResourceId))
                {
                    continue;
                }

                hasCopySourceImportAccess |= (access.RequiredState == GraphResourceState.CopySource) && !access.IsWrite;
                hasCopyDestinationImportAccess |= (access.RequiredState == GraphResourceState.CopyDestination) && access.IsWrite;
            }

            if (hasSampledUsage || hasStorageUsage)
            {
                var supportedSubresourceRange = hasSampledUsage
                    ? (import.Version.Description.Dimension == TextureDimension.Dimension2D)
                        && (import.Version.Description.ArrayLayers == 1)
                        && (import.Version.Range.Aspect == TextureAspect.Color)
                        && (import.Version.Range.LayerCount == 1)
                    : (import.Version.Description.Dimension == TextureDimension.Dimension2D)
                        && (import.Version.Description.ArrayLayers == 1)
                        && (import.Version.Range.Aspect == TextureAspect.Color)
                        && (import.Version.Range.LayerCount == 1);

                if (hasSampledUsage == hasStorageUsage
                    || !supportedSubresourceRange
                    || hasCopySourceImportAccess
                    || hasCopyDestinationImportAccess)
                {
                    throw new NotSupportedException("Native graph submission supports single-layer 2D color sampled/storage image ranges.");
                }

                if (hasSampledUsage)
                {
                    if (!import.InitialContentsValid
                        || (import.InitialState is not (GraphResourceState.General or GraphResourceState.ShaderSampledRead)))
                    {
                        throw new NotSupportedException("Native graph submission requires initialized Frame sampled images.");
                    }

                    if (!sampledTextureImports.TryAdd(import.Version.ResourceId, import))
                    {
                        throw new InvalidOperationException("The graph imported a duplicate Frame sampled-image resource identity.");
                    }
                }
                else
                {
                    if ((import.InitialContentsValid && import.InitialState == GraphResourceState.Undefined)
                        || (import.InitialState is not (
                            GraphResourceState.Undefined
                            or GraphResourceState.General
                            or GraphResourceState.StorageWrite
                            or GraphResourceState.StorageReadWrite)))
                    {
                        throw new NotSupportedException("Native graph submission supports Frame storage images in an initialized or undefined state.");
                    }

                    if (!storageTextureImports.TryAdd(import.Version.ResourceId, import))
                    {
                        throw new InvalidOperationException("The graph imported a duplicate Frame storage-image resource identity.");
                    }
                }
            }
            else
            {
                if ((!hasCopySourceImportAccess && !hasCopyDestinationImportAccess)
                    || (hasCopySourceImportAccess && hasCopyDestinationImportAccess && !import.IsTransient))
                {
                    throw new NotSupportedException("Native graph copy textures require declared copy-source or copy-destination uses; a texture may fill both roles only when transient.");
                }

                if (hasCopySourceImportAccess)
                {
                    if (((usage & TextureUsage.CopySource) == 0)
                        || (import.IsTransient
                            ? (import.InitialContentsValid || (import.InitialState != GraphResourceState.Undefined))
                            : (!import.InitialContentsValid
                                || (import.InitialState is not (
                                    GraphResourceState.General
                                    or GraphResourceState.CopySource
                                    or GraphResourceState.CopyDestination
                                    or GraphResourceState.ColorAttachment
                                    or GraphResourceState.ShaderSampledRead
                                    or GraphResourceState.StorageRead
                                    or GraphResourceState.StorageWrite
                                    or GraphResourceState.StorageReadWrite))))
                        || !copySourceTextureImports.TryAdd(import.Version.ResourceId, import))
                    {
                        throw new NotSupportedException("Native graph texture copy sources require initialized imports or transient textures written earlier in the graph.");
                    }
                }

                if (hasCopyDestinationImportAccess)
                {
                    if (((usage & TextureUsage.CopyDestination) == 0)
                        || (import.IsTransient
                            ? (import.InitialContentsValid || (import.InitialState != GraphResourceState.Undefined))
                            : ((import.InitialContentsValid && (import.InitialState == GraphResourceState.Undefined))
                                || (import.InitialState is not (
                                    GraphResourceState.Undefined
                                    or GraphResourceState.General
                                    or GraphResourceState.CopySource
                                    or GraphResourceState.CopyDestination
                                    or GraphResourceState.ColorAttachment
                                    or GraphResourceState.ShaderSampledRead
                                    or GraphResourceState.StorageRead
                                    or GraphResourceState.StorageWrite
                                    or GraphResourceState.StorageReadWrite))))
                        || !copyDestinationTextureImports.TryAdd(import.Version.ResourceId, import))
                    {
                        throw new NotSupportedException("Native graph texture copy destinations require CopyDestination resources in supported initial states.");
                    }
                }
            }
        }

        var aliasedTextureResources = new HashSet<ulong>();

        foreach (var aliasingBarrier in TextureAliasingBarriers)
        {
            if (!textureImportsByResourceId.TryGetValue(aliasingBarrier.BeforeResourceId, out var beforeImport)
                || !textureImportsByResourceId.TryGetValue(aliasingBarrier.AfterResourceId, out var afterImport)
                || !beforeImport.IsTransient
                || !afterImport.IsTransient
                || (aliasingBarrier.DeviceEpoch != DeviceEpoch)
                || !orderedPassIndices.TryGetValue(aliasingBarrier.PassId, out var aliasPassIndex)
                || !aliasedTextureResources.Add(aliasingBarrier.AfterResourceId))
            {
                throw new InvalidOperationException("A transient texture aliasing barrier does not match distinct materialized graph textures and a scheduled pass.");
            }

            var beforeLastPassIndex = -1;
            var afterFirstPassIndex = int.MaxValue;

            foreach (var access in Accesses)
            {
                if (!access.IsTexture)
                {
                    continue;
                }

                if (access.ResourceId == aliasingBarrier.BeforeResourceId)
                {
                    beforeLastPassIndex = Math.Max(beforeLastPassIndex, orderedPassIndices[access.PassId]);
                }

                if (access.ResourceId == aliasingBarrier.AfterResourceId)
                {
                    afterFirstPassIndex = Math.Min(afterFirstPassIndex, orderedPassIndices[access.PassId]);
                }
            }

            if ((beforeLastPassIndex >= afterFirstPassIndex)
                || (afterFirstPassIndex != aliasPassIndex))
            {
                throw new InvalidOperationException("Transient texture alias lifetimes overlap or the alias barrier is not at the new resource's first pass.");
            }
        }

        foreach (var import in storageBufferImports.Values)
        {
            if ((import.InitialContentsValid && (import.InitialState == GraphResourceState.Undefined))
                || (import.InitialState is not (
                    GraphResourceState.Undefined
                    or GraphResourceState.General
                    or GraphResourceState.CopySource
                    or GraphResourceState.CopyDestination
                    or GraphResourceState.StorageWrite
                    or GraphResourceState.StorageReadWrite)))
            {
                throw new NotSupportedException("Native graph submission requires Frame storage buffers in supported initial states.");
            }
        }

        foreach (var import in vertexBufferImports.Values)
        {
            if (import.IsTransient
                || (import.Version.Range != new GraphBufferRange(0, import.Buffer.ByteLength))
                || !import.InitialContentsValid
                || (import.InitialState is not (
                    GraphResourceState.General
                    or GraphResourceState.CopySource
                    or GraphResourceState.CopyDestination
                    or GraphResourceState.VertexRead)))
            {
                throw new NotSupportedException("Native graph submission requires full-range imported vertex buffers with valid contents and a supported initial state.");
            }
        }

        foreach (var import in indexBufferImports.Values)
        {
            if (import.IsTransient
                || (import.Version.Range != new GraphBufferRange(0, import.Buffer.ByteLength))
                || !import.InitialContentsValid
                || (import.InitialState is not (
                    GraphResourceState.General
                    or GraphResourceState.CopySource
                    or GraphResourceState.CopyDestination
                    or GraphResourceState.IndexRead)))
            {
                throw new NotSupportedException("Native graph submission requires full-range imported index buffers with valid contents and a supported initial state.");
            }
        }

        foreach (var import in copySourceBufferImports.Values)
        {
            if (import.IsTransient
                    ? (import.InitialContentsValid || (import.InitialState != GraphResourceState.Undefined))
                    : (!import.InitialContentsValid
                        || (import.InitialState is not (
                            GraphResourceState.General
                            or GraphResourceState.CopySource
                            or GraphResourceState.CopyDestination
                            or GraphResourceState.StorageRead
                            or GraphResourceState.StorageWrite
                            or GraphResourceState.StorageReadWrite))))
            {
                throw new NotSupportedException("Native graph submission requires copy-source buffers with valid imported or transient state.");
            }
        }

        foreach (var import in copyDestinationBufferImports.Values)
        {
            if ((import.InitialContentsValid && (import.InitialState == GraphResourceState.Undefined))
                || (import.InitialState is not (
                    GraphResourceState.Undefined
                    or GraphResourceState.General
                    or GraphResourceState.CopySource
                    or GraphResourceState.CopyDestination
                    or GraphResourceState.StorageRead
                    or GraphResourceState.StorageWrite
                    or GraphResourceState.StorageReadWrite)))
            {
                throw new NotSupportedException("Native graph submission requires copy-destination buffers with a supported initial state.");
            }
        }

        var hasColorClear = false;
        var hasVertexBufferPipeline = false;
        var hasVertexBufferBinding = false;
        var hasIndexBufferBinding = false;
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
        var vertexBufferBindingPairs = new HashSet<(RenderPassId PassId, ulong ResourceId)>();
        var vertexBufferResourcesUsed = new HashSet<ulong>();
        var indexBufferBindingPairs = new HashSet<(RenderPassId PassId, ulong ResourceId)>();
        var indexBufferResourcesUsed = new HashSet<ulong>();
        var uniformBufferPasses = new HashSet<RenderPassId>();
        var sampledTextureBindingPairs = new HashSet<(RenderPassId PassId, ulong ResourceId)>();
        var sampledTextureResourcesUsed = new HashSet<ulong>();
        var storageBufferBindingPairs = new HashSet<(RenderPassId PassId, ulong ResourceId)>();
        var storageBufferResourcesUsed = new HashSet<ulong>();
        var storageTextureBindingPairs = new HashSet<(RenderPassId PassId, ulong ResourceId)>();
        var storageTextureResourcesUsed = new HashSet<ulong>();
        var copySourcePasses = new HashSet<RenderPassId>();
        var copyDestinationPasses = new HashSet<RenderPassId>();
        var copySourceTexturePasses = new HashSet<RenderPassId>();
        var copyDestinationTexturePasses = new HashSet<RenderPassId>();
        var copySourceTextureResourcesUsed = new HashSet<ulong>();
        var copyDestinationTextureResourcesUsed = new HashSet<ulong>();

        foreach (var pass in Passes)
        {
            if (pass.Kind == CommandRecorderPassKind.Presentation)
            {
                if (!pass.Commands.IsEmpty || !pass.Attachments.IsEmpty)
                {
                    throw new InvalidOperationException("A synthetic terminal-output pass cannot record commands or attachments.");
                }

                if (SurfaceTarget is not null)
                {
                    var hasPresentationSourceRead = false;

                    foreach (var access in Accesses)
                    {
                        hasPresentationSourceRead |= (access.PassId == pass.PassId)
                            && access.IsTexture
                            && (access.ResourceId == targetResourceId)
                            && ReferenceEquals(access.TextureVersion, PresentationSource)
                            && (access.TextureRange == new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color))
                            && (access.RequiredState == GraphResourceState.CopySource)
                            && !access.IsWrite;
                    }

                    if (!hasPresentationSourceRead)
                    {
                        throw new InvalidOperationException("A Surface terminal pass must read its selected source for presentation.");
                    }

                    _ = copySourceTexturePasses.Add(pass.PassId);
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
                    throw new NotSupportedException("Native graph submission currently supports only stored mip-0/layer-0 color clears to its graph output target.");
                }

                hasColorClear = true;
            }

            GpuPipeline? currentPipeline = null;
            GraphBufferImportRecord? currentVertexBufferImport = null;
            GraphBufferImportRecord? currentIndexBufferImport = null;
            var currentPipelineRequiresVertexBuffer = false;
            var currentPipelineRequiresUniformBuffer = false;
            var currentPipelineRequiresSampledImage = false;
            var currentPipelineRequiresSampler = false;
            var currentPipelineSampledImageBindings = new HashSet<int>();
            var currentPipelineSamplerBindings = new HashSet<int>();
            var currentPipelineRequiresStorageBuffer = false;
            var currentPipelineStorageBufferBindingCount = 0;
            var currentPipelineStorageBufferWriteOnlyBindings = new HashSet<int>();
            var currentPipelineRequiresStorageImage = false;
            var currentPipelineStorageImageBindingCount = 0;
            var currentPipelineStorageImageFormats = new Dictionary<int, ShaderStorageImageFormat>();
            var currentPipelineBindingsSet = false;
            var hasDraw = false;
            var hasDispatch = false;
            var hasBufferCopy = false;
            var hasTextureCopy = false;

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
                            || !ReferenceEquals(pipeline.ResourceOwner.Device, OutputColorTarget.ResourceOwner.Device)
                            || (layout.IsCompute != isComputePass)
                            || (isComputePass && ((layout.VertexStride != 0) || !layout.VertexElements.IsEmpty)))
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
                            throw new NotSupportedException("Native graph submission requires compute pipelines without attachments and triangle-list graphics pipelines for its output color format.");
                        }

                        var reflectedResources = new HashSet<ShaderResourceSignature>();
                        currentPipelineRequiresVertexBuffer = !isComputePass && (layout.VertexStride != 0);
                        currentVertexBufferImport = null;
                        currentIndexBufferImport = null;
                        currentPipelineStorageBufferBindingCount = 0;
                        currentPipelineStorageBufferWriteOnlyBindings.Clear();
                        currentPipelineSampledImageBindings.Clear();
                        currentPipelineSamplerBindings.Clear();
                        currentPipelineStorageImageBindingCount = 0;
                        currentPipelineStorageImageFormats.Clear();

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
                                    || ((resource.Kind == ShaderResourceKind.UniformBuffer)
                                        && (resource.Binding != 0))
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

                                    if (!currentPipelineSampledImageBindings.Add(resource.Binding))
                                    {
                                        throw new NotSupportedException("A graphics pipeline cannot declare conflicting sampled-image signatures at one binding.");
                                    }

                                    break;
                                }

                                case ShaderResourceKind.Sampler:
                                {
                                    currentPipelineRequiresSampler = true;

                                    if (!currentPipelineSamplerBindings.Add(resource.Binding))
                                    {
                                        throw new NotSupportedException("A graphics pipeline cannot declare conflicting sampler signatures at one binding.");
                                    }

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
                                    currentPipelineStorageImageBindingCount++;

                                    if (!currentPipelineStorageImageFormats.TryAdd(resource.Binding, resource.StorageImageFormat))
                                    {
                                        throw new NotSupportedException("A compute pipeline cannot declare conflicting storage-image signatures at one binding.");
                                    }

                                    break;
                                }
                            }
                        }

                        if (!currentPipelineSampledImageBindings.SetEquals(currentPipelineSamplerBindings))
                        {
                            throw new NotSupportedException("Every Frame sampled-image binding requires a sampler at the matching binding.");
                        }

                        currentPipeline = pipeline;
                        currentPipelineBindingsSet = false;
                        hasVertexBufferPipeline |= currentPipelineRequiresVertexBuffer;
                        hasUniformBufferPipeline |= currentPipelineRequiresUniformBuffer;
                        hasSampledImagePipeline |= currentPipelineRequiresSampledImage;
                        hasSamplerPipeline |= currentPipelineRequiresSampler;
                        hasStorageBufferPipeline |= currentPipelineRequiresStorageBuffer;
                        hasStorageImagePipeline |= currentPipelineRequiresStorageImage;
                        break;
                    }

                    case GraphicsCommandKind.SetVertexBuffer:
                    {
                        if ((pass.Kind != CommandRecorderPassKind.Raster)
                            || (currentPipeline is null)
                            || !currentPipelineRequiresVertexBuffer)
                        {
                            throw new NotSupportedException("Native graph vertex buffers require a vertex-input raster pipeline.");
                        }

                        var binding = command.VertexBuffer;

                        if ((binding.Slot != 0)
                            || (binding.Stride != currentPipeline.BindingLayout.VertexStride)
                            || !vertexBufferImports.TryGetValue(binding.Buffer.ResourceId, out var vertexBufferImport)
                            || !ReferenceEquals(binding.Buffer, vertexBufferImport.Version)
                            || !binding.Range.IsWithin(vertexBufferImport.Version.Range))
                        {
                            throw new NotSupportedException("A native graph vertex-buffer binding must match an imported vertex resource and pipeline stride.");
                        }

                        var accessDeclared = false;

                        foreach (var access in Accesses)
                        {
                            if (!access.IsTexture
                                && (access.PassId == pass.PassId)
                                && (access.ResourceId == binding.Buffer.ResourceId)
                                && (access.RequiredState == GraphResourceState.VertexRead)
                                && !access.IsWrite
                                && access.ContentsValid
                                && ReferenceEquals(access.BufferVersion, binding.Buffer)
                                && binding.Range.IsWithin(access.BufferRange))
                            {
                                accessDeclared = true;
                                break;
                            }
                        }

                        if (!accessDeclared)
                        {
                            throw new InvalidOperationException("The graph vertex-buffer binding has no matching declared VertexRead range.");
                        }

                        currentVertexBufferImport = vertexBufferImport;
                        _ = vertexBufferBindingPairs.Add((pass.PassId, binding.Buffer.ResourceId));
                        hasVertexBufferBinding = true;
                        break;
                    }

                    case GraphicsCommandKind.SetIndexBuffer:
                    {
                        if ((pass.Kind != CommandRecorderPassKind.Raster)
                            || (currentPipeline is null))
                        {
                            throw new NotSupportedException("Native graph index buffers require a raster pipeline.");
                        }

                        var binding = command.IndexBuffer;
                        var elementSize = binding.Format switch {
                            IndexFormat.UInt16 => sizeof(ushort),
                            IndexFormat.UInt32 => sizeof(uint),
                            _ => throw new ArgumentOutOfRangeException(nameof(command)),
                        };

                        if (!indexBufferImports.TryGetValue(binding.Buffer.ResourceId, out var indexBufferImport)
                            || !ReferenceEquals(binding.Buffer, indexBufferImport.Version)
                            || !binding.Range.IsWithin(indexBufferImport.Version.Range)
                            || ((binding.Range.ByteOffset % elementSize) != 0)
                            || ((binding.Range.ByteLength % elementSize) != 0))
                        {
                            throw new NotSupportedException("A native graph index-buffer binding must match an imported aligned index resource.");
                        }

                        var accessDeclared = false;

                        foreach (var access in Accesses)
                        {
                            if (!access.IsTexture
                                && (access.PassId == pass.PassId)
                                && (access.ResourceId == binding.Buffer.ResourceId)
                                && (access.RequiredState == GraphResourceState.IndexRead)
                                && !access.IsWrite
                                && access.ContentsValid
                                && ReferenceEquals(access.BufferVersion, binding.Buffer)
                                && binding.Range.IsWithin(access.BufferRange))
                            {
                                accessDeclared = true;
                                break;
                            }
                        }

                        if (!accessDeclared)
                        {
                            throw new InvalidOperationException("The graph index-buffer binding has no matching declared IndexRead range.");
                        }

                        currentIndexBufferImport = indexBufferImport;
                        _ = indexBufferBindingPairs.Add((pass.PassId, binding.Buffer.ResourceId));
                        hasIndexBufferBinding = true;
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
                        var expectedTextureBindingCount = currentPipelineSampledImageBindings.Count
                            + currentPipelineStorageImageBindingCount;

                        if ((bindings.Textures.Length != expectedTextureBindingCount)
                            || (bindings.Buffers.Length != expectedBufferBindingCount)
                            || (bindings.Samplers.Length != currentPipelineSamplerBindings.Count))
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
                                        || !binding.Range.IsWithin(uniformBufferImport.Version.Range))
                                    {
                                        throw new NotSupportedException("The Frame uniform-buffer binding must fit the imported graph buffer range.");
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
                                        || !binding.Range.IsWithin(storageBufferImport.Version.Range))
                                    {
                                        throw new NotSupportedException("The Frame storage-buffer binding must fit the imported graph buffer range.");
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
                                    if (!currentPipelineSampledImageBindings.Contains(binding.Binding)
                                        || !sampledTextureImports.TryGetValue(binding.Resource.ResourceId, out var sampledTextureImport)
                                        || (binding.Group != ShaderBindingGroup.Frame)
                                        || !binding.Range.IsWithin(sampledTextureImport.Version.Range)
                                        || (binding.Range.Aspect != TextureAspect.Color)
                                        || (binding.Range.LayerCount != 1))
                                    {
                                        throw new NotSupportedException("The Frame sampled-image binding must fit its imported single-layer color range.");
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
                                    _ = sampledTextureBindingPairs.Add((pass.PassId, binding.Resource.ResourceId));
                                    break;
                                }

                                case ShaderResourceKind.StorageImage:
                                {
                                    if (!currentPipelineStorageImageFormats.TryGetValue(binding.Binding, out var storageImageFormat))
                                    {
                                        throw new NotSupportedException("The Frame storage-image binding has no matching reflected pipeline signature.");
                                    }

                                    var expectedStorageImageTextureFormat = storageImageFormat switch {
                                        ShaderStorageImageFormat.Rgba16Float => TextureFormat.Rgba16Float,
                                        ShaderStorageImageFormat.R32Float => TextureFormat.R32Float,
                                        ShaderStorageImageFormat.R32UInt => TextureFormat.R32UInt,
                                        ShaderStorageImageFormat.R32SInt
                                            => throw new NotSupportedException("Native graph submission has no texture format for an R32SInt storage image."),
                                        _ => throw new InvalidOperationException("The storage-image pipeline omitted its reflected format."),
                                    };

                                    if (!storageTextureImports.TryGetValue(binding.Resource.ResourceId, out var storageTextureImport)
                                        || (binding.Group != ShaderBindingGroup.Frame)
                                        || !binding.Range.IsWithin(storageTextureImport.Version.Range)
                                        || (binding.Range.MipCount != 1)
                                        || (binding.Range.LayerCount != 1)
                                        || (binding.Range.Aspect != TextureAspect.Color)
                                        || (binding.Resource.Description.Format != expectedStorageImageTextureFormat))
                                    {
                                        throw new NotSupportedException("The Frame storage-image binding must match its reflected format and fit one imported color mip.");
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
                                    _ = storageTextureBindingPairs.Add((pass.PassId, binding.Resource.ResourceId));
                                    break;
                                }

                                default:
                                {
                                    throw new NotSupportedException($"Native graph submission does not support {binding.Kind} texture bindings.");
                                }
                            }
                        }

                        foreach (var binding in bindings.Samplers)
                        {
                            if (!currentPipelineSamplerBindings.Contains(binding.Binding)
                                || (binding.Group != ShaderBindingGroup.Frame)
                                || !ReferenceEquals(binding.Sampler.ResourceOwner.Device, OutputColorTarget.ResourceOwner.Device)
                                || (binding.Sampler.DeviceEpoch != DeviceEpoch))
                            {
                                throw new NotSupportedException("Every Frame sampler binding must identify a live same-device sampler at its reflected binding.");
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
                            if (!storageTextureImports.TryGetValue(barrierTexture.ResourceId, out var storageTextureImport)
                                || !command.StorageBarrierTextureRange.IsWithin(storageTextureImport.Version.Range)
                                || !storageTextureBindingPairs.Contains((pass.PassId, barrierTexture.ResourceId)))
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

                        if (currentPipelineRequiresVertexBuffer != (currentVertexBufferImport is not null))
                        {
                            throw new InvalidOperationException("A graph draw must bind a vertex buffer exactly when its pipeline has vertex inputs.");
                        }

                        if (currentVertexBufferImport is not null)
                        {
                            _ = vertexBufferResourcesUsed.Add(currentVertexBufferImport.Version.ResourceId);
                        }

                        hasDraw |= (command.Draw.VertexCount > 0) && (command.Draw.InstanceCount > 0);
                        break;
                    }

                    case GraphicsCommandKind.DrawIndexed:
                    {
                        if ((pass.Kind != CommandRecorderPassKind.Raster)
                            || (currentPipeline is null))
                        {
                            throw new NotSupportedException("Native graph indexed draws require a raster pipeline.");
                        }

                        if ((currentPipelineRequiresUniformBuffer
                                || currentPipelineRequiresSampledImage
                                || currentPipelineRequiresSampler
                                || currentPipelineRequiresStorageBuffer
                                || currentPipelineRequiresStorageImage)
                            && !currentPipelineBindingsSet)
                        {
                            throw new InvalidOperationException("A resource-bearing graph indexed draw has no recorded binding set.");
                        }

                        if ((command.DrawIndexed.IndexCount > 0) && (command.DrawIndexed.InstanceCount > 0))
                        {
                            if (currentIndexBufferImport is null)
                            {
                                throw new InvalidOperationException("An indexed graph draw has no preceding index-buffer binding.");
                            }

                            if (currentPipelineRequiresVertexBuffer != (currentVertexBufferImport is not null))
                            {
                                throw new InvalidOperationException("An indexed graph draw must bind a vertex buffer exactly when its pipeline has vertex inputs.");
                            }

                            _ = indexBufferResourcesUsed.Add(currentIndexBufferImport.Version.ResourceId);

                            if (currentVertexBufferImport is not null)
                            {
                                _ = vertexBufferResourcesUsed.Add(currentVertexBufferImport.Version.ResourceId);
                            }
                        }

                        hasDraw |= (command.DrawIndexed.IndexCount > 0) && (command.DrawIndexed.InstanceCount > 0);
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

                    case GraphicsCommandKind.CopyTexture:
                    {
                        if (pass.Kind != CommandRecorderPassKind.Copy)
                        {
                            throw new NotSupportedException("Native graph texture copies must be recorded by a copy pass.");
                        }

                        var source = command.CopySourceTexture
                            ?? throw new InvalidOperationException("The graph copy command has no source texture version.");
                        var destination = command.CopyDestinationTexture
                            ?? throw new InvalidOperationException("The graph copy command has no destination texture version.");

                        if (!copySourceTextureImports.TryGetValue(source.ResourceId, out var copySourceTextureImport)
                            || !copyDestinationTextureImports.TryGetValue(destination.ResourceId, out var copyDestinationTextureImport)
                            || (source.ResourceId == destination.ResourceId)
                            || (source.Description != copySourceTextureImport.Version.Description)
                            || (destination.Description != copyDestinationTextureImport.Version.Description)
                            || (source.Range != copySourceTextureImport.Version.Range)
                            || (destination.Range != copyDestinationTextureImport.Version.Range))
                        {
                            throw new NotSupportedException("Native graph texture copies require distinct imported copy-source and copy-destination subresources.");
                        }

                        var sourceSubresource = new TextureSubresource(source.Range.FirstMip, source.Range.FirstLayer, source.Range.Aspect);
                        var destinationSubresource = new TextureSubresource(destination.Range.FirstMip, destination.Range.FirstLayer, destination.Range.Aspect);
                        var (sourceWidth, sourceHeight, _) = GraphicsDevice.GetTextureSubresourceExtent(source.Description, sourceSubresource);
                        var (destinationWidth, destinationHeight, _) = GraphicsDevice.GetTextureSubresourceExtent(destination.Description, destinationSubresource);

                        if ((source.Description.Format != destination.Description.Format)
                            || !command.CopyTextureRegion.IsWithin(
                                sourceWidth,
                                sourceHeight,
                                destinationWidth,
                                destinationHeight))
                        {
                            throw new NotSupportedException("Native graph texture copies require matching formats and a source rectangle that fills the destination subresource.");
                        }

                        var sourceAccessDeclared = false;
                        var destinationAccessDeclared = false;

                        foreach (var access in Accesses)
                        {
                            if (!access.IsTexture || (access.PassId != pass.PassId))
                            {
                                continue;
                            }

                            sourceAccessDeclared |= (access.ResourceId == source.ResourceId)
                                && (access.RequiredState == GraphResourceState.CopySource)
                                && !access.IsWrite
                                && access.ContentsValid
                                && ReferenceEquals(access.TextureVersion, source)
                                && source.Range.IsWithin(access.TextureRange);
                            destinationAccessDeclared |= (access.ResourceId == destination.ResourceId)
                                && (access.RequiredState == GraphResourceState.CopyDestination)
                                && access.IsWrite
                                && ReferenceEquals(access.TextureVersion, destination)
                                && destination.Range.IsWithin(access.TextureRange);
                        }

                        if (!sourceAccessDeclared || !destinationAccessDeclared)
                        {
                            throw new InvalidOperationException("A native graph texture copy has no matching declared source and destination ranges.");
                        }

                        _ = copySourceTexturePasses.Add(pass.PassId);
                        _ = copyDestinationTexturePasses.Add(pass.PassId);
                        hasTextureCopy = true;
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

            if ((pass.Kind == CommandRecorderPassKind.Copy) && !pass.Commands.IsEmpty && !hasBufferCopy && !hasTextureCopy)
            {
                throw new NotSupportedException("Native graph copy passes require a supported buffer or texture copy command.");
            }
        }

        foreach (var access in Accesses)
        {
            if (access.IsTexture
                && (access.ResourceId == targetResourceId)
                && (access.RequiredState != GraphResourceState.CopySource))
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
                && vertexBufferImports.ContainsKey(access.ResourceId)
                && (access.RequiredState == GraphResourceState.VertexRead)
                && !access.IsWrite)
            {
                if (!vertexBufferBindingPairs.Contains((access.PassId, access.ResourceId)))
                {
                    throw new InvalidOperationException("The imported vertex buffer is declared by a pass without a matching vertex-buffer binding.");
                }

                continue;
            }

            if (!access.IsTexture
                && indexBufferImports.ContainsKey(access.ResourceId)
                && (access.RequiredState == GraphResourceState.IndexRead)
                && !access.IsWrite)
            {
                if (!indexBufferBindingPairs.Contains((access.PassId, access.ResourceId)))
                {
                    throw new InvalidOperationException("The imported index buffer is declared by a pass without a matching index-buffer binding.");
                }

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
                && copySourceTextureImports.ContainsKey(access.ResourceId)
                && (access.RequiredState == GraphResourceState.CopySource)
                && !access.IsWrite)
            {
                if (!copySourceTexturePasses.Contains(access.PassId))
                {
                    throw new InvalidOperationException("The imported copy-source texture is declared by a pass without a matching copy command.");
                }

                _ = copySourceTextureResourcesUsed.Add(access.ResourceId);
                continue;
            }

            if (access.IsTexture
                && copyDestinationTextureImports.ContainsKey(access.ResourceId)
                && (access.RequiredState == GraphResourceState.CopyDestination)
                && access.IsWrite)
            {
                if (!copyDestinationTexturePasses.Contains(access.PassId))
                {
                    throw new InvalidOperationException("The imported copy-destination texture is declared by a pass without a matching copy command.");
                }

                _ = copyDestinationTextureResourcesUsed.Add(access.ResourceId);
                continue;
            }

            if (access.IsTexture
                && sampledTextureImports.ContainsKey(access.ResourceId)
                && (access.RequiredState == GraphResourceState.ShaderSampledRead)
                && !access.IsWrite)
            {
                if (!sampledTextureBindingPairs.Contains((access.PassId, access.ResourceId)))
                {
                    throw new InvalidOperationException("The imported Frame sampled image is declared by a pass without a matching binding set.");
                }

                hasSampledImageAccess = true;
                _ = sampledTextureResourcesUsed.Add(access.ResourceId);
                continue;
            }

            if (access.IsTexture
                && storageTextureImports.ContainsKey(access.ResourceId)
                && (access.RequiredState == GraphResourceState.StorageReadWrite)
                && access.IsStorageReadWritePair)
            {
                if (!storageTextureBindingPairs.Contains((access.PassId, access.ResourceId)))
                {
                    throw new InvalidOperationException("The imported Frame storage image is declared by a pass without a matching binding set.");
                }

                hasStorageImageAccess = true;
                _ = storageTextureResourcesUsed.Add(access.ResourceId);
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
                && vertexBufferImports.ContainsKey(transition.ResourceId)
                && (transition.AfterState == GraphResourceState.VertexRead)
                && (transition.BeforeState is
                    GraphResourceState.General
                    or GraphResourceState.CopySource
                    or GraphResourceState.CopyDestination
                    or GraphResourceState.VertexRead)
                && vertexBufferBindingPairs.Contains((transition.PassId, transition.ResourceId)))
            {
                continue;
            }

            if (!transition.IsTexture
                && indexBufferImports.ContainsKey(transition.ResourceId)
                && (transition.AfterState == GraphResourceState.IndexRead)
                && (transition.BeforeState is
                    GraphResourceState.General
                    or GraphResourceState.CopySource
                    or GraphResourceState.CopyDestination
                    or GraphResourceState.VertexRead
                    or GraphResourceState.IndexRead)
                && indexBufferBindingPairs.Contains((transition.PassId, transition.ResourceId)))
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
                && copySourceTextureImports.ContainsKey(transition.ResourceId)
                && (transition.AfterState == GraphResourceState.CopySource)
                && (transition.BeforeState is
                    GraphResourceState.General
                    or GraphResourceState.CopySource
                    or GraphResourceState.CopyDestination
                    or GraphResourceState.ColorAttachment
                    or GraphResourceState.ShaderSampledRead
                    or GraphResourceState.StorageRead
                    or GraphResourceState.StorageWrite
                    or GraphResourceState.StorageReadWrite)
                && copySourceTexturePasses.Contains(transition.PassId))
            {
                continue;
            }

            if (transition.IsTexture
                && copyDestinationTextureImports.ContainsKey(transition.ResourceId)
                && (transition.AfterState == GraphResourceState.CopyDestination)
                && (transition.BeforeState is
                    GraphResourceState.Undefined
                    or GraphResourceState.General
                    or GraphResourceState.CopySource
                    or GraphResourceState.CopyDestination
                    or GraphResourceState.ColorAttachment
                    or GraphResourceState.ShaderSampledRead
                    or GraphResourceState.StorageRead
                    or GraphResourceState.StorageWrite
                    or GraphResourceState.StorageReadWrite)
                && copyDestinationTexturePasses.Contains(transition.PassId))
            {
                continue;
            }

            if (transition.IsTexture
                && sampledTextureImports.ContainsKey(transition.ResourceId)
                && (transition.AfterState == GraphResourceState.ShaderSampledRead)
                && (transition.BeforeState == GraphResourceState.General)
                && sampledTextureBindingPairs.Contains((transition.PassId, transition.ResourceId)))
            {
                continue;
            }

            if (transition.IsTexture
                && storageTextureImports.ContainsKey(transition.ResourceId)
                && (transition.AfterState == GraphResourceState.StorageReadWrite)
                && (transition.BeforeState is
                    GraphResourceState.General
                    or GraphResourceState.StorageRead
                    or GraphResourceState.StorageWrite
                    or GraphResourceState.StorageReadWrite)
                && storageTextureBindingPairs.Contains((transition.PassId, transition.ResourceId)))
            {
                continue;
            }

            if ((uniformBufferImport is null || transition.ResourceId != uniformBufferImport.Version.ResourceId)
                && !vertexBufferImports.ContainsKey(transition.ResourceId)
                && !indexBufferImports.ContainsKey(transition.ResourceId)
                && !storageBufferImports.ContainsKey(transition.ResourceId)
                && !copySourceTextureImports.ContainsKey(transition.ResourceId)
                && !copyDestinationTextureImports.ContainsKey(transition.ResourceId)
                && !sampledTextureImports.ContainsKey(transition.ResourceId)
                && !storageTextureImports.ContainsKey(transition.ResourceId))
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

        if ((vertexBufferImports.Count != 0)
            && (!hasVertexBufferPipeline
                || !hasVertexBufferBinding
                || (vertexBufferResourcesUsed.Count != vertexBufferImports.Count)))
        {
            throw new NotSupportedException("Every imported vertex buffer must be bound by a vertex-input graphics pipeline and used by a draw.");
        }

        if ((indexBufferImports.Count != 0)
            && (!hasIndexBufferBinding
                || (indexBufferResourcesUsed.Count != indexBufferImports.Count)))
        {
            throw new NotSupportedException("Every imported index buffer must be bound and used by an indexed draw.");
        }

        if (copySourceResourcesUsed.Count != copySourceBufferImports.Count)
        {
            throw new NotSupportedException("Every imported copy-source buffer must be read by a graph copy command.");
        }

        if (copyDestinationResourcesUsed.Count != copyDestinationBufferImports.Count)
        {
            throw new NotSupportedException("Every imported copy-destination buffer must be written by a graph copy command.");
        }

        if (copySourceTextureResourcesUsed.Count != copySourceTextureImports.Count)
        {
            throw new NotSupportedException(
                $"Every imported copy-source texture must be read by a graph copy command ({copySourceTextureResourcesUsed.Count} of {copySourceTextureImports.Count} were used).");
        }

        if (copyDestinationTextureResourcesUsed.Count != copyDestinationTextureImports.Count)
        {
            throw new NotSupportedException("Every imported copy-destination texture must be written by a graph copy command.");
        }

        if ((sampledTextureImports.Count != 0)
            && (!hasSampledImagePipeline
                || !hasSampledImageBinding
                || !hasSampledImageAccess
                || !hasSamplerPipeline
                || !hasSamplerBinding
                || (sampledTextureResourcesUsed.Count != sampledTextureImports.Count)))
        {
            throw new NotSupportedException("Every imported Frame sampled image must be reflected, bound with its sampler, and declared by a graph pass.");
        }

        if ((storageBufferImports.Count != 0)
            && (!hasStorageBufferPipeline
                || !hasStorageBufferBinding
                || !hasStorageBufferAccess
                || (storageBufferResourcesUsed.Count != storageBufferImports.Count)))
        {
            throw new NotSupportedException("Every imported Frame storage buffer must be reflected, bound, and declared by a graph pass.");
        }

        if ((storageTextureImports.Count != 0)
            && (!hasStorageImagePipeline
                || !hasStorageImageBinding
                || !hasStorageImageAccess
                || (storageTextureResourcesUsed.Count != storageTextureImports.Count)))
        {
            throw new NotSupportedException("Every imported Frame storage image must be reflected, bound, and declared by a graph pass.");
        }

        if (!hasColorClear)
        {
            throw new NotSupportedException("Native graph submission requires at least one stored graph-output color clear.");
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
