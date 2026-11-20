// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Assets;

namespace TerraFX.Rendering;

internal static class RenderAtlasPageLoader
{
    internal static async Task<RenderAtlasPageSet> LoadAsync(
        AssetStore assets,
        AssetId atlasAssetId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assets);
        var atlasLease = await assets.LoadAsync<RenderAtlasAsset>(
            atlasAssetId,
            cancellationToken).ConfigureAwait(false);
        return await CreateOwnedAsync(atlasLease, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<RenderAtlasPageSet> RetainAsync(
        AssetLease<RenderAtlasAsset> atlasLease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(atlasLease);
        using var progress = new OwnedProgress();
        progress.RetainAtlasLease(atlasLease);
        var result = await CreateOwnedCoreAsync(
            progress,
            RendererAssetReadMode.Ordinary,
            cancellationToken).ConfigureAwait(false);
        _ = progress.TakeResult();
        return result;
    }

    internal static async Task<RenderAtlasPageSet> RetainOwnedAsync(
        AssetLease<RenderAtlasAsset> atlasLease,
        OwnedProgress progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(atlasLease);
        ArgumentNullException.ThrowIfNull(progress);
        progress.RetainAtlasLease(atlasLease);
        return await CreateOwnedCoreAsync(
            progress,
            RendererAssetReadMode.ScenePreparation,
            cancellationToken).ConfigureAwait(false);
    }

    private static int CompareRectangles(PaddedRectangle left, PaddedRectangle right)
    {
        var comparison = left.Page.CompareTo(right.Page);

        if (comparison != 0)
        {
            return comparison;
        }

        comparison = left.Left.CompareTo(right.Left);

        if (comparison != 0)
        {
            return comparison;
        }

        comparison = left.Top.CompareTo(right.Top);

        if (comparison != 0)
        {
            return comparison;
        }

        comparison = left.Right.CompareTo(right.Right);
        return (comparison != 0) ? comparison : left.Bottom.CompareTo(right.Bottom);
    }

    private static async Task<RenderAtlasPageSet> CreateOwnedAsync(
        AssetLease<RenderAtlasAsset> atlasLease,
        CancellationToken cancellationToken)
    {
        using var progress = new OwnedProgress();
        progress.SetAtlasLease(atlasLease);
        var result = await CreateOwnedCoreAsync(
            progress,
            RendererAssetReadMode.Ordinary,
            cancellationToken).ConfigureAwait(false);
        _ = progress.TakeResult();
        return result;
    }

    private static async Task<RenderAtlasPageSet> CreateOwnedCoreAsync(
        OwnedProgress progress,
        RendererAssetReadMode readMode,
        CancellationToken cancellationToken)
    {
        var atlasLease = progress.AtlasLease
            ?? throw new InvalidOperationException("The Atlas page operation has no retained Atlas lease.");
        var pageLeaseBuilder = ImmutableArray.CreateBuilder<AssetLease<RenderTextureAsset>>(
            atlasLease.Value.Pages.Length);
        progress.PageLeases = pageLeaseBuilder;

        foreach (var pageId in atlasLease.Value.Pages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (readMode is RendererAssetReadMode.ScenePreparation)
            {
                var descriptor = RendererAssetCodecProfiles.DescribeTextureRead(atlasLease, pageId);
                progress.PendingPageLease = await atlasLease.LoadDescribedFromPackageAsync<RenderTextureAsset>(
                    descriptor,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                progress.PendingPageLease = await atlasLease.LoadFromPackageAsync<RenderTextureAsset>(
                    pageId,
                    cancellationToken).ConfigureAwait(false);
            }

            var pendingPageLease = progress.PendingPageLease
                ?? throw new InvalidOperationException("The Atlas page load returned no owned lease.");
            pageLeaseBuilder.Add(pendingPageLease);
            progress.PendingPageLease = null;
        }

        var pageLeases = pageLeaseBuilder.ToImmutable();
        progress.PageLeaseArray = pageLeases;
        progress.PageLeases = null;
        ValidatePageAssets(atlasLease.Value, pageLeases, progress);
        var result = new RenderAtlasPageSet(atlasLease, pageLeases);
        progress.Result = result;
        progress.AtlasLease = null;
        progress.PageLeaseArray = default;
        progress.ClearValidationScratch();
        return result;
    }

    private static void EnsureClampedPixel(
        ReadOnlySpan<byte> pixels,
        int pageWidth,
        int targetX,
        int targetY,
        int sourceX,
        int sourceY)
    {
        var targetOffset = checked((checked(targetY * pageWidth) + targetX) * 4);
        var sourceOffset = checked((checked(sourceY * pageWidth) + sourceX) * 4);

        if (!pixels.Slice(targetOffset, 4).SequenceEqual(pixels.Slice(sourceOffset, 4)))
        {
            throw new InvalidDataException("An Atlas frame does not have its exact clamped-edge border.");
        }
    }

    private static void ValidateClampedBorder(
        ReadOnlySpan<byte> pixels,
        int pageWidth,
        RenderAtlasFrame frame)
    {
        var padding = frame.Padding;

        for (var x = -padding; x < frame.Width + padding; x++)
        {
            var sourceX = frame.X + Math.Clamp(x, 0, frame.Width - 1);

            for (var y = -padding; y < 0; y++)
            {
                EnsureClampedPixel(
                    pixels,
                    pageWidth,
                    frame.X + x,
                    frame.Y + y,
                    sourceX,
                    frame.Y);
            }

            for (var y = frame.Height; y < frame.Height + padding; y++)
            {
                EnsureClampedPixel(
                    pixels,
                    pageWidth,
                    frame.X + x,
                    frame.Y + y,
                    sourceX,
                    frame.Y + frame.Height - 1);
            }
        }

        for (var y = 0; y < frame.Height; y++)
        {
            var targetY = frame.Y + y;

            for (var x = -padding; x < 0; x++)
            {
                EnsureClampedPixel(
                    pixels,
                    pageWidth,
                    frame.X + x,
                    targetY,
                    frame.X,
                    targetY);
            }

            for (var x = frame.Width; x < frame.Width + padding; x++)
            {
                EnsureClampedPixel(
                    pixels,
                    pageWidth,
                    frame.X + x,
                    targetY,
                    frame.X + frame.Width - 1,
                    targetY);
            }
        }
    }

    private static void ValidatePaddedFrameOverlap(
        PaddedRectangle[] rectangles,
        int pageCount,
        OwnedProgress progress)
    {
        Array.Sort(rectangles, CompareRectangles);
        var maximumTreeLazy = progress.MaximumTreeLazy = new int[RangeMaximumTree.NodeCount];
        var maximumTreeValues = progress.MaximumTreeValues = new int[RangeMaximumTree.NodeCount];
        var maximumTree = progress.MaximumTree = new RangeMaximumTree(maximumTreeLazy, maximumTreeValues);
        var active = progress.ActiveRectangles = new PriorityQueue<PaddedRectangle, (int Right, int Sequence)>();
        var rectangleIndex = 0;

        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            maximumTree.Reset();
            active.Clear();
            var sequence = 0;

            while ((rectangleIndex < rectangles.Length) && (rectangles[rectangleIndex].Page == pageIndex))
            {
                var rectangle = rectangles[rectangleIndex];

                while (active.TryPeek(out var expiredRectangle, out _)
                    && (expiredRectangle.Right <= rectangle.Left))
                {
                    _ = active.Dequeue();
                    maximumTree.Add(expiredRectangle.Top, expiredRectangle.Bottom, -1);
                }

                if (maximumTree.GetMaximum(rectangle.Top, rectangle.Bottom) != 0)
                {
                    throw new InvalidDataException("Atlas frames' padded rectangles overlap on one Texture page.");
                }

                maximumTree.Add(rectangle.Top, rectangle.Bottom, 1);
                active.Enqueue(rectangle, (rectangle.Right, sequence));
                sequence++;
                rectangleIndex++;
            }
        }

        if (rectangleIndex != rectangles.Length)
        {
            throw new InvalidDataException("An Atlas frame names a page outside the ordered page map.");
        }
    }

    private static void ValidatePageAssets(
        RenderAtlasAsset atlas,
        ImmutableArray<AssetLease<RenderTextureAsset>> pageLeases,
        OwnedProgress progress)
    {
        for (var pageIndex = 0; pageIndex < pageLeases.Length; pageIndex++)
        {
            var page = pageLeases[pageIndex].Value;

            if ((page.Width != page.Height)
                || (page.AlphaMode is not RenderTextureAlphaMode.Premultiplied)
                || (page.Semantic is not RenderTextureSemantic.Color)
                || (page.MipPolicy is not RenderTextureMipPolicy.BaseOnly)
                || (page.Mips.Length != 1))
            {
                throw new InvalidDataException("Atlas pages must be square, premultiplied BaseOnly Color textures.");
            }
        }

        var rectangles = progress.Rectangles = new PaddedRectangle[atlas.Frames.Length];

        for (var index = 0; index < atlas.Frames.Length; index++)
        {
            var frame = atlas.Frames[index];
            var page = pageLeases[frame.Page].Value;
            var left = frame.X - frame.Padding;
            var top = frame.Y - frame.Padding;
            var right = checked(frame.X + frame.Width + frame.Padding);
            var bottom = checked(frame.Y + frame.Height + frame.Padding);

            if ((left < 0) || (top < 0) || (right > page.Width) || (bottom > page.Height))
            {
                throw new InvalidDataException("An Atlas frame and its padded border exceed the actual Texture page.");
            }

            ValidateClampedBorder(page.Mips[0].Pixels.AsSpan(), page.Width, frame);
            rectangles[index] = new PaddedRectangle(frame.Page, left, top, right, bottom);
        }

        ValidatePaddedFrameOverlap(rectangles, pageLeases.Length, progress);
    }

    internal sealed class OwnedProgress : IDisposable
    {
        internal PriorityQueue<PaddedRectangle, (int Right, int Sequence)>? ActiveRectangles;
        internal AssetLease<RenderAtlasAsset>? AtlasLease;
        internal RangeMaximumTree? MaximumTree;
        internal int[]? MaximumTreeLazy;
        internal int[]? MaximumTreeValues;
        internal ImmutableArray<AssetLease<RenderTextureAsset>> PageLeaseArray;
        internal ImmutableArray<AssetLease<RenderTextureAsset>>.Builder? PageLeases;
        internal AssetLease<RenderTextureAsset>? PendingPageLease;
        internal PaddedRectangle[]? Rectangles;
        internal RenderAtlasPageSet? Result;
        private int _additionalCleanupFailureCount;
        private Exception? _disposeFailure;
        private int _disposeState;

        [SuppressMessage(
            "Usage",
            "CA1031:Do not catch general exception types",
            Justification = "Each independently owned Atlas helper value is attempted while failed references remain rooted.")]
        private static void DisposePending<T>(
            ref T? value,
            ref Exception? firstFailure,
            ref int additionalCleanupFailureCount)
            where T : class, IDisposable
        {
            if (value is not { } pending)
            {
                return;
            }

            try
            {
                pending.Dispose();
                value = null;
            }
            catch (Exception failure)
            {
                if (firstFailure is null)
                {
                    firstFailure = failure;
                }
                else
                {
                    additionalCleanupFailureCount = checked(additionalCleanupFailureCount + 1);
                }
            }
        }

        [SuppressMessage(
            "Usage",
            "CA1031:Do not catch general exception types",
            Justification = "All page, Atlas and helper-owned release operations are attempted before failure remains rooted.")]
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
                throw new InvalidOperationException("Atlas helper cleanup is already in progress.");
            }

            Exception? firstFailure = null;
            DisposePending(ref Result, ref firstFailure, ref _additionalCleanupFailureCount);
            DisposePending(ref PendingPageLease, ref firstFailure, ref _additionalCleanupFailureCount);

            if (PageLeases is { } pageLeases)
            {
                for (var index = 0; index < pageLeases.Count; index++)
                {
                    try
                    {
                        pageLeases[index].Dispose();
                    }
                    catch (Exception failure)
                    {
                        if (firstFailure is null)
                        {
                            firstFailure = failure;
                        }
                        else
                        {
                            _additionalCleanupFailureCount = checked(_additionalCleanupFailureCount + 1);
                        }
                    }
                }

                if (firstFailure is null)
                {
                    PageLeases = null;
                }
            }
            else if (!PageLeaseArray.IsDefault)
            {
                for (var index = 0; index < PageLeaseArray.Length; index++)
                {
                    try
                    {
                        PageLeaseArray[index].Dispose();
                    }
                    catch (Exception failure)
                    {
                        if (firstFailure is null)
                        {
                            firstFailure = failure;
                        }
                        else
                        {
                            _additionalCleanupFailureCount = checked(_additionalCleanupFailureCount + 1);
                        }
                    }
                }

                if (firstFailure is null)
                {
                    PageLeaseArray = default;
                }
            }

            if (AtlasLease is not null)
            {
                try
                {
                    AtlasLease.Dispose();
                    AtlasLease = null;
                }
                catch (Exception failure)
                {
                    if (firstFailure is null)
                    {
                        firstFailure = failure;
                    }
                    else
                    {
                        _additionalCleanupFailureCount = checked(_additionalCleanupFailureCount + 1);
                    }
                }
            }

            ClearValidationScratch();

            if (firstFailure is not null)
            {
                Volatile.Write(ref _disposeFailure, firstFailure);
                Volatile.Write(ref _disposeState, 3);
                RethrowDisposeFailure();
            }

            PageLeases = null;
            PageLeaseArray = default;
            Volatile.Write(ref _disposeState, 2);
        }

