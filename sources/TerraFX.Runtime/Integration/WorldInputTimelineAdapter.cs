// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX;
using TerraFX.Entities;
using TerraFX.Input;

namespace TerraFX.Integration;

internal sealed class WorldInputTimelineAdapter
{
    private bool _isDisposed;
    private readonly int _ownerThreadId;
    private readonly InputRouter _router;
    private SimulationClockSegment _segment;
    private readonly HostEventSequenceLease _sequenceLease;
    private readonly World _world;

    internal WorldInputTimelineAdapter(World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _ = world.CurrentFrame3;

        _ownerThreadId = Environment.CurrentManagedThreadId;
        _sequenceLease = new HostEventSequenceLease();
        _world = world;
        _router = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            OwnerIdAllocator.Allocate(),
            _ownerThreadId,
            _sequenceLease);
    }

    internal SimulationClockSegment CurrentSegment
    {
        get
        {
            EnsureOwnerThread();
            ThrowIfDisposed();
            return _segment;
        }
    }

    internal InputRouter Router
    {
        get
        {
            EnsureOwnerThread();
            ThrowIfDisposed();
            return _router;
        }
    }

    internal SimulationClockSegment BeginPumpBoundary(long hostTimestamp, bool isPaused)
    {
        EnsureOwnerThread();
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(hostTimestamp);
        _ = _world.CurrentFrame3;

        if (_router.IsPauseControlPending || (_router.IsPaused != isPaused))
        {
            throw new InvalidOperationException("The input recipient pause state must be reconciled before installing its clock segment.");
        }

        var nextOpenTick = _world.NextTickIndex;

        if (_segment.IsValid && (_router.NextOpenTick != nextOpenTick))
        {
            throw new InvalidOperationException("The World and input recipient next-open ticks are not synchronized.");
        }

        if (_segment.IsValid && (hostTimestamp < _segment.HostTimestampStartTicks))
        {
            throw new InvalidOperationException("The input clock boundary precedes the active segment.");
        }

        var hostSequenceStart = _sequenceLease.Allocate();
        var elapsedTicks = 0L;
        var fractionalTicks = 0.0;

        if (_segment.IsValid)
        {
            if ((hostTimestamp == _segment.HostTimestampStartTicks)
                && (hostSequenceStart < _segment.HostSequenceStart))
            {
                throw new InvalidOperationException("The input clock boundary precedes the active segment.");
            }

            if (_segment.IsPaused || (_segment.TimeScale == 0.0f))
            {
                elapsedTicks = _segment.SimulationElapsedStartTicks;
                fractionalTicks = _segment.FractionalSimulationTicks;
            }
            else if (!_segment.TryGetSimulationTimeAtHostEvent(
                hostTimestamp,
                hostSequenceStart,
                out elapsedTicks,
                out fractionalTicks))
            {
                throw new InvalidOperationException("The active input clock segment could not be advanced to this boundary.");
            }
        }

        var revision = _segment.IsValid ? checked(_segment.Revision + 1) : 1;
        var segment = new SimulationClockSegment(
            revision,
            hostTimestamp,
            hostSequenceStart,
            elapsedTicks,
            fractionalTicks,
            _world.TickRate,
            nextOpenTick,
            _world.CurrentTimeScale,
            isPaused);
        _router.SetSimulationClockSegment(in segment);
        _router.BeginHostControlBoundary(nextOpenTick, hostTimestamp);
        _segment = segment;
        return segment;
    }

    internal ImmutableArray<InputActionTransition> DisposeAndGetTransitions()
    {
        EnsureOwnerThread();

        if (_isDisposed)
        {
            return [];
        }

        var transitions = _router.DisposeAndGetTransitions();
        _sequenceLease.Revoke();
        _isDisposed = true;
        return transitions;
    }

    internal InputRoutingReceipt EnqueueCapturedInput(
        InputInjection injection,
        long hostTimestamp,
        long hostSequence)
    {
        EnsureOwnerThread();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(injection);
        _ = _world.CurrentFrame3;
        return _router.EnqueueHeadlessInjection(injection, hostTimestamp, hostSequence);
    }

    internal InputRoutingReceipt InjectHeadlessInput(InputInjection injection, long hostTimestamp)
    {
        EnsureOwnerThread();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(injection);
        ArgumentOutOfRangeException.ThrowIfNegative(hostTimestamp);
        return EnqueueCapturedInput(injection, hostTimestamp, _sequenceLease.Allocate());
    }

    internal InputActionSnapshot PublishInputStage(long tickIndex)
    {
        EnsureOwnerThread();
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(tickIndex);

        if (tickIndex != _world.NextTickIndex)
        {
            throw new InvalidOperationException("The input snapshot must be published for the World's next open tick.");
        }

        return _router.PublishSimulationActionSnapshot(tickIndex);
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("The World input timeline is bound to its creating owner thread.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}
