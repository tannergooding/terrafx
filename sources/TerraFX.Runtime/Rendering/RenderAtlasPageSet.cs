// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;
using TerraFX.Assets;

namespace TerraFX.Rendering;

internal sealed class RenderAtlasPageSet : IDisposable
{
    private readonly AssetLease<RenderAtlasAsset> _atlasLease;
    private int _isDisposed;
    private readonly ImmutableArray<AssetLease<RenderTextureAsset>> _pageLeases;

    internal RenderAtlasPageSet(
        AssetLease<RenderAtlasAsset> atlasLease,
        ImmutableArray<AssetLease<RenderTextureAsset>> pageLeases)
    {
        ArgumentNullException.ThrowIfNull(atlasLease);

        if (pageLeases.IsDefaultOrEmpty || (pageLeases.Length != atlasLease.Value.Pages.Length))
        {
            throw new ArgumentException("The Atlas page leases must align with its ordered page map.", nameof(pageLeases));
        }

        _atlasLease = atlasLease;
        _pageLeases = pageLeases;
    }

    internal RenderAtlasAsset Atlas
    {
        get
        {
            ThrowIfDisposed();
            return _atlasLease.Value;
        }
    }

    internal int PageCount
    {
        get
        {
            ThrowIfDisposed();
            return _pageLeases.Length;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        foreach (var pageLease in _pageLeases)
        {
            pageLease.Dispose();
        }

        _atlasLease.Dispose();
    }

    internal RenderTextureAsset GetPage(int pageIndex)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, _pageLeases.Length);
        return _pageLeases[pageIndex].Value;
    }

    internal RenderAtlasPageSet Retain()
    {
        ThrowIfDisposed();
        var atlasLease = _atlasLease.Retain();
        var pageLeases = ImmutableArray.CreateBuilder<AssetLease<RenderTextureAsset>>(_pageLeases.Length);

        try
        {
            foreach (var pageLease in _pageLeases)
            {
                pageLeases.Add(pageLease.Retain());
            }

            return new RenderAtlasPageSet(atlasLease, pageLeases.ToImmutable());
        }
        catch
        {
            foreach (var pageLease in pageLeases)
            {
                pageLease.Dispose();
            }

            atlasLease.Dispose();
            throw;
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);
}
