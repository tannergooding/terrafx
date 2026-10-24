// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using NUnit.Framework;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests;

[TestFixture]
internal sealed class SystemExecutionScheduleTests
{
    private const int MaximumAccessCount = 65_536;
    private const int MaximumDependencyCount = 65_536;
    private const int MaximumResourceCount = 4096;
    private const int MaximumSystemCount = 4096;
    private static readonly Guid s_resourceId = CreateGuid(100_000);

    private static ImmutableArray<SystemResourceAccess> CreateAccesses(int systemIndex, int count)
    {
        var builder = ImmutableArray.CreateBuilder<SystemResourceAccess>(count);

        for (var index = 0; index < count; index++)
        {
            builder.Add(new SystemResourceAccess(
                SystemResourceKind.Component,
                CreateGuid(200_000 + (systemIndex * 256) + index),
                SystemAccessMode.Read));
        }

        return builder.MoveToImmutable();
    }

    private static ImmutableArray<SystemDescription> CreateConflictComparisonRegistration(
        bool oneBeyond,
        out ImmutableArray<SystemResourceDescription> resources)
    {
        var resource1 = CreateResource(900_001, allowParallelAccess: true);
        var resource2 = CreateResource(900_002, allowParallelAccess: true);
        var resource3 = CreateResource(900_003, allowParallelAccess: true);
        var resource4 = CreateResource(900_004, allowParallelAccess: true);
        resources = [resource1, resource2, resource3, resource4];

        var fullRange = new SystemPartitionRange(0, 1);
        var access1 = new SystemResourceAccess(SystemResourceKind.Resource, resource1.ResourceId, SystemAccessMode.Write, fullRange);
        var access2 = new SystemResourceAccess(SystemResourceKind.Resource, resource2.ResourceId, SystemAccessMode.Write, fullRange);
        var access3 = new SystemResourceAccess(SystemResourceKind.Resource, resource3.ResourceId, SystemAccessMode.Write, fullRange);
        var access4Write = new SystemResourceAccess(SystemResourceKind.Resource, resource4.ResourceId, SystemAccessMode.Write, fullRange);
        var access4Read = new SystemResourceAccess(SystemResourceKind.Resource, resource4.ResourceId, SystemAccessMode.Read, fullRange);
        var descriptions = ImmutableArray.CreateBuilder<SystemDescription>(MaximumSystemCount);

        for (var index = 0; index < MaximumSystemCount; index++)
        {
            var accesses = ImmutableArray.CreateBuilder<SystemResourceAccess>(4);
            accesses.Add(access1);
            accesses.Add(access2);

            if (index < 91)
            {
                accesses.Add(access3);
            }

            if (index == 0)
            {
                accesses.Add(access4Write);
            }
            else if ((index == 1) || (oneBeyond && (index == 2)))
            {
                accesses.Add(access4Read);
            }

            descriptions.Add(CreateDescription(index + 1, index, access: accesses.ToImmutable()));
        }

        return descriptions.MoveToImmutable();
    }

    private static SystemDescription CreateDescription(
        int id,
        int order,
        SimulationStage stage = SimulationStage.Input,
        ImmutableArray<SystemResourceAccess> access = default,
        ImmutableArray<Guid> before = default,
        ImmutableArray<Guid> after = default,
        SystemThreadMode threadMode = SystemThreadMode.OwnerThread)
    {
        return new SystemDescription(
            CreateGuid(id),
            order,
            stage,
            access.IsDefault ? [] : access,
            before.IsDefault ? [] : before,
            after.IsDefault ? [] : after,
            threadMode);
    }

    private static Guid CreateGuid(int value) => new(value, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1);

    private static WorldSchedulingLimits CreateLimits(long maximumGraphBytes = 8L * 1024 * 1024, int maximumConflictComparisons = 16_777_216)
    {
        return new WorldSchedulingLimits(
            16L * 1024 * 1024,
            maximumGraphBytes,
            64L * 1024 * 1024,
            32L * 1024 * 1024,
            2L * 1024 * 1024 * 1024,
            8L * 1024 * 1024,
            maximumConflictComparisons);
    }

