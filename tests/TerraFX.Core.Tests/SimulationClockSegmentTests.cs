// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;

namespace TerraFX.Core.Tests;

[TestFixture]
internal sealed class SimulationClockSegmentTests
{
    private static SimulationClockSegment Create(
        long revision = 1,
        long hostTimestampStartTicks = 0,
        long hostSequenceStart = 1,
        long simulationElapsedStartTicks = 0,
        double fractionalSimulationTicks = 0.0,
        int tickRate = 60,
        long nextOpenTick = 0,
        float timeScale = 1.0f,
        bool isPaused = false)
        => new SimulationClockSegment(
            revision,
            hostTimestampStartTicks,
            hostSequenceStart,
            simulationElapsedStartTicks,
            fractionalSimulationTicks,
            tickRate,
            nextOpenTick,
            timeScale,
            isPaused);

    [Test]
    public void AllowsFractionalCarryAtMaximumWholeTimeAndRejectsWholeTickOverflow()
    {
        var segment = Create(simulationElapsedStartTicks: long.MaxValue, fractionalSimulationTicks: 0.5);

        Assert.That(segment.TryGetTickIndexForHostEvent(0, 1, out _, out _), Is.True);
        _ = Assert.Throws<OverflowException>(() => segment.TryGetTickIndexForHostEvent(1, 1, out _, out _));
    }

    [Test]
    public void CarriesFractionalSimulationTicksAcrossSegments()
    {
        var segment = Create(
            hostTimestampStartTicks: 100,
            simulationElapsedStartTicks: 166_666,
            fractionalSimulationTicks: 0.5,
            timeScale: 0.5f);

        Assert.That(segment.TryGetTickIndexForHostEvent(100, 5, out var carriedTick, out var carriedLate), Is.True);
        Assert.That(carriedTick, Is.Zero);
        Assert.That(carriedLate, Is.False);

        Assert.That(segment.TryGetTickIndexForHostEvent(101, 5, out var scaledTick, out var scaledLate), Is.True);
        Assert.That(scaledTick, Is.EqualTo(1));
        Assert.That(scaledLate, Is.False);

        Assert.That(segment.TryGetSimulationTimeAtHostEvent(100, 5, out var wholeElapsedTicks, out var fractionalTicks), Is.True);
        Assert.That(wholeElapsedTicks, Is.EqualTo(166_666));
        Assert.That(fractionalTicks, Is.EqualTo(0.5));
    }

    [Test]
    public void ClampsPastEligibleEventsAndDoesNotMapPausedTime()
    {
        var segment = Create(nextOpenTick: 3);

        Assert.That(segment.TryGetTickIndexForHostEvent(0, 1, out var lateTick, out var late), Is.True);
        Assert.That(lateTick, Is.EqualTo(3));
        Assert.That(late, Is.True);

        var pausedSegment = Create(nextOpenTick: 3, isPaused: true);

        Assert.That(pausedSegment.TryGetTickIndexForHostEvent(0, 1, out var pausedTick, out var pausedLate), Is.False);
        Assert.That(pausedTick, Is.EqualTo(3));
        Assert.That(pausedLate, Is.False);

        var zeroScaleSegment = Create(nextOpenTick: 3, timeScale: 0.0f);

        Assert.That(zeroScaleSegment.TryGetTickIndexForHostEvent(0, 1, out var zeroScaleTick, out var zeroScaleLate), Is.False);
        Assert.That(zeroScaleTick, Is.EqualTo(3));
        Assert.That(zeroScaleLate, Is.False);
    }

    [Test]
    public void ConstructorRejectsInvalidClockAndTimelineValues()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => Create(revision: 0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => Create(hostTimestampStartTicks: -1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => Create(hostSequenceStart: 0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => Create(simulationElapsedStartTicks: -1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => Create(fractionalSimulationTicks: double.NaN));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => Create(fractionalSimulationTicks: 1.0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => Create(tickRate: 0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => Create(tickRate: 1001));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => Create(nextOpenTick: -1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => Create(timeScale: float.NaN));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => Create(timeScale: -1.0f));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => Create(timeScale: 4.1f));
    }

    [Test]
    public void ConvertsMaximumHostDeltaWithoutPrematureSignedOverflow()
    {
        var segment = Create(timeScale: 1.0f);
        var elapsedTicks = (UInt128)long.MaxValue * 60;
        var expectedTick = (long)(((elapsedTicks + 10_000_000 - 1) / 10_000_000) - 1);

        Assert.That(segment.TryGetTickIndexForHostEvent(long.MaxValue, 1, out var assignedTick, out var late), Is.True);
        Assert.That(assignedTick, Is.EqualTo(expectedTick));
        Assert.That(late, Is.False);

        var overflowingSegment = Create(timeScale: 4.0f);
        _ = Assert.Throws<OverflowException>(() => overflowingSegment.TryGetTickIndexForHostEvent(long.MaxValue, 1, out _, out _));
    }

    [Test]
    public void DefaultSegmentIsInvalidAndCannotMapEvents()
    {
        var segment = default(SimulationClockSegment);

        Assert.That(segment.IsValid, Is.False);
        _ = Assert.Throws<InvalidOperationException>(() => segment.TryGetTickIndexForHostEvent(0, 1, out _, out _));
    }

    [Test]
    public void MapsEventsAtAndAroundIdealTickBoundaries()
    {
        var segment = Create(hostTimestampStartTicks: 100, hostSequenceStart: 5);

        Assert.That(segment.TryGetTickIndexForHostEvent(100, 4, out var beforeStartTick, out var beforeStartLate), Is.False);
        Assert.That(beforeStartTick, Is.Zero);
        Assert.That(beforeStartLate, Is.False);

        Assert.That(segment.TryGetTickIndexForHostEvent(100, 5, out var startTick, out var startLate), Is.True);
        Assert.That(startTick, Is.Zero);
        Assert.That(startLate, Is.False);

        Assert.That(segment.TryGetTickIndexForHostEvent(100 + 166_666, 5, out var boundaryTick, out var boundaryLate), Is.True);
        Assert.That(boundaryTick, Is.Zero);
        Assert.That(boundaryLate, Is.False);

        Assert.That(segment.TryGetTickIndexForHostEvent(100 + 166_667, 5, out var nextTick, out var nextLate), Is.True);
        Assert.That(nextTick, Is.EqualTo(1));
        Assert.That(nextLate, Is.False);
    }

    [Test]
    public void RejectsInvalidEventIdentity()
    {
        var segment = Create();

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => segment.TryGetTickIndexForHostEvent(-1, 1, out _, out _));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => segment.TryGetTickIndexForHostEvent(0, 0, out _, out _));
    }

    [Test]
    public void SupportsSmallestPositiveTimeScale()
    {
        var segment = Create(timeScale: float.Epsilon);

        Assert.That(segment.TryGetSimulationTimeAtHostEvent(long.MaxValue, 1, out var wholeElapsedTicks, out var fractionalTicks), Is.True);
        Assert.That(wholeElapsedTicks, Is.Zero);
        Assert.That(fractionalTicks, Is.GreaterThan(0.0).And.LessThan(1.0));
    }
}
