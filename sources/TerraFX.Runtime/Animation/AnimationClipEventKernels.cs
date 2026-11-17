// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;

namespace TerraFX.Animation;

internal static class AnimationClipEventKernels
{
    private const byte LoopModeClamp = 0;
    private const byte LoopModeLoop = 1;

    internal static bool TryCountCrossedEvents(
        AnimationClipAsset clip,
        long firstLoopOrdinal,
        long firstTimeTicks,
        long secondLoopOrdinal,
        long secondTimeTicks,
        int maximumCount,
        out int count)
    {
        ArgumentNullException.ThrowIfNull(clip);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCount);
        var first = CreateEndpoint(clip, firstLoopOrdinal, firstTimeTicks);
        var second = CreateEndpoint(clip, secondLoopOrdinal, secondTimeTicks);

        if (clip.Events.IsDefault)
        {
            throw new InvalidDataException("Animation event keys are uninitialized.");
        }

        if (Compare(first, second) == 0)
        {
            count = 0;
            return true;
        }

        var forward = Compare(first, second) < 0;
        var firstAbsoluteTicks = ToAbsoluteTicks(clip, first);
        var secondAbsoluteTicks = ToAbsoluteTicks(clip, second);
        var total = 0;

        foreach (var clipEvent in clip.Events)
        {
            if ((clipEvent.TimeTicks < 0) || (clipEvent.TimeTicks > clip.DurationTicks))
            {
                throw new InvalidDataException("An Animation event key is outside its Clip duration.");
            }

            var eventCount = (clip.LoopMode is LoopModeClamp)
                ? (IsCrossedClampEvent(clipEvent.TimeTicks, first, second, forward) ? 1 : 0)
                : CountLoopOccurrences(
                    clipEvent.TimeTicks,
                    clip.DurationTicks,
                    firstAbsoluteTicks,
                    secondAbsoluteTicks,
                    forward);
            var remainingCapacity = maximumCount - total;

            if (eventCount > remainingCapacity)
            {
                count = 0;
                return false;
            }

            total = checked(total + (int)eventCount);
        }

