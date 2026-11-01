// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Assets;

namespace TerraFX.Rendering;

internal static class RenderMaterialTextureLoader
{
    internal static async Task<RenderMaterialTextureSet> LoadAsync(
        AssetStore assets,
        AssetId materialAssetId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assets);
        var materialLease = await assets.LoadAsync<RenderMaterialAsset>(
            materialAssetId,
            cancellationToken).ConfigureAwait(false);
        return await CreateOwnedAsync(materialLease, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<RenderMaterialTextureSet> RetainAsync(
        AssetLease<RenderMaterialAsset> materialLease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(materialLease);
        var retainedMaterialLease = materialLease.Retain();
        return await CreateOwnedAsync(retainedMaterialLease, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<RenderMaterialTextureSet> CreateOwnedAsync(
        AssetLease<RenderMaterialAsset> materialLease,
        CancellationToken cancellationToken)
    {
        var textureLeases = ImmutableArray.CreateBuilder<AssetLease<RenderTextureAsset>>();

        try
        {
            var material = materialLease.Value;

            if (material.IsSprite)
            {
                if (material.Parameters.SpriteTexture is { } spriteTexture)
                {
                    await LoadTextureAsync(
                        materialLease,
                        textureLeases,
                        spriteTexture,
                        RenderTextureSemantic.Color,
                        expectedColorSpace: null,
                        RenderTextureAlphaMode.Premultiplied,
                        cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                if (material.Parameters.BaseTexture is { } baseTexture)
                {
                    await LoadTextureAsync(
                        materialLease,
                        textureLeases,
                        baseTexture,
                        RenderTextureSemantic.Color,
                        RenderTextureColorSpace.Srgb,
                        RenderTextureAlphaMode.Straight,
                        cancellationToken).ConfigureAwait(false);
                }

                if (material.Parameters.PackedMetalRoughOcclusion is { } packedTexture)
                {
                    await LoadTextureAsync(
                        materialLease,
                        textureLeases,
                        packedTexture,
                        RenderTextureSemantic.Data,
                        RenderTextureColorSpace.Linear,
                        RenderTextureAlphaMode.Straight,
                        cancellationToken).ConfigureAwait(false);
                }

                if (material.Parameters.NormalTexture is { } normalTexture)
                {
                    await LoadTextureAsync(
                        materialLease,
                        textureLeases,
                        normalTexture,
                        RenderTextureSemantic.Normal,
                        RenderTextureColorSpace.Linear,
                        RenderTextureAlphaMode.Straight,
                        cancellationToken).ConfigureAwait(false);
                }

                if (material.Parameters.EmissiveTexture is { } emissiveTexture)
                {
                    await LoadTextureAsync(
                        materialLease,
                        textureLeases,
                        emissiveTexture,
                        RenderTextureSemantic.Color,
                        RenderTextureColorSpace.Srgb,
                        RenderTextureAlphaMode.Straight,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            return new RenderMaterialTextureSet(materialLease, textureLeases.ToImmutable());
        }
        catch
        {
            foreach (var textureLease in textureLeases)
            {
                textureLease.Dispose();
            }

            materialLease.Dispose();
            throw;
        }
    }

    private static async Task LoadTextureAsync(
        AssetLease<RenderMaterialAsset> materialLease,
        ImmutableArray<AssetLease<RenderTextureAsset>>.Builder textureLeases,
        RenderMaterialAsset.TextureBinding binding,
        RenderTextureSemantic expectedSemantic,
        RenderTextureColorSpace? expectedColorSpace,
        RenderTextureAlphaMode expectedAlphaMode,
        CancellationToken cancellationToken)
    {
        foreach (var textureLease in textureLeases)
        {
            if (textureLease.AssetId == binding.TextureAssetId)
            {
                ValidateTexture(
                    binding,
                    textureLease.Value,
                    expectedSemantic,
                    expectedColorSpace,
                    expectedAlphaMode);
                return;
            }
        }

        var loadedLease = await materialLease.LoadFromPackageAsync<RenderTextureAsset>(
            binding.TextureAssetId,
            cancellationToken).ConfigureAwait(false);

        try
        {
            ValidateTexture(
                binding,
                loadedLease.Value,
                expectedSemantic,
                expectedColorSpace,
                expectedAlphaMode);
            textureLeases.Add(loadedLease);
        }
        catch
        {
            loadedLease.Dispose();
            throw;
        }
    }

    private static void ValidateTexture(
        RenderMaterialAsset.TextureBinding binding,
        RenderTextureAsset texture,
        RenderTextureSemantic expectedSemantic,
        RenderTextureColorSpace? expectedColorSpace,
        RenderTextureAlphaMode expectedAlphaMode)
    {
        if ((texture.Semantic != expectedSemantic)
            || (expectedColorSpace.HasValue && (texture.ColorSpace != expectedColorSpace.Value))
            || (texture.AlphaMode != expectedAlphaMode)
            || ((binding.Sampler.Mip != 0) && (texture.MipPolicy is RenderTextureMipPolicy.BaseOnly)))
        {
            throw new InvalidDataException("A Material texture does not satisfy its profile slot's semantic, color, alpha, or mip-sampling contract.");
        }
    }
}
