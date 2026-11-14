// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Graphics;
using TerraFX.Numerics;
using TerraFX.Scenes;

namespace TerraFX.Rendering;

public sealed partial class Renderer
{
    private static readonly Guid s_meshC0LayoutId = new ContentSchemaIdentityAssignment("material/layout/pbr/v1").SchemaId;
    private static readonly Guid s_meshC0PassId = new ContentSchemaIdentityAssignment("material/pass/pbr/v1").SchemaId;
    private static readonly Guid s_meshC0ProfileId = new ContentSchemaIdentityAssignment("material/profile/pbr-metallic-roughness/v1").SchemaId;
    private static readonly Guid s_meshC0ShaderId = new ContentSchemaIdentityAssignment("material/shader/pbr-metallic-roughness/v1").SchemaId;

    private static byte[] CreateMeshFrameBuffer(
        RenderSnapshot3D snapshot,
        int uniformStride,
        out int frameStride)
    {
        frameStride = uniformStride;
        var buffer = new byte[checked(snapshot.Cameras.Length * frameStride)];

        for (var cameraIndex = 0; cameraIndex < snapshot.Cameras.Length; cameraIndex++)
        {
            var camera = snapshot.Cameras[cameraIndex];

            if (!snapshot.MeshCameraOrders[cameraIndex].DrawIndices.IsEmpty)
            {
                ValidateNoMeshC0Lights(snapshot, camera);
            }

            var area = RenderCameraViewport.CreateArea(camera.Viewport, snapshot.TargetExtent);
            RenderMeshC0FrameData.Create(
                camera,
                area,
                Vector3.Zero,
                Vector3.Zero,
                illuminance: 0.0f,
                lightEnabled: false).WriteTo(
                    buffer.AsSpan(checked(cameraIndex * frameStride), RenderMeshC0FrameData.SizeInBytes));
        }

        return buffer;
    }

    private static byte[] CreateMeshIndexBuffer(RenderMeshAsset mesh)
    {
        if (mesh.IndexFormat is RenderMeshIndexFormat.UInt16)
        {
            var bytes = new byte[checked(mesh.Indices.Length * sizeof(ushort))];

            for (var index = 0; index < mesh.Indices.Length; index++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(
                    bytes.AsSpan(checked(index * sizeof(ushort))),
                    checked((ushort)mesh.Indices[index]));
            }

            return bytes;
        }

        return MemoryMarshal.AsBytes(mesh.Indices.AsSpan()).ToArray();
    }

    private static byte[] CreateMeshMaterialBuffer(
        ReadOnlySpan<RenderMaterialAsset> materials,
        int uniformStride)
    {
        var buffer = new byte[checked(materials.Length * uniformStride)];

        for (var index = 0; index < materials.Length; index++)
        {
            new RenderMeshC0MaterialData(materials[index]).WriteTo(
                buffer.AsSpan(checked(index * uniformStride), RenderMeshC0MaterialData.SizeInBytes));
        }

        return buffer;
    }

    private static byte[] CreateMeshObjectBuffer(
        ReadOnlySpan<RenderMeshSnapshot> draws,
        int uniformStride)
    {
        var buffer = new byte[checked(draws.Length * uniformStride)];

        for (var index = 0; index < draws.Length; index++)
        {
            CreateMeshObjectData(draws[index]).WriteTo(
                buffer.AsSpan(checked(index * uniformStride), ObjectGpuData.SizeInBytes));
        }

        return buffer;
    }

    private static ObjectGpuData CreateMeshObjectData(RenderMeshSnapshot draw)
    {
        if (!SpatialMath.TryToFrameRelative(draw.Position, draw.Frame3, out var position))
        {
            throw new InvalidOperationException("The Mesh position is outside its captured spatial frame.");
        }

        var world = draw.LinearTransform * Matrix4x4.CreateTranslation(position);

        if (!world.IsFinite().All()
            || !Matrix4x4.TryInvert(world, out var inverseWorld))
        {
            throw new InvalidOperationException("The Mesh world transform is not finite or invertible.");
        }

        var normalMatrix = Matrix4x4.Transpose(inverseWorld);

        if (!normalMatrix.IsFinite().All())
        {
            throw new InvalidOperationException("The Mesh normal transform is not finite.");
        }

        return new ObjectGpuData(
            world,
            world,
            normalMatrix.Row1,
            normalMatrix.Row2,
            normalMatrix.Row3,
            metadata0: 0,
            metadata1: 0,
            metadata2: 0,
            metadata3: 0);
    }