        count = total;
        return true;
    }

    internal static int WriteCrossedEvents(
        AnimationClipAsset clip,
        long firstLoopOrdinal,
        long firstTimeTicks,
        long secondLoopOrdinal,
        long secondTimeTicks,
        scoped Span<Occurrence> destination)
    {
        if (!TryCountCrossedEvents(
            clip,
            firstLoopOrdinal,
            firstTimeTicks,
            secondLoopOrdinal,
            secondTimeTicks,
            destination.Length,
            out var expectedCount))
        {
            throw new ArgumentException("The Animation event destination is smaller than the complete crossed-event set.", nameof(destination));
        }

        if (expectedCount == 0)
        {
            return 0;
        }

        var first = CreateEndpoint(clip, firstLoopOrdinal, firstTimeTicks);
        var second = CreateEndpoint(clip, secondLoopOrdinal, secondTimeTicks);
        var forward = Compare(first, second) < 0;
        var writtenCount = (clip.LoopMode is LoopModeClamp)
            ? WriteClampEvents(clip, first, second, forward, destination)
            : WriteLoopEvents(clip, first, second, forward, destination);

        if (writtenCount != expectedCount)
        {
            throw new InvalidDataException("Animation event counting and traversal produced different results.");
        }

        return writtenCount;
    }

    private static Int128 CeilingDivide(Int128 value, long positiveDivisor)
    {
        var quotient = value / positiveDivisor;
        var remainder = value % positiveDivisor;
        return (remainder > 0) ? quotient + 1 : quotient;
    }

    private static long Compare(Endpoint first, Endpoint second)
    {
        var loopOrder = first.LoopOrdinal.CompareTo(second.LoopOrdinal);
        return (loopOrder != 0) ? loopOrder : first.TimeTicks.CompareTo(second.TimeTicks);
    }

    private static Int128 CountLoopOccurrences(
        long eventTimeTicks,
        long durationTicks,
        Int128 firstAbsoluteTicks,
        Int128 secondAbsoluteTicks,
        bool forward)
    {
        if (forward)
        {
            var firstLoop = FloorDivide(firstAbsoluteTicks - eventTimeTicks, durationTicks) + 1;
            var lastLoop = FloorDivide(secondAbsoluteTicks - eventTimeTicks, durationTicks);
            return (lastLoop >= firstLoop) ? (lastLoop - firstLoop + 1) : 0;
        }

        var firstLoopReverse = CeilingDivide(secondAbsoluteTicks - eventTimeTicks, durationTicks);
        var lastLoopReverse = CeilingDivide(firstAbsoluteTicks - eventTimeTicks, durationTicks) - 1;
        return (lastLoopReverse >= firstLoopReverse) ? (lastLoopReverse - firstLoopReverse + 1) : 0;
    }

    private static Endpoint CreateEndpoint(
        AnimationClipAsset clip,
        long loopOrdinal,
        long timeTicks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(timeTicks);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(timeTicks, clip.DurationTicks);

        if (clip.LoopMode is LoopModeClamp)
        {
            ArgumentOutOfRangeException.ThrowIfNotEqual(loopOrdinal, 0);
            return new Endpoint(loopOrdinal, timeTicks);
        }

        if (clip.LoopMode is not LoopModeLoop)
        {
            throw new InvalidDataException("The selected Animation Clip has an unsupported loop mode.");
        }

        return (timeTicks == clip.DurationTicks)
            ? new Endpoint(checked(loopOrdinal + 1), 0)
            : new Endpoint(loopOrdinal, timeTicks);
    }

    private static Int128 FloorDivide(Int128 value, long positiveDivisor)
    {
        var quotient = value / positiveDivisor;
        var remainder = value % positiveDivisor;
        return (remainder < 0) ? quotient - 1 : quotient;
    }

    private static bool IsCrossedClampEvent(
        long eventTimeTicks,
        Endpoint first,
        Endpoint second,
        bool forward)
        => forward
            ? ((eventTimeTicks > first.TimeTicks) && (eventTimeTicks <= second.TimeTicks))
            : ((eventTimeTicks >= second.TimeTicks) && (eventTimeTicks < first.TimeTicks));

    private static Int128 ToAbsoluteTicks(AnimationClipAsset clip, Endpoint endpoint)
        => ((Int128)endpoint.LoopOrdinal * clip.DurationTicks) + endpoint.TimeTicks;

    private static int WriteClampEvents(
        AnimationClipAsset clip,
        Endpoint first,
        Endpoint second,
        bool forward,
        scoped Span<Occurrence> destination)
    {
        var writtenCount = 0;

        if (forward)
        {
            foreach (var clipEvent in clip.Events)
            {
                if (IsCrossedClampEvent(clipEvent.TimeTicks, first, second, forward))
                {
                    destination[writtenCount++] = new Occurrence(clipEvent, 0, Direction: 1);
                }
            }
        }
        else
        {
            for (var index = clip.Events.Length - 1; index >= 0; index--)
            {
                var clipEvent = clip.Events[index];

                if (IsCrossedClampEvent(clipEvent.TimeTicks, first, second, forward))
                {
                    destination[writtenCount++] = new Occurrence(clipEvent, 0, Direction: -1);
                }
            }
        }

        return writtenCount;
    }

    private static int WriteLoopEvents(
        AnimationClipAsset clip,
        Endpoint first,
        Endpoint second,
        bool forward,
        scoped Span<Occurrence> destination)
    {
        var writtenCount = 0;

        if (forward)
        {
            var loopOrdinal = first.LoopOrdinal;

            while (true)
            {
                var isFirstLoop = loopOrdinal == first.LoopOrdinal;
                var isLastLoop = loopOrdinal == second.LoopOrdinal;

                foreach (var clipEvent in clip.Events)
                {
                    if ((!isFirstLoop || (clipEvent.TimeTicks > first.TimeTicks))
                        && (!isLastLoop || (clipEvent.TimeTicks <= second.TimeTicks)))
                    {
                        destination[writtenCount++] = new Occurrence(clipEvent, loopOrdinal, Direction: 1);
                    }
                }

                if (isLastLoop)
                {
                    break;
                }

                loopOrdinal = checked(loopOrdinal + 1);
            }
        }
        else
        {
            var loopOrdinal = first.LoopOrdinal;

            while (true)
            {
                var isFirstLoop = loopOrdinal == first.LoopOrdinal;
                var isLastLoop = loopOrdinal == second.LoopOrdinal;

                for (var index = clip.Events.Length - 1; index >= 0; index--)
                {
                    var clipEvent = clip.Events[index];

                    if ((!isFirstLoop || (clipEvent.TimeTicks < first.TimeTicks))
                        && (!isLastLoop || (clipEvent.TimeTicks >= second.TimeTicks)))
                    {
                        destination[writtenCount++] = new Occurrence(clipEvent, loopOrdinal, Direction: -1);
                    }
                }

                if (isLastLoop)
                {
                    break;
                }

                loopOrdinal = checked(loopOrdinal - 1);
            }
        }

        return writtenCount;
    }

    internal readonly record struct Occurrence(
        AnimationClipAsset.AnimationClipEvent Event,
        long LoopOrdinal,
        sbyte Direction);

    private readonly record struct Endpoint(long LoopOrdinal, long TimeTicks);
}
