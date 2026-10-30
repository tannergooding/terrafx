// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldWorkPlanTests
{
    // The exact growth peak is a 64-entry current array plus a 65-entry candidate array.
    private const long WorkPlanGrowthPeakBytes = 4256;
    private const long WorkPlanGrowthShortOfPeak = WorkPlanGrowthPeakBytes - 1;

    [TestCase(WorkPlanGrowthPeakBytes, true)]
    [TestCase(WorkPlanGrowthShortOfPeak, false)]
    public void WorkPlanGrowthAdmitsExactCurrentAndCandidatePeak(
        long workPlanBudget,
        bool admitNextPartition)
    {
        var defaults = WorldSchedulingLimits.Default;
        var limits = new WorldSchedulingLimits(
            defaults.MaximumRegistrationBytes,
            defaults.MaximumGraphBytes,
            workPlanBudget,
            defaults.MaximumCommandStagingBytes,
            defaults.MaximumOwnedStateBytes,
            defaults.MaximumDiagnosticBytes,
            defaults.MaximumConflictComparisons);
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        using var world = World.Create(new WorldOptions(registration) {
            CpuWorkerCount = 0,
            MaximumEntitySlots = 4097,
            MaximumLiveEntities = 4097,
            SchedulingLimits = limits,
        });
        var description = new EntityDescription();

        for (var index = 0; index < 4096; index++)
        {
            _ = world.CreateEntity(description);
        }

        if (admitNextPartition)
        {
            _ = world.CreateEntity(description);
        }
        else
        {
            _ = Assert.Throws<InvalidOperationException>(() => world.CreateEntity(description));
        }

        var allEntities = new EntityQueryDescription([], [], []);
        var allEntitiesProbe = new CountEntitiesAction();
        world.ForEachChunk(allEntities, ref allEntitiesProbe);
        Assert.That(allEntitiesProbe.Count, Is.EqualTo(admitNextPartition ? 4097 : 4096));

        var selectedPartition = admitNextPartition ? 64 : 63;
        var finalPartition = new EntityQueryDescription(
            [],
            [],
            [],
            new SystemPartitionRange(selectedPartition, selectedPartition + 1));
        var partitionProbe = new CountEntitiesAction();
        world.ForEachChunk(finalPartition, ref partitionProbe);
        Assert.That(partitionProbe.Count, Is.EqualTo(admitNextPartition ? 1 : 64));
    }

    private struct CountEntitiesAction : IAction<EntityChunkView>
    {
        internal int Count;

        public void Invoke(scoped EntityChunkView chunk)
            => Count += chunk.Entities.Length;
    }
}
