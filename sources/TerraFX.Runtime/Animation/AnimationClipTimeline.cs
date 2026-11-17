// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Animation;

internal struct AnimationClipTimeline
{
    private long _currentLoopOrdinal;
    private long _currentTimeTicks;
    private readonly long _durationTicks;
    private bool _isCompleted;
    private sbyte _lastDirection;
    private readonly byte _loopMode;
    private long _previousLoopOrdinal;
    private long _previousTimeTicks;
    private AnimationTickRemainder192 _remainder;

    internal AnimationClipTimeline(AnimationClipAsset clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        _durationTicks = clip.DurationTicks;
        _loopMode = clip.LoopMode;
        _currentLoopOrdinal = 0;
        _currentTimeTicks = 0;
        _isCompleted = false;
        _lastDirection = 1;
        _previousLoopOrdinal = 0;
        _previousTimeTicks = 0;
        _remainder = default;
    }

    internal readonly long CurrentLoopOrdinal => _currentLoopOrdinal;

    internal readonly long CurrentTimeTicks => _currentTimeTicks;

    internal readonly bool IsCompleted => _isCompleted;

    internal readonly bool IsValid => (_durationTicks > 0) && (_loopMode <= 1);

    internal readonly sbyte LastDirection => _lastDirection;

    internal readonly long PreviousLoopOrdinal => _previousLoopOrdinal;

    internal readonly long PreviousTimeTicks => _previousTimeTicks;

    internal readonly AnimationTickRemainder192 Remainder => _remainder;

    internal bool Advance(float speed, int ticksPerSecond)
    {
        EnsureValid();
        var tickAdvance = AnimationTickKernels.Advance(_remainder, speed, ticksPerSecond);
        _previousLoopOrdinal = _currentLoopOrdinal;
        _previousTimeTicks = _currentTimeTicks;
        _remainder = tickAdvance.Remainder;
        var completedEdge = false;

        if (_loopMode is 1)
        {
            var unwrappedTimeTicks = (Int128)_currentTimeTicks + tickAdvance.DeltaTicks;
            var loopDelta = unwrappedTimeTicks / _durationTicks;
            var localTimeTicks = unwrappedTimeTicks % _durationTicks;

            if (localTimeTicks < 0)
            {
                loopDelta--;
                localTimeTicks += _durationTicks;
            }

            _currentLoopOrdinal = checked(_currentLoopOrdinal + checked((long)loopDelta));
            _currentTimeTicks = checked((long)localTimeTicks);
        }
        else if (_loopMode is 0)
        {
            var unwrappedTimeTicks = (Int128)_currentTimeTicks + tickAdvance.DeltaTicks;

            if (unwrappedTimeTicks >= _durationTicks)
            {
                completedEdge = !_isCompleted && (tickAdvance.DeltaTicks > 0);
                _currentTimeTicks = _durationTicks;
                _isCompleted = _isCompleted || completedEdge;
            }
            else if (unwrappedTimeTicks <= 0)
            {
                _currentTimeTicks = 0;
            }
            else
            {
                _currentTimeTicks = checked((long)unwrappedTimeTicks);
            }
        }
        else
        {
            throw new InvalidOperationException("The Animation timeline has an unsupported loop mode.");
        }

        if ((_currentLoopOrdinal != _previousLoopOrdinal)
            || (_currentTimeTicks != _previousTimeTicks)
            || completedEdge)
        {
            _lastDirection = (tickAdvance.DeltaTicks > 0) ? (sbyte)1 : (sbyte)-1;
        }

        return completedEdge;
    }

    internal void ResetForPlay()
    {
        EnsureValid();
        _currentLoopOrdinal = 0;
        _currentTimeTicks = 0;
        _isCompleted = false;
        _lastDirection = 1;
        _previousLoopOrdinal = 0;
        _previousTimeTicks = 0;
        _remainder = default;
    }

    internal void Seek(long positionTicks)
    {
        EnsureValid();
        ArgumentOutOfRangeException.ThrowIfNegative(positionTicks);
        _previousLoopOrdinal = _currentLoopOrdinal;
        _previousTimeTicks = _currentTimeTicks;
        var previousAbsoluteTicks = ((Int128)_currentLoopOrdinal * _durationTicks) + _currentTimeTicks;

        if (_loopMode is 1)
        {
            _currentLoopOrdinal = positionTicks / _durationTicks;
            _currentTimeTicks = positionTicks % _durationTicks;
        }
        else if (_loopMode is 0)
        {
            _currentLoopOrdinal = 0;
            _currentTimeTicks = Math.Min(positionTicks, _durationTicks);
        }
        else
        {
            throw new InvalidOperationException("The Animation timeline has an unsupported loop mode.");
        }

        _remainder = default;
        var nextAbsoluteTicks = ((Int128)_currentLoopOrdinal * _durationTicks) + _currentTimeTicks;

        if (nextAbsoluteTicks > previousAbsoluteTicks)
        {
            _lastDirection = 1;
        }
        else if (nextAbsoluteTicks < previousAbsoluteTicks)
        {
            _lastDirection = -1;
        }
    }

    private readonly void EnsureValid()
    {
        if (!IsValid)
        {
            throw new InvalidOperationException("The Animation clip timeline is uninitialized.");
        }
    }
}
