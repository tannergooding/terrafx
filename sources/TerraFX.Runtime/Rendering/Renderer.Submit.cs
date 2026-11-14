// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Graphics;

namespace TerraFX.Rendering;

public sealed partial class Renderer
{
    private static bool MatchesTextureDescription(TextureDescription expected, TextureDescription actual)
        => (expected.Dimension == actual.Dimension)
            && (expected.Width == actual.Width)
            && (expected.Height == actual.Height)
            && (expected.Depth == actual.Depth)
            && (expected.ArrayLayers == actual.ArrayLayers)
            && (expected.MipLevels == actual.MipLevels)
            && (expected.Format == actual.Format)
            && (expected.Usage == actual.Usage);

    /// <summary>Submits an immutable graph compiled for the exact render snapshot.</summary>
    /// <param name="snapshot">The World-produced snapshot used to build the graph.</param>
    /// <param name="graph">The successfully compiled graph plan.</param>
    /// <param name="cancellationToken">A token that cancels admission but not accepted GPU work.</param>
    /// <returns>A receipt after GPU completion and submission-pin retirement.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> or <paramref name="graph"/> is null.</exception>
    /// <exception cref="ArgumentException">The graph was compiled for another snapshot or target.</exception>
    /// <exception cref="InvalidOperationException">The graph belongs to a stale GraphicsDevice epoch.</exception>
    /// <exception cref="NotSupportedException">This Renderer or backend does not support the graph's target or commands.</exception>
    public async Task<RenderSubmissionReceipt> SubmitAsync(
        RenderSnapshot snapshot,
        RenderGraphPlan graph,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(graph);
        cancellationToken.ThrowIfCancellationRequested();

        var device = _graphicsDevice
            ?? throw new NotSupportedException("Headless rendering does not submit native render graphs.");
        ObjectDisposedException.ThrowIf(snapshot.IsDisposed, snapshot);
        using var snapshotAssetPins = snapshot.RetainAssetPins();

        if ((graph.SnapshotId != snapshot.SnapshotId)
            || (graph.WorldGuid != snapshot.WorldGuid)
            || (graph.TickIndex != snapshot.TickIndex)
            || (graph.FrameDimension != snapshot.FrameDimension)
            || (graph.FrameRevision != snapshot.FrameRevision)
            || (graph.ProfileId != snapshot.ProfileId)
            || (graph.TargetExtent != snapshot.TargetExtent)
            || !graph.CameraIds.AsSpan().SequenceEqual(snapshot.CameraIds.AsSpan()))
        {
            throw new ArgumentException("The graph plan does not match the exact render snapshot.", nameof(graph));
        }

        if (graph.DeviceEpoch != device.DeviceEpoch)
        {
            throw new InvalidOperationException("The graph plan belongs to a stale GraphicsDevice epoch.");
        }

        var offscreenOutputTarget = graph.OffscreenColorTarget;

        if (offscreenOutputTarget is not null
            && (!ReferenceEquals(offscreenOutputTarget.ResourceOwner.Device, device)
                || (offscreenOutputTarget.DeviceEpoch != graph.DeviceEpoch)))
        {
            throw new ArgumentException("The graph output target belongs to another device or epoch.", nameof(graph));
        }

        var surfaceTarget = graph.SurfaceTarget;

        if (surfaceTarget is not null)
        {
            ObjectDisposedException.ThrowIf(!surfaceTarget.IsLive, surfaceTarget);
            var surfaceSnapshot = surfaceTarget.CaptureSnapshot();

            if (!ReferenceEquals(surfaceTarget.Device, device))
            {
                throw new ArgumentException("The graph Surface belongs to another GraphicsDevice.", nameof(graph));
            }

            if ((surfaceTarget.Device.DeviceEpoch != graph.DeviceEpoch)
                || (surfaceSnapshot.Capabilities.SurfaceEpoch != graph.SurfaceEpoch)
                || (surfaceSnapshot.Capabilities.SwapchainEpoch != graph.SwapchainEpoch)
                || (surfaceSnapshot.ClientPixelSize != graph.TargetExtent))
            {
                throw new InvalidOperationException("The graph plan belongs to a stale Surface or swapchain generation.");
            }
        }

        var graphResourceOwners = new HashSet<GraphicsResourceOwner>();

        if (offscreenOutputTarget is not null)
        {
            _ = graphResourceOwners.Add(offscreenOutputTarget.ResourceOwner);
        }

        foreach (var import in graph.BufferImports)
        {
            _ = graphResourceOwners.Add(import.Buffer.ResourceOwner);
        }

        foreach (var import in graph.TextureImports)
        {
            _ = graphResourceOwners.Add(import.Texture.ResourceOwner);
        }

        var transientBuffers = new List<GpuBuffer>(graph.TransientBuffers.Length);
        var transientBufferGroups = new List<GraphicsTransientBufferGroup>(graph.TransientBufferGroups.Length);
        var transientBuffersByResourceId = new Dictionary<ulong, GpuBuffer>(graph.TransientBuffers.Length);
        var transientTextures = new List<GpuTexture>(graph.TransientTextures.Length);
        var transientTextureGroups = new List<GraphicsTransientTextureGroup>(graph.TransientTextureGroups.Length);
        var transientTexturesByResourceId = new Dictionary<ulong, GpuTexture>(graph.TransientTextures.Length);
        var deferredTextureResidencies = new Dictionary<ulong, GpuResidencyLease>();
        var deferredTextureInitialStates = new Dictionary<ulong, GraphResourceState>();
        var deferredTextureLeases = new List<GpuResidencyLease>();
        var initializedSpriteBufferIds = new HashSet<ulong>();
        GpuPipeline? spritePipeline = null;
        GpuPipeline? meshAlphaMaskPipeline = null;
        GpuPipeline? meshPipeline = null;
        GpuPipeline? meshBlendPipeline = null;
        GpuPipeline? texturedMeshAlphaMaskPipeline = null;
        GpuPipeline? texturedMeshPipeline = null;
        GpuPipeline? texturedMeshBlendPipeline = null;
        GpuPipeline? spriteToneMapPipeline = null;
        GpuSampler? spriteSampler = null;
        RenderBuiltInSubmissionResources? builtInResources = null;
        GraphicsDevice.GraphResourceUseLeaseSet? graphResourceUseLeases = null;
        GraphicsDevice.GraphFrameSlotReservation? graphFrameSlotReservation = null;
        var graphResourceUseLeasesTransferred = false;
        IDisposable? spriteSubmissionLease = null;

        if (graph.BuiltInSpriteState is not null)
        {
            spriteSubmissionLease = await (_gpuResidencyService
                ?? throw new InvalidOperationException("The built-in Sprite graph has no GPU residency service."))
                .AcquireSpriteSubmissionLeaseAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        try
        {
            graphResourceUseLeases = device.AcquireGraphResourceUseLeases(graphResourceOwners);
            graphFrameSlotReservation = await device.AcquireGraphFrameSlotAsync(cancellationToken).ConfigureAwait(false);

            if (graph.BuiltInSpriteState is { } spriteState)
            {
                var retainedAssetPins = snapshotAssetPins
                    ?? throw new InvalidOperationException("The built-in Sprite graph has no retained snapshot assets.");

                foreach (var upload in spriteState.BufferUploads)
                {
                    _ = initializedSpriteBufferIds.Add(upload.Version.ResourceId);
                }

                foreach (var deferredTexture in spriteState.DeferredTextures)
                {
                    var residency = await EnsureSpritePageResidencyAsync(
                        snapshot,
                        retainedAssetPins,
                        deferredTexture.DrawIndex,
                        new ResidencyOptions(TextureUsage.Sampled),
                        cancellationToken).ConfigureAwait(false);
                    deferredTextureLeases.Add(residency);

                    if ((residency.DeviceEpoch != graph.DeviceEpoch)
                        || !MatchesTextureDescription(
                            residency.Texture.Description,
                            deferredTexture.Version.Description))
                    {
                        throw new InvalidOperationException("A Sprite Atlas page residency does not match the compiled graph resource.");
                    }

                    deferredTextureResidencies.Add(deferredTexture.Version.ResourceId, residency);
                    var (pageState, pageContentsValid) =
                        residency.Texture.ResourceOwner.GetGraphImportState(deferredTexture.Version.Range);

                    if (!pageContentsValid
                        || (pageState is not (GraphResourceState.General or GraphResourceState.ShaderSampledRead)))
                    {
                        throw new InvalidOperationException("A Sprite Atlas page has unsupported content or state for sampling.");
                    }

                    deferredTextureInitialStates.Add(deferredTexture.Version.ResourceId, pageState);
                }

                foreach (var deferredTexture in spriteState.MeshDeferredTextures)
                {
                    var meshDraws = snapshot is RenderSnapshot3D meshSnapshot
                        ? meshSnapshot.MeshDraws
                        : throw new InvalidOperationException("The built-in Mesh graph has no 3D snapshot.");
                    var meshDraw = meshDraws[deferredTexture.DrawIndex];
                    var sceneAssets = retainedAssetPins.GetSceneAssets(meshDraw.SceneInstanceId);
                    var textureSet = sceneAssets.GetMaterialTextureSet(meshDraw.MaterialAssetId)
                        ?? throw new InvalidOperationException("The Mesh Material base-color texture is not retained by its Scene assets.");
                    using var textureAssetLease = textureSet.RetainTexture(new TerraFX.Assets.AssetId(deferredTexture.TextureAssetId));
                    var residency = await (_gpuResidencyService
                        ?? throw new InvalidOperationException("The built-in Mesh graph has no GPU residency service."))
                        .EnsureGpuResidencyAsync(
                            textureAssetLease,
                            new ResidencyOptions(TextureUsage.Sampled),
                            cancellationToken).ConfigureAwait(false);
                    deferredTextureLeases.Add(residency);

                    if ((residency.DeviceEpoch != graph.DeviceEpoch)
                        || !MatchesTextureDescription(
                            residency.Texture.Description,
                            deferredTexture.Version.Description))
                    {
                        throw new InvalidOperationException("A Mesh base-color texture residency does not match the compiled graph resource.");
                    }

                    deferredTextureResidencies.Add(deferredTexture.Version.ResourceId, residency);
                    var (textureState, textureContentsValid) =
                        residency.Texture.ResourceOwner.GetGraphImportState(deferredTexture.Version.Range);

                    if (!textureContentsValid
                        || (textureState is not (GraphResourceState.General or GraphResourceState.ShaderSampledRead)))
                    {
                        throw new InvalidOperationException("A Mesh base-color texture has unsupported content or state for sampling.");
                    }

                    deferredTextureInitialStates.Add(deferredTexture.Version.ResourceId, textureState);
                }

                if (spriteState.Passes.Count != 0)
                {
                    spritePipeline = await CreateSpriteC0PipelineAsync(
                        spriteState.SpriteColorTargetFormat,
                        cancellationToken).ConfigureAwait(false);
                }

                var hasUntexturedMeshPass = false;
                var hasUntexturedAlphaMaskPass = false;
                var hasUntexturedBlendPass = false;
                var hasTexturedMeshPass = false;
                var hasTexturedAlphaMaskPass = false;
                var hasTexturedBlendPass = false;

                foreach (var meshPass in spriteState.MeshPasses.Values)
                {
                    var hasTexture = (meshPass.BaseColorTexture is not null)
                        || (meshPass.EmissiveTexture is not null)
                        || (meshPass.PackedMetalRoughOcclusionTexture is not null);

                    if (meshPass.AlphaBlend)
                    {
                        hasTexturedBlendPass |= hasTexture;
                        hasUntexturedBlendPass |= !hasTexture;
                    }
                    else if (meshPass.AlphaMask)
                    {
                        hasTexturedAlphaMaskPass |= hasTexture;
                        hasUntexturedAlphaMaskPass |= !hasTexture;
                    }
                    else if (hasTexture)
                    {
                        hasTexturedMeshPass = true;
                    }
                    else
                    {
                        hasUntexturedMeshPass = true;
                    }
                }

                if (hasUntexturedAlphaMaskPass)
                {
                    meshAlphaMaskPipeline = await CreateMeshC0AlphaMaskPipelineAsync(
                        spriteState.SpriteColorTargetFormat,
                        cancellationToken).ConfigureAwait(false);
                }

                if (hasUntexturedMeshPass)
                {
                    meshPipeline = await CreateMeshC0PipelineAsync(
                        spriteState.SpriteColorTargetFormat,
                        cancellationToken).ConfigureAwait(false);
                }

                if (hasUntexturedBlendPass)
                {
                    meshBlendPipeline = await CreateMeshC0BlendPipelineAsync(
                        spriteState.SpriteColorTargetFormat,
                        cancellationToken).ConfigureAwait(false);
                }

                if (hasTexturedAlphaMaskPass)
                {
                    texturedMeshAlphaMaskPipeline = await CreateTexturedMeshC0AlphaMaskPipelineAsync(
                        spriteState.SpriteColorTargetFormat,
                        cancellationToken).ConfigureAwait(false);
                }

                if (hasTexturedMeshPass)
                {
                    texturedMeshPipeline = await CreateTexturedMeshC0PipelineAsync(
                        spriteState.SpriteColorTargetFormat,
                        cancellationToken).ConfigureAwait(false);
                }

                if (hasTexturedBlendPass)
                {
                    texturedMeshBlendPipeline = await CreateTexturedMeshC0BlendPipelineAsync(
                        spriteState.SpriteColorTargetFormat,
                        cancellationToken).ConfigureAwait(false);
                }

                if (spriteState.ToneMapPasses.Count != 0)
                {
                    spriteToneMapPipeline = await CreateSpriteToneMapPipelineAsync(
                        spriteState.ToneMapColorTargetFormat,
                        cancellationToken).ConfigureAwait(false);
                }

                if (((spriteState.Passes.Count != 0) && (spritePipeline is null))
                    || (hasUntexturedAlphaMaskPass && (meshAlphaMaskPipeline is null))
                    || (hasUntexturedMeshPass && (meshPipeline is null))
                    || (hasUntexturedBlendPass && (meshBlendPipeline is null))
                    || (hasTexturedAlphaMaskPass && (texturedMeshAlphaMaskPipeline is null))
                    || (hasTexturedMeshPass && (texturedMeshPipeline is null))
                    || (hasTexturedBlendPass && (texturedMeshBlendPipeline is null))
                    || ((spriteState.ToneMapPasses.Count != 0) && (spriteToneMapPipeline is null)))
                {
                    throw new InvalidOperationException("The built-in render graph has an incomplete pipeline set.");
                }

                if (spriteState.Passes.Count != 0
                    || hasTexturedMeshPass
                    || hasTexturedAlphaMaskPass
                    || hasTexturedBlendPass
                    || spriteState.ToneMapPasses.Count != 0)
                {
                    spriteSampler = await device.CreateSamplerAsync(
                        new SamplerDescription(
                            SamplerFilter.Linear,
                            SamplerFilter.Linear,
                            SamplerFilter.None,
                            SamplerAddressMode.Clamp,
                            SamplerAddressMode.Clamp,
                            SamplerAddressMode.Clamp,
                            maximumAnisotropy: 1.0f),
                        cancellationToken).ConfigureAwait(false);
                }

                if (spriteState.Passes.Count != 0
                    || spriteState.MeshPasses.Count != 0
                    || spriteState.ToneMapPasses.Count != 0)
                {
                    builtInResources = new RenderBuiltInSubmissionResources(
                        meshAlphaMaskPipeline,
                        spritePipeline,
                        meshPipeline,
                        meshBlendPipeline,
                        texturedMeshAlphaMaskPipeline,
                        texturedMeshPipeline,
                        texturedMeshBlendPipeline,
                        spriteToneMapPipeline,
                        spriteSampler);
                }
            }

            foreach (var transientGroup in graph.TransientBufferGroups)
            {
                if (transientGroup.Length > 1)
                {
                    var materializedGroup = await device.CreateTransientBufferGroupAsync(
                        transientGroup,
                        cancellationToken).ConfigureAwait(false);
                    transientBufferGroups.Add(materializedGroup);

                    for (var index = 0; index < transientGroup.Length; index++)
                    {
                        transientBuffersByResourceId.Add(
                            transientGroup[index].ResourceId,
                            materializedGroup.Buffers[index]);
                    }
                }
                else
                {
                    var transientVersion = transientGroup[0];
                    var buffer = await device.CreateBufferAsync(
                        transientVersion.Description,
                        cancellationToken).ConfigureAwait(false);
                    transientBuffers.Add(buffer);
                    transientBuffersByResourceId.Add(transientVersion.ResourceId, buffer);
                }
            }

            if (graph.BuiltInSpriteState is { } uploadState)
            {
                foreach (var upload in uploadState.BufferUploads)
                {
                    var buffer = transientBuffersByResourceId[upload.Version.ResourceId];
                    var receipt = await device.UploadBufferAsync(
                        buffer,
                        destinationOffset: 0,
                        upload.Bytes,
                        cancellationToken).ConfigureAwait(false);

                    if ((receipt.Status != UploadStatus.Ready) || (receipt.DeviceEpoch != graph.DeviceEpoch))
                    {
                        throw new InvalidOperationException("A built-in Sprite buffer upload did not complete on the graph device epoch.");
                    }

                    var range = new GraphBufferRange(0, buffer.ByteLength);
                    var (state, contentsValid) = buffer.ResourceOwner.GetGraphImportState(range);

                    if ((state != GraphResourceState.General) || !contentsValid)
                    {
                        throw new InvalidOperationException("A built-in Sprite buffer upload did not publish initialized General-state contents.");
                    }
                }
            }

            var transientBufferImports = ImmutableArray.CreateBuilder<GraphBufferImportRecord>(graph.TransientBuffers.Length);

            for (var index = 0; index < graph.TransientBuffers.Length; index++)
            {
                var transientVersion = graph.TransientBuffers[index];
                var buffer = transientBuffersByResourceId[transientVersion.ResourceId];

                if (initializedSpriteBufferIds.Contains(transientVersion.ResourceId))
                {
                    transientBufferImports.Add(new GraphBufferImportRecord(
                        buffer,
                        transientVersion,
                        new GraphImportOptions(
                            GraphResourceState.General,
                            contentsValid: true,
                            GraphImportCompletionPolicy.RequireIdle),
                        isTransient: true));
                }
                else
                {
                    transientBufferImports.Add(new GraphBufferImportRecord(
                        buffer,
                        transientVersion,
                        isTransient: true));
                }
            }

            foreach (var transientGroup in graph.TransientTextureGroups)
            {
                if (transientGroup.Length > 1)
                {
                    var materializedGroup = await device.CreateTransientTextureGroupAsync(
                        transientGroup,
                        cancellationToken).ConfigureAwait(false);
                    transientTextureGroups.Add(materializedGroup);

                    for (var index = 0; index < transientGroup.Length; index++)
                    {
                        transientTexturesByResourceId.Add(
                            transientGroup[index].ResourceId,
                            materializedGroup.Textures[index]);
                    }
                }
                else
                {
                    var transientVersion = transientGroup[0];

                    if (deferredTextureResidencies.TryGetValue(transientVersion.ResourceId, out var residency))
                    {
                        transientTexturesByResourceId.Add(transientVersion.ResourceId, residency.Texture);
                    }
                    else
                    {
                        var texture = await device.CreateTextureAsync(
                            transientVersion.Description,
                            cancellationToken).ConfigureAwait(false);
                        transientTextures.Add(texture);
                        transientTexturesByResourceId.Add(transientVersion.ResourceId, texture);
                    }
                }
            }

            var transientTextureImports = ImmutableArray.CreateBuilder<GraphTextureImportRecord>(graph.TransientTextures.Length);

            foreach (var transientVersion in graph.TransientTextures)
            {
                var texture = transientTexturesByResourceId[transientVersion.ResourceId];

                if (deferredTextureResidencies.ContainsKey(transientVersion.ResourceId))
                {
                    var (state, contentsValid) = texture.ResourceOwner.GetGraphImportState(transientVersion.Range);
                    var expectedState = deferredTextureInitialStates[transientVersion.ResourceId];

                    if ((state != expectedState) || !contentsValid)
                    {
                        throw new InvalidOperationException("A deferred Sprite or Mesh texture changed state before graph submission.");
                    }

                    transientTextureImports.Add(new GraphTextureImportRecord(
                        texture,
                        transientVersion,
                        new GraphImportOptions(
                            state,
                            contentsValid: true,
                            GraphImportCompletionPolicy.WaitForLastUse),
                        isTransient: false));
                    continue;
                }

                transientTextureImports.Add(new GraphTextureImportRecord(
                    texture,
                    transientVersion,
                    new GraphImportOptions(
                        GraphResourceState.Undefined,
                        contentsValid: false,
                        GraphImportCompletionPolicy.WaitForLastUse),
                    isTransient: true));
            }

            GpuTexture outputColorTarget;

            if (offscreenOutputTarget is not null)
            {
                outputColorTarget = offscreenOutputTarget;
            }
            else
            {
                var outputResourceId = graph.PresentationSource.ResourceId;
                GpuTexture? selectedTarget = null;

                foreach (var import in graph.TextureImports)
                {
                    if (import.Version.ResourceId == outputResourceId)
                    {
                        selectedTarget = import.Texture;
                        break;
                    }
                }

                if ((selectedTarget is null)
                    && !transientTexturesByResourceId.TryGetValue(outputResourceId, out selectedTarget))
                {
                    throw new InvalidOperationException("The Surface presentation source was not materialized for submission.");
                }

                outputColorTarget = selectedTarget;
            }

            var commandLists = await graph.RecordPassCallbacksAsync(builtInResources).ConfigureAwait(false);
            GraphicsGraphSubmission submission;

            try
            {
                submission = graph.CreateGraphicsSubmission(
                    outputColorTarget,
                    commandLists,
                    transientBufferImports.MoveToImmutable(),
                    transientTextureImports.MoveToImmutable(),
                    deferredTextureInitialStates);
            }
            catch
            {
                foreach (var commandList in commandLists)
                {
                    commandList.Dispose();
                }

                throw;
            }

            var admittedResourceUseLeases = graphResourceUseLeases
                ?? throw new InvalidOperationException("The graph resource-use leases were not admitted.");
            var submissionTask = device.SubmitGraphAsync(
                submission,
                admittedResourceUseLeases,
                graphFrameSlotReservation,
                cancellationToken);
            graphResourceUseLeasesTransferred = true;
            graphFrameSlotReservation = null;
            var result = await submissionTask.ConfigureAwait(false);
            return new RenderSubmissionReceipt(
                result.SubmissionSequence,
                result.CompletedFence,
                result.PresentationSequence,
                result.DeviceEpoch,
                result.SurfaceEpoch,
                result.SwapchainEpoch);
        }
        finally
        {
            try
            {
                if (!graphResourceUseLeasesTransferred)
                {
                    graphResourceUseLeases?.Dispose();
                }

                graphFrameSlotReservation?.Dispose();
                spriteSampler?.Dispose();
                spriteToneMapPipeline?.Dispose();
                meshAlphaMaskPipeline?.Dispose();
                meshBlendPipeline?.Dispose();
                meshPipeline?.Dispose();
                texturedMeshAlphaMaskPipeline?.Dispose();
                texturedMeshBlendPipeline?.Dispose();
                texturedMeshPipeline?.Dispose();
                spritePipeline?.Dispose();

                foreach (var deferredTextureLease in deferredTextureLeases)
                {
                    deferredTextureLease.Dispose();
                }

                foreach (var transientBufferGroup in transientBufferGroups)
                {
                    transientBufferGroup.Dispose();
                }

                foreach (var transientBuffer in transientBuffers)
                {
                    transientBuffer.Dispose();
                }

                foreach (var transientTexture in transientTextures)
                {
                    transientTexture.Dispose();
                }

                foreach (var transientTextureGroup in transientTextureGroups)
                {
                    transientTextureGroup.Dispose();
                }
            }
            finally
            {
                spriteSubmissionLease?.Dispose();
            }
        }
    }
}
