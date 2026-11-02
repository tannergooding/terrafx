// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;

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
