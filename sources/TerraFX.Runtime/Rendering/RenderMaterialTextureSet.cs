// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Threading;
using TerraFX.Assets;

namespace TerraFX.Rendering;

internal sealed class RenderMaterialTextureSet : IDisposable
{
    private Exception? _disposeFailure;
    private int _disposeState;
    private AssetLease<RenderMaterialAsset>? _materialLease;
    private bool _materialLeaseReleaseFailed;
    private int _textureLeaseFailureMask;
    private ImmutableArray<AssetLease<RenderTextureAsset>> _textureLeases;

    internal RenderMaterialTextureSet(
        AssetLease<RenderMaterialAsset> materialLease,
        ImmutableArray<AssetLease<RenderTextureAsset>> textureLeases)
    {
        ArgumentNullException.ThrowIfNull(materialLease);

        if (textureLeases.IsDefault || (textureLeases.Length > 4))
        {
            throw new ArgumentException("The Material texture leases must be initialized and within the profile slot bound.", nameof(textureLeases));
        }

        _materialLease = materialLease;
        _textureLeases = textureLeases;
    }

    internal RenderMaterialAsset Material
    {
        get
        {
            ThrowIfDisposed();
            return (_materialLease ?? throw new ObjectDisposedException(nameof(RenderMaterialTextureSet))).Value;
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Every independently owned Texture and the Material lease are attempted before failure is retained.")]
    public void Dispose()
    {
        var state = Interlocked.CompareExchange(ref _disposeState, 1, 0);

        if (state is 2)
        {
            return;
        }

        if (state is 3)
        {
            RethrowDisposeFailure();
        }

        if (state is not 0)
        {
            throw new InvalidOperationException("Material texture-set cleanup is already in progress.");
        }

        Exception? firstFailure = null;
        var textureLeaseFailureMask = 0;

        for (var index = 0; index < _textureLeases.Length; index++)
        {
            try
            {
                _textureLeases[index].Dispose();
            }
            catch (Exception failure)
            {
                firstFailure ??= failure;
                textureLeaseFailureMask |= 1 << index;
            }
        }

        var materialLease = _materialLease;

        try
        {
            materialLease?.Dispose();
        }
        catch (Exception failure)
        {
            firstFailure ??= failure;
            _materialLeaseReleaseFailed = true;
        }

        if (firstFailure is not null)
        {
            _textureLeaseFailureMask = textureLeaseFailureMask;
            Volatile.Write(ref _disposeFailure, firstFailure);
            Volatile.Write(ref _disposeState, 3);
            RethrowDisposeFailure();
        }

        _textureLeases = default;
        _materialLease = null;
        Volatile.Write(ref _disposeState, 2);
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
        var materialLease = (_materialLease ?? throw new ObjectDisposedException(nameof(RenderMaterialTextureSet))).Retain();
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

    internal AssetLease<RenderTextureAsset> RetainTexture(AssetId assetId)
    {
        ThrowIfDisposed();

        foreach (var textureLease in _textureLeases)
        {
            if (textureLease.AssetId == assetId)
            {
                return textureLease.Retain();
            }
        }

        throw new ArgumentException("The Texture is not retained by this Material texture set.", nameof(assetId));
    }

    private void RethrowDisposeFailure()
    {
        if ((_textureLeaseFailureMask == 0) && !_materialLeaseReleaseFailed)
        {
            throw new InvalidOperationException("Material texture-set cleanup has no recorded failed child release.");
        }

        var failure = Volatile.Read(ref _disposeFailure)
            ?? new InvalidOperationException("Material texture-set cleanup failed without a retained failure.");
        ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
}