        internal void ClearValidationScratch()
        {
            ActiveRectangles = null;
            MaximumTree = null;
            MaximumTreeLazy = null;
            MaximumTreeValues = null;
            Rectangles = null;
        }

        internal void RetainAtlasLease(AssetLease<RenderAtlasAsset> atlasLease)
        {
            EnsureEmpty();
            AtlasLease = atlasLease.Retain();
        }

        internal void SetAtlasLease(AssetLease<RenderAtlasAsset> atlasLease)
        {
            EnsureEmpty();
            AtlasLease = atlasLease;
        }

        internal RenderAtlasPageSet TakeResult()
        {
            var result = Result
                ?? throw new InvalidOperationException("The Atlas page operation has no completed page set.");
            Result = null;
            return result;
        }

        private void EnsureEmpty()
        {
            if ((_disposeState != 0)
                || (AtlasLease is not null)
                || (PageLeases is not null)
                || !PageLeaseArray.IsDefault
                || (PendingPageLease is not null)
                || (Result is not null)
                || (ActiveRectangles is not null)
                || (MaximumTree is not null)
                || (MaximumTreeLazy is not null)
                || (MaximumTreeValues is not null)
                || (Rectangles is not null))
            {
                throw new InvalidOperationException("The Atlas page operation still owns unfinished progress.");
            }
        }

