// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace TerraFX.Graphics;

internal sealed class GraphBufferStateMap
{
    private readonly Dictionary<GraphResourceState, GraphBufferRangeCoverage> _rangesByState = [];

    internal ImmutableArray<(GraphBufferRange Range, GraphResourceState State)> GetStateSegments(GraphBufferRange range)
    {
        var segments = new List<(GraphBufferRange Range, GraphResourceState State)>();
        var undefinedRanges = new GraphBufferRangeCoverage();
        undefinedRanges.Add(range);

        foreach (var entry in _rangesByState)
        {
            foreach (var overlap in entry.Value.GetOverlappingRanges(range))
            {
                segments.Add((overlap, entry.Key));
                undefinedRanges.Remove(overlap);
            }
        }

        foreach (var undefinedRange in undefinedRanges.Ranges)
        {
            segments.Add((undefinedRange, GraphResourceState.Undefined));
        }

        segments.Sort(static (left, right) => left.Range.ByteOffset.CompareTo(right.Range.ByteOffset));
        return [.. segments];
    }

    internal void Set(GraphBufferRange range, GraphResourceState state)
    {
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        foreach (var entry in _rangesByState)
        {
            entry.Value.Remove(range);
        }

        if (state == GraphResourceState.Undefined)
        {
            return;
        }

        if (!_rangesByState.TryGetValue(state, out var ranges))
        {
            ranges = new GraphBufferRangeCoverage();
            _rangesByState.Add(state, ranges);
        }

        ranges.Add(range);
    }
}
