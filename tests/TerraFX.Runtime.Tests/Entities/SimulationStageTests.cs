// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using NUnit.Framework;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests;

[TestFixture]
internal sealed class SimulationStageTests
{
    [Test]
    public void TickStageValuesMatchThePipelineOrder()
    {
        Assert.That((int)SimulationStage.Begin, Is.EqualTo(0));
        Assert.That((int)SimulationStage.Input, Is.EqualTo(1));
        Assert.That((int)SimulationStage.Animation, Is.EqualTo(2));
        Assert.That((int)SimulationStage.Transforms, Is.EqualTo(3));
        Assert.That((int)SimulationStage.Physics, Is.EqualTo(4));
        Assert.That((int)SimulationStage.PoseSync, Is.EqualTo(5));
        Assert.That((int)SimulationStage.Events, Is.EqualTo(6));
        Assert.That((int)SimulationStage.Structural, Is.EqualTo(7));
        Assert.That((int)SimulationStage.Extract, Is.EqualTo(8));
        Assert.That((int)SimulationStage.Commit, Is.EqualTo(9));
    }
}
