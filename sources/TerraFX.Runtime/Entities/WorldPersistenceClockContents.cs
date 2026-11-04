// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal sealed class WorldPersistenceClockContents
{
    internal WorldPersistenceClockContents(
        long nextTick,
        int tickRate,
        float timeScale,
        long nextPublicationSequence,
        int segmentCount,
        long currentSegmentSequence,
        bool hasInitialFrame2,
        ReadOnlyMemory<byte> initialFrame2Record,
        ReadOnlyMemory<byte> initialFrame3Record,
        long historyBaseNextTick)
    {
        NextTick = nextTick;
        TickRate = tickRate;
        TimeScale = timeScale;
        NextPublicationSequence = nextPublicationSequence;
        SegmentCount = segmentCount;
        CurrentSegmentSequence = currentSegmentSequence;
        HasInitialFrame2 = hasInitialFrame2;
        InitialFrame2Record = initialFrame2Record;
        InitialFrame3Record = initialFrame3Record;
        HistoryBaseNextTick = historyBaseNextTick;
    }

    internal long CurrentSegmentSequence { get; }

    internal bool HasInitialFrame2 { get; }

    internal long HistoryBaseNextTick { get; }

    internal ReadOnlyMemory<byte> InitialFrame2Record { get; }

    internal ReadOnlyMemory<byte> InitialFrame3Record { get; }

    internal long NextPublicationSequence { get; }

    internal long NextTick { get; }

    internal int SegmentCount { get; }

    internal int TickRate { get; }

    internal float TimeScale { get; }
}
