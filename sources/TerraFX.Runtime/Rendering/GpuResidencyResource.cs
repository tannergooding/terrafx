// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using TerraFX.Assets;
using TerraFX.Graphics;

namespace TerraFX.Rendering;

internal sealed class GpuResidencyResource : IDisposable
{
    private readonly AssetLease<RenderTextureAsset> _assetLease;
    private int _isDisposed;
    private readonly GpuTexture _texture;

    internal GpuResidencyResource(GpuTexture texture, AssetLease<RenderTextureAsset> assetLease)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentNullException.ThrowIfNull(assetLease);
        _assetLease = assetLease;
        _texture = texture;
    }

    internal RenderTextureAsset Asset
    {
        get
        {
            ThrowIfDisposed();
            return _assetLease.Value;
        }
    }

    internal GpuTexture Texture
    {
        get
        {
            ThrowIfDisposed();
            return _texture;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        try
        {
            _texture.Dispose();
        }
        finally
        {
            _assetLease.Dispose();
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);
}
