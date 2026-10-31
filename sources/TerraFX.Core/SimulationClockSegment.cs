// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX;

/// <summary>Maps normalized host event times to one simulation timeline.</summary>
public readonly struct SimulationClockSegment : IEquatable<SimulationClockSegment>
{
    private const uint FloatImplicitSignificandBit = 0x00800000;
    private const uint FloatSignificandMask = 0x007FFFFF;
    private const uint TimeSpanTicksPerSecond = 10_000_000;

    private readonly double _fractionalSimulationTicks;
    private readonly long _hostSequenceStart;
    private readonly long _hostTimestampStartTicks;
    private readonly bool _isPaused;
    private readonly long _nextOpenTick;
    private readonly long _revision;
    private readonly long _simulationElapsedStartTicks;
    private readonly int _tickRate;
    private readonly float _timeScale;

    internal SimulationClockSegment(
        long revision,
        long hostTimestampStartTicks,
        long hostSequenceStart,
        long simulationElapsedStartTicks,
        double fractionalSimulationTicks,
        int tickRate,
        long nextOpenTick,
        float timeScale,
        bool isPaused)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(revision, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(hostTimestampStartTicks);
        ArgumentOutOfRangeException.ThrowIfLessThan(hostSequenceStart, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(simulationElapsedStartTicks);

        if (!double.IsFinite(fractionalSimulationTicks) || (fractionalSimulationTicks < 0.0) || (fractionalSimulationTicks >= 1.0))
        {
            throw new ArgumentOutOfRangeException(nameof(fractionalSimulationTicks));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(tickRate, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tickRate, 1000);
        ArgumentOutOfRangeException.ThrowIfNegative(nextOpenTick);

        if (!float.IsFinite(timeScale) || (timeScale < 0.0f) || (timeScale > 4.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(timeScale));
        }

        _fractionalSimulationTicks = fractionalSimulationTicks;
        _hostSequenceStart = hostSequenceStart;
        _hostTimestampStartTicks = hostTimestampStartTicks;
        _isPaused = isPaused;
        _nextOpenTick = nextOpenTick;
        _revision = revision;
        _simulationElapsedStartTicks = simulationElapsedStartTicks;
        _tickRate = tickRate;
        _timeScale = (timeScale == 0.0f) ? 0.0f : timeScale;
    }

    /// <summary>Gets the carried fractional simulation <see cref="TimeSpan"/> tick.</summary>
    public double FractionalSimulationTicks => _fractionalSimulationTicks;

    /// <summary>Gets the first host event sequence included in this segment.</summary>
    public long HostSequenceStart => _hostSequenceStart;

    /// <summary>Gets the first normalized host timestamp included in this segment.</summary>
    public long HostTimestampStartTicks => _hostTimestampStartTicks;

    /// <summary>Gets a value indicating whether the timeline is paused.</summary>
    public bool IsPaused => _isPaused;

    /// <summary>Gets a value indicating whether this is an initialized segment.</summary>
    public bool IsValid => _revision > 0;

    /// <summary>Gets the next simulation tick that was open when this segment began.</summary>
    public long NextOpenTick => _nextOpenTick;

    /// <summary>Gets the positive revision of this segment.</summary>
    public long Revision => _revision;

    /// <summary>Gets the whole simulation <see cref="TimeSpan"/> ticks elapsed at the segment start.</summary>
    public long SimulationElapsedStartTicks => _simulationElapsedStartTicks;

    /// <summary>Gets the fixed simulation tick rate, in ticks per second.</summary>
    public int TickRate => _tickRate;

    /// <summary>Gets the binary32 wall-to-simulation time scale.</summary>
    public float TimeScale => _timeScale;

    /// <summary>Negates equality of the complete segment.</summary>
    public static bool operator !=(SimulationClockSegment left, SimulationClockSegment right) => !left.Equals(right);

    /// <summary>Compares the complete segment.</summary>
    public static bool operator ==(SimulationClockSegment left, SimulationClockSegment right) => left.Equals(right);

    private static void ConvertElapsedHostTicks(
        long elapsedHostTicks,
        float timeScale,
        double fractionalStartTicks,
        out UInt128 wholeSimulationTicks,
        out double fractionalSimulationTicks)
    {
        var timeScaleBits = BitConverter.SingleToUInt32Bits(timeScale);
        var exponentBits = (int)((timeScaleBits >> 23) & 0xFF);
        var significand = timeScaleBits & FloatSignificandMask;
        var denominatorShift = 149;

        if (exponentBits != 0)
        {
            significand |= FloatImplicitSignificandBit;
            denominatorShift = 150 - exponentBits;
        }

        var scaledProduct = (UInt128)(ulong)elapsedHostTicks * significand;
        UInt128 elapsedWholeTicks;
        UInt128 elapsedRemainder;

        if (denominatorShift < 128)
        {
            elapsedWholeTicks = scaledProduct >> denominatorShift;
            elapsedRemainder = scaledProduct & (((UInt128)1 << denominatorShift) - 1);
        }
        else
        {
            elapsedWholeTicks = 0;
            elapsedRemainder = scaledProduct;
        }

        var elapsedFractionalTicks = Math.ScaleB((double)elapsedRemainder, -denominatorShift);
        var fractionalSum = elapsedFractionalTicks + fractionalStartTicks;

        if (fractionalSum >= 2.0)
        {
            wholeSimulationTicks = elapsedWholeTicks + 1;
            fractionalSimulationTicks = Math.BitDecrement(1.0);
        }
        else if (fractionalSum >= 1.0)
        {
            wholeSimulationTicks = elapsedWholeTicks + 1;
            fractionalSimulationTicks = fractionalSum - 1.0;
        }
        else
        {
            wholeSimulationTicks = elapsedWholeTicks;
            fractionalSimulationTicks = fractionalSum;
        }
    }

    /// <inheritdoc />
    public bool Equals(SimulationClockSegment other)
        => _fractionalSimulationTicks.Equals(other._fractionalSimulationTicks)
            && (_hostSequenceStart == other._hostSequenceStart)
            && (_hostTimestampStartTicks == other._hostTimestampStartTicks)
            && (_isPaused == other._isPaused)
            && (_nextOpenTick == other._nextOpenTick)
            && (_revision == other._revision)
            && (_simulationElapsedStartTicks == other._simulationElapsedStartTicks)
            && (_tickRate == other._tickRate)
            && (_timeScale == other._timeScale);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SimulationClockSegment other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(
            HashCode.Combine(_fractionalSimulationTicks, _hostSequenceStart, _hostTimestampStartTicks, _isPaused),
            HashCode.Combine(_nextOpenTick, _revision, _simulationElapsedStartTicks, _tickRate),
            _timeScale);

    /// <summary>Maps a captured host event to this timeline's eligible tick.</summary>
    /// <param name="hostTimestampTicks">The nonnegative normalized host timestamp.</param>
    /// <param name="hostSequence">The positive process-wide capture sequence.</param>
    /// <param name="assignedTick">Receives the eligible or next-open tick.</param>
    /// <param name="late">Receives whether the event was clamped to the next-open tick.</param>
    /// <returns><see langword="true"/> when the event maps to an active simulation tick; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A host timestamp is negative or a host sequence is not positive.</exception>
    /// <exception cref="InvalidOperationException">This segment is default initialized.</exception>
    /// <exception cref="OverflowException">The mapped simulation elapsed time exceeds the signed <see cref="TimeSpan"/> range.</exception>
    public bool TryGetTickIndexForHostEvent(long hostTimestampTicks, long hostSequence, out long assignedTick, out bool late)
    {
        assignedTick = _nextOpenTick;
        late = false;

        if (!TryGetSimulationTimeAtHostEvent(hostTimestampTicks, hostSequence, out var wholeElapsedTicks, out _))
        {
            return false;
        }

        var scaledBoundaries = (UInt128)(ulong)wholeElapsedTicks * (uint)_tickRate;
        var completedBoundaries = scaledBoundaries / TimeSpanTicksPerSecond;
        var boundaryRemainder = scaledBoundaries % TimeSpanTicksPerSecond;

        long candidateTick;

        if ((boundaryRemainder == 0) && (completedBoundaries > 0))
        {
            candidateTick = (long)completedBoundaries - 1;
        }
        else
        {
            candidateTick = (long)completedBoundaries;
        }

        if (candidateTick < _nextOpenTick)
        {
            assignedTick = _nextOpenTick;
            late = true;
        }
        else
        {
            assignedTick = candidateTick;
        }

        return true;
    }

    internal bool TryGetSimulationTimeAtHostEvent(
        long hostTimestampTicks,
        long hostSequence,
        out long wholeElapsedTicks,
        out double fractionalTicks)
    {
        if (!IsValid)
        {
            ThrowHelper.ThrowInvalidOperationException("The simulation clock segment is default initialized.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(hostTimestampTicks);
        ArgumentOutOfRangeException.ThrowIfLessThan(hostSequence, 1);

        wholeElapsedTicks = _simulationElapsedStartTicks;
        fractionalTicks = _fractionalSimulationTicks;

        if ((hostTimestampTicks < _hostTimestampStartTicks)
            || ((hostTimestampTicks == _hostTimestampStartTicks) && (hostSequence < _hostSequenceStart))
            || _isPaused
            || (_timeScale == 0.0f))
        {
            return false;
        }

        var elapsedHostTicks = hostTimestampTicks - _hostTimestampStartTicks;
        ConvertElapsedHostTicks(elapsedHostTicks, _timeScale, _fractionalSimulationTicks, out var scaledWholeTicks, out var scaledFractionalTicks);
        var simulationElapsedTicks = (UInt128)(ulong)_simulationElapsedStartTicks + scaledWholeTicks;

        if (simulationElapsedTicks > (UInt128)long.MaxValue)
        {
            ThrowHelper.ThrowOverflowException("The mapped simulation time exceeds the TimeSpan range.");
        }

        wholeElapsedTicks = (long)simulationElapsedTicks;
        fractionalTicks = scaledFractionalTicks;
        return true;
    }
}