    private static bool IsMeshC0Material(RenderMaterialAsset material)
        => !material.IsSprite
        && (material.ProfileId == s_meshC0ProfileId)
        && (material.ShaderId == s_meshC0ShaderId)
        && (material.PassId == s_meshC0PassId)
        && (material.LayoutId == s_meshC0LayoutId)
        && (material.AlphaMode == RenderMaterialAsset.MaterialAlphaMode.Opaque)
        && (material.OutputAlpha == RenderMaterialAsset.MaterialOutputAlpha.Straight)
        && material.DepthTest
        && material.DepthWrite
        && material.DoubleSided
        && (material.CullMode == RenderMaterialAsset.MaterialCullMode.None)
        && (material.RequiredCapabilities == 0x191)
        && !material.Parameters.BaseTexture.HasValue
        && !material.Parameters.PackedMetalRoughOcclusion.HasValue
        && !material.Parameters.NormalTexture.HasValue
        && !material.Parameters.EmissiveTexture.HasValue;

    private static void ValidateNoMeshC0Lights(RenderSnapshot3D snapshot, CameraSnapshot camera)
    {
        foreach (var light in snapshot.DirectionalLights)
        {
            if ((light.LayerMask & camera.LayerMask) != 0)
            {
                throw new NotSupportedException("Mesh C0 refuses active directional lights until the contracted shadow pass is available.");
            }
        }

        foreach (var light in snapshot.PointLights)
        {
            if ((light.LayerMask & camera.LayerMask) != 0)
            {
                throw new NotSupportedException("Mesh C0 refuses point lights until their contracted attenuation and shadow paths are available.");
            }
        }

        foreach (var light in snapshot.SpotLights)
        {
            if ((light.LayerMask & camera.LayerMask) != 0)
            {
                throw new NotSupportedException("Mesh C0 refuses spot lights until their contracted cone, attenuation and shadow paths are available.");
            }
        }
    }

