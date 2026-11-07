// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Graphics;

internal sealed class GraphicsTransientTextureGroup : IDisposable
{
    internal GraphicsTransientTextureGroup(ImmutableArray<GpuTexture> textures)
    {
        if (textures.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A materialized transient-texture group requires at least one texture.", nameof(textures));
        }

        Textures = [.. textures];
    }

    internal ImmutableArray<GpuTexture> Textures { get; }

    public void Dispose()
    {
        foreach (var texture in Textures)
        {
            texture.Dispose();
        }
    }
}
