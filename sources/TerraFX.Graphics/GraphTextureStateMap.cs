// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace TerraFX.Graphics;

internal sealed class GraphTextureStateMap
{
    private readonly Dictionary<GraphResourceState, GraphTextureRangeCoverage> _rangesByState = [];

    internal GraphResourceState GetUniformState(GraphTextureRange range)
    {
        if (!range.IsValid)
        {
            throw new ArgumentException("The selected graph texture range must be valid.", nameof(range));
        }

        GraphResourceState? overlappingState = null;

        foreach (var entry in _rangesByState)
        {
            if (entry.Value.Covers(range))
            {
                return entry.Key;
            }

            if (entry.Value.Overlaps(range))
            {
                if ((overlappingState is GraphResourceState selectedState) && (selectedState != entry.Key))
                {
                    throw new InvalidOperationException("The texture range spans multiple tracked graph resource states.");
                }

                overlappingState = entry.Key;
            }
        }

        if (overlappingState.HasValue)
        {
            throw new InvalidOperationException("The texture range does not have one uniform tracked graph resource state.");
        }

        return GraphResourceState.Undefined;
    }

    internal ImmutableArray<(GraphTextureRange Range, GraphResourceState State)> GetUniformStateSegments(GraphTextureRange range)
    {
        if (!range.IsValid)
        {
            throw new ArgumentException("The selected graph texture range must be valid.", nameof(range));
        }

        var segments = new List<(GraphTextureRange Range, GraphResourceState State)>();
        var undefinedRanges = new GraphTextureRangeCoverage();
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

        segments.Sort(static (left, right) => {
            var comparison = left.Range.FirstMip.CompareTo(right.Range.FirstMip);

            if (comparison == 0)
            {
                comparison = left.Range.FirstLayer.CompareTo(right.Range.FirstLayer);
            }

            if (comparison == 0)
            {
                comparison = left.Range.MipCount.CompareTo(right.Range.MipCount);
            }

            if (comparison == 0)
            {
                comparison = left.Range.LayerCount.CompareTo(right.Range.LayerCount);
            }

            return (comparison != 0)
                ? comparison
                : left.State.CompareTo(right.State);
        });
        return [.. segments];
    }

    internal void Set(GraphTextureRange range, GraphResourceState state)
    {
        if (!range.IsValid)
        {
            throw new ArgumentException("The selected graph texture range must be valid.", nameof(range));
        }

        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        var emptyStates = new List<GraphResourceState>();

        foreach (var entry in _rangesByState)
        {
            entry.Value.Remove(range);

            if (entry.Value.IsEmpty)
            {
                emptyStates.Add(entry.Key);
            }
        }

        foreach (var emptyState in emptyStates)
        {
            _ = _rangesByState.Remove(emptyState);
        }

        if (state == GraphResourceState.Undefined)
        {
            return;
        }

        if (!_rangesByState.TryGetValue(state, out var ranges))
        {
            ranges = new GraphTextureRangeCoverage();
            _rangesByState.Add(state, ranges);
        }

        ranges.Add(range);
    }
}