    private static SystemResourceDescription CreateResource(int id, int partitionCount = 1, bool allowParallelAccess = false)
        => new(CreateGuid(id), partitionCount, allowParallelAccess);

    private static ImmutableArray<Guid> GetExecutionIds(SystemExecutionSchedule schedule)
    {
        var builder = ImmutableArray.CreateBuilder<Guid>(schedule.ExecutionCount);

        for (var index = 0; index < schedule.ExecutionCount; index++)
        {
            builder.Add(schedule.GetSystemAtExecutionIndex(index).SystemId);
        }

        return builder.MoveToImmutable();
    }

    [Test]
    public void CompileAdmitsExactConflictComparisonLimitAndRejectsOneBelow()
    {
        const int MaximumComparisons = 16_777_216;
        var registration = CreateConflictComparisonRegistration(oneBeyond: false, out var resources);
        var belowLimit = Assert.Throws<SystemScheduleException>(() =>
            SystemExecutionSchedule.Compile(
                registration,
                resources,
                CreateLimits(maximumConflictComparisons: MaximumComparisons - 1)));

        Assert.That(belowLimit!.Failure, Is.EqualTo(SystemScheduleFailure.ConflictBudgetExceeded));
        Assert.That(belowLimit.Required, Is.EqualTo(MaximumComparisons));
        Assert.That(belowLimit.Maximum, Is.EqualTo(MaximumComparisons - 1));

        var atLimit = SystemExecutionSchedule.Compile(
            registration,
            resources,
            CreateLimits(maximumConflictComparisons: MaximumComparisons));

        Assert.That(atLimit.ExecutionCount, Is.EqualTo(MaximumSystemCount));
    }

    [Test]
    public void CompileAdmitsExactGlobalAccessCount()
    {
        const int SystemCount = 256;
        const int AccessesPerSystem = 256;
        var descriptions = ImmutableArray.CreateBuilder<SystemDescription>(SystemCount);

        for (var index = 0; index < SystemCount; index++)
        {
            descriptions.Add(CreateDescription(index + 1, index, access: CreateAccesses(index, AccessesPerSystem)));
        }

        var schedule = SystemExecutionSchedule.Compile(descriptions.MoveToImmutable(), WorldSchedulingLimits.Default);

        Assert.That(schedule.ExecutionCount, Is.EqualTo(SystemCount));
    }

    [Test]
    public void CompileAdmitsExactGlobalDependencyCount()
    {
        const int SystemCount = 257;
        var descriptions = ImmutableArray.CreateBuilder<SystemDescription>(SystemCount);
        var dependencyCount = 0;

        for (var systemIndex = 0; systemIndex < SystemCount; systemIndex++)
        {
            var before = ImmutableArray.CreateBuilder<Guid>();

            for (var dependencyIndex = systemIndex + 1; dependencyIndex < SystemCount; dependencyIndex++)
            {
                if ((systemIndex != 0) || (dependencyIndex > 128))
                {
                    before.Add(CreateGuid(dependencyIndex + 1));
                }
            }

            var after = ImmutableArray.CreateBuilder<Guid>();

            for (var dependencyIndex = 0; dependencyIndex < systemIndex; dependencyIndex++)
            {
                if ((dependencyIndex != 0) || (systemIndex > 128))
                {
                    after.Add(CreateGuid(dependencyIndex + 1));
                }
            }

            dependencyCount += before.Count + after.Count;
            descriptions.Add(CreateDescription(
                systemIndex + 1,
                systemIndex,
                before: before.ToImmutable(),
                after: after.ToImmutable()));
        }

        Assert.That(dependencyCount, Is.EqualTo(MaximumDependencyCount));

        var schedule = SystemExecutionSchedule.Compile(descriptions.MoveToImmutable(), WorldSchedulingLimits.Default);

        Assert.That(schedule.ExecutionCount, Is.EqualTo(SystemCount));
    }