    private void BuildMeshC0Graph(RenderSnapshot snapshot, RenderGraph graph)
    {
        if (snapshot is not RenderSnapshot3D meshSnapshot || meshSnapshot.MeshDraws.IsEmpty)
        {
            return;
        }

        if (!snapshot.SpriteDraws.IsEmpty)
        {
            throw new NotSupportedException("Mesh C0 and Sprite C0 cannot share a graph until their camera/pass ordering is admitted together.");
        }

        var device = _graphicsDevice
            ?? throw new NotSupportedException("Headless rendering does not construct built-in Mesh passes.");
        using var assetPins = snapshot.RetainAssetPins()
            ?? throw new InvalidOperationException("The Mesh snapshot has no retained Scene assets.");
        var selectedDraws = new bool[meshSnapshot.MeshDraws.Length];
        var selectedIndices = new int[meshSnapshot.MeshDraws.Length];
        Array.Fill(selectedIndices, -1);
        var selectedDrawCount = 0;

        for (var cameraIndex = 0; cameraIndex < meshSnapshot.MeshCameraOrders.Length; cameraIndex++)
        {
            foreach (var drawIndex in meshSnapshot.MeshCameraOrders[cameraIndex].DrawIndices)
            {
                if (!selectedDraws[drawIndex])
                {
                    selectedDraws[drawIndex] = true;
                    selectedDrawCount++;
                }
            }
        }

        if (selectedDrawCount == 0)
        {
            return;
        }

        var alignment = checked((int)device.Capabilities.Limits.UniformBufferOffsetAlignment);
        var uniformStride = ObjectGpuData.GetUniformAllocationStride(alignment);
        var frameBytes = CreateMeshFrameBuffer(meshSnapshot, uniformStride, out var frameStride);
        var selectedDrawsInOrder = new List<RenderMeshSnapshot>(selectedDrawCount);
        var meshAssets = new List<RenderMeshAsset>(selectedDrawCount);
        var materialAssets = new List<RenderMaterialAsset>(selectedDrawCount);
        var vertexBytesByMesh = new List<byte[]>(selectedDrawCount);
        var indexBytesByMesh = new List<byte[]>(selectedDrawCount);
        var meshGeometryIndices = new int[meshSnapshot.MeshDraws.Length];
        Array.Fill(meshGeometryIndices, -1);
        var meshGeometryIndexByKey = new Dictionary<(Guid SceneInstanceId, int MeshAssetId), int>();
        var preparedIndex = 0;

        for (var drawIndex = 0; drawIndex < meshSnapshot.MeshDraws.Length; drawIndex++)
        {
            if (!selectedDraws[drawIndex])
            {
                continue;
            }

            var draw = meshSnapshot.MeshDraws[drawIndex];
            var sceneAssets = assetPins.GetSceneAssets(draw.SceneInstanceId);
            var mesh = sceneAssets.GetMesh(draw.MeshAssetId);
            var material = sceneAssets.GetMaterial(draw.MaterialAssetId);

            if (!IsMeshC0Material(material))
            {
                throw new NotSupportedException("Mesh C0 admits only opaque, double-sided PBR materials without texture bindings or blending.");
            }

            if (!mesh.Colors.IsDefault)
            {
                throw new NotSupportedException("Mesh C0 does not consume optional vertex-color streams.");
            }

            if (!mesh.JointIndices.IsDefault || (draw.PaletteIndex >= 0))
            {
                throw new NotSupportedException("Mesh C0 currently draws only unskinned Meshes; bind-pose palette evaluation requires its GPU deformation path.");
            }

            if ((draw.GroupIndex < 0) || (draw.GroupIndex >= mesh.Groups.Length))
            {
                throw new InvalidOperationException("A Mesh snapshot draw references an unavailable group.");
            }

            selectedDrawsInOrder.Add(draw);
            selectedIndices[drawIndex] = preparedIndex++;
            materialAssets.Add(material);
            var geometryKey = (draw.SceneInstanceId, draw.MeshAssetId);

            if (!meshGeometryIndexByKey.TryGetValue(geometryKey, out var meshGeometryIndex))
            {
                meshGeometryIndex = meshAssets.Count;
                meshGeometryIndexByKey.Add(geometryKey, meshGeometryIndex);
                meshAssets.Add(mesh);
                vertexBytesByMesh.Add(RenderMeshC0VertexBufferBuilder.Create(mesh));
                indexBytesByMesh.Add(CreateMeshIndexBuffer(mesh));
            }

            meshGeometryIndices[drawIndex] = meshGeometryIndex;
        }

        var objectBytes = CreateMeshObjectBuffer(CollectionsMarshal.AsSpan(selectedDrawsInOrder), uniformStride);
        var materialBytes = CreateMeshMaterialBuffer(CollectionsMarshal.AsSpan(materialAssets), uniformStride);
        var frameBuffer = graph.CreateInitializedTransientBuffer(
            new BufferDescription(
                frameBytes.Length,
                BufferUsage.Uniform | BufferUsage.CopyDestination,
                GpuMemoryKind.DeviceLocal,
                structuredStride: null,
                "mesh-frame-data"),
            frameBytes);
        var objectBuffer = graph.CreateInitializedTransientBuffer(
            new BufferDescription(
                objectBytes.Length,
                BufferUsage.Uniform | BufferUsage.CopyDestination,
                GpuMemoryKind.DeviceLocal,
                structuredStride: null,
                "mesh-object-data"),
            objectBytes);
        var materialBuffer = graph.CreateInitializedTransientBuffer(
            new BufferDescription(
                materialBytes.Length,
                BufferUsage.Uniform | BufferUsage.CopyDestination,
                GpuMemoryKind.DeviceLocal,
                structuredStride: null,
                "mesh-material-data"),
            materialBytes);
        var vertexBuffers = new GraphBufferVersion[meshAssets.Count];
        var indexBuffers = new GraphBufferVersion[meshAssets.Count];

        for (var meshIndex = 0; meshIndex < meshAssets.Count; meshIndex++)
        {
            vertexBuffers[meshIndex] = graph.CreateInitializedTransientBuffer(
                new BufferDescription(
                    vertexBytesByMesh[meshIndex].Length,
                    BufferUsage.Vertex | BufferUsage.CopyDestination,
                    GpuMemoryKind.DeviceLocal,
                    structuredStride: null,
                    $"mesh-vertices-{meshIndex}"),
                vertexBytesByMesh[meshIndex]);
            indexBuffers[meshIndex] = graph.CreateInitializedTransientBuffer(
                new BufferDescription(
                    indexBytesByMesh[meshIndex].Length,
                    BufferUsage.Index | BufferUsage.CopyDestination,
                    GpuMemoryKind.DeviceLocal,
                    structuredStride: null,
                    $"mesh-indices-{meshIndex}"),
                indexBytesByMesh[meshIndex]);
        }

        var meshPassCount = 0;
        var hdrVersion = graph.CreateTransientTexture(new TextureDescription(
            TextureDimension.Dimension2D,
            snapshot.TargetExtent.Width,
            snapshot.TargetExtent.Height,
            depth: 1,
            arrayLayers: 1,
            mipLevels: 1,
            TextureFormat.Rgba16Float,
            TextureUsage.ColorAttachment | TextureUsage.Sampled,
            "mesh-hdr-working-target"));
        var depthVersion = graph.CreateTransientTexture(new TextureDescription(
            TextureDimension.Dimension2D,
            snapshot.TargetExtent.Width,
            snapshot.TargetExtent.Height,
            depth: 1,
            arrayLayers: 1,
            mipLevels: 1,
            TextureFormat.D32Float,
            TextureUsage.DepthAttachment,
            "mesh-depth-target"));
        var currentColorVersion = hdrVersion;
        var currentDepthVersion = depthVersion;
        var hasColorContents = false;
        var hasDepthContents = false;
        var previousPass = default(RenderPassId);
        var hasPreviousPass = false;
        var passIndex = 0;

        for (var cameraIndex = 0; cameraIndex < snapshot.Cameras.Length; cameraIndex++)
        {
            var camera = snapshot.Cameras[cameraIndex];
            var area = RenderCameraViewport.CreateArea(camera.Viewport, snapshot.TargetExtent);
            var clearsFullTarget = (area.X == 0)
                && (area.Y == 0)
                && (area.Width == snapshot.TargetExtent.Width)
                && (area.Height == snapshot.TargetExtent.Height);
            var clearColor = (camera.ClearFlags & CameraClearFlags.Color) != 0;
            var clearDepth = (camera.ClearFlags & CameraClearFlags.Depth) != 0;
            var cameraDrawIndices = meshSnapshot.MeshCameraOrders[cameraIndex].DrawIndices;

            if (clearColor
                && ((camera.ClearColor.X < 0.0f)
                    || (camera.ClearColor.Y < 0.0f)
                    || (camera.ClearColor.Z < 0.0f)))
            {
                throw new NotSupportedException("Mesh C0 requires nonnegative camera clear RGB for its HDR tone-map path.");
            }

            if (cameraDrawIndices.IsEmpty)
            {
                if (!clearColor && !clearDepth)
                {
                    continue;
                }

                if ((!hasColorContents && clearColor && !clearsFullTarget)
                    || (!hasDepthContents && clearDepth && !clearsFullTarget))
                {
                    throw new NotSupportedException("Mesh C0 requires each first partial color or depth clear to cover the full target.");
                }

                var clearPass = graph.AddRasterPass(new RenderPassDescription(
                    $"TerraFX.MeshC0.Camera{cameraIndex}.Clear",
                    passIndex++,
                    hasPreviousPass ? [previousPass] : [],
                    hasExternalSideEffects: false,
                    renderArea: area));
                previousPass = clearPass;
                hasPreviousPass = true;

                if (clearColor)
                {
                    currentColorVersion = graph.WriteColorAttachment(
                        clearPass,
                        currentColorVersion,
                        new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color),
                        AttachmentLoadAction.Clear,
                        AttachmentStoreAction.Store,
                        camera.ClearColor);
                    hasColorContents = true;
                }

                if (clearDepth)
                {
                    currentDepthVersion = graph.WriteDepthAttachment(
                        clearPass,
                        currentDepthVersion,
                        new GraphTextureRange(0, 1, 0, 1, TextureAspect.Depth),
                        AttachmentLoadAction.Clear,
                        AttachmentStoreAction.Store,
                        0.0f);
                    hasDepthContents = true;
                }

                continue;
            }

            for (var orderIndex = 0; orderIndex < cameraDrawIndices.Length; orderIndex++)
            {
                var drawIndex = cameraDrawIndices[orderIndex];
                var selectedIndex = selectedIndices[drawIndex];

                if (selectedIndex < 0)
                {
                    throw new InvalidOperationException("A camera-selected Mesh draw was not prepared for graph submission.");
                }

                var draw = selectedDrawsInOrder[selectedIndex];
                var meshIndex = meshGeometryIndices[drawIndex];
                var mesh = meshAssets[meshIndex];
                var group = mesh.Groups[draw.GroupIndex];
                var vertexBuffer = vertexBuffers[meshIndex];
                var indexBuffer = indexBuffers[meshIndex];
                var meshPass = new RenderMeshC0GraphPass(
                    selectedIndex,
                    group,
                    mesh.IndexFormat,
                    frameBuffer,
                    frameStride,
                    cameraIndex,
                    objectBuffer,
                    uniformStride,
                    materialBuffer,
                    uniformStride,
                    vertexBuffer,
                    indexBuffer);
                var pass = graph.AddBuiltInMeshRasterPass(
                    new RenderPassDescription(
                        $"TerraFX.MeshC0.Camera{cameraIndex}.Draw{orderIndex}",
                        passIndex++,
                        hasPreviousPass ? [previousPass] : [],
                        hasExternalSideEffects: false,
                        renderArea: area),
                    meshPass);
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
                    materialBuffer,
                    GraphBufferUsage.UniformRead,
                    new GraphBufferRange(0, materialBuffer.Description.ByteLength));
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
                var colorLoadAction = AttachmentLoadAction.Load;
                var depthLoadAction = AttachmentLoadAction.Load;

                if (!hasColorContents)
                {
                    if (!clearColor || !clearsFullTarget)
                    {
                        throw new NotSupportedException("Mesh C0 requires its first HDR camera write to clear the full target.");
                    }

                    colorLoadAction = AttachmentLoadAction.Clear;
                }
                else if ((orderIndex == 0) && clearColor)
                {
                    colorLoadAction = AttachmentLoadAction.Clear;
                }

                if (!hasDepthContents)
                {
                    if (!clearDepth || !clearsFullTarget)
                    {
                        throw new NotSupportedException("Mesh C0 requires its first reversed-Z depth write to clear the full target.");
                    }

                    depthLoadAction = AttachmentLoadAction.Clear;
                }
                else if ((orderIndex == 0) && clearDepth)
                {
                    depthLoadAction = AttachmentLoadAction.Clear;
                }

                currentColorVersion = graph.WriteColorAttachment(
                    pass,
                    currentColorVersion,
                    new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color),
                    colorLoadAction,
                    AttachmentStoreAction.Store,
                    camera.ClearColor);
                currentDepthVersion = graph.WriteDepthAttachment(
                    pass,
                    currentDepthVersion,
                    new GraphTextureRange(0, 1, 0, 1, TextureAspect.Depth),
                    depthLoadAction,
                    AttachmentStoreAction.Store,
                    0.0f);
                hasColorContents = true;
                hasDepthContents = true;
                meshPassCount++;
                previousPass = pass;
                hasPreviousPass = true;
            }
        }

        if (meshPassCount == 0)
        {
            return;
        }

        var toneMapFrameBytes = RenderCameraGpuDataBufferBuilder.CreateToneMapFrameBuffer(
            new RenderArea(0, 0, snapshot.TargetExtent.Width, snapshot.TargetExtent.Height),
            alignment,
            out _);
        var toneMapObjectBytes = RenderSpriteGpuDataBufferBuilder.CreateToneMapObjectBuffer(alignment, out _);
        var toneMapVertexBytes = RenderSpriteVertexBufferBuilder.CreateFullScreenQuad();
        var toneMapIndexBytes = MemoryMarshal.AsBytes(RenderSpriteGeometry.Indices.AsSpan()).ToArray();
        var toneMapFrameBuffer = graph.CreateInitializedTransientBuffer(
            new BufferDescription(
                toneMapFrameBytes.Length,
                BufferUsage.Uniform | BufferUsage.CopyDestination,
                GpuMemoryKind.DeviceLocal,
                structuredStride: null,
                "mesh-tonemap-frame-data"),
            toneMapFrameBytes);
        var toneMapObjectBuffer = graph.CreateInitializedTransientBuffer(
            new BufferDescription(
                toneMapObjectBytes.Length,
                BufferUsage.Uniform | BufferUsage.CopyDestination,
                GpuMemoryKind.DeviceLocal,
                structuredStride: null,
                "mesh-tonemap-object-data"),
            toneMapObjectBytes);
        var toneMapVertexBuffer = graph.CreateInitializedTransientBuffer(
            new BufferDescription(
                toneMapVertexBytes.Length,
                BufferUsage.Vertex | BufferUsage.CopyDestination,
                GpuMemoryKind.DeviceLocal,
                structuredStride: null,
                "mesh-tonemap-vertices"),
            toneMapVertexBytes);
        var toneMapIndexBuffer = graph.CreateInitializedTransientBuffer(
            new BufferDescription(
                toneMapIndexBytes.Length,
                BufferUsage.Index | BufferUsage.CopyDestination,
                GpuMemoryKind.DeviceLocal,
                structuredStride: null,
                "mesh-tonemap-indices"),
            toneMapIndexBytes);
        var toneMapArea = new RenderArea(0, 0, snapshot.TargetExtent.Width, snapshot.TargetExtent.Height);
        var toneMapRange = new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color);
        var toneMapPass = graph.AddBuiltInSpriteToneMapRasterPass(
            new RenderPassDescription(
                "TerraFX.MeshC0.ToneMap",
                passIndex,
                hasPreviousPass ? [previousPass] : [],
                hasExternalSideEffects: false,
                renderArea: toneMapArea),
            new RenderSpriteToneMapGraphPass(
                currentColorVersion,
                toneMapFrameBuffer,
                toneMapObjectBuffer,
                toneMapVertexBuffer,
                toneMapIndexBuffer));
        graph.ReadBuffer(toneMapPass, toneMapFrameBuffer, GraphBufferUsage.UniformRead, new GraphBufferRange(0, toneMapFrameBuffer.Description.ByteLength));
        graph.ReadBuffer(toneMapPass, toneMapObjectBuffer, GraphBufferUsage.UniformRead, new GraphBufferRange(0, toneMapObjectBuffer.Description.ByteLength));
        graph.ReadBuffer(toneMapPass, toneMapVertexBuffer, GraphBufferUsage.VertexRead, new GraphBufferRange(0, toneMapVertexBuffer.Description.ByteLength));
        graph.ReadBuffer(toneMapPass, toneMapIndexBuffer, GraphBufferUsage.IndexRead, new GraphBufferRange(0, toneMapIndexBuffer.Description.ByteLength));
        graph.ReadTexture(toneMapPass, currentColorVersion, GraphTextureUsage.SampledRead, toneMapRange);
        var outputColorVersion = graph.GetBuiltInOutputColorVersion();
        var toneMappedVersion = graph.WriteColorAttachment(
            toneMapPass,
            outputColorVersion,
            toneMapRange,
            AttachmentLoadAction.Clear,
            AttachmentStoreAction.Store,
            Vector4.Zero);
        graph.SetDefaultPresentationSource(toneMappedVersion);
    }
}
