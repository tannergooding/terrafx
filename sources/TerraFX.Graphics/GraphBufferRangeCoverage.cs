// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;

namespace TerraFX.Graphics;

internal sealed class GraphBufferRangeCoverage
{
    private readonly List<GraphBufferRange> _ranges = [];

    private static void ValidateRange(GraphBufferRange range)
    {
        if (!range.IsValid)
        {
            throw new ArgumentException("The selected graph buffer range must be valid.", nameof(range));
        }
    }

    internal void Add(GraphBufferRange range)
    {
        ValidateRange(range);
        _ranges.Add(range);
        _ranges.Sort(static (left, right) => left.ByteOffset.CompareTo(right.ByteOffset));

        var merged = new List<GraphBufferRange>(_ranges.Count);
        var start = 0L;
        var end = 0L;
        var hasCurrent = false;

        foreach (var current in _ranges)
        {
            if (!hasCurrent)
            {
                start = current.ByteOffset;
                end = current.EndExclusive;
                hasCurrent = true;
            }
            else if (current.ByteOffset <= end)
            {
                end = Math.Max(end, current.EndExclusive);
            }
            else
            {
                merged.Add(new GraphBufferRange(start, end - start));
                start = current.ByteOffset;
                end = current.EndExclusive;
            }
        }

        if (hasCurrent)
        {
            merged.Add(new GraphBufferRange(start, end - start));
        }

        _ranges.Clear();
        _ranges.AddRange(merged);
    }

    internal GraphBufferRangeCoverage Clone()
    {
        var clone = new GraphBufferRangeCoverage();
        clone._ranges.AddRange(_ranges);
        return clone;
    }

    internal bool Covers(GraphBufferRange range)
    {
        ValidateRange(range);
        var end = range.EndExclusive;
        var cursor = range.ByteOffset;

        foreach (var current in _ranges)
        {
            if (current.EndExclusive <= cursor)
            {
                continue;
            }

            if (current.ByteOffset > cursor)
            {
                return false;
            }

            cursor = current.EndExclusive;

            if (cursor >= end)
            {
                return true;
            }
        }

        return false;
    }

    internal void Remove(GraphBufferRange range)
    {
        ValidateRange(range);
        var remaining = new List<GraphBufferRange>(_ranges.Count + 1);
        var rangeEnd = range.EndExclusive;

        foreach (var current in _ranges)
        {
            if (!current.Overlaps(range))
            {
                remaining.Add(current);
                continue;
            }

            if (current.ByteOffset < range.ByteOffset)
            {
                remaining.Add(new GraphBufferRange(
                    current.ByteOffset,
                    range.ByteOffset - current.ByteOffset));
            }

            if (rangeEnd < current.EndExclusive)
            {
                remaining.Add(new GraphBufferRange(
                    rangeEnd,
                    current.EndExclusive - rangeEnd));
            }
        }

        _ranges.Clear();
        _ranges.AddRange(remaining);
    }
}