    [Test]
    public void CompileAdmitsExactSystemAndResourceCounts()
    {
        var descriptions = ImmutableArray.CreateBuilder<SystemDescription>(MaximumSystemCount);
        var resources = ImmutableArray.CreateBuilder<SystemResourceDescription>(MaximumResourceCount);

        for (var index = 0; index < MaximumSystemCount; index++)
        {
            descriptions.Add(CreateDescription(index + 1, index));
        }

        for (var index = 0; index < MaximumResourceCount; index++)
        {
            resources.Add(CreateResource(800_000 + index));
        }

        var schedule = SystemExecutionSchedule.Compile(
            descriptions.MoveToImmutable(),
            resources.MoveToImmutable(),
            WorldSchedulingLimits.Default);

        Assert.That(schedule.ExecutionCount, Is.EqualTo(MaximumSystemCount));
    }

    [Test]
    public void CompileChargesConflictComparisonsBeforeBuildingConflictEdges()
    {
        var write = new SystemResourceAccess(SystemResourceKind.Component, s_resourceId, SystemAccessMode.Write);
        var exception = Assert.Throws<SystemScheduleException>(() =>
            SystemExecutionSchedule.Compile(
                [
                    CreateDescription(1, 0, access: [write]),
                    CreateDescription(2, 1, access: [write]),
                    CreateDescription(3, 2, access: [write]),
                ],
                CreateLimits(maximumConflictComparisons: 2)));

        Assert.That(exception!.Failure, Is.EqualTo(SystemScheduleFailure.ConflictBudgetExceeded));
        Assert.That(exception.Required, Is.EqualTo(3));
        Assert.That(exception.Maximum, Is.EqualTo(2));
    }

    [Test]
    public void CompileOrdersReadBeforeWriteBySignedOrder()
    {
        var read = new SystemResourceAccess(SystemResourceKind.Component, s_resourceId, SystemAccessMode.Read);
        var write = new SystemResourceAccess(SystemResourceKind.Component, s_resourceId, SystemAccessMode.Write);
        var schedule = SystemExecutionSchedule.Compile(
            [
                CreateDescription(1, 10, access: [write]),
                CreateDescription(2, -10, access: [read]),
            ],
            WorldSchedulingLimits.Default);

        Assert.That(GetExecutionIds(schedule), Is.EqualTo(ImmutableArray.Create(CreateGuid(2), CreateGuid(1))));

        var readOnly = new SystemResourceAccess(SystemResourceKind.Component, s_resourceId, SystemAccessMode.Read);
        var reverseDependency = SystemExecutionSchedule.Compile(
            [
                CreateDescription(1, 0, access: [readOnly]),
                CreateDescription(2, 1, access: [readOnly], before: [CreateGuid(1)]),
            ],
            WorldSchedulingLimits.Default);

        Assert.That(GetExecutionIds(reverseDependency), Is.EqualTo(ImmutableArray.Create(CreateGuid(2), CreateGuid(1))));
    }

    [Test]
    public void CompileProducesDeterministicOrderRespectingDependenciesAndSignedOrder()
    {
        var first = CreateDescription(1, 10, before: [CreateGuid(3)]);
        var second = CreateDescription(2, -2);
        var third = CreateDescription(3, -5);

        var left = SystemExecutionSchedule.Compile([first, second, third], WorldSchedulingLimits.Default);
        var right = SystemExecutionSchedule.Compile([third, first, second], WorldSchedulingLimits.Default);

        var expected = ImmutableArray.Create(CreateGuid(2), CreateGuid(1), CreateGuid(3));
        Assert.That(GetExecutionIds(left), Is.EqualTo(expected));
        Assert.That(GetExecutionIds(right), Is.EqualTo(expected));

        var after = SystemExecutionSchedule.Compile(
            [CreateDescription(2, -2, after: [CreateGuid(1)]), CreateDescription(1, 10)],
            WorldSchedulingLimits.Default);

        Assert.That(GetExecutionIds(after), Is.EqualTo(ImmutableArray.Create(CreateGuid(1), CreateGuid(2))));
    }

