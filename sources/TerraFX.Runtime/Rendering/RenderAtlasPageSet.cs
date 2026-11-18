// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Threading;
using TerraFX;
using TerraFX.Assets;

namespace TerraFX.Rendering;

internal sealed class RenderAtlasPageSet : IDisposable
{
    private AssetLease<RenderAtlasAsset>? _atlasLease;
    private bool _atlasLeaseReleaseFailed;
    private Exception? _disposeFailure;
    private int _disposeState;
    private uint _pageLeaseFailureMask;
    private ImmutableArray<AssetLease<RenderTextureAsset>> _pageLeases;

    internal RenderAtlasPageSet(
        AssetLease<RenderAtlasAsset> atlasLease,
        ImmutableArray<AssetLease<RenderTextureAsset>> pageLeases)
    {
        ArgumentNullException.ThrowIfNull(atlasLease);

        if (pageLeases.IsDefaultOrEmpty
            || (pageLeases.Length > 16)
            || (pageLeases.Length != atlasLease.Value.Pages.Length))
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
            return (_atlasLease ?? throw new ObjectDisposedException(nameof(RenderAtlasPageSet))).Value;
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

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Every independently owned page and the Atlas lease are attempted before failure is retained.")]
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
            throw new InvalidOperationException("Atlas page-set cleanup is already in progress.");
        }

        Exception? firstFailure = null;
        uint pageLeaseFailureMask = 0;

        for (var index = 0; index < _pageLeases.Length; index++)
        {
            try
            {
                _pageLeases[index].Dispose();
            }
            catch (Exception failure)
            {
                firstFailure ??= failure;
                pageLeaseFailureMask |= 1U << index;
            }
        }

        var atlasLease = _atlasLease;

        try
        {
            atlasLease?.Dispose();
        }
        catch (Exception failure)
        {
            firstFailure ??= failure;
            _atlasLeaseReleaseFailed = true;
        }

        if (firstFailure is not null)
        {
            _pageLeaseFailureMask = pageLeaseFailureMask;
            Volatile.Write(ref _disposeFailure, firstFailure);
            Volatile.Write(ref _disposeState, 3);
            RethrowDisposeFailure();
        }

        _pageLeases = default;
        _atlasLease = null;
        Volatile.Write(ref _disposeState, 2);
    }

    internal RenderAtlasFrame GetFrame(Guid frameId)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfEqual(frameId, Guid.Empty);
        var frames = (_atlasLease ?? throw new ObjectDisposedException(nameof(RenderAtlasPageSet))).Value.Frames;
        var low = 0;
        var high = frames.Length - 1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = FoundationCatalogue.CompareRfc(frames[middle].FrameId, frameId);

            if (comparison == 0)
            {
                return frames[middle];
            }

            if (comparison < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        throw new ArgumentException("The Atlas does not contain the requested Sprite frame.", nameof(frameId));
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
        var atlasLease = (_atlasLease ?? throw new ObjectDisposedException(nameof(RenderAtlasPageSet))).Retain();
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

    internal AssetLease<RenderTextureAsset> RetainPageLease(int pageIndex)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, _pageLeases.Length);
        return _pageLeases[pageIndex].Retain();
    }

    private void RethrowDisposeFailure()
    {
        if ((_pageLeaseFailureMask == 0) && !_atlasLeaseReleaseFailed)
        {
            throw new InvalidOperationException("Atlas page-set cleanup has no recorded failed child release.");
        }

        var failure = Volatile.Read(ref _disposeFailure)
            ?? new InvalidOperationException("Atlas page-set cleanup failed without a retained failure.");
        ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
}
