// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;

namespace TerraFX.Platform.Tests;

[TestFixture]
internal sealed class WaylandKeyboardRepeatScheduleTests
{
    [Test]
    public void ConfigurationRequiresNonnegativeRateAndDelay()
    {
        var schedule = new WaylandKeyboardRepeatSchedule(timestampFrequency: 1000);

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => schedule.Configure(rate: -1, delayMilliseconds: 0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => schedule.Configure(rate: 1, delayMilliseconds: -1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new WaylandKeyboardRepeatSchedule(timestampFrequency: 0));
    }

    [Test]
    public void DelayedPumpsPreserveRepeatDeadlinesAcrossBoundedBatches()
    {
        var schedule = new WaylandKeyboardRepeatSchedule(timestampFrequency: 1000);
        schedule.Configure(rate: 10, delayMilliseconds: 100);
        schedule.StartKey(key: 30, timestamp: 0, repeatable: true);

        Assert.That(schedule.TakeDueKeys(timestamp: 550, maximumCount: 2), Is.EqualTo(new uint[] { 30, 30 }));
        Assert.That(schedule.TakeDueKeys(timestamp: 550, maximumCount: 4), Is.EqualTo(new uint[] { 30, 30, 30 }));
    }

    [Test]
    public void NonrepeatableKeysAreNotScheduled()
    {
        var schedule = new WaylandKeyboardRepeatSchedule(timestampFrequency: 1000);
        schedule.Configure(rate: 30, delayMilliseconds: 100);
        schedule.StartKey(key: 30, timestamp: 50, repeatable: false);

        Assert.That(schedule.TakeDueKeys(timestamp: 1000, maximumCount: 4096), Is.Empty);
    }

    [Test]
    public void ReconfigurationAndKeyReleaseClearPendingRepeats()
    {
        var schedule = new WaylandKeyboardRepeatSchedule(timestampFrequency: 1000);
        schedule.Configure(rate: 30, delayMilliseconds: 100);
        schedule.StartKey(key: 30, timestamp: 50, repeatable: true);
        schedule.Configure(rate: 0, delayMilliseconds: 100);

        Assert.That(schedule.TakeDueKeys(timestamp: 1000, maximumCount: 4096), Is.Empty);

        schedule.Configure(rate: 30, delayMilliseconds: 100);
        schedule.StartKey(key: 31, timestamp: 50, repeatable: true);
        schedule.StopKey(key: 31);

        Assert.That(schedule.TakeDueKeys(timestamp: 1000, maximumCount: 4096), Is.Empty);
    }

    [Test]
    public void RepeatUsesCompositorDelayAndRate()
    {
        var schedule = new WaylandKeyboardRepeatSchedule(timestampFrequency: 1000);
        schedule.Configure(rate: 10, delayMilliseconds: 200);
        schedule.StartKey(key: 30, timestamp: 50, repeatable: true);

        Assert.That(schedule.TakeDueKeys(timestamp: 249, maximumCount: 4096), Is.Empty);
        Assert.That(schedule.TakeDueKeys(timestamp: 250, maximumCount: 4096), Is.EqualTo(new uint[] { 30 }));
        Assert.That(schedule.TakeDueKeys(timestamp: 349, maximumCount: 4096), Is.Empty);
        Assert.That(schedule.TakeDueKeys(timestamp: 350, maximumCount: 4096), Is.EqualTo(new uint[] { 30 }));
    }
}