    [Test]
    public void CompileRejectsBackwardStageDependencyAndAcceptsForwardDependency()
    {
        var backward = Assert.Throws<SystemScheduleException>(() =>
            SystemExecutionSchedule.Compile(
                [
                    CreateDescription(1, 0, SimulationStage.Physics, before: [CreateGuid(2)]),
                    CreateDescription(2, 1, SimulationStage.Input),
                ],
                WorldSchedulingLimits.Default));

        Assert.That(backward!.Failure, Is.EqualTo(SystemScheduleFailure.BackwardStageDependency));
        Assert.That(backward.SystemId, Is.EqualTo(CreateGuid(1)));
        Assert.That(backward.RelatedSystemId, Is.EqualTo(CreateGuid(2)));

        var forward = SystemExecutionSchedule.Compile(
            [
                CreateDescription(1, 0, SimulationStage.Input, before: [CreateGuid(2)]),
                CreateDescription(2, 1, SimulationStage.Physics),
            ],
            WorldSchedulingLimits.Default);

        Assert.That(GetExecutionIds(forward), Is.EqualTo(ImmutableArray.Create(CreateGuid(1), CreateGuid(2))));
        Assert.That(forward.GetStageStart(SimulationStage.Input), Is.Zero);
        Assert.That(forward.GetStageEnd(SimulationStage.Input), Is.EqualTo(1));
        Assert.That(forward.GetStageStart(SimulationStage.Physics), Is.EqualTo(1));
        Assert.That(forward.GetStageEnd(SimulationStage.Physics), Is.EqualTo(2));
    }

    [Test]
    public void CompileRejectsDefaultDescriptions()
    {
        var exception = Assert.Throws<ArgumentException>(() => SystemExecutionSchedule.Compile(default, WorldSchedulingLimits.Default));

        Assert.That(exception!.ParamName, Is.EqualTo("descriptions"));

        var defaultResources = Assert.Throws<ArgumentException>(() =>
            SystemExecutionSchedule.Compile([], default, WorldSchedulingLimits.Default));

        Assert.That(defaultResources!.ParamName, Is.EqualTo("resourceDescriptions"));
    }

    [Test]
    public void CompileRejectsDuplicateSystemIdentityAndOrder()
    {
        var duplicateIdentity = Assert.Throws<SystemScheduleException>(() =>
            SystemExecutionSchedule.Compile(
                [CreateDescription(1, 0), CreateDescription(1, 1)],
                WorldSchedulingLimits.Default));

        Assert.That(duplicateIdentity!.Failure, Is.EqualTo(SystemScheduleFailure.DuplicateSystemId));

        var duplicateOrder = Assert.Throws<SystemScheduleException>(() =>
            SystemExecutionSchedule.Compile(
                [CreateDescription(1, 0), CreateDescription(2, 0)],
                WorldSchedulingLimits.Default));

        Assert.That(duplicateOrder!.Failure, Is.EqualTo(SystemScheduleFailure.DuplicateSystemOrder));

        var duplicateResource = Assert.Throws<SystemScheduleException>(() =>
            SystemExecutionSchedule.Compile([], [CreateResource(3), CreateResource(3)], WorldSchedulingLimits.Default));

        Assert.That(duplicateResource!.Failure, Is.EqualTo(SystemScheduleFailure.DuplicateResourceId));
        Assert.That(duplicateResource.ResourceId, Is.EqualTo(CreateGuid(3)));
    }

    [Test]
    public void CompileRejectsExcessiveGlobalAccessCountBeforeGraphAllocation()
    {
        const int SystemCount = 257;
        var descriptions = ImmutableArray.CreateBuilder<SystemDescription>(SystemCount);

        for (var index = 0; index < SystemCount; index++)
        {
            descriptions.Add(CreateDescription(index + 1, index, access: CreateAccesses(index, 256)));
        }

        var exception = Assert.Throws<SystemScheduleException>(() =>
            SystemExecutionSchedule.Compile(descriptions.MoveToImmutable(), WorldSchedulingLimits.Default));

        Assert.That(exception!.Failure, Is.EqualTo(SystemScheduleFailure.AccessCountExceeded));
        Assert.That(exception.Required, Is.EqualTo((long)SystemCount * 256));
        Assert.That(exception.Maximum, Is.EqualTo(MaximumAccessCount));
    }

