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
        using var progress = new OwnedProgress();
        progress.RetainMaterialLease(materialLease);
        var result = await CreateOwnedCoreAsync(progress, cancellationToken).ConfigureAwait(false);
        _ = progress.TakeResult();
        return result;
    }

    internal static async Task<RenderMaterialTextureSet> RetainOwnedAsync(
        AssetLease<RenderMaterialAsset> materialLease,
        OwnedProgress progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(materialLease);
        ArgumentNullException.ThrowIfNull(progress);
        progress.RetainMaterialLease(materialLease);
        return await CreateOwnedCoreAsync(progress, cancellationToken).ConfigureAwait(false);
    }

    internal static void ValidateSlot(
        RenderMaterialAsset.TextureBinding binding,
        RenderTextureAsset texture,
        RenderTextureSemantic expectedSemantic,
        RenderTextureColorSpace? expectedColorSpace,
        RenderTextureAlphaMode expectedAlphaMode)
        => ValidateTexture(binding, texture, expectedSemantic, expectedColorSpace, expectedAlphaMode);

    private static async Task<RenderMaterialTextureSet> CreateOwnedAsync(
        AssetLease<RenderMaterialAsset> materialLease,
        CancellationToken cancellationToken)
    {
        using var progress = new OwnedProgress();
        progress.SetMaterialLease(materialLease);
        var result = await CreateOwnedCoreAsync(progress, cancellationToken).ConfigureAwait(false);
        _ = progress.TakeResult();
        return result;
    }

    private static async Task<RenderMaterialTextureSet> CreateOwnedCoreAsync(
        OwnedProgress progress,
        CancellationToken cancellationToken)
    {
        var materialLease = progress.MaterialLease
            ?? throw new InvalidOperationException("The Material texture operation has no retained Material lease.");
        var material = materialLease.Value;
        var textureLeaseBuilder = ImmutableArray.CreateBuilder<AssetLease<RenderTextureAsset>>(
            material.IsSprite ? 1 : 4);
        progress.TextureLeases = textureLeaseBuilder;

        if (material.IsSprite)
        {
            if (material.Parameters.SpriteTexture is { } spriteTexture)
            {
                await LoadTextureAsync(
                    materialLease,
                    progress,
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
                    progress,
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
                    progress,
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
                    progress,
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
                    progress,
                    emissiveTexture,
                    RenderTextureSemantic.Color,
                    RenderTextureColorSpace.Srgb,
                    RenderTextureAlphaMode.Straight,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        var textureLeases = textureLeaseBuilder.ToImmutable();
        progress.TextureLeaseArray = textureLeases;
        progress.TextureLeases = null;
        var result = new RenderMaterialTextureSet(materialLease, textureLeases);
        progress.Result = result;
        progress.MaterialLease = null;
        progress.TextureLeaseArray = default;
        return result;
    }

    private static async Task LoadTextureAsync(
        AssetLease<RenderMaterialAsset> materialLease,
        OwnedProgress progress,
        RenderMaterialAsset.TextureBinding binding,
        RenderTextureSemantic expectedSemantic,
        RenderTextureColorSpace? expectedColorSpace,
        RenderTextureAlphaMode expectedAlphaMode,
        CancellationToken cancellationToken)
    {
        var textureLeases = progress.TextureLeases
            ?? throw new InvalidOperationException("The Material texture operation has no texture lease builder.");

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

        progress.PendingTextureLease = await materialLease.LoadFromPackageAsync<RenderTextureAsset>(
            binding.TextureAssetId,
            cancellationToken).ConfigureAwait(false);
        var pendingTextureLease = progress.PendingTextureLease
            ?? throw new InvalidOperationException("The Material texture load returned no owned lease.");
        ValidateTexture(
            binding,
            pendingTextureLease.Value,
            expectedSemantic,
            expectedColorSpace,
            expectedAlphaMode);
        textureLeases.Add(pendingTextureLease);
        progress.PendingTextureLease = null;
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

    internal sealed class OwnedProgress : IDisposable
    {
        internal AssetLease<RenderMaterialAsset>? MaterialLease;
        internal AssetLease<RenderTextureAsset>? PendingTextureLease;
        internal RenderMaterialTextureSet? Result;
        internal ImmutableArray<AssetLease<RenderTextureAsset>> TextureLeaseArray;
        internal ImmutableArray<AssetLease<RenderTextureAsset>>.Builder? TextureLeases;

        private static void DisposePending<T>(ref T? value)
            where T : class, IDisposable
        {
            var pending = value;

            if (pending is null)
            {
                return;
            }

            pending.Dispose();
            value = null;
        }

        public void Dispose()
        {
            try
            {
                DisposePending(ref Result);
            }
            finally
            {
                try
                {
                    DisposePending(ref PendingTextureLease);
                }
                finally
                {
                    try
                    {
                        if (TextureLeases is { } textureLeases)
                        {
                            foreach (var textureLease in textureLeases)
                            {
                                textureLease.Dispose();
                            }

                            TextureLeases = null;
                        }
                        else if (!TextureLeaseArray.IsDefault)
                        {
                            foreach (var textureLease in TextureLeaseArray)
                            {
                                textureLease.Dispose();
                            }

                            TextureLeaseArray = default;
                        }
                    }
                    finally
                    {
                        var materialLease = MaterialLease;
                        materialLease?.Dispose();

                        if (materialLease is not null)
                        {
                            MaterialLease = null;
                        }
                    }
                }
            }
        }

        internal void RetainMaterialLease(AssetLease<RenderMaterialAsset> materialLease)
        {
            EnsureEmpty();
            MaterialLease = materialLease.Retain();
        }

        internal void SetMaterialLease(AssetLease<RenderMaterialAsset> materialLease)
        {
            EnsureEmpty();
            MaterialLease = materialLease;
        }

        internal RenderMaterialTextureSet TakeResult()
        {
            var result = Result
                ?? throw new InvalidOperationException("The Material texture operation has no completed texture set.");
            Result = null;
            return result;
        }

        private void EnsureEmpty()
        {
            if ((MaterialLease is not null)
                || (PendingTextureLease is not null)
                || (Result is not null)
                || !TextureLeaseArray.IsDefault
                || (TextureLeases is not null))
            {
                throw new InvalidOperationException("The Material texture operation still owns unfinished progress.");
            }
        }
    }
}
