// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using TerraFX.Assets;

namespace TerraFX.Rendering;

internal static class RenderSpriteBatcher
{
    internal static ImmutableArray<RenderSpriteBatch> Create(RenderSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ObjectDisposedException.ThrowIf(snapshot.IsDisposed, snapshot);

        if (snapshot.SpriteDraws.IsEmpty)
        {
            return [];
        }

        using var assetPins = snapshot.RetainAssetPins()
            ?? throw new InvalidOperationException("The Sprite snapshot has no retained Scene assets.");
        var batches = ImmutableArray.CreateBuilder<RenderSpriteBatch>();

        foreach (var cameraOrder in snapshot.SpriteCameraOrders)
        {
            var cameraKeys = new RenderSpriteBatchKey[cameraOrder.DrawIndices.Length];

            for (var index = 0; index < cameraOrder.DrawIndices.Length; index++)
            {
                var drawIndex = cameraOrder.DrawIndices[index];
                var sprite = snapshot.SpriteDraws[drawIndex];
                var sceneAssets = assetPins.GetSceneAssets(sprite.SceneInstanceId);
                var atlas = sceneAssets.GetAtlasPageSet(sprite.AtlasAssetId);
                var frame = atlas.GetFrame(sprite.FrameId);
                var pageAssetId = atlas.Atlas.Pages[frame.Page];

                if (!pageAssetId.IsValid)
                {
                    throw new InvalidOperationException("The Sprite Atlas page has no valid AssetId.");
                }

                if (sprite.MaterialAssetId is int materialAssetId)
                {
                    _ = sceneAssets.GetMaterial(materialAssetId);
                }

                cameraKeys[index] = new RenderSpriteBatchKey(
                    sprite.SceneInstanceId,
                    pageAssetId.Value,
                    sprite.MaterialAssetId);
            }

            GroupAdjacent(cameraOrder.CameraId, cameraOrder.DrawIndices, cameraKeys, batches);
        }

        return batches.ToImmutable();
    }

    internal static void GroupAdjacent(
        Guid cameraId,
        ImmutableArray<int> drawIndices,
        ReadOnlySpan<RenderSpriteBatchKey> keys,
        ImmutableArray<RenderSpriteBatch>.Builder batches)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(cameraId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(batches);

        if (drawIndices.IsDefault || (drawIndices.Length != keys.Length))
        {
            throw new ArgumentException("Sprite batch keys must align with the camera's ordered draw indices.", nameof(keys));
        }

        var selectedDrawIndices = ImmutableArray.CreateBuilder<int>();
        RenderSpriteBatchKey? selectedKey = null;

        for (var index = 0; index < drawIndices.Length; index++)
        {
            var key = keys[index];

            if (selectedKey is RenderSpriteBatchKey previousKey && (previousKey != key))
            {
                batches.Add(new RenderSpriteBatch(cameraId, previousKey, selectedDrawIndices.ToImmutable()));
                selectedDrawIndices.Clear();
            }

            selectedKey = key;
            selectedDrawIndices.Add(drawIndices[index]);
        }

        if (selectedKey is RenderSpriteBatchKey lastKey)
        {
            batches.Add(new RenderSpriteBatch(cameraId, lastKey, selectedDrawIndices.ToImmutable()));
        }
    }
}