    [Test]
    public void CompileRejectsExcessiveGlobalDependencyCountBeforeGraphAllocation()
    {
        const int SystemCount = 17;
        const int PerSystemDependencyCount = 4095;
        var descriptions = ImmutableArray.CreateBuilder<SystemDescription>(SystemCount);

        for (var systemIndex = 0; systemIndex < SystemCount; systemIndex++)
        {
            var dependencies = ImmutableArray.CreateBuilder<Guid>(PerSystemDependencyCount);

            for (var dependencyIndex = 0; dependencyIndex < PerSystemDependencyCount; dependencyIndex++)
            {
                dependencies.Add(CreateGuid(500_000 + (systemIndex * PerSystemDependencyCount) + dependencyIndex));
            }

            descriptions.Add(CreateDescription(systemIndex + 1, systemIndex, before: dependencies.MoveToImmutable()));
        }

        var exception = Assert.Throws<SystemScheduleException>(() =>
            SystemExecutionSchedule.Compile(descriptions.MoveToImmutable(), WorldSchedulingLimits.Default));

        Assert.That(exception!.Failure, Is.EqualTo(SystemScheduleFailure.DependencyCountExceeded));
        Assert.That(exception.Required, Is.EqualTo((long)SystemCount * PerSystemDependencyCount));
        Assert.That(exception.Maximum, Is.EqualTo(MaximumDependencyCount));
    }

    [Test]
    public void CompileRejectsExcessiveResourceCountBeforeGraphAllocation()
    {
        var resources = ImmutableArray.CreateBuilder<SystemResourceDescription>(MaximumResourceCount + 1);

        for (var index = 0; index <= MaximumResourceCount; index++)
        {
            resources.Add(CreateResource(300_000 + index));
        }

        var exception = Assert.Throws<SystemScheduleException>(() =>
            SystemExecutionSchedule.Compile([], resources.MoveToImmutable(), WorldSchedulingLimits.Default));

        Assert.That(exception!.Failure, Is.EqualTo(SystemScheduleFailure.ResourceCountExceeded));
        Assert.That(exception.Required, Is.EqualTo(MaximumResourceCount + 1));
        Assert.That(exception.Maximum, Is.EqualTo(MaximumResourceCount));
    }

    [Test]
    public void CompileRejectsExcessiveSystemCountBeforeAllocatingGraph()
    {
        var descriptions = ImmutableArray.CreateBuilder<SystemDescription>(MaximumSystemCount + 1);

        for (var index = 0; index <= MaximumSystemCount; index++)
        {
            descriptions.Add(CreateDescription(index + 1, index));
        }

        var exception = Assert.Throws<SystemScheduleException>(() =>
            SystemExecutionSchedule.Compile(descriptions.MoveToImmutable(), WorldSchedulingLimits.Default));

        Assert.That(exception!.Failure, Is.EqualTo(SystemScheduleFailure.SystemCountExceeded));
        Assert.That(exception.Required, Is.EqualTo(MaximumSystemCount + 1));
        Assert.That(exception.Maximum, Is.EqualTo(MaximumSystemCount));
    }

    [Test]
    public void CompileRejectsGraphExceedingConfiguredByteBudget()
    {
        var descriptions = ImmutableArray.CreateBuilder<SystemDescription>(128);

        for (var index = 0; index < 128; index++)
        {
            descriptions.Add(CreateDescription(index + 1, index));
        }

        var exception = Assert.Throws<SystemScheduleException>(() =>
            SystemExecutionSchedule.Compile(descriptions.MoveToImmutable(), CreateLimits(maximumGraphBytes: 4096)));

        Assert.That(exception!.Failure, Is.EqualTo(SystemScheduleFailure.GraphBudgetExceeded));
        Assert.That(exception.Required, Is.GreaterThan(exception.Maximum));
        Assert.That(exception.Maximum, Is.EqualTo(4096));

        const int ResourceCount = 1025;
        var resources = ImmutableArray.CreateBuilder<SystemResourceDescription>(ResourceCount);

        for (var index = 0; index < ResourceCount; index++)
        {
            resources.Add(CreateResource(400_000 + index));
        }

        var resourceGraphException = Assert.Throws<SystemScheduleException>(() =>
            SystemExecutionSchedule.Compile([], resources.MoveToImmutable(), CreateLimits(maximumGraphBytes: 4096)));

        Assert.That(resourceGraphException!.Failure, Is.EqualTo(SystemScheduleFailure.GraphBudgetExceeded));
        Assert.That(resourceGraphException.Required, Is.GreaterThan(resourceGraphException.Maximum));
    }