        private void RethrowDisposeFailure()
        {
            var failure = Volatile.Read(ref _disposeFailure)
                ?? new InvalidOperationException("Atlas helper cleanup failed without a retained failure.");
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    internal readonly record struct PaddedRectangle(int Page, int Left, int Top, int Right, int Bottom);

    internal sealed class RangeMaximumTree
    {
        internal const int NodeCount = 16_384;

        private readonly int[] _lazy;
        private readonly int[] _maximum;

        internal RangeMaximumTree(int[] lazy, int[] maximum)
        {
            ArgumentNullException.ThrowIfNull(lazy);
            ArgumentNullException.ThrowIfNull(maximum);

            if ((lazy.Length != NodeCount) || (maximum.Length != NodeCount))
            {
                throw new ArgumentException("The RangeMaximumTree backing arrays have the wrong size.");
            }

            _lazy = lazy;
            _maximum = maximum;
        }

        internal void Add(int start, int end, int value)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(start);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(end, 4096);

            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(start, end);

            AddRange(1, 0, 4096, start, end, value);
        }

        internal int GetMaximum(int start, int end)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(start);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(end, 4096);

            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(start, end);

            return GetMaximumRange(1, 0, 4096, start, end);
        }

        internal void Reset()
        {
            Array.Clear(_lazy);
            Array.Clear(_maximum);
        }

