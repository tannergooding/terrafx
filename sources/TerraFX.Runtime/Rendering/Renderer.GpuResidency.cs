// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace TerraFX.Rendering;

public sealed partial class Renderer
{
    internal async Task<GpuResidencyLease> EnsureSpritePageResidencyAsync(
        RenderSnapshot snapshot,
        int spriteDrawIndex,
        ResidencyOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(options);
        ObjectDisposedException.ThrowIf(snapshot.IsDisposed, snapshot);
        ArgumentOutOfRangeException.ThrowIfNegative(spriteDrawIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(spriteDrawIndex, snapshot.SpriteDraws.Length);

        var residencyService = _gpuResidencyService
            ?? throw new NotSupportedException("Headless rendering does not upload Sprite assets.");
        var sprite = snapshot.SpriteDraws[spriteDrawIndex];
        var sceneAssets = snapshot.AssetPins?.GetSceneAssets(sprite.SceneInstanceId)
            ?? throw new InvalidOperationException("The Sprite snapshot has no retained Scene assets.");
        var pageSet = sceneAssets.GetAtlasPageSet(sprite.AtlasAssetId);
        var pageIndex = -1;

        foreach (var frame in pageSet.Atlas.Frames)
        {
            if (frame.FrameId == sprite.FrameId)
            {
                pageIndex = frame.Page;
                break;
            }
        }

        if (pageIndex < 0)
        {
            throw new InvalidOperationException("The Sprite snapshot frame is absent from its retained Atlas.");
        }

        using var pageLease = pageSet.RetainPageLease(pageIndex);
        return await residencyService.EnsureGpuResidencyAsync(
            pageLease,
            options,
            cancellationToken).ConfigureAwait(false);
    }
}
