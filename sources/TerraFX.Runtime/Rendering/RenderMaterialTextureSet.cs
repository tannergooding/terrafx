// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;
using TerraFX.Assets;

namespace TerraFX.Rendering;

internal sealed class RenderMaterialTextureSet : IDisposable
{
    private int _isDisposed;
    private readonly AssetLease<RenderMaterialAsset> _materialLease;
    private readonly ImmutableArray<AssetLease<RenderTextureAsset>> _textureLeases;

    internal RenderMaterialTextureSet(
        AssetLease<RenderMaterialAsset> materialLease,
        ImmutableArray<AssetLease<RenderTextureAsset>> textureLeases)
    {
        ArgumentNullException.ThrowIfNull(materialLease);

        if (textureLeases.IsDefault)
        {
            throw new ArgumentException("The Material texture leases must be initialized.", nameof(textureLeases));
        }

        _materialLease = materialLease;
        _textureLeases = textureLeases;
    }

    internal RenderMaterialAsset Material
    {
        get
        {
            ThrowIfDisposed();
            return _materialLease.Value;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        foreach (var textureLease in _textureLeases)
        {
            textureLease.Dispose();
        }

        _materialLease.Dispose();
    }

    internal RenderTextureAsset GetTexture(AssetId assetId)
    {
        ThrowIfDisposed();

        foreach (var textureLease in _textureLeases)
        {
            if (textureLease.AssetId == assetId)
            {
                return textureLease.Value;
            }
        }

        throw new ArgumentException("The Texture is not retained by this Material texture set.", nameof(assetId));
    }

    internal RenderMaterialTextureSet Retain()
    {
        ThrowIfDisposed();
        var materialLease = _materialLease.Retain();
        var textureLeases = ImmutableArray.CreateBuilder<AssetLease<RenderTextureAsset>>(_textureLeases.Length);

        try
        {
            foreach (var textureLease in _textureLeases)
            {
                textureLeases.Add(textureLease.Retain());
            }

            return new RenderMaterialTextureSet(materialLease, textureLeases.MoveToImmutable());
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

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);
}