        private void AddRange(int node, int left, int right, int start, int end, int value)
        {
            if ((start <= left) && (right <= end))
            {
                _maximum[node] += value;
                _lazy[node] += value;
                return;
            }

            Push(node);
            var middle = left + ((right - left) / 2);

            if (start < middle)
            {
                AddRange(node * 2, left, middle, start, end, value);
            }

            if (end > middle)
            {
                AddRange((node * 2) + 1, middle, right, start, end, value);
            }

            _maximum[node] = Math.Max(_maximum[node * 2], _maximum[(node * 2) + 1]);
        }

        private int GetMaximumRange(int node, int left, int right, int start, int end)
        {
            if ((start <= left) && (right <= end))
            {
                return _maximum[node];
            }

            Push(node);
            var middle = left + ((right - left) / 2);
            var maximum = 0;

            if (start < middle)
            {
                maximum = GetMaximumRange(node * 2, left, middle, start, end);
            }

            if (end > middle)
            {
                maximum = Math.Max(maximum, GetMaximumRange((node * 2) + 1, middle, right, start, end));
            }

            return maximum;
        }

        private void Push(int node)
        {
            var value = _lazy[node];

            if (value == 0)
            {
                return;
            }

            var left = node * 2;
            var right = left + 1;
            _maximum[left] += value;
            _maximum[right] += value;
            _lazy[left] += value;
            _lazy[right] += value;
            _lazy[node] = 0;
        }
    }
}
