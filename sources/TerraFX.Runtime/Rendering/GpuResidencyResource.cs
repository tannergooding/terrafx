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
    private int _ownerReleased;
    private int _referenceCount = 1;
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
        if (Interlocked.Exchange(ref _ownerReleased, 1) != 0)
        {
            return;
        }

        ReleaseReference();
    }

    internal void ReleaseLease()
        => ReleaseReference();

    internal GpuResidencyResourceLease Retain()
    {
        while (true)
        {
            var referenceCount = Volatile.Read(ref _referenceCount);
            ObjectDisposedException.ThrowIf(referenceCount == 0, this);

            if (referenceCount == int.MaxValue)
            {
                throw new InvalidOperationException("The GPU residency resource has too many live leases.");
            }

            if (Interlocked.CompareExchange(ref _referenceCount, referenceCount + 1, referenceCount) == referenceCount)
            {
                try
                {
                    return new GpuResidencyResourceLease(this);
                }
                catch
                {
                    ReleaseReference();
                    throw;
                }
            }
        }
    }

    private void ReleaseReference()
    {
        var referenceCount = Interlocked.Decrement(ref _referenceCount);

        if (referenceCount < 0)
        {
            throw new InvalidOperationException("The GPU residency resource reference count became negative.");
        }

        if (referenceCount != 0)
        {
            return;
        }

        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            throw new InvalidOperationException("The GPU residency resource was disposed more than once.");
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
