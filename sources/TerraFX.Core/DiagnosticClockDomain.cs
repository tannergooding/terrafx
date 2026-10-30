// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX;

internal static class DiagnosticClockDomain
{
    private static long s_lastTimestamp;
    private static readonly ClockState s_state = new(TimeProvider.System);

    internal static long TimestampFrequency => 10_000_000;

    internal static void EnsureInitialized()
    {
        if (s_state.Frequency <= 0)
        {
            throw new InvalidOperationException("The selected diagnostic clock frequency must be positive.");
        }
    }

    internal static long GetElapsedTimestamp()
    {
        EnsureInitialized();

        var sourceTimestamp = s_state.Provider.GetTimestamp();
        var elapsedSourceTicks = (Int128)sourceTimestamp - s_state.Origin;
        var candidate = 0L;

        if (elapsedSourceTicks > 0)
        {
            var elapsedTicks = (UInt128)elapsedSourceTicks * 10_000_000u / (ulong)s_state.Frequency;
            candidate = (elapsedTicks > (UInt128)long.MaxValue) ? long.MaxValue : (long)elapsedTicks;
        }

        while (true)
        {
            var previous = Volatile.Read(ref s_lastTimestamp);

            if (candidate <= previous)
            {
                return previous;
            }

            if (Interlocked.CompareExchange(ref s_lastTimestamp, candidate, previous) == previous)
            {
                return candidate;
            }
        }
    }

    private readonly struct ClockState
    {
        private readonly long _frequency;
        private readonly long _origin;
        private readonly TimeProvider _provider;

        internal ClockState(TimeProvider provider)
        {
            _provider = provider;
            _frequency = provider.TimestampFrequency;
            _origin = provider.GetTimestamp();
        }

        internal long Frequency => _frequency;

        internal long Origin => _origin;

        internal TimeProvider Provider => _provider;
    }
}
