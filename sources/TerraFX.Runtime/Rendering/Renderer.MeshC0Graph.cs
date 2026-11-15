// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
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

    private static byte[] CreateMeshDirectionalLightBuffer(
        ImmutableArray<RenderDirectionalLightSnapshot> directionalLights,
        Guid shadowedLightId,
        int uniformBufferOffsetAlignment)
        => RenderDirectionalLightBufferBuilder.Create(
            directionalLights.AsSpan(),
            shadowedLightId,
            uniformBufferOffsetAlignment);

    private static byte[] CreateMeshFrameBuffer(
        RenderSnapshot3D snapshot,
        ImmutableArray<RenderPointLightSnapshot>[] pointLightsByCamera,
        RenderDirectionalShadowCameraData?[] shadowDataByCamera,
        ImmutableArray<RenderSpotShadowData>[] spotShadowDataByCamera,
        int uniformStride,
        out int frameStride)
    {
        if (pointLightsByCamera.Length != snapshot.Cameras.Length)
        {
            throw new ArgumentException("Point-light data must be captured for every selected Mesh camera.", nameof(pointLightsByCamera));
        }

        if (shadowDataByCamera.Length != snapshot.Cameras.Length)
        {
            throw new ArgumentException("Shadow data must be captured for every selected Mesh camera.", nameof(shadowDataByCamera));
        }

        if (spotShadowDataByCamera.Length != snapshot.Cameras.Length)
        {
            throw new ArgumentException("Spot shadow data must be captured for every selected Mesh camera.", nameof(spotShadowDataByCamera));
        }

        frameStride = uniformStride;
        var buffer = new byte[checked(snapshot.Cameras.Length * frameStride)];

        for (var cameraIndex = 0; cameraIndex < snapshot.Cameras.Length; cameraIndex++)
        {
            var camera = snapshot.Cameras[cameraIndex];
            var pointLights = pointLightsByCamera[cameraIndex];

            if (pointLights.IsDefault)
            {
                throw new ArgumentException("Point-light data must be initialized for every Mesh camera.", nameof(pointLightsByCamera));
            }

            var area = RenderCameraViewport.CreateArea(camera.Viewport, snapshot.TargetExtent);
            RenderMeshC0FrameData.Create(
                camera,
                area,
                pointLights.Length,
                shadowDataByCamera[cameraIndex],
                spotShadowDataByCamera[cameraIndex]).WriteTo(
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
        ReadOnlySpan<bool> hasBaseColorTextures,
        ReadOnlySpan<bool> hasEmissiveTextures,
        ReadOnlySpan<bool> hasPackedMetalRoughOcclusionTextures,
        ReadOnlySpan<bool> hasNormalTextures,
        int uniformStride)
    {
        if ((hasBaseColorTextures.Length != materials.Length)
            || (hasEmissiveTextures.Length != materials.Length)
            || (hasPackedMetalRoughOcclusionTextures.Length != materials.Length)
            || (hasNormalTextures.Length != materials.Length))
        {
            throw new ArgumentException("Mesh texture-presence flags must match the Material count.");
        }

        var buffer = new byte[checked(materials.Length * uniformStride)];

        for (var index = 0; index < materials.Length; index++)
        {
            new RenderMeshC0MaterialData(
                materials[index],
                hasBaseColorTextures[index],
                hasEmissiveTextures[index],
                hasPackedMetalRoughOcclusionTextures[index],
                hasNormalTextures[index]).WriteTo(
                buffer.AsSpan(checked(index * uniformStride), RenderMeshC0MaterialData.SizeInBytes));
        }

        return buffer;
    }

    private static byte[] CreateMeshObjectBuffer(
        ReadOnlySpan<RenderMeshSnapshot> draws,
        ReadOnlySpan<int> bonePaletteOffsets,
        int uniformStride)
    {
        if (bonePaletteOffsets.Length != draws.Length)
        {
            throw new ArgumentException("Mesh palette offsets must match the draw count.", nameof(bonePaletteOffsets));
        }

        var buffer = new byte[checked(draws.Length * uniformStride)];

        for (var index = 0; index < draws.Length; index++)
        {
            CreateMeshObjectData(draws[index], bonePaletteOffsets[index]).WriteTo(
                buffer.AsSpan(checked(index * uniformStride), ObjectGpuData.SizeInBytes));
        }

        return buffer;
    }

    private static ObjectGpuData CreateMeshObjectData(RenderMeshSnapshot draw, int bonePaletteOffset)
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

        if (((draw.PaletteIndex < 0) && (bonePaletteOffset != -1))
            || ((draw.PaletteIndex >= 0) && (bonePaletteOffset < 0)))
        {
            throw new ArgumentException("The Mesh palette offset does not match the draw's captured palette.", nameof(bonePaletteOffset));
        }

        return new ObjectGpuData(
            world,
            world,
            normalMatrix.Row1,
            normalMatrix.Row2,
            normalMatrix.Row3,
            metadata0: draw.PaletteIndex >= 0 ? checked((uint)bonePaletteOffset + 1) : 0,
            metadata1: 0,
            metadata2: 0,
            metadata3: 0);
    }

    private static byte[] CreateMeshPointLightBuffer(
        ImmutableArray<RenderPointLightSnapshot> pointLights,
        SpatialFrame3 frame3)
        => RenderMeshC0PointLightBufferBuilder.Create(pointLights.AsSpan(), frame3);

    private static byte[] CreateMeshShadowFrameBuffer(
        int cameraCount,
        RenderDirectionalShadowCameraData?[] shadowDataByCamera,
        ImmutableArray<RenderSpotShadowData>[] spotShadowDataByCamera,
        int uniformStride,
        out int frameStride)
    {
        if (shadowDataByCamera.Length != cameraCount)
        {
            throw new ArgumentException("Shadow data must be captured for every selected Mesh camera.", nameof(shadowDataByCamera));
        }

        if (spotShadowDataByCamera.Length != cameraCount)
        {
            throw new ArgumentException("Spot shadow data must be captured for every selected Mesh camera.", nameof(spotShadowDataByCamera));
        }

        frameStride = uniformStride;
        var buffer = new byte[checked(cameraCount * RenderDirectionalShadowCameraData.CascadeCount * 2 * frameStride)];

        for (var cameraIndex = 0; cameraIndex < cameraCount; cameraIndex++)
        {
            var shadowData = shadowDataByCamera[cameraIndex].GetValueOrDefault();

            for (var cascadeIndex = 0; cascadeIndex < RenderDirectionalShadowCameraData.CascadeCount; cascadeIndex++)
            {
                var frameOffset = checked(
                    ((cameraIndex * RenderDirectionalShadowCameraData.CascadeCount * 2) + cascadeIndex)
                    * frameStride);
                shadowData.WriteViewProjectionTo(
                    buffer.AsSpan(frameOffset, 16 * sizeof(float)),
                    destinationOffset: 0,
                    cascadeIndex);
            }

            var spotShadows = spotShadowDataByCamera[cameraIndex];

            if (spotShadows.IsDefault)
            {
                throw new ArgumentException("Spot shadow data must be initialized for every Mesh camera.", nameof(spotShadowDataByCamera));
            }

            for (var spotIndex = 0; spotIndex < spotShadows.Length; spotIndex++)
            {
                var shadowSlot = RenderDirectionalShadowCameraData.CascadeCount + spotIndex;
                var frameOffset = checked(((cameraIndex * RenderDirectionalShadowCameraData.CascadeCount * 2) + shadowSlot) * frameStride);
                spotShadows[spotIndex].WriteViewProjectionTo(buffer.AsSpan(frameOffset, RenderSpotShadowData.SizeInBytes), destinationOffset: 0);
            }
        }

        return buffer;
    }

    private static ImmutableArray<RenderDirectionalLightSnapshot> GetMeshC0DirectionalLights(
        RenderSnapshot3D snapshot,
        CameraSnapshot camera)
    {
        var selectedLights = new List<RenderDirectionalLightSnapshot>(snapshot.DirectionalLights.Length);

        foreach (var light in snapshot.DirectionalLights)
        {
            if ((light.LayerMask & camera.LayerMask) != 0)
            {
                selectedLights.Add(light);
            }
        }

        selectedLights.Sort(static (left, right) => {
            var priorityComparison = right.Priority.CompareTo(left.Priority);
            return priorityComparison != 0
                ? priorityComparison
                : FoundationCatalogue.CompareRfc(left.LightId, right.LightId);
        });
        return [.. selectedLights];
    }

    private static ImmutableArray<RenderPointLightSnapshot> GetMeshC0PointLights(
        RenderSnapshot3D snapshot,
        CameraSnapshot camera)
    {
        var selectedLights = new List<RenderPointLightSnapshot>(snapshot.PointLights.Length);

        foreach (var light in snapshot.PointLights)
        {
            if ((light.LayerMask & camera.LayerMask) != 0)
            {
                selectedLights.Add(light);
            }
        }

        return [.. selectedLights];
    }

    private static ImmutableArray<RenderSpotLightSnapshot> GetMeshC0SpotLights(
        RenderSnapshot3D snapshot,
        CameraSnapshot camera)
    {
        var selectedLights = new List<RenderSpotLightSnapshot>(snapshot.SpotLights.Length);

        foreach (var light in snapshot.SpotLights)
        {
            if ((light.LayerMask & camera.LayerMask) != 0)
            {
                selectedLights.Add(light);
            }
        }

        return [.. selectedLights];
    }

    private static ImmutableArray<RenderSpotShadowData> GetMeshC0SpotShadows(
        RenderGraph graph,
        ImmutableArray<RenderSpotLightSnapshot> spotLights,
        CameraSnapshot camera)
    {
        var shadowCandidates = new List<RenderSpotLightSnapshot>(spotLights);
        shadowCandidates.Sort(static (left, right) => {
            var priorityComparison = right.Priority.CompareTo(left.Priority);
            return priorityComparison != 0
                ? priorityComparison
                : FoundationCatalogue.CompareRfc(left.LightId, right.LightId);
        });

        if (shadowCandidates.Count > RenderSpotShadowData.Capacity)
        {
            graph.AddBuildWarning(
                "ShadowBudgetExceeded",
                $"Camera {camera.CameraId} has {shadowCandidates.Count} selected spot lights; only the four highest-priority spot lights receive shadow atlas cells.");
        }

        var shadows = ImmutableArray.CreateBuilder<RenderSpotShadowData>(
            Math.Min(shadowCandidates.Count, RenderSpotShadowData.Capacity));

        for (var index = 0; (index < shadowCandidates.Count) && (index < RenderSpotShadowData.Capacity); index++)
        {
            var light = shadowCandidates[index];

            if (RenderSpotShadowData.TryCreate(light, camera, out var shadow))
            {
                shadows.Add(shadow);
            }
            else
            {
                graph.AddBuildWarning(
                    "ShadowRangeInvalid",
                    $"Spot light {light.LightId} has no representable positive shadow range and remains unshadowed.");
            }
        }

        return shadows.ToImmutable();
    }

    private static GraphTextureVersion GetMeshC0TextureVersion(
        RenderGraph graph,
        RenderSnapshotAssetPins.SceneAssetPins sceneAssets,
        int materialAssetId,
        int drawIndex,
        RenderMaterialAsset.TextureBinding binding,
        Dictionary<(AssetPackageBackingToken BackingToken, int TextureAssetId), GraphTextureVersion> textureVersions)
    {
        var textureSet = sceneAssets.GetMaterialTextureSet(materialAssetId)
            ?? throw new InvalidOperationException("The Mesh Material texture is not retained by its Scene assets.");
        using var textureLease = textureSet.RetainTexture(binding.TextureAssetId);
        var textureKey = (textureLease.BackingToken, binding.TextureAssetId.Value);

        if (textureVersions.TryGetValue(textureKey, out var textureVersion))
        {
            return textureVersion;
        }

        var textureDescription = GpuResidencyService.CreateTextureDescription(
            textureLease.Value,
            TextureUsage.Sampled,
            $"mesh-color-{binding.TextureAssetId.Value}");
        textureVersion = graph.CreateDeferredMeshTexture(
            textureDescription,
            drawIndex,
            binding.TextureAssetId.Value);
        textureVersions.Add(textureKey, textureVersion);
        return textureVersion;
    }

    private static bool IsMeshC0Material(RenderMaterialAsset material)
    {
        if (material.IsSprite
            || (material.ProfileId != s_meshC0ProfileId)
            || (material.ShaderId != s_meshC0ShaderId)
            || (material.PassId != s_meshC0PassId)
            || (material.LayoutId != s_meshC0LayoutId)
            || !material.DepthTest)
        {
            return false;
        }

        var isNormalMapped = material.Parameters.NormalTexture.HasValue;
        var isBlend = material.AlphaMode is RenderMaterialAsset.MaterialAlphaMode.Blend;
        var isDoubleSided = material.DoubleSided
            && (material.CullMode is RenderMaterialAsset.MaterialCullMode.None);
        var isSingleSided = !material.DoubleSided
            && (material.CullMode is RenderMaterialAsset.MaterialCullMode.Back);
        var alphaModeSupported = material.AlphaMode is RenderMaterialAsset.MaterialAlphaMode.Opaque
            or RenderMaterialAsset.MaterialAlphaMode.Mask
            or RenderMaterialAsset.MaterialAlphaMode.Blend;
        var sidednessSupported = isNormalMapped
            ? isSingleSided && (material.AlphaMode is RenderMaterialAsset.MaterialAlphaMode.Opaque)
            : isDoubleSided;
        var depthWriteSupported = isBlend ? !material.DepthWrite : material.DepthWrite;
        var outputAlphaSupported = material.OutputAlpha
            == (isBlend
                ? RenderMaterialAsset.MaterialOutputAlpha.Premultiplied
                : RenderMaterialAsset.MaterialOutputAlpha.Straight);
        var requiredCapabilities = 0x191UL
            | (material.AlphaMode is RenderMaterialAsset.MaterialAlphaMode.Mask ? 0x004UL : 0UL)
            | (isBlend ? 0x008UL : 0UL)
            | (isNormalMapped ? 0x040UL : 0UL);
        return alphaModeSupported
            && sidednessSupported
            && depthWriteSupported
            && outputAlphaSupported
            && (material.RequiredCapabilities == requiredCapabilities);
    }

    private static bool IsMeshC0ShadowCaster(
        RenderMeshSnapshot draw,
        RenderMeshAsset mesh,
        Vector3 lightDirection,
        Matrix4x4 viewProjection)
    {
        if (!SpatialMath.TryToFrameRelative(draw.Position, draw.Frame3, out var position))
        {
            throw new InvalidOperationException("The shadow caster position is outside its captured spatial frame.");
        }

        var world = draw.LinearTransform * Matrix4x4.CreateTranslation(position);
        var bounds = mesh.Bounds;
        var minimumX = float.PositiveInfinity;
        var minimumY = float.PositiveInfinity;
        var minimumZ = float.PositiveInfinity;
        var maximumX = float.NegativeInfinity;
        var maximumY = float.NegativeInfinity;
        var maximumZ = float.NegativeInfinity;

        for (var cornerIndex = 0; cornerIndex < 8; cornerIndex++)
        {
            var corner = new Vector3(
                (cornerIndex & 1) == 0 ? bounds.Minimum.X : bounds.Maximum.X,
                (cornerIndex & 2) == 0 ? bounds.Minimum.Y : bounds.Maximum.Y,
                (cornerIndex & 4) == 0 ? bounds.Minimum.Z : bounds.Maximum.Z);
            var worldCorner = Vector3.TransformPoint(corner, world);

            for (var extensionIndex = 0; extensionIndex < 2; extensionIndex++)
            {
                var casterCorner = extensionIndex == 0
                    ? worldCorner
                    : worldCorner + (lightDirection * 10.0f);
                var clip = Vector4.Transform(new Vector4(casterCorner, 1.0f), viewProjection);

                if (!clip.IsFinite().All() || (clip.W <= 0.0f))
                {
                    throw new InvalidOperationException("A directional shadow caster bound is not representable.");
                }

                var inverseW = 1.0f / clip.W;
                var x = clip.X * inverseW;
                var y = clip.Y * inverseW;
                var z = clip.Z * inverseW;
                minimumX = MathF.Min(minimumX, x);
                minimumY = MathF.Min(minimumY, y);
                minimumZ = MathF.Min(minimumZ, z);
                maximumX = MathF.Max(maximumX, x);
                maximumY = MathF.Max(maximumY, y);
                maximumZ = MathF.Max(maximumZ, z);
            }
        }

        return (minimumX <= 1.0f)
            && (maximumX >= -1.0f)
            && (minimumY <= 1.0f)
            && (maximumY >= -1.0f)
            && (minimumZ <= 1.0f)
            && (maximumZ >= 0.0f);
    }

    private static bool IsMeshC0SpotShadowCaster(
        RenderMeshSnapshot draw,
        RenderMeshAsset mesh,
        Matrix4x4 viewProjection)
    {
        if (!SpatialMath.TryToFrameRelative(draw.Position, draw.Frame3, out var position))
        {
            throw new InvalidOperationException("The spot shadow caster position is outside its captured spatial frame.");
        }

        var world = draw.LinearTransform * Matrix4x4.CreateTranslation(position);
        Span<bool> intersectsClipPlanes = stackalloc bool[6];
        intersectsClipPlanes.Clear();
        var bounds = mesh.Bounds;

        for (var cornerIndex = 0; cornerIndex < 8; cornerIndex++)
        {
            var corner = new Vector3(
                (cornerIndex & 1) == 0 ? bounds.Minimum.X : bounds.Maximum.X,
                (cornerIndex & 2) == 0 ? bounds.Minimum.Y : bounds.Maximum.Y,
                (cornerIndex & 4) == 0 ? bounds.Minimum.Z : bounds.Maximum.Z);
            var worldCorner = Vector3.TransformPoint(corner, world);
            var clip = Vector4.Transform(new Vector4(worldCorner, 1.0f), viewProjection);

            if (!clip.IsFinite().All())
            {
                throw new InvalidOperationException("A spot shadow caster bound is not representable.");
            }

            intersectsClipPlanes[0] |= clip.W + clip.X >= 0.0f;
            intersectsClipPlanes[1] |= clip.W - clip.X >= 0.0f;
            intersectsClipPlanes[2] |= clip.W + clip.Y >= 0.0f;
            intersectsClipPlanes[3] |= clip.W - clip.Y >= 0.0f;
            intersectsClipPlanes[4] |= clip.Z >= 0.0f;
            intersectsClipPlanes[5] |= clip.W - clip.Z >= 0.0f;
        }

        for (var planeIndex = 0; planeIndex < intersectsClipPlanes.Length; planeIndex++)
        {
            if (!intersectsClipPlanes[planeIndex])
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsMirroredTransform(Matrix4x4 transform)
    {
        var determinant =
            ((double)transform.Row1.X * ((transform.Row2.Y * transform.Row3.Z) - (transform.Row2.Z * transform.Row3.Y)))
            - ((double)transform.Row1.Y * ((transform.Row2.X * transform.Row3.Z) - (transform.Row2.Z * transform.Row3.X)))
            + ((double)transform.Row1.Z * ((transform.Row2.X * transform.Row3.Y) - (transform.Row2.Y * transform.Row3.X)));
        return determinant < 0.0;
    }

    private static bool IsSupportedMeshC0Texture(RenderMaterialAsset.TextureBinding binding)
        => (binding.UvSet == 0)
        && (binding.Sampler.Minification == 1)
        && (binding.Sampler.Magnification == 1)
        && (binding.Sampler.Mip == 0)
        && (binding.Sampler.AddressU == 0)
        && (binding.Sampler.AddressV == 0);

    private void BuildMeshC0Graph(
        RenderSnapshot snapshot,
        RenderGraph graph,
        ref RenderGraphBuiltInColorState? colorState,
        Action<int>? afterCamera = null)
    {
        if (snapshot is not RenderSnapshot3D meshSnapshot || meshSnapshot.MeshDraws.IsEmpty)
        {
            return;
        }

        var device = _graphicsDevice
            ?? throw new NotSupportedException("Headless rendering does not construct built-in Mesh passes.");
        using var assetPins = snapshot.RetainAssetPins()
            ?? throw new InvalidOperationException("The Mesh snapshot has no retained Scene assets.");
        var selectedDraws = new bool[meshSnapshot.MeshDraws.Length];
        var selectedIndices = new int[meshSnapshot.MeshDraws.Length];
        Array.Fill(selectedIndices, -1);
        var selectedDrawCount = 0;
        var directionalLightsByCamera = new ImmutableArray<RenderDirectionalLightSnapshot>[meshSnapshot.Cameras.Length];
        var shadowDataByCamera = new RenderDirectionalShadowCameraData?[meshSnapshot.Cameras.Length];
        var spotLightsByCamera = new ImmutableArray<RenderSpotLightSnapshot>[meshSnapshot.Cameras.Length];
        var spotShadowDataByCamera = new ImmutableArray<RenderSpotShadowData>[meshSnapshot.Cameras.Length];

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
        var uniformStride = RenderMeshC0FrameData.GetUniformAllocationStride(alignment);
        var pointLightsByCamera = new ImmutableArray<RenderPointLightSnapshot>[meshSnapshot.Cameras.Length];

        for (var cameraIndex = 0; cameraIndex < meshSnapshot.Cameras.Length; cameraIndex++)
        {
            var camera = meshSnapshot.Cameras[cameraIndex];

            if (meshSnapshot.MeshCameraOrders[cameraIndex].DrawIndices.IsEmpty)
            {
                pointLightsByCamera[cameraIndex] = [];
                directionalLightsByCamera[cameraIndex] = [];
                spotLightsByCamera[cameraIndex] = [];
                spotShadowDataByCamera[cameraIndex] = [];
            }
            else
            {
                pointLightsByCamera[cameraIndex] = GetMeshC0PointLights(meshSnapshot, camera);
                directionalLightsByCamera[cameraIndex] = GetMeshC0DirectionalLights(meshSnapshot, camera);
                spotLightsByCamera[cameraIndex] = GetMeshC0SpotLights(meshSnapshot, camera);
                spotShadowDataByCamera[cameraIndex] = GetMeshC0SpotShadows(
                    graph,
                    spotLightsByCamera[cameraIndex],
                    camera);

                if (!directionalLightsByCamera[cameraIndex].IsEmpty)
                {
                    if (directionalLightsByCamera[cameraIndex].Length > 1)
                    {
                        graph.AddBuildWarning(
                            "ShadowBudgetExceeded",
                            $"Camera {camera.CameraId} has {directionalLightsByCamera[cameraIndex].Length} selected directional lights; only the highest-priority light receives the directional shadow atlas.");
                    }

                    var end = camera.Far.HasValue
                        ? MathF.Min(RenderingKernels.DefaultDirectionalShadowDistance, camera.Far.Value)
                        : RenderingKernels.DefaultDirectionalShadowDistance;

                    if (end > camera.Near)
                    {
                        shadowDataByCamera[cameraIndex] = RenderDirectionalShadowCameraData.Create(
                            camera,
                            directionalLightsByCamera[cameraIndex][0].Direction);
                    }
                    else
                    {
                        graph.AddBuildWarning(
                            "ShadowRangeInvalid",
                            $"Camera {camera.CameraId} has no positive directional shadow range; selected directional lights remain unshadowed.");
                    }
                }
            }
        }

        var shadowDrawIndicesByCameraSlot = new ImmutableArray<int>[
            checked(meshSnapshot.Cameras.Length * RenderDirectionalShadowCameraData.CascadeCount * 2)];
        Array.Fill(shadowDrawIndicesByCameraSlot, []);

        for (var cameraIndex = 0; cameraIndex < meshSnapshot.Cameras.Length; cameraIndex++)
        {
            if (shadowDataByCamera[cameraIndex] is RenderDirectionalShadowCameraData shadowData)
            {
                var shadowLight = directionalLightsByCamera[cameraIndex][0];

                for (var cascadeIndex = 0; cascadeIndex < RenderDirectionalShadowCameraData.CascadeCount; cascadeIndex++)
                {
                    var cascadeDrawIndices = ImmutableArray.CreateBuilder<int>();
                    var viewProjection = shadowData.GetViewProjection(cascadeIndex);

                    for (var drawIndex = 0; drawIndex < meshSnapshot.MeshDraws.Length; drawIndex++)
                    {
                        var draw = meshSnapshot.MeshDraws[drawIndex];

                        if ((draw.LayerMask & shadowLight.LayerMask) == 0)
                        {
                            continue;
                        }

                        var sceneAssets = assetPins.GetSceneAssets(draw.SceneInstanceId);
                        var mesh = sceneAssets.GetMesh(draw.MeshAssetId);
                        var material = sceneAssets.GetMaterial(draw.MaterialAssetId);

                        if (material.AlphaMode is RenderMaterialAsset.MaterialAlphaMode.Blend)
                        {
                            continue;
                        }

                        if (!IsMeshC0ShadowCaster(draw, mesh, shadowLight.Direction, viewProjection))
                        {
                            continue;
                        }

                        cascadeDrawIndices.Add(drawIndex);

                        if (!selectedDraws[drawIndex])
                        {
                            selectedDraws[drawIndex] = true;
                            selectedDrawCount++;
                        }
                    }

                    shadowDrawIndicesByCameraSlot[
                        (cameraIndex * RenderDirectionalShadowCameraData.CascadeCount * 2) + cascadeIndex] =
                        cascadeDrawIndices.ToImmutable();
                }
            }

            var spotShadows = spotShadowDataByCamera[cameraIndex];

            for (var spotIndex = 0; spotIndex < spotShadows.Length; spotIndex++)
            {
                var cascadeDrawIndices = ImmutableArray.CreateBuilder<int>();
                var spotShadow = spotShadows[spotIndex];

                for (var drawIndex = 0; drawIndex < meshSnapshot.MeshDraws.Length; drawIndex++)
                {
                    var draw = meshSnapshot.MeshDraws[drawIndex];

                    if ((draw.LayerMask & spotShadow.LayerMask) == 0)
                    {
                        continue;
                    }

                    var sceneAssets = assetPins.GetSceneAssets(draw.SceneInstanceId);
                    var mesh = sceneAssets.GetMesh(draw.MeshAssetId);
                    var material = sceneAssets.GetMaterial(draw.MaterialAssetId);

                    if ((material.AlphaMode is RenderMaterialAsset.MaterialAlphaMode.Blend)
                        || !IsMeshC0SpotShadowCaster(draw, mesh, spotShadow.ViewProjection))
                    {
                        continue;
                    }

                    cascadeDrawIndices.Add(drawIndex);

                    if (!selectedDraws[drawIndex])
                    {
                        selectedDraws[drawIndex] = true;
                        selectedDrawCount++;
                    }
                }

                shadowDrawIndicesByCameraSlot[
                    (cameraIndex * RenderDirectionalShadowCameraData.CascadeCount * 2)
                    + RenderDirectionalShadowCameraData.CascadeCount
                    + spotIndex] = cascadeDrawIndices.ToImmutable();
            }
        }

        var hasShadowData = false;

        for (var cameraIndex = 0; cameraIndex < shadowDataByCamera.Length; cameraIndex++)
        {
            hasShadowData |= shadowDataByCamera[cameraIndex].HasValue
                || !spotShadowDataByCamera[cameraIndex].IsEmpty;
        }

        var frameBytes = CreateMeshFrameBuffer(
            meshSnapshot,
            pointLightsByCamera,
            shadowDataByCamera,
            spotShadowDataByCamera,
            uniformStride,
            out var frameStride);
        var shadowFrameStride = uniformStride;
        var shadowFrameBytes = hasShadowData
            ? CreateMeshShadowFrameBuffer(
                meshSnapshot.Cameras.Length,
                shadowDataByCamera,
                spotShadowDataByCamera,
                uniformStride,
                out shadowFrameStride)
            : [];
        var selectedDrawsInOrder = new List<RenderMeshSnapshot>(selectedDrawCount);
        var meshAssets = new List<RenderMeshAsset>(selectedDrawCount);
        var materialAssets = new List<RenderMaterialAsset>(selectedDrawCount);
        var hasBaseColorTextures = new List<bool>(selectedDrawCount);
        var hasEmissiveTextures = new List<bool>(selectedDrawCount);
        var hasPackedMetalRoughOcclusionTextures = new List<bool>(selectedDrawCount);
        var hasNormalTextures = new List<bool>(selectedDrawCount);
        var selectedBonePaletteOffsets = new List<int>(selectedDrawCount);
        var vertexBytesByMesh = new List<byte[]>(selectedDrawCount);
        var indexBytesByMesh = new List<byte[]>(selectedDrawCount);
        var baseColorTextureVersions = new GraphTextureVersion?[meshSnapshot.MeshDraws.Length];
        var emissiveTextureVersions = new GraphTextureVersion?[meshSnapshot.MeshDraws.Length];
        var packedMetalRoughOcclusionTextureVersions = new GraphTextureVersion?[meshSnapshot.MeshDraws.Length];
        var normalTextureVersions = new GraphTextureVersion?[meshSnapshot.MeshDraws.Length];
        var meshGeometryIndices = new int[meshSnapshot.MeshDraws.Length];
        Array.Fill(meshGeometryIndices, -1);
        var meshGeometryIndexByKey = new Dictionary<(Guid SceneInstanceId, int MeshAssetId), int>();
        var meshTextureVersionsByKey = new Dictionary<(AssetPackageBackingToken BackingToken, int TextureAssetId), GraphTextureVersion>();
        var bonePaletteOffsets = new int[meshSnapshot.MeshPalettes.Length];
        var bonePaletteBytes = RenderMeshC0BoneBufferBuilder.Create(
            meshSnapshot.MeshPalettes.AsSpan(),
            bonePaletteOffsets);
        var hasSkinnedDraws = false;
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
                throw new NotSupportedException("Mesh C0 admits only its supported PBR alpha, sidedness, depth, and capability combinations.");
            }

            var baseColorTexture = material.Parameters.BaseTexture;
            var emissiveTexture = material.Parameters.EmissiveTexture;
            var packedMetalRoughOcclusionTexture = material.Parameters.PackedMetalRoughOcclusion;
            var normalTexture = material.Parameters.NormalTexture;
            var isSkinned = draw.PaletteIndex >= 0;

            if (normalTexture.HasValue && IsMirroredTransform(draw.LinearTransform))
            {
                throw new NotSupportedException("Mesh C0 normal maps do not support mirrored transforms.");
            }

            if (baseColorTexture.HasValue
                || emissiveTexture.HasValue
                || packedMetalRoughOcclusionTexture.HasValue
                || normalTexture.HasValue)
            {
                if ((baseColorTexture is RenderMaterialAsset.TextureBinding baseTextureBinding
                        && !IsSupportedMeshC0Texture(baseTextureBinding))
                    || (emissiveTexture is RenderMaterialAsset.TextureBinding emissiveTextureBinding
                        && !IsSupportedMeshC0Texture(emissiveTextureBinding))
                    || (packedMetalRoughOcclusionTexture is RenderMaterialAsset.TextureBinding packedTextureBinding
                        && !IsSupportedMeshC0Texture(packedTextureBinding))
                    || (normalTexture is RenderMaterialAsset.TextureBinding normalTextureBinding
                        && !IsSupportedMeshC0Texture(normalTextureBinding)))
                {
                    throw new NotSupportedException("Mesh C0 textures require UV0, linear minification/magnification, no mip filtering, and clamp addressing.");
                }

                if (mesh.Uv0.IsDefault)
                {
                    throw new NotSupportedException("Mesh C0 textures require an authored UV0 vertex stream.");
                }

                if (normalTexture.HasValue && mesh.Tangents.IsDefault)
                {
                    throw new NotSupportedException("Mesh C0 normal maps require an authored tangent vertex stream.");
                }

                if (baseColorTexture is RenderMaterialAsset.TextureBinding selectedBaseTexture)
                {
                    baseColorTextureVersions[drawIndex] = GetMeshC0TextureVersion(
                        graph,
                        sceneAssets,
                        draw.MaterialAssetId,
                        drawIndex,
                        selectedBaseTexture,
                        meshTextureVersionsByKey);
                }

                if (emissiveTexture is RenderMaterialAsset.TextureBinding selectedEmissiveTexture)
                {
                    emissiveTextureVersions[drawIndex] = GetMeshC0TextureVersion(
                        graph,
                        sceneAssets,
                        draw.MaterialAssetId,
                        drawIndex,
                        selectedEmissiveTexture,
                        meshTextureVersionsByKey);
                }

                if (packedMetalRoughOcclusionTexture is RenderMaterialAsset.TextureBinding selectedPackedTexture)
                {
                    packedMetalRoughOcclusionTextureVersions[drawIndex] = GetMeshC0TextureVersion(
                        graph,
                        sceneAssets,
                        draw.MaterialAssetId,
                        drawIndex,
                        selectedPackedTexture,
                        meshTextureVersionsByKey);
                }

                if (normalTexture is RenderMaterialAsset.TextureBinding selectedNormalTexture)
                {
                    normalTextureVersions[drawIndex] = GetMeshC0TextureVersion(
                        graph,
                        sceneAssets,
                        draw.MaterialAssetId,
                        drawIndex,
                        selectedNormalTexture,
                        meshTextureVersionsByKey);
                }
            }

            if (mesh.JointIndices.IsDefault != (draw.PaletteIndex < 0))
            {
                throw new InvalidOperationException("A skinned Mesh draw does not match its captured bind-pose palette.");
            }

            if ((draw.GroupIndex < 0) || (draw.GroupIndex >= mesh.Groups.Length))
            {
                throw new InvalidOperationException("A Mesh snapshot draw references an unavailable group.");
            }

            selectedDrawsInOrder.Add(draw);
            selectedIndices[drawIndex] = preparedIndex++;
            materialAssets.Add(material);
            var bonePaletteOffset = isSkinned ? bonePaletteOffsets[draw.PaletteIndex] : -1;
            selectedBonePaletteOffsets.Add(bonePaletteOffset);
            hasSkinnedDraws |= isSkinned;
            hasBaseColorTextures.Add(baseColorTexture.HasValue);
            hasEmissiveTextures.Add(emissiveTexture.HasValue);
            hasPackedMetalRoughOcclusionTextures.Add(packedMetalRoughOcclusionTexture.HasValue);
            hasNormalTextures.Add(normalTexture.HasValue);
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

        var objectBytes = CreateMeshObjectBuffer(
            CollectionsMarshal.AsSpan(selectedDrawsInOrder),
            CollectionsMarshal.AsSpan(selectedBonePaletteOffsets),
            uniformStride);
        var materialBytes = CreateMeshMaterialBuffer(
            CollectionsMarshal.AsSpan(materialAssets),
            CollectionsMarshal.AsSpan(hasBaseColorTextures),
            CollectionsMarshal.AsSpan(hasEmissiveTextures),
            CollectionsMarshal.AsSpan(hasPackedMetalRoughOcclusionTextures),
            CollectionsMarshal.AsSpan(hasNormalTextures),
            uniformStride);
        var frameBuffer = graph.CreateInitializedTransientBuffer(
            new BufferDescription(
                frameBytes.Length,
                BufferUsage.Uniform | BufferUsage.CopyDestination,
                GpuMemoryKind.DeviceLocal,
                structuredStride: null,
                "mesh-frame-data"),
            frameBytes);
        GraphBufferVersion? shadowFrameBuffer = null;

        if (hasShadowData)
        {
            shadowFrameBuffer = graph.CreateInitializedTransientBuffer(
                new BufferDescription(
                    shadowFrameBytes.Length,
                    BufferUsage.Uniform | BufferUsage.CopyDestination,
                    GpuMemoryKind.DeviceLocal,
                    structuredStride: null,
                    "mesh-shadow-frame-data"),
                shadowFrameBytes);
        }

        var pointLightBuffers = new GraphBufferVersion[pointLightsByCamera.Length];
        var directionalLightBuffers = new GraphBufferVersion[directionalLightsByCamera.Length];
        var spotLightBuffers = new GraphBufferVersion[spotLightsByCamera.Length];

        for (var cameraIndex = 0; cameraIndex < pointLightsByCamera.Length; cameraIndex++)
        {
            var pointLightBytes = CreateMeshPointLightBuffer(
                pointLightsByCamera[cameraIndex],
                meshSnapshot.Cameras[cameraIndex].Frame3);
            pointLightBuffers[cameraIndex] = graph.CreateInitializedTransientBuffer(
                new BufferDescription(
                    pointLightBytes.Length,
                    BufferUsage.Uniform | BufferUsage.CopyDestination,
                    GpuMemoryKind.DeviceLocal,
                    structuredStride: null,
                    $"mesh-point-lights-{cameraIndex}"),
                pointLightBytes);
            var directionalLights = directionalLightsByCamera[cameraIndex];
            var shadowedLightId = shadowDataByCamera[cameraIndex].HasValue
                ? directionalLights[0].LightId
                : Guid.Empty;
            var directionalLightBytes = CreateMeshDirectionalLightBuffer(
                directionalLights,
                shadowedLightId,
                alignment);
            directionalLightBuffers[cameraIndex] = graph.CreateInitializedTransientBuffer(
                new BufferDescription(
                    directionalLightBytes.Length,
                    BufferUsage.Uniform | BufferUsage.CopyDestination,
                    GpuMemoryKind.DeviceLocal,
                    structuredStride: null,
                    $"mesh-directional-lights-{cameraIndex}"),
                directionalLightBytes);
            var spotLightBytes = RenderMeshC0SpotLightBufferBuilder.Create(
                spotLightsByCamera[cameraIndex],
                meshSnapshot.Cameras[cameraIndex].Frame3,
                spotShadowDataByCamera[cameraIndex],
                alignment);
            spotLightBuffers[cameraIndex] = graph.CreateInitializedTransientBuffer(
                new BufferDescription(
                    spotLightBytes.Length,
                    BufferUsage.Uniform | BufferUsage.CopyDestination,
                    GpuMemoryKind.DeviceLocal,
                    structuredStride: null,
                    $"mesh-spot-lights-{cameraIndex}"),
                spotLightBytes);
        }

        var objectBuffer = graph.CreateInitializedTransientBuffer(
            new BufferDescription(
                objectBytes.Length,
                BufferUsage.Uniform | BufferUsage.CopyDestination,
                GpuMemoryKind.DeviceLocal,
                structuredStride: null,
                "mesh-object-data"),
            objectBytes);
        GraphBufferVersion? currentBonePaletteBuffer = null;

        if (hasSkinnedDraws)
        {
            currentBonePaletteBuffer = graph.CreateInitializedTransientBuffer(
                new BufferDescription(
                    bonePaletteBytes.Length,
                    BufferUsage.Storage | BufferUsage.CopyDestination,
                    GpuMemoryKind.DeviceLocal,
                    RenderMeshC0BoneBufferBuilder.Stride,
                    "mesh-bone-palette-data"),
                bonePaletteBytes);
        }

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
        colorState ??= new RenderGraphBuiltInColorState(
            graph.CreateTransientTexture(new TextureDescription(
                TextureDimension.Dimension2D,
                snapshot.TargetExtent.Width,
                snapshot.TargetExtent.Height,
                depth: 1,
                arrayLayers: 1,
                mipLevels: 1,
                TextureFormat.Rgba16Float,
                TextureUsage.ColorAttachment | TextureUsage.Sampled,
                "builtin-hdr-working-target")));
        var builtInColorState = colorState;
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
        var currentColorVersion = colorState.CurrentColorVersion;
        var currentDepthVersion = depthVersion;
        var hasColorContents = colorState.HasColorContents;
        var hasDepthContents = false;
        var previousPass = colorState.PreviousPass;
        var hasPreviousPass = colorState.HasPreviousPass;
        var passIndex = colorState.NextPassIndex;

        void StoreColorState()
        {
            builtInColorState.CurrentColorVersion = currentColorVersion;
            builtInColorState.HasColorContents = hasColorContents;
            builtInColorState.HasPreviousPass = hasPreviousPass;
            builtInColorState.NextPassIndex = passIndex;
            builtInColorState.PreviousPass = previousPass;
        }

        void LoadColorState()
        {
            currentColorVersion = builtInColorState.CurrentColorVersion;
            hasColorContents = builtInColorState.HasColorContents;
            previousPass = builtInColorState.PreviousPass;
            hasPreviousPass = builtInColorState.HasPreviousPass;
            passIndex = builtInColorState.NextPassIndex;
        }

        var shadowAtlasResolution = hasShadowData ? RenderingKernels.ShadowAtlasResolution : 1;
        var shadowAtlasVersion = graph.CreateTransientTexture(new TextureDescription(
            TextureDimension.Dimension2D,
            shadowAtlasResolution,
            shadowAtlasResolution,
            depth: 1,
            arrayLayers: 1,
            mipLevels: 1,
            TextureFormat.D32Float,
            TextureUsage.DepthAttachment | TextureUsage.Sampled,
            hasShadowData ? "mesh-shadow-atlas" : "mesh-unshadowed-depth"));
        var shadowAtlasClearArea = new RenderArea(0, 0, shadowAtlasResolution, shadowAtlasResolution);
        var shadowAtlasClearPass = graph.AddRasterPass(new RenderPassDescription(
            "TerraFX.MeshC0.ShadowAtlas.Clear",
            passIndex++,
            hasPreviousPass ? [previousPass] : [],
            hasExternalSideEffects: false,
            renderArea: shadowAtlasClearArea));
        shadowAtlasVersion = graph.WriteDepthAttachment(
            shadowAtlasClearPass,
            shadowAtlasVersion,
            new GraphTextureRange(0, 1, 0, 1, TextureAspect.Depth),
            AttachmentLoadAction.Clear,
            AttachmentStoreAction.Store,
            0.0f);
        previousPass = shadowAtlasClearPass;
        hasPreviousPass = true;

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
                    afterCamera?.Invoke(cameraIndex);
                    LoadColorState();
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
                    colorState.MarkColorWrite(camera.CameraId);
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

                StoreColorState();
                afterCamera?.Invoke(cameraIndex);
                LoadColorState();
                continue;
            }

            if (shadowDataByCamera[cameraIndex].HasValue || !spotShadowDataByCamera[cameraIndex].IsEmpty)
            {
                var shadowFrameBufferVersion = shadowFrameBuffer
                    ?? throw new InvalidOperationException("The graph has shadow cascades without a shadow frame buffer.");

                for (var shadowSlot = 0; shadowSlot < (RenderDirectionalShadowCameraData.CascadeCount * 2); shadowSlot++)
                {
                    var casterIndices = shadowDrawIndicesByCameraSlot[
                        (cameraIndex * RenderDirectionalShadowCameraData.CascadeCount * 2) + shadowSlot];

                    if (casterIndices.IsEmpty)
                    {
                        continue;
                    }

                    RenderingKernels.GetShadowAtlasCellOrigin(shadowSlot, out var cellX, out var cellY);
                    var shadowArea = new RenderArea(
                        cellX,
                        cellY,
                        RenderingKernels.ShadowAtlasCellSize,
                        RenderingKernels.ShadowAtlasCellSize);

                    foreach (var drawIndex in casterIndices)
                    {
                        var selectedIndex = selectedIndices[drawIndex];

                        if (selectedIndex < 0)
                        {
                            throw new InvalidOperationException("A shadow-selected Mesh draw was not prepared for graph submission.");
                        }

                        var draw = selectedDrawsInOrder[selectedIndex];
                        var meshIndex = meshGeometryIndices[drawIndex];
                        var mesh = meshAssets[meshIndex];
                        var group = mesh.Groups[draw.GroupIndex];
                        var vertexBuffer = vertexBuffers[meshIndex];
                        var indexBuffer = indexBuffers[meshIndex];
                        var shadowPass = new RenderMeshC0ShadowGraphPass(
                            selectedIndex,
                            group,
                            mesh.IndexFormat,
                            shadowFrameBufferVersion,
                            checked(
                                (((long)cameraIndex * RenderDirectionalShadowCameraData.CascadeCount * 2) + shadowSlot)
                                * shadowFrameStride),
                            objectBuffer,
                            uniformStride,
                            materialBuffer,
                            uniformStride,
                            vertexBuffer,
                            indexBuffer,
                            draw.PaletteIndex >= 0
                                ? currentBonePaletteBuffer
                                    ?? throw new InvalidOperationException("The graph has a skinned shadow pass without a bone-palette buffer.")
                                : null,
                            draw.PaletteIndex >= 0,
                            baseColorTextureVersions[drawIndex]);
                        var shadowPassId = graph.AddBuiltInMeshShadowRasterPass(
                            new RenderPassDescription(
                                $"TerraFX.MeshC0.Camera{cameraIndex}.Shadow{shadowSlot}.Draw{drawIndex}",
                                passIndex++,
                                hasPreviousPass ? [previousPass] : [],
                                hasExternalSideEffects: false,
                                renderArea: shadowArea),
                            shadowPass);
                        var shadowFrameOffset = checked(
                            (((long)cameraIndex * RenderDirectionalShadowCameraData.CascadeCount * 2) + shadowSlot)
                            * shadowFrameStride);
                        var sourceShadowAtlas = shadowAtlasVersion;
                        shadowAtlasVersion = graph.WriteDepthAttachment(
                            shadowPassId,
                            sourceShadowAtlas,
                            new GraphTextureRange(0, 1, 0, 1, TextureAspect.Depth),
                            AttachmentLoadAction.Load,
                            AttachmentStoreAction.Store,
                            0.0f);

                        if (shadowPass.Skinned)
                        {
                            var sourceBonePaletteBuffer = currentBonePaletteBuffer
                                ?? throw new InvalidOperationException("The graph has a skinned shadow pass without a bone-palette buffer.");
                            currentBonePaletteBuffer = graph.ReadWriteBuffer(
                                shadowPassId,
                                sourceBonePaletteBuffer,
                                new GraphBufferRange(0, sourceBonePaletteBuffer.Description.ByteLength));
                            shadowPass.SetBonePaletteWriteVersion(currentBonePaletteBuffer);
                        }

                        graph.ReadBuffer(
                            shadowPassId,
                            shadowFrameBufferVersion,
                            GraphBufferUsage.UniformRead,
                            new GraphBufferRange(shadowFrameOffset, 16 * sizeof(float)));
                        graph.ReadBuffer(
                            shadowPassId,
                            objectBuffer,
                            GraphBufferUsage.UniformRead,
                            new GraphBufferRange(
                                checked((long)selectedIndex * uniformStride),
                                ObjectGpuData.SizeInBytes));
                        graph.ReadBuffer(
                            shadowPassId,
                            materialBuffer,
                            GraphBufferUsage.UniformRead,
                            new GraphBufferRange(
                                checked((long)selectedIndex * uniformStride),
                                RenderMeshC0MaterialData.SizeInBytes));
                        graph.ReadBuffer(
                            shadowPassId,
                            vertexBuffer,
                            GraphBufferUsage.VertexRead,
                            new GraphBufferRange(0, vertexBuffer.Description.ByteLength));
                        graph.ReadBuffer(
                            shadowPassId,
                            indexBuffer,
                            GraphBufferUsage.IndexRead,
                            new GraphBufferRange(0, indexBuffer.Description.ByteLength));

                        if (baseColorTextureVersions[drawIndex] is GraphTextureVersion baseTexture)
                        {
                            graph.ReadTexture(
                                shadowPassId,
                                baseTexture,
                                GraphTextureUsage.SampledRead,
                                new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color));
                        }

                        previousPass = shadowPassId;
                        hasPreviousPass = true;
                    }
                }
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
                    pointLightBuffers[cameraIndex],
                    directionalLightBuffers[cameraIndex],
                    spotLightBuffers[cameraIndex],
                    shadowAtlasVersion,
                    frameStride,
                    cameraIndex,
                    objectBuffer,
                    uniformStride,
                    materialBuffer,
                    uniformStride,
                    vertexBuffer,
                    indexBuffer,
                    draw.PaletteIndex >= 0
                        ? currentBonePaletteBuffer
                            ?? throw new InvalidOperationException("The graph has a skinned Mesh pass without a bone-palette buffer.")
                        : null,
                    draw.PaletteIndex >= 0,
                    baseColorTextureVersions[drawIndex],
                    emissiveTextureVersions[drawIndex],
                    packedMetalRoughOcclusionTextureVersions[drawIndex],
                    normalTextureVersions[drawIndex],
                    materialAssets[selectedIndex].AlphaMode is RenderMaterialAsset.MaterialAlphaMode.Mask,
                    materialAssets[selectedIndex].AlphaMode is RenderMaterialAsset.MaterialAlphaMode.Blend);
                var pass = graph.AddBuiltInMeshRasterPass(
                    new RenderPassDescription(
                        $"TerraFX.MeshC0.Camera{cameraIndex}.Draw{orderIndex}",
                        passIndex++,
                        hasPreviousPass ? [previousPass] : [],
                        hasExternalSideEffects: false,
                        renderArea: area),
                    meshPass);

                if (meshPass.Skinned)
                {
                    var sourceBonePaletteBuffer = currentBonePaletteBuffer
                        ?? throw new InvalidOperationException("The graph has a skinned Mesh pass without a bone-palette buffer.");
                    currentBonePaletteBuffer = graph.ReadWriteBuffer(
                        pass,
                        sourceBonePaletteBuffer,
                        new GraphBufferRange(0, sourceBonePaletteBuffer.Description.ByteLength));
                    meshPass.SetBonePaletteWriteVersion(currentBonePaletteBuffer);
                }

                graph.ReadBuffer(
                    pass,
                    frameBuffer,
                    GraphBufferUsage.UniformRead,
                    new GraphBufferRange(0, frameBuffer.Description.ByteLength));
                graph.ReadBuffer(
                    pass,
                    pointLightBuffers[cameraIndex],
                    GraphBufferUsage.UniformRead,
                    new GraphBufferRange(0, pointLightBuffers[cameraIndex].Description.ByteLength));
                graph.ReadBuffer(
                    pass,
                    directionalLightBuffers[cameraIndex],
                    GraphBufferUsage.UniformRead,
                    new GraphBufferRange(0, directionalLightBuffers[cameraIndex].Description.ByteLength));
                graph.ReadBuffer(
                    pass,
                    spotLightBuffers[cameraIndex],
                    GraphBufferUsage.UniformRead,
                    new GraphBufferRange(0, spotLightBuffers[cameraIndex].Description.ByteLength));
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
                graph.ReadTexture(
                    pass,
                    shadowAtlasVersion,
                    GraphTextureUsage.SampledRead,
                    new GraphTextureRange(0, 1, 0, 1, TextureAspect.Depth));

                if (baseColorTextureVersions[drawIndex] is GraphTextureVersion baseTexture)
                {
                    graph.ReadTexture(
                        pass,
                        baseTexture,
                        GraphTextureUsage.SampledRead,
                        new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color));
                }

                if (emissiveTextureVersions[drawIndex] is GraphTextureVersion emissiveTextureVersion
                    && (baseColorTextureVersions[drawIndex]?.ResourceId != emissiveTextureVersion.ResourceId))
                {
                    graph.ReadTexture(
                        pass,
                        emissiveTextureVersion,
                        GraphTextureUsage.SampledRead,
                        new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color));
                }

                if (packedMetalRoughOcclusionTextureVersions[drawIndex] is GraphTextureVersion packedTextureVersion
                    && (baseColorTextureVersions[drawIndex]?.ResourceId != packedTextureVersion.ResourceId)
                    && (emissiveTextureVersions[drawIndex]?.ResourceId != packedTextureVersion.ResourceId))
                {
                    graph.ReadTexture(
                        pass,
                        packedTextureVersion,
                        GraphTextureUsage.SampledRead,
                        new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color));
                }

                if (normalTextureVersions[drawIndex] is GraphTextureVersion normalTextureVersion
                    && (baseColorTextureVersions[drawIndex]?.ResourceId != normalTextureVersion.ResourceId)
                    && (emissiveTextureVersions[drawIndex]?.ResourceId != normalTextureVersion.ResourceId)
                    && (packedMetalRoughOcclusionTextureVersions[drawIndex]?.ResourceId != normalTextureVersion.ResourceId))
                {
                    graph.ReadTexture(
                        pass,
                        normalTextureVersion,
                        GraphTextureUsage.SampledRead,
                        new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color));
                }

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
                colorState.MarkColorWrite(camera.CameraId);
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

            StoreColorState();
            afterCamera?.Invoke(cameraIndex);
            LoadColorState();
        }

        StoreColorState();

        if (meshPassCount == 0)
        {
            return;
        }
    }
}
