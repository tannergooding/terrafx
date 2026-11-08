// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace TerraFX.Platform;

internal sealed class WaylandKeyboardRepeatSchedule
{
    private readonly SortedDictionary<uint, long> _nextRepeatTimestamps;
    private long _repeatDelayTicks;
    private long _repeatIntervalTicks;
    private readonly long _timestampFrequency;

    internal WaylandKeyboardRepeatSchedule(long timestampFrequency)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timestampFrequency);
        _nextRepeatTimestamps = [];
        _timestampFrequency = timestampFrequency;
    }

    internal bool IsEnabled { get; private set; }

    internal void Configure(int rate, int delayMilliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rate);
        ArgumentOutOfRangeException.ThrowIfNegative(delayMilliseconds);

        _nextRepeatTimestamps.Clear();
        _repeatDelayTicks = GetTicksForMilliseconds(delayMilliseconds);
        _repeatIntervalTicks = (rate == 0)
            ? 0
            : Math.Max(1L, checked((long)Math.Ceiling((double)_timestampFrequency / rate)));
        IsEnabled = rate != 0;
    }

    internal void Reset()
        => _nextRepeatTimestamps.Clear();

    internal void StartKey(uint key, long timestamp, bool repeatable)
    {
        if (!IsEnabled || !repeatable)
        {
            _ = _nextRepeatTimestamps.Remove(key);
            return;
        }

        _nextRepeatTimestamps[key] = checked(timestamp + _repeatDelayTicks);
    }

    internal void StopKey(uint key)
        => _ = _nextRepeatTimestamps.Remove(key);

    internal ImmutableArray<uint> TakeDueKeys(long timestamp, int maximumCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCount);

        if (!IsEnabled || _nextRepeatTimestamps.Count == 0)
        {
            return [];
        }

        var dueKeys = ImmutableArray.CreateBuilder<uint>(maximumCount);
        var keys = new List<uint>(_nextRepeatTimestamps.Keys);
        var anyDueKey = true;

        while (anyDueKey && (dueKeys.Count < maximumCount))
        {
            anyDueKey = false;

            foreach (var key in keys)
            {
                var nextRepeatTimestamp = _nextRepeatTimestamps[key];

                if (nextRepeatTimestamp > timestamp)
                {
                    continue;
                }

                dueKeys.Add(key);
                _nextRepeatTimestamps[key] = checked(nextRepeatTimestamp + _repeatIntervalTicks);
                anyDueKey = true;

                if (dueKeys.Count == maximumCount)
                {
                    break;
                }
            }
        }

        return dueKeys.ToImmutable();
    }

    private long GetTicksForMilliseconds(int milliseconds)
        => checked((long)Math.Ceiling((double)_timestampFrequency * milliseconds / 1000.0));
}
