// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Animation;
using TerraFX.Assets;

namespace TerraFX.Rendering;

internal static class RenderMeshAssetBinding
{
    internal static async Task<AssetLease<SkeletonAsset>?> LoadSkeletonAsync(
        AssetLease<RenderMeshAsset> meshLease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(meshLease);
        using var retainedMeshLease = meshLease.Retain();
        var mesh = retainedMeshLease.Value;

        if (mesh.SkeletonAssetId is not { } skeletonAssetId)
        {
            return null;
        }

        var skeletonLease = await retainedMeshLease.LoadFromPackageAsync<SkeletonAsset>(
            skeletonAssetId,
            cancellationToken).ConfigureAwait(false);
        var isValid = false;

        try
        {
            foreach (var jointIndex in mesh.JointIndices)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (jointIndex >= skeletonLease.Value.Joints.Length)
                {
                    throw new InvalidDataException("A Mesh vertex joint index is outside its selected Skeleton's joint domain.");
                }
            }

            isValid = true;
            return skeletonLease;
        }
        finally
        {
            if (!isValid)
            {
                skeletonLease.Dispose();
            }
        }
    }

    internal static void ValidateMaterialBindings(RenderMeshAsset mesh, RenderMaterialAsset material)
    {
        if (material.IsSprite)
        {
            if (!mesh.JointIndices.IsDefault)
            {
                throw new InvalidDataException("A Sprite-profile Material does not admit a skinned Mesh.");
            }

            if (mesh.Uv0.IsDefault)
            {
                throw new InvalidDataException("A Sprite-profile Mesh Material requires the Mesh UVSet0 stream.");
            }

            ValidateTextureBinding(mesh, material.Parameters.SpriteTexture);
            return;
        }

        if (mesh.Normals.IsDefault)
        {
            throw new InvalidDataException("A PBR Mesh Material requires the Mesh Normal stream.");
        }

        ValidateTextureBinding(mesh, material.Parameters.BaseTexture);
        ValidateTextureBinding(mesh, material.Parameters.PackedMetalRoughOcclusion);
        ValidateTextureBinding(mesh, material.Parameters.NormalTexture);
        ValidateTextureBinding(mesh, material.Parameters.EmissiveTexture);

        if (material.Parameters.NormalTexture.HasValue && mesh.Tangents.IsDefault)
        {
            throw new InvalidDataException("A PBR normal-texture Mesh Material requires the Mesh Tangent stream.");
        }
    }

    internal static async Task ValidatePackageBindingsAsync(
        AssetLease<RenderMeshAsset> meshLease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(meshLease);
        cancellationToken.ThrowIfCancellationRequested();
        using var retainedMeshLease = meshLease.Retain();
        var mesh = retainedMeshLease.Value;
        using var skeletonLease = await LoadSkeletonAsync(retainedMeshLease, cancellationToken).ConfigureAwait(false);
        var validatedMaterialIds = new HashSet<AssetId>();

        foreach (var materialAssetId in mesh.MaterialSlots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!validatedMaterialIds.Add(materialAssetId))
            {
                continue;
            }

            using var materialLease = await retainedMeshLease.LoadFromPackageAsync<RenderMaterialAsset>(
                materialAssetId,
                cancellationToken).ConfigureAwait(false);
            ValidateMaterialBindings(mesh, materialLease.Value);
            using var materialTextureSet = await RenderMaterialTextureLoader.RetainAsync(
                materialLease,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static void ValidateTextureBinding(
        RenderMeshAsset mesh,
        RenderMaterialAsset.TextureBinding? textureBinding)
    {
        if (textureBinding is not { } binding)
        {
            return;
        }

        var hasUvStream = binding.UvSet switch {
            0 => !mesh.Uv0.IsDefault,
            1 => !mesh.Uv1.IsDefault,
            _ => false,
        };

        if (!hasUvStream)
        {
            throw new InvalidDataException("A Mesh Material texture binding selects a UV set absent from the Mesh.");
        }
    }
}
