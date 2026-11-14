// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX.Graphics;

namespace TerraFX.Rendering;

internal sealed class RenderSpriteGraphState
{
    internal RenderSpriteGraphState(
        TextureFormat spriteColorTargetFormat,
        TextureFormat toneMapColorTargetFormat,
        ImmutableArray<RenderSpriteGraphBufferUpload> bufferUploads,
        ImmutableArray<RenderSpriteGraphDeferredTexture> deferredTextures,
        ImmutableDictionary<RenderPassId, RenderSpriteC0GraphPass> passes,
        ImmutableDictionary<RenderPassId, RenderSpriteToneMapGraphPass> toneMapPasses)
    {
        if (!Enum.IsDefined(spriteColorTargetFormat) || !Enum.IsDefined(toneMapColorTargetFormat))
        {
            throw new ArgumentOutOfRangeException(nameof(spriteColorTargetFormat));
        }

        if (bufferUploads.IsDefault || deferredTextures.IsDefault || passes is null || toneMapPasses is null)
        {
            throw new ArgumentException("Built-in Sprite graph state must be initialized.");
        }

        SpriteColorTargetFormat = spriteColorTargetFormat;
        ToneMapColorTargetFormat = toneMapColorTargetFormat;
        BufferUploads = bufferUploads;
        DeferredTextures = deferredTextures;
        Passes = passes;
        ToneMapPasses = toneMapPasses;
    }

    internal ImmutableArray<RenderSpriteGraphBufferUpload> BufferUploads { get; }

    internal ImmutableArray<RenderSpriteGraphDeferredTexture> DeferredTextures { get; }

    internal ImmutableDictionary<RenderPassId, RenderSpriteC0GraphPass> Passes { get; }

    internal TextureFormat SpriteColorTargetFormat { get; }

    internal TextureFormat ToneMapColorTargetFormat { get; }

    internal ImmutableDictionary<RenderPassId, RenderSpriteToneMapGraphPass> ToneMapPasses { get; }
}
