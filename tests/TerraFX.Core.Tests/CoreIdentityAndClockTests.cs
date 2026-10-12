// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using NUnit.Framework;

namespace TerraFX.Core.Tests;

[TestFixture]
internal sealed class CoreIdentityAndClockTests
{
    [Test]
    [Category("EN001")]
    public void FixedTickTimeConvertsCompletedTicksWithoutRoundedIncrementDrift()
    {
        var tick0 = SimulationTickTime.ToElapsedTime(1, 60);
        var tick1 = SimulationTickTime.ToElapsedTime(2, 60);
        var tick2 = SimulationTickTime.ToElapsedTime(3, 60);

        Assert.That(SimulationTickTime.ToElapsedTime(60, 60), Is.EqualTo(TimeSpan.FromSeconds(1)));
        Assert.That(tick0.Ticks, Is.EqualTo(166666));
        Assert.That(tick1.Ticks - tick0.Ticks, Is.EqualTo(166667));
        Assert.That(tick2.Ticks - tick1.Ticks, Is.EqualTo(166667));
    }

    [Test]
    [Category("EN002")]
    public void FixedTickTimeRejectsNegativeCountsAndRatesOutsideTheContract()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => SimulationTickTime.ToElapsedTime(-1, 60));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => SimulationTickTime.ToElapsedTime(0, 0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => SimulationTickTime.ToElapsedTime(0, 1001));

        Assert.That(SimulationTickTime.ToElapsedTime(1, 60).Ticks, Is.EqualTo(166666));
        Assert.That(SimulationTickTime.ToElapsedTime(2, 60).Ticks, Is.EqualTo(333333));
    }

    [TestCase(1)]
    [TestCase(3)]
    [TestCase(59)]
    [TestCase(60)]
    [TestCase(1000)]
    [Category("EN002")]
    public void FixedTickTimeUsesCompletedCountAndExactFloorConversion(int tickRate)
    {
        foreach (var completedTickCount in new long[] { 0, 1, tickRate - 1, tickRate, 1234567 })
        {
            var expected = (BigInteger)completedTickCount * TimeSpan.TicksPerSecond / tickRate;
            Assert.That(SimulationTickTime.ToElapsedTime(completedTickCount, tickRate).Ticks, Is.EqualTo((long)expected));
        }

        Assert.That(SimulationTickTime.ToElapsedTime(1, tickRate).Ticks, Is.GreaterThan(0));

        var maximumElapsedTimeTicks = (BigInteger)long.MaxValue + 1;
        var maximumScaledTickCount = maximumElapsedTimeTicks * tickRate;
        var maximumCompletedCount = (maximumScaledTickCount - 1) / TimeSpan.TicksPerSecond;

        var expectedAtLimit = maximumCompletedCount * TimeSpan.TicksPerSecond / tickRate;
        Assert.That(SimulationTickTime.ToElapsedTime((long)maximumCompletedCount, tickRate).Ticks,
            Is.EqualTo((long)expectedAtLimit));
        _ = Assert.Throws<OverflowException>(() => SimulationTickTime.ToElapsedTime((long)maximumCompletedCount + 1, tickRate));
        _ = Assert.Throws<OverflowException>(() => SimulationTickTime.ToElapsedTime(long.MaxValue, tickRate));
    }

    [Test]
    [Category("EN012")]
    public void OwnerIdsAreUniqueUnderConcurrentAllocationAndNeverWrap()
    {
        Assert.That(OwnerIdAllocator.GetNext(0), Is.EqualTo(1));
        Assert.That(OwnerIdAllocator.GetNext((long)uint.MaxValue - 1), Is.EqualTo(uint.MaxValue));
        _ = Assert.Throws<InvalidOperationException>(() => OwnerIdAllocator.GetNext(uint.MaxValue));
        _ = Assert.Throws<InvalidOperationException>(() => OwnerIdAllocator.GetNext(-1));

        var identities = new uint[2048];

        _ = Parallel.For(0, identities.Length, index => identities[index] = OwnerIdAllocator.Allocate());
        Assert.That(identities, Has.All.GreaterThan(0));
        Assert.That(new HashSet<uint>(identities).Count, Is.EqualTo(identities.Length));
    }
}
