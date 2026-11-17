// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Animation;
using TerraFX.Assets;

namespace TerraFX.Rendering;

internal static class RenderMeshAsset2Binding
{
    internal static async Task<ImmutableArray<AssetId>> ValidatePackageBindingsAsync(
        AssetLease<RenderMeshAsset2> meshLease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(meshLease);
        cancellationToken.ThrowIfCancellationRequested();
        using var retainedMesh = meshLease.Retain();
        var mesh = retainedMesh.Value;
        var membership = new HashSet<AssetId> { mesh.AssetId };

        if (mesh.SkeletonAssetId is { } skeletonAssetId)
        {
            using var skeletonLease = await retainedMesh.LoadFromPackageAsync<SkeletonAsset>(
                skeletonAssetId,
                cancellationToken).ConfigureAwait(false);

            foreach (var jointIndex in mesh.Vertices.JointIndices)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (jointIndex >= skeletonLease.Value.Joints.Length)
                {
                    throw new InvalidDataException("A Mesh LOD vertex joint is outside its retained Skeleton's joint domain.");
                }
            }

            _ = membership.Add(skeletonAssetId);
        }

        foreach (var slot in mesh.MaterialSlots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (slot.SchemaMajor == 1)
            {
                using var materialLease = await retainedMesh.LoadFromPackageAsync<RenderMaterialAsset>(
                    slot.AssetId,
                    cancellationToken).ConfigureAwait(false);
                var material = materialLease.Value;
                await ValidateMaterialAsync(
                    mesh,
                    materialLease,
                    material.IsSprite,
                    material.Parameters,
                    membership,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                using var materialLease = await retainedMesh.LoadFromPackageAsync<RenderMaterialAsset2>(
                    slot.AssetId,
                    cancellationToken).ConfigureAwait(false);
                var material = materialLease.Value.Data;
                await ValidateMaterialAsync(
                    mesh,
                    materialLease,
                    material.IsSprite,
                    material.Parameters,
                    membership,
                    cancellationToken).ConfigureAwait(false);
            }

            _ = membership.Add(slot.AssetId);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var ordered = new AssetId[membership.Count];
        membership.CopyTo(ordered);
        Array.Sort(ordered, static (left, right) => left.Value.CompareTo(right.Value));
        return [.. ordered];
    }

    private static async Task ValidateMaterialAsync<T>(
        RenderMeshAsset2 mesh,
        AssetLease<T> materialLease,
        bool isSprite,
        RenderMaterialAsset.ParameterValues parameters,
        HashSet<AssetId> membership,
        CancellationToken cancellationToken)
    {
        if (isSprite)
        {
            if (!mesh.Vertices.JointIndices.IsDefault || mesh.Vertices.Uv0.IsDefault)
            {
                throw new InvalidDataException("A Sprite-profile Mesh requires UVSet0 and does not admit skinning.");
            }

            await ValidateTextureAsync(
                mesh,
                materialLease,
                parameters.SpriteTexture,
                RenderTextureSemantic.Color,
                expectedColorSpace: null,
                RenderTextureAlphaMode.Premultiplied,
                membership,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (mesh.Vertices.Normals.IsDefault
            || (parameters.NormalTexture.HasValue && mesh.Vertices.Tangents.IsDefault))
        {
            throw new InvalidDataException("A PBR Mesh requires Normals and a normal texture additionally requires Tangents.");
        }

        await ValidateTextureAsync(
            mesh,
            materialLease,
            parameters.BaseTexture,
            RenderTextureSemantic.Color,
            RenderTextureColorSpace.Srgb,
            RenderTextureAlphaMode.Straight,
            membership,
            cancellationToken).ConfigureAwait(false);
        await ValidateTextureAsync(
            mesh,
            materialLease,
            parameters.PackedMetalRoughOcclusion,
            RenderTextureSemantic.Data,
            RenderTextureColorSpace.Linear,
            RenderTextureAlphaMode.Straight,
            membership,
            cancellationToken).ConfigureAwait(false);
        await ValidateTextureAsync(
            mesh,
            materialLease,
            parameters.NormalTexture,
            RenderTextureSemantic.Normal,
            RenderTextureColorSpace.Linear,
            RenderTextureAlphaMode.Straight,
            membership,
            cancellationToken).ConfigureAwait(false);
        await ValidateTextureAsync(
            mesh,
            materialLease,
            parameters.EmissiveTexture,
            RenderTextureSemantic.Color,
            RenderTextureColorSpace.Srgb,
            RenderTextureAlphaMode.Straight,
            membership,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task ValidateTextureAsync<T>(
        RenderMeshAsset2 mesh,
        AssetLease<T> materialLease,
        RenderMaterialAsset.TextureBinding? textureBinding,
        RenderTextureSemantic expectedSemantic,
        RenderTextureColorSpace? expectedColorSpace,
        RenderTextureAlphaMode expectedAlphaMode,
        HashSet<AssetId> membership,
        CancellationToken cancellationToken)
    {
        if (textureBinding is not { } binding)
        {
            return;
        }

        var hasUv = binding.UvSet == 0 ? !mesh.Vertices.Uv0.IsDefault : !mesh.Vertices.Uv1.IsDefault;

        if (!hasUv)
        {
            throw new InvalidDataException("A Mesh LOD Material texture selects an absent UV stream.");
        }

        using var textureLease = await materialLease.LoadFromPackageAsync<RenderTextureAsset>(
            binding.TextureAssetId,
            cancellationToken).ConfigureAwait(false);
        RenderMaterialTextureLoader.ValidateSlot(
            binding,
            textureLease.Value,
            expectedSemantic,
            expectedColorSpace,
            expectedAlphaMode);
        _ = membership.Add(binding.TextureAssetId);
    }
}
