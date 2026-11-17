// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Assets;

namespace TerraFX.Rendering;

internal static class CosmeticParticleEmitterAssetBinding
{
    internal static async Task<ImmutableArray<AssetId>> ValidatePackageBindingsAsync(
        AssetLease<CosmeticParticleEmitterAsset> emitterLease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(emitterLease);
        cancellationToken.ThrowIfCancellationRequested();
        using var retainedEmitter = emitterLease.Retain();
        var emitter = retainedEmitter.Value;
        var membership = new HashSet<AssetId> { emitter.AssetId };
        using var atlasLease = await retainedEmitter.LoadFromPackageAsync<RenderAtlasAsset>(
            emitter.AtlasAssetId,
            cancellationToken).ConfigureAwait(false);
        using var pageSet = await RenderAtlasPageLoader.RetainAsync(
            atlasLease,
            cancellationToken).ConfigureAwait(false);
        var frame = FindFrame(pageSet.Atlas.Frames, emitter.FrameId);
        _ = membership.Add(emitter.AtlasAssetId);

        foreach (var pageAssetId in pageSet.Atlas.Pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = membership.Add(pageAssetId);
        }

        if (emitter.Material is CosmeticParticleEmitterAsset.MaterialReference materialReference)
        {
            if (materialReference.SchemaMajor == 1)
            {
                using var materialLease = await retainedEmitter.LoadFromPackageAsync<RenderMaterialAsset>(
                    materialReference.AssetId,
                    cancellationToken).ConfigureAwait(false);
                ValidateMaterial(frame, pageSet, materialLease.Value.IsSprite, materialLease.Value.Parameters);
            }
            else
            {
                using var materialLease = await retainedEmitter.LoadFromPackageAsync<RenderMaterialAsset2>(
                    materialReference.AssetId,
                    cancellationToken).ConfigureAwait(false);
                var material = materialLease.Value.Data;
                ValidateMaterial(frame, pageSet, material.IsSprite, material.Parameters);
            }

            _ = membership.Add(materialReference.AssetId);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var ordered = new AssetId[membership.Count];
        membership.CopyTo(ordered);
        Array.Sort(ordered, static (left, right) => left.Value.CompareTo(right.Value));
        return [.. ordered];
    }

    private static RenderAtlasFrame FindFrame(ImmutableArray<RenderAtlasFrame> frames, Guid frameId)
    {
        var low = 0;
        var high = frames.Length - 1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = FoundationCatalogue.CompareRfc(frames[middle].FrameId, frameId);

            if (comparison == 0)
            {
                return frames[middle];
            }

            if (comparison < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        throw new InvalidDataException("The cosmetic emitter FrameId is absent from its exact retained Atlas.");
    }

    private static void ValidateMaterial(
        RenderAtlasFrame frame,
        RenderAtlasPageSet pageSet,
        bool isSprite,
        RenderMaterialAsset.ParameterValues parameters)
    {
        if (!isSprite || (parameters.SpriteTexture is not { } binding))
        {
            throw new InvalidDataException("An explicit cosmetic emitter Material must select the Sprite profile and its Atlas page.");
        }

        if ((binding.UvSet != 0)
            || (binding.TextureAssetId != pageSet.Atlas.Pages[frame.Page]))
        {
            throw new InvalidDataException("A cosmetic emitter Material must sample UVSet0 from the page selected by its FrameId.");
        }

        RenderMaterialTextureLoader.ValidateSlot(
            binding,
            pageSet.GetPage(frame.Page),
            RenderTextureSemantic.Color,
            expectedColorSpace: null,
            RenderTextureAlphaMode.Premultiplied);
    }
}
