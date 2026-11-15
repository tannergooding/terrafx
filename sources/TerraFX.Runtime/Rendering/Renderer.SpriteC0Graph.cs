// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using TerraFX.Assets;
using TerraFX.Graphics;
using TerraFX.Numerics;
using TerraFX.Scenes;

namespace TerraFX.Rendering;

public sealed partial class Renderer
{
    private static Vector4 Multiply(Vector4 left, Vector4 right)
        => new(left.X * right.X, left.Y * right.Y, left.Z * right.Z, left.W * right.W);

    private void BuildSpriteC0Graph(
        RenderSnapshot snapshot,
        RenderGraph graph,
        ref RenderGraphBuiltInColorState? colorState)
    {
        if (snapshot.SpriteDraws.IsEmpty)
        {
            return;
        }

        var device = _graphicsDevice
            ?? throw new NotSupportedException("Headless rendering does not construct built-in Sprite passes.");
        var batches = RenderSpriteBatcher.Create(snapshot);

        if (batches.IsEmpty)
        {
            throw new InvalidOperationException("The Sprite snapshot has no camera batches.");
        }

        using var assetPins = snapshot.RetainAssetPins()
            ?? throw new InvalidOperationException("The Sprite snapshot has no retained Scene assets.");
        var gpuSprites = snapshot.SpriteDraws.AsSpan().ToArray();

        for (var index = 0; index < gpuSprites.Length; index++)
        {
            var sprite = gpuSprites[index];
            var tint = sprite.Tint;

            if (sprite.MaterialAssetId is int materialAssetId)
            {
                var material = assetPins.GetSceneAssets(sprite.SceneInstanceId).GetMaterial(materialAssetId);

                if (!material.IsSprite
                    || (material.AlphaMode != RenderMaterialAsset.MaterialAlphaMode.Blend)
                    || material.DepthTest
                    || material.DepthWrite
                    || (material.CullMode != RenderMaterialAsset.MaterialCullMode.None)
                    || (material.OutputAlpha != RenderMaterialAsset.MaterialOutputAlpha.Premultiplied)
                    || material.Parameters.SpriteTexture.HasValue)
                {
                    throw new NotSupportedException("Sprite C0 supports only premultiplied, unlit Sprite materials without depth, culling, or material texture overrides.");
                }

                tint = Multiply(tint, material.Parameters.Color);
            }

            if ((tint.X < 0.0f) || (tint.Y < 0.0f) || (tint.Z < 0.0f) || (tint.W < 0.0f))
            {
                throw new NotSupportedException("Sprite C0 requires nonnegative linear RGBA tint for its HDR tone-map path.");
            }

            gpuSprites[index] = sprite with { Tint = tint };
        }

        colorState ??= new RenderGraphBuiltInColorState(
            graph.CreateTransientTexture(
                new TextureDescription(
                    TextureDimension.Dimension2D,
                    snapshot.TargetExtent.Width,
                    snapshot.TargetExtent.Height,
                    depth: 1,
                    arrayLayers: 1,
                    mipLevels: 1,
                    TextureFormat.Rgba16Float,
                    TextureUsage.ColorAttachment | TextureUsage.Sampled,
                    "builtin-hdr-working-target")));
        var alignment = checked((int)device.Capabilities.Limits.UniformBufferOffsetAlignment);
        var frameBytes = RenderCameraGpuDataBufferBuilder.CreateFrameBuffer(
            snapshot.Cameras.AsSpan(),
            alignment,
            out var frameStride);
        var objectBytes = RenderSpriteGpuDataBufferBuilder.CreateObjectBuffer(
            gpuSprites,
            alignment,
            out var objectStride);
        var vertexBytes = RenderSpriteVertexBufferBuilder.Create(snapshot.SpriteDraws.AsSpan());
        var indexBytes = MemoryMarshal.AsBytes(RenderSpriteGeometry.Indices.AsSpan()).ToArray();
        var frameBuffer = graph.CreateInitializedTransientBuffer(
            new BufferDescription(
                frameBytes.Length,
                BufferUsage.Uniform | BufferUsage.CopyDestination,
                GpuMemoryKind.DeviceLocal,
                structuredStride: null,
                "sprite-frame-data"),
            frameBytes);
        var objectBuffer = graph.CreateInitializedTransientBuffer(
            new BufferDescription(
                objectBytes.Length,
                BufferUsage.Uniform | BufferUsage.CopyDestination,
                GpuMemoryKind.DeviceLocal,
                structuredStride: null,
                "sprite-object-data"),
            objectBytes);
        var vertexBuffer = graph.CreateInitializedTransientBuffer(
            new BufferDescription(
                vertexBytes.Length,
                BufferUsage.Vertex | BufferUsage.CopyDestination,
                GpuMemoryKind.DeviceLocal,
                structuredStride: null,
                "sprite-vertex-data"),
            vertexBytes);
        var indexBuffer = graph.CreateInitializedTransientBuffer(
            new BufferDescription(
                indexBytes.Length,
                BufferUsage.Index | BufferUsage.CopyDestination,
                GpuMemoryKind.DeviceLocal,
                structuredStride: null,
                "sprite-index-data"),
            indexBytes);
        var pageVersions = new Dictionary<(AssetPackageBackingToken BackingToken, int TextureAssetId), GraphTextureVersion>();
        var batchPageVersions = new Dictionary<(Guid SceneInstanceId, int TextureAssetId), GraphTextureVersion>();

        foreach (var batch in batches)
        {
            var batchKey = (batch.Key.SceneInstanceId, batch.Key.TextureAssetId);

            if (batchPageVersions.ContainsKey(batchKey))
            {
                continue;
            }

            var drawIndex = batch.DrawIndices[0];
            var sprite = snapshot.SpriteDraws[drawIndex];
            var sceneAssets = assetPins.GetSceneAssets(sprite.SceneInstanceId);
            var pageSet = sceneAssets.GetAtlasPageSet(sprite.AtlasAssetId);
            var frame = pageSet.GetFrame(sprite.FrameId);
            using var pageLease = pageSet.RetainPageLease(frame.Page);
            var residencyKey = (pageLease.BackingToken, batch.Key.TextureAssetId);

            if (!pageVersions.TryGetValue(residencyKey, out var version))
            {
                var description = GpuResidencyService.CreateTextureDescription(
                    pageLease.Value,
                    TextureUsage.Sampled,
                    $"runtime-texture-{batch.Key.TextureAssetId}");
                version = graph.CreateDeferredTransientTexture(description, drawIndex);
                pageVersions.Add(residencyKey, version);
            }

            batchPageVersions.Add(batchKey, version);
        }

        var hdrTargetInitialized = colorState.HasColorContents;
        var currentColorVersion = colorState.CurrentColorVersion;
        var previousPass = colorState.PreviousPass;
        var hasPreviousPass = colorState.HasPreviousPass;
        var passIndex = colorState.NextPassIndex;
        var batchesByCamera = new Dictionary<Guid, List<RenderSpriteBatch>>();

        foreach (var batch in batches)
        {
            if (!batchesByCamera.TryGetValue(batch.CameraId, out var cameraBatches))
            {
                cameraBatches = [];
                batchesByCamera.Add(batch.CameraId, cameraBatches);
            }

            cameraBatches.Add(batch);
        }

        for (var cameraIndex = 0; cameraIndex < snapshot.Cameras.Length; cameraIndex++)
        {
            var camera = snapshot.Cameras[cameraIndex];
            var area = RenderCameraViewport.CreateArea(camera.Viewport, snapshot.TargetExtent);
            var clearsFullTarget = (area.X == 0)
                && (area.Y == 0)
                && (area.Width == snapshot.TargetExtent.Width)
                && (area.Height == snapshot.TargetExtent.Height);
            var clearColor = (camera.ClearFlags & CameraClearFlags.Color) != 0;

            if (clearColor
                && ((camera.ClearColor.X < 0.0f)
                    || (camera.ClearColor.Y < 0.0f)
                    || (camera.ClearColor.Z < 0.0f)))
            {
                throw new NotSupportedException("Sprite C0 requires nonnegative camera clear RGB for its HDR tone-map path.");
            }

            _ = batchesByCamera.TryGetValue(camera.CameraId, out var cameraBatches);
            var batchCount = cameraBatches?.Count ?? 0;
            var cameraAlreadyWroteColor = colorState.HasColorWrite(camera.CameraId);

            if (batchCount == 0)
            {
                if (!clearColor || cameraAlreadyWroteColor)
                {
                    continue;
                }

                if (!hdrTargetInitialized && !clearsFullTarget)
                {
                    throw new NotSupportedException("Sprite C0 requires its first HDR camera write to clear the full target.");
                }

                var clearPass = graph.AddRasterPass(new RenderPassDescription(
                    $"TerraFX.SpriteC0.Camera{cameraIndex}.Clear",
                    passIndex++,
                    hasPreviousPass ? [previousPass] : [],
                    hasExternalSideEffects: false,
                    renderArea: area));
                previousPass = clearPass;
                hasPreviousPass = true;
                currentColorVersion = graph.WriteColorAttachment(
                    clearPass,
                    currentColorVersion,
                    new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color),
                    AttachmentLoadAction.Clear,
                    AttachmentStoreAction.Store,
                    camera.ClearColor);
                hdrTargetInitialized = true;
                colorState.MarkColorWrite(camera.CameraId);
                continue;
            }

            if (!hdrTargetInitialized)
            {
                if (!clearColor || !clearsFullTarget)
                {
                    throw new NotSupportedException("Sprite C0 requires its first HDR camera write to clear the full target.");
                }

                hdrTargetInitialized = true;
            }

            var clearCamera = clearColor && !cameraAlreadyWroteColor;

            for (var batchIndex = 0; batchIndex < batchCount; batchIndex++)
            {
                var batch = cameraBatches![batchIndex];
                var pageVersion = batchPageVersions[(batch.Key.SceneInstanceId, batch.Key.TextureAssetId)];
                var spritePass = new RenderSpriteC0GraphPass(
                    batch,
                    pageVersion,
                    frameBuffer,
                    frameStride,
                    cameraIndex,
                    objectBuffer,
                    objectStride,
                    vertexBuffer,
                    indexBuffer);
                var pass = graph.AddBuiltInSpriteRasterPass(
                    new RenderPassDescription(
                        $"TerraFX.SpriteC0.Camera{cameraIndex}.Batch{batchIndex}",
                        passIndex++,
                        hasPreviousPass ? [previousPass] : [],
                        hasExternalSideEffects: false,
                        renderArea: area),
                    spritePass);
                graph.ReadBuffer(
                    pass,
                    frameBuffer,
                    GraphBufferUsage.UniformRead,
                    new GraphBufferRange(0, frameBuffer.Description.ByteLength));
                graph.ReadBuffer(
                    pass,
                    objectBuffer,
                    GraphBufferUsage.UniformRead,
                    new GraphBufferRange(0, objectBuffer.Description.ByteLength));
                graph.ReadBuffer(
                    pass,
                    vertexBuffer,
                    GraphBufferUsage.VertexRead,
                    new GraphBufferRange(0, vertexBuffer.Description.ByteLength));
                graph.ReadBuffer(
                    pass,
                    indexBuffer,
                    GraphBufferUsage.IndexRead,
                    new GraphBufferRange(0, indexBuffer.Description.ByteLength));
                graph.ReadTexture(
                    pass,
                    pageVersion,
                    GraphTextureUsage.SampledRead,
                    new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color));
                currentColorVersion = graph.WriteColorAttachment(
                    pass,
                    currentColorVersion,
                    new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color),
                    (batchIndex == 0 && clearCamera) ? AttachmentLoadAction.Clear : AttachmentLoadAction.Load,
                    AttachmentStoreAction.Store,
                    camera.ClearColor);
                previousPass = pass;
                hasPreviousPass = true;
                colorState.MarkColorWrite(camera.CameraId);
            }
        }

        colorState.CurrentColorVersion = currentColorVersion;
        colorState.HasColorContents = hdrTargetInitialized;
        colorState.HasPreviousPass = hasPreviousPass;
        colorState.NextPassIndex = passIndex;
        colorState.PreviousPass = previousPass;
    }

    private void BuildToneMapGraph(
        RenderSnapshot snapshot,
        RenderGraph graph,
        RenderGraphBuiltInColorState colorState)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(colorState);

        if (!colorState.HasColorContents)
        {
            return;
        }

        var device = _graphicsDevice
            ?? throw new NotSupportedException("Headless rendering does not construct built-in tone-map passes.");
        var alignment = checked((int)device.Capabilities.Limits.UniformBufferOffsetAlignment);
        var toneMapArea = new RenderArea(0, 0, snapshot.TargetExtent.Width, snapshot.TargetExtent.Height);
        var toneMapFrameBytes = RenderCameraGpuDataBufferBuilder.CreateToneMapFrameBuffer(
            toneMapArea,
            alignment,
            out _);
        var toneMapObjectBytes = RenderSpriteGpuDataBufferBuilder.CreateToneMapObjectBuffer(
            alignment,
            out _);
        var toneMapVertexBytes = RenderSpriteVertexBufferBuilder.CreateFullScreenQuad();
        var toneMapIndexBytes = MemoryMarshal.AsBytes(RenderSpriteGeometry.Indices.AsSpan()).ToArray();
        var toneMapFrameBuffer = graph.CreateInitializedTransientBuffer(
            new BufferDescription(
                toneMapFrameBytes.Length,
                BufferUsage.Uniform | BufferUsage.CopyDestination,
                GpuMemoryKind.DeviceLocal,
                structuredStride: null,
                "builtin-tonemap-frame-data"),
            toneMapFrameBytes);
        var toneMapObjectBuffer = graph.CreateInitializedTransientBuffer(
            new BufferDescription(
                toneMapObjectBytes.Length,
                BufferUsage.Uniform | BufferUsage.CopyDestination,
                GpuMemoryKind.DeviceLocal,
                structuredStride: null,
                "builtin-tonemap-object-data"),
            toneMapObjectBytes);
        var toneMapVertexBuffer = graph.CreateInitializedTransientBuffer(
            new BufferDescription(
                toneMapVertexBytes.Length,
                BufferUsage.Vertex | BufferUsage.CopyDestination,
                GpuMemoryKind.DeviceLocal,
                structuredStride: null,
                "builtin-tonemap-vertices"),
            toneMapVertexBytes);
        var toneMapIndexBuffer = graph.CreateInitializedTransientBuffer(
            new BufferDescription(
                toneMapIndexBytes.Length,
                BufferUsage.Index | BufferUsage.CopyDestination,
                GpuMemoryKind.DeviceLocal,
                structuredStride: null,
                "builtin-tonemap-indices"),
            toneMapIndexBytes);
        var toneMapRange = new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color);
        var toneMapPass = graph.AddBuiltInSpriteToneMapRasterPass(
            new RenderPassDescription(
                "TerraFX.BuiltIn.ToneMap",
                colorState.NextPassIndex,
                colorState.HasPreviousPass ? [colorState.PreviousPass] : [],
                hasExternalSideEffects: false,
                renderArea: toneMapArea),
            new RenderSpriteToneMapGraphPass(
                colorState.CurrentColorVersion,
                toneMapFrameBuffer,
                toneMapObjectBuffer,
                toneMapVertexBuffer,
                toneMapIndexBuffer));
        graph.ReadBuffer(
            toneMapPass,
            toneMapFrameBuffer,
            GraphBufferUsage.UniformRead,
            new GraphBufferRange(0, toneMapFrameBuffer.Description.ByteLength));
        graph.ReadBuffer(
            toneMapPass,
            toneMapObjectBuffer,
            GraphBufferUsage.UniformRead,
            new GraphBufferRange(0, toneMapObjectBuffer.Description.ByteLength));
        graph.ReadBuffer(
            toneMapPass,
            toneMapVertexBuffer,
            GraphBufferUsage.VertexRead,
            new GraphBufferRange(0, toneMapVertexBuffer.Description.ByteLength));
        graph.ReadBuffer(
            toneMapPass,
            toneMapIndexBuffer,
            GraphBufferUsage.IndexRead,
            new GraphBufferRange(0, toneMapIndexBuffer.Description.ByteLength));
        graph.ReadTexture(
            toneMapPass,
            colorState.CurrentColorVersion,
            GraphTextureUsage.SampledRead,
            toneMapRange);
        var toneMappedVersion = graph.WriteColorAttachment(
            toneMapPass,
            graph.GetBuiltInOutputColorVersion(),
            toneMapRange,
            AttachmentLoadAction.Clear,
            AttachmentStoreAction.Store,
            Vector4.Zero);
        graph.SetDefaultPresentationSource(toneMappedVersion);
    }
}
