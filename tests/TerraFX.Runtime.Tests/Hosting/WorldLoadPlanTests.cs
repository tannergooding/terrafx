// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Hosting;

namespace TerraFX.Runtime.Tests.Hosting;

[TestFixture]
internal sealed class WorldLoadPlanTests
{
    private static World CreateWorld()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        return World.Create(new WorldOptions(registration) {
            CpuWorkerCount = 0,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
    }

    [Test]
    public void RejectsMismatchedTargetIdentity()
    {
        using var targetWorld = CreateWorld();

        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => new WorldLoadPlan(
                targetWorld,
                Guid.NewGuid(),
                Guid.NewGuid(),
                targetWorld.WorldGuid,
                snapshotTick: 1));
    }

    [Test]
    public void RetainsExactTargetBinding()
    {
        using var targetWorld = CreateWorld();
        using var plan = new WorldLoadPlan(
            targetWorld,
            targetWorld.WorldGuid,
            Guid.NewGuid(),
            targetWorld.WorldGuid,
            snapshotTick: 1);

        Assert.That(plan.TargetWorld, Is.SameAs(targetWorld));
    }
}
