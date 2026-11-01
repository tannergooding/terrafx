// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX.Assets;

namespace TerraFX.Rendering;

internal sealed class RenderAtlasAsset
{
    internal RenderAtlasAsset(
        ImmutableArray<AssetId> pages,
        ImmutableArray<RenderAtlasFrame> frames)
    {
        if (pages.IsDefaultOrEmpty || (pages.Length > 16))
        {
            throw new ArgumentOutOfRangeException(nameof(pages));
        }

        if (frames.IsDefaultOrEmpty)
        {
            throw new ArgumentException("An Atlas asset requires an initialized frame map.", nameof(frames));
        }

        Pages = pages;
        Frames = frames;
    }

    internal ImmutableArray<RenderAtlasFrame> Frames { get; }

    internal ImmutableArray<AssetId> Pages { get; }
}