    [Test]
    public void CompileRejectsOneBeyondConflictComparisonLimit()
    {
        const int MaximumComparisons = 16_777_216;
        var registration = CreateConflictComparisonRegistration(oneBeyond: true, out var resources);
        var exception = Assert.Throws<SystemScheduleException>(() =>
            SystemExecutionSchedule.Compile(
                registration,
                resources,
                CreateLimits(maximumConflictComparisons: MaximumComparisons)));

        Assert.That(exception!.Failure, Is.EqualTo(SystemScheduleFailure.ConflictBudgetExceeded));
        Assert.That(exception.Required, Is.EqualTo(MaximumComparisons + 1));
        Assert.That(exception.Maximum, Is.EqualTo(MaximumComparisons));
    }

    [Test]
    public void CompileRejectsUnknownAccessedResource()
    {
        var access = new SystemResourceAccess(SystemResourceKind.Resource, CreateGuid(2), SystemAccessMode.Read);
        var exception = Assert.Throws<SystemScheduleException>(() =>
            SystemExecutionSchedule.Compile(
                [CreateDescription(1, 0, access: [access])],
                WorldSchedulingLimits.Default));

        Assert.That(exception!.Failure, Is.EqualTo(SystemScheduleFailure.UnknownResource));
        Assert.That(exception.SystemId, Is.EqualTo(CreateGuid(1)));
        Assert.That(exception.ResourceId, Is.EqualTo(CreateGuid(2)));
    }

    [Test]
    public void CompileRejectsUnknownDependency()
    {
        var exception = Assert.Throws<SystemScheduleException>(() =>
            SystemExecutionSchedule.Compile(
                [CreateDescription(1, 0, before: [CreateGuid(2)])],
                WorldSchedulingLimits.Default));

        Assert.That(exception!.Failure, Is.EqualTo(SystemScheduleFailure.UnknownDependency));
        Assert.That(exception.SystemId, Is.EqualTo(CreateGuid(1)));
        Assert.That(exception.RelatedSystemId, Is.EqualTo(CreateGuid(2)));
    }

    [Test]
    public void CompileReportsDeterministicBoundedCyclePath()
    {
        var descriptions = ImmutableArray.Create(
            CreateDescription(1, 0, before: [CreateGuid(2)]),
            CreateDescription(2, 1, before: [CreateGuid(3)]),
            CreateDescription(3, 2, before: [CreateGuid(1)]));
        var exception = Assert.Throws<SystemScheduleException>(() =>
            SystemExecutionSchedule.Compile(descriptions, WorldSchedulingLimits.Default));
        Guid[] expectedCycle = [CreateGuid(1), CreateGuid(2), CreateGuid(3), CreateGuid(1)];

        Assert.That(exception!.Failure, Is.EqualTo(SystemScheduleFailure.DependencyCycle));
        Assert.That(exception.Stage, Is.EqualTo((int)SimulationStage.Input));
        Assert.That(exception.CyclePath.ToArray(), Is.EqualTo(expectedCycle));

        var reorderedException = Assert.Throws<SystemScheduleException>(() =>
            SystemExecutionSchedule.Compile(
                [descriptions[2], descriptions[0], descriptions[1]],
                WorldSchedulingLimits.Default));

        Assert.That(reorderedException!.CyclePath.ToArray(), Is.EqualTo(expectedCycle));
    }

