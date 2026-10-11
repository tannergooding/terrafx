// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX;

internal static class SimulationTickTime
{
    internal static TimeSpan ToElapsedTime(long completedTickCount, int tickRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(completedTickCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(tickRate, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tickRate, 1000);

        var elapsedTicks = (UInt128)(ulong)completedTickCount * (uint)TimeSpan.TicksPerSecond / (uint)tickRate;

        if (elapsedTicks > long.MaxValue)
        {
            ThrowHelper.ThrowOverflowException("The completed simulation time exceeds the TimeSpan range.");
        }

        return TimeSpan.FromTicks(checked((long)elapsedTicks));
    }
}
