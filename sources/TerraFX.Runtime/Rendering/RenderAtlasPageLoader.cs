// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
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
        var atlasLease = await assets.LoadAsync<RenderAtlasAsset>(atlasAssetId, cancellationToken).ConfigureAwait(false);
        return await CreateOwnedAsync(atlasLease, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<RenderAtlasPageSet> RetainAsync(
        AssetLease<RenderAtlasAsset> atlasLease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(atlasLease);
        var retainedAtlasLease = atlasLease.Retain();
        return await CreateOwnedAsync(retainedAtlasLease, cancellationToken).ConfigureAwait(false);
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
        var pageLeases = ImmutableArray.CreateBuilder<AssetLease<RenderTextureAsset>>(atlasLease.Value.Pages.Length);

        try
        {
            foreach (var pageId in atlasLease.Value.Pages)
            {
                var pageLease = await atlasLease.LoadFromPackageAsync<RenderTextureAsset>(
                    pageId,
                    cancellationToken).ConfigureAwait(false);
                pageLeases.Add(pageLease);
            }

            var pages = pageLeases.ToImmutable();
            ValidatePageAssets(atlasLease.Value, pages);
            return new RenderAtlasPageSet(atlasLease, pages);
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

    private static void ValidatePaddedFrameOverlap(PaddedRectangle[] rectangles, int pageCount)
    {
        Array.Sort(rectangles, CompareRectangles);
        var maximumTree = new RangeMaximumTree();
        var rectangleIndex = 0;

        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            maximumTree.Reset();
            var active = new PriorityQueue<PaddedRectangle, (int Right, int Sequence)>();
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
        ImmutableArray<AssetLease<RenderTextureAsset>> pageLeases)
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

        var rectangles = new PaddedRectangle[atlas.Frames.Length];

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

        ValidatePaddedFrameOverlap(rectangles, pageLeases.Length);
    }

    private readonly record struct PaddedRectangle(int Page, int Left, int Top, int Right, int Bottom);

    private sealed class RangeMaximumTree
    {
        private readonly int[] _lazy;
        private readonly int[] _maximum;

        internal RangeMaximumTree()
        {
            _lazy = new int[16_384];
            _maximum = new int[16_384];
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
