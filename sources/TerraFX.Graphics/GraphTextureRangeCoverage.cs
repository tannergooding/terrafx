// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;

namespace TerraFX.Graphics;

internal sealed class GraphTextureRangeCoverage
{
    private readonly List<GraphTextureRange> _ranges = [];

    private static List<GraphTextureRange> Subtract(GraphTextureRange source, GraphTextureRange removed)
    {
        if (!source.Overlaps(removed))
        {
            return [source];
        }

        var overlapFirstMip = Math.Max(source.FirstMip, removed.FirstMip);
        var overlapMipEnd = Math.Min(source.MipEndExclusive, removed.MipEndExclusive);
        var overlapFirstLayer = Math.Max(source.FirstLayer, removed.FirstLayer);
        var overlapLayerEnd = Math.Min(source.LayerEndExclusive, removed.LayerEndExclusive);
        var remaining = new List<GraphTextureRange>(4);

        if (source.FirstMip < overlapFirstMip)
        {
            remaining.Add(new GraphTextureRange(
                source.FirstMip,
                overlapFirstMip - source.FirstMip,
                source.FirstLayer,
                source.LayerCount,
                source.Aspect));
        }

        if (overlapMipEnd < source.MipEndExclusive)
        {
            remaining.Add(new GraphTextureRange(
                overlapMipEnd,
                source.MipEndExclusive - overlapMipEnd,
                source.FirstLayer,
                source.LayerCount,
                source.Aspect));
        }

        var overlapMipCount = overlapMipEnd - overlapFirstMip;

        if (source.FirstLayer < overlapFirstLayer)
        {
            remaining.Add(new GraphTextureRange(
                overlapFirstMip,
                overlapMipCount,
                source.FirstLayer,
                overlapFirstLayer - source.FirstLayer,
                source.Aspect));
        }

        if (overlapLayerEnd < source.LayerEndExclusive)
        {
            remaining.Add(new GraphTextureRange(
                overlapFirstMip,
                overlapMipCount,
                overlapLayerEnd,
                source.LayerEndExclusive - overlapLayerEnd,
                source.Aspect));
        }

        return remaining;
    }

    private static void ValidateRange(GraphTextureRange range)
    {
        if (!range.IsValid)
        {
            throw new ArgumentException("The selected graph texture range must be valid.", nameof(range));
        }
    }

    internal void Add(GraphTextureRange range)
    {
        ValidateRange(range);
        List<GraphTextureRange> remaining = [range];

        foreach (var existing in _ranges)
        {
            var next = new List<GraphTextureRange>();

            foreach (var candidate in remaining)
            {
                next.AddRange(Subtract(candidate, existing));
            }

            remaining = next;

            if (remaining.Count == 0)
            {
                return;
            }
        }

        _ranges.AddRange(remaining);
    }

    internal GraphTextureRangeCoverage Clone()
    {
        var clone = new GraphTextureRangeCoverage();
        clone._ranges.AddRange(_ranges);
        return clone;
    }

    internal bool Covers(GraphTextureRange range)
    {
        ValidateRange(range);
        List<GraphTextureRange> remaining = [range];

        foreach (var existing in _ranges)
        {
            var next = new List<GraphTextureRange>();

            foreach (var candidate in remaining)
            {
                next.AddRange(Subtract(candidate, existing));
            }

            remaining = next;

            if (remaining.Count == 0)
            {
                return true;
            }
        }

        return false;
    }

    internal void Remove(GraphTextureRange range)
    {
        ValidateRange(range);
        var remaining = new List<GraphTextureRange>();

        foreach (var existing in _ranges)
        {
            remaining.AddRange(Subtract(existing, range));
        }

        _ranges.Clear();
        _ranges.AddRange(remaining);
    }
}
