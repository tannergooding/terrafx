// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Rendering;

internal sealed class RenderTextureAsset
{
    internal RenderTextureAsset(
        int width,
        int height,
        byte format,
        RenderTextureColorSpace colorSpace,
        RenderTextureAlphaMode alphaMode,
        RenderTextureSemantic semantic,
        RenderTextureMipPolicy mipPolicy,
        ImmutableArray<RenderTextureMip> mips)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (mips.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A texture asset requires an initialized mip chain.", nameof(mips));
        }

        Width = width;
        Height = height;
        Format = format;
        ColorSpace = colorSpace;
        AlphaMode = alphaMode;
        Semantic = semantic;
        MipPolicy = mipPolicy;
        Mips = mips;
    }

    internal RenderTextureAlphaMode AlphaMode { get; }

    internal RenderTextureColorSpace ColorSpace { get; }

    internal byte Format { get; }

    internal int Height { get; }

    internal RenderTextureMipPolicy MipPolicy { get; }

    internal ImmutableArray<RenderTextureMip> Mips { get; }

    internal RenderTextureSemantic Semantic { get; }

    internal int Width { get; }
}
