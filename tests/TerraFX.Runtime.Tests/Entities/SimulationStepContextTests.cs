// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests;

[TestFixture]
internal sealed class SimulationStepContextTests
{
    [Test]
    public void ConstructorRejectsNegativeTickIndex()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new SimulationStepContext(-1, 60));
    }

    [TestCase(0)]
    [TestCase(1001)]
    public void ConstructorRejectsUnsupportedTickRate(int tickRate)
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new SimulationStepContext(0, tickRate));
    }

    [TestCase(0, 1)]
    [TestCase(1, 60)]
    [TestCase(long.MaxValue, 1000)]
    [Test]
    public void ConstructorRetainsTickMetadataAndComputesTheFixedStep(long tickIndex, int tickRate)
    {
        var context = new SimulationStepContext(tickIndex, tickRate);

        Assert.That(context.TickIndex, Is.EqualTo(tickIndex));
        Assert.That(context.TickRate, Is.EqualTo(tickRate));
        Assert.That(context.FixedDeltaTimeSeconds, Is.EqualTo(1.0f / tickRate));
        Assert.That(context, Is.EqualTo(new SimulationStepContext(tickIndex, tickRate)));
    }

    [Test]
    public void DefaultIsNotAnIssuedSimulationStep()
    {
        var context = default(SimulationStepContext);

        Assert.That(context.TickIndex, Is.Zero);
        Assert.That(context.TickRate, Is.Zero);
        Assert.That(context.FixedDeltaTimeSeconds, Is.Zero);
    }
}