    [Test]
    public void CompileUsesOverlappingRangesForWriteConflicts()
    {
        var firstWrite = new SystemResourceAccess(
            SystemResourceKind.Component,
            s_resourceId,
            SystemAccessMode.Write,
            new SystemPartitionRange(0, 4));
        var overlappingWrite = new SystemResourceAccess(
            SystemResourceKind.Component,
            s_resourceId,
            SystemAccessMode.Write,
            new SystemPartitionRange(3, 2));
        var disjointWrite = new SystemResourceAccess(
            SystemResourceKind.Component,
            s_resourceId,
            SystemAccessMode.Write,
            new SystemPartitionRange(4, 2));

        var overlapping = Assert.Throws<SystemScheduleException>(() =>
            SystemExecutionSchedule.Compile(
                [
                    CreateDescription(1, 0, access: [firstWrite]),
                    CreateDescription(2, 1, access: [overlappingWrite], before: [CreateGuid(1)]),
                ],
                WorldSchedulingLimits.Default));

        Assert.That(overlapping!.Failure, Is.EqualTo(SystemScheduleFailure.DependencyCycle));
        Guid[] overlappingCycle = [CreateGuid(1), CreateGuid(2), CreateGuid(1)];
        Assert.That(overlapping.CyclePath.ToArray(), Is.EqualTo(overlappingCycle));

        var disjoint = SystemExecutionSchedule.Compile(
            [
                CreateDescription(1, 0, access: [firstWrite]),
                CreateDescription(2, 1, access: [disjointWrite], before: [CreateGuid(1)]),
            ],
            WorldSchedulingLimits.Default);

        Assert.That(GetExecutionIds(disjoint), Is.EqualTo(ImmutableArray.Create(CreateGuid(2), CreateGuid(1))));
    }

    [Test]
    public void CompileValidatesResourcePartitionRangesAndParallelEligibility()
    {
        var resource = CreateResource(3, partitionCount: 4);
        var resources = ImmutableArray.Create(CreateResource(4), resource);
        var inRangeAccess = new SystemResourceAccess(
            SystemResourceKind.Resource,
            resource.ResourceId,
            SystemAccessMode.Read,
            new SystemPartitionRange(2, 2));

        var schedule = SystemExecutionSchedule.Compile(
            [CreateDescription(1, 0, access: [inRangeAccess])],
            resources,
            WorldSchedulingLimits.Default);

        Assert.That(GetExecutionIds(schedule), Is.EqualTo(ImmutableArray.Create(CreateGuid(1))));

        var outOfRangeAccess = new SystemResourceAccess(
            SystemResourceKind.Resource,
            resource.ResourceId,
            SystemAccessMode.Read,
            new SystemPartitionRange(3, 2));
        var outOfRange = Assert.Throws<SystemScheduleException>(() =>
            SystemExecutionSchedule.Compile(
                [CreateDescription(1, 0, access: [outOfRangeAccess])],
                resources,
                WorldSchedulingLimits.Default));

        Assert.That(outOfRange!.Failure, Is.EqualTo(SystemScheduleFailure.ResourcePartitionRangeExceeded));
        Assert.That(outOfRange.ResourceId, Is.EqualTo(resource.ResourceId));

        var parallelAccess = new SystemResourceAccess(SystemResourceKind.Resource, resource.ResourceId, SystemAccessMode.Read);
        var parallelDenied = Assert.Throws<SystemScheduleException>(() =>
            SystemExecutionSchedule.Compile(
                [CreateDescription(1, 0, access: [parallelAccess], threadMode: SystemThreadMode.Parallel)],
                resources,
                WorldSchedulingLimits.Default));

        Assert.That(parallelDenied!.Failure, Is.EqualTo(SystemScheduleFailure.ParallelResourceAccessDenied));
        Assert.That(parallelDenied.ResourceId, Is.EqualTo(resource.ResourceId));

        var parallelResource = CreateResource(3, allowParallelAccess: true);
        var parallelAllowed = SystemExecutionSchedule.Compile(
            [CreateDescription(1, 0, access: [parallelAccess], threadMode: SystemThreadMode.Parallel)],
            [parallelResource],
            WorldSchedulingLimits.Default);

        Assert.That(GetExecutionIds(parallelAllowed), Is.EqualTo(ImmutableArray.Create(CreateGuid(1))));
    }
}
