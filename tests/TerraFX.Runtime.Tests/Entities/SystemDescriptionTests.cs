// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using NUnit.Framework;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests;

[TestFixture]
internal sealed class SystemDescriptionTests
{
    private const int MaximumAccessCount = 256;
    private const int MaximumDependencyCount = 4095;
    private static readonly Guid s_systemId = CreateGuid(1);

    private static ImmutableArray<SystemResourceAccess> CreateAccesses(int count)
    {
        var builder = ImmutableArray.CreateBuilder<SystemResourceAccess>(count);

        for (var index = 0; index < count; index++)
        {
            builder.Add(new SystemResourceAccess(SystemResourceKind.Component, CreateGuid(index + 10), SystemAccessMode.Read));
        }

        return builder.MoveToImmutable();
    }

    private static ImmutableArray<Guid> CreateDependencies(int count)
    {
        var builder = ImmutableArray.CreateBuilder<Guid>(count);

        for (var index = 0; index < count; index++)
        {
            builder.Add(CreateGuid(index + 1000));
        }

        return builder.MoveToImmutable();
    }

    private static SystemDescription CreateDescription(
        Guid? systemId = null,
        SimulationStage stage = SimulationStage.Input,
        ImmutableArray<SystemResourceAccess> access = default,
        ImmutableArray<Guid> before = default,
        ImmutableArray<Guid> after = default,
        SystemThreadMode threadMode = SystemThreadMode.OwnerThread)
    {
        return new SystemDescription(
            systemId ?? s_systemId,
            0,
            stage,
            access.IsDefault ? [] : access,
            before.IsDefault ? [] : before,
            after.IsDefault ? [] : after,
            threadMode);
    }

    private static Guid CreateGuid(int value) => new(value, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1);

    [Test]
    public void AccessCountAdmitsMaximumAndRejectsFirstExcess()
    {
        var maximum = CreateAccesses(MaximumAccessCount);
        var excess = CreateAccesses(MaximumAccessCount + 1);

        Assert.That(CreateDescription(access: maximum).Access.Length, Is.EqualTo(MaximumAccessCount));

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => CreateDescription(access: excess));

        Assert.That(exception!.ParamName, Is.EqualTo("access"));
    }

    [Test]
    public void ConstructorCopiesAllMetadataArrays()
    {
        var access = new[] { new SystemResourceAccess(SystemResourceKind.Component, CreateGuid(2), SystemAccessMode.Read) };
        var before = new[] { CreateGuid(3) };
        var after = new[] { CreateGuid(4) };
        var description = new SystemDescription(
            s_systemId,
            int.MinValue,
            SimulationStage.Input,
            ImmutableCollectionsMarshal.AsImmutableArray(access),
            ImmutableCollectionsMarshal.AsImmutableArray(before),
            ImmutableCollectionsMarshal.AsImmutableArray(after),
            SystemThreadMode.OwnerThread);

        access[0] = default;
        before[0] = Guid.Empty;
        after[0] = Guid.Empty;

        Assert.That(description.Access[0].IsValid, Is.True);
        Assert.That(description.Before[0], Is.EqualTo(CreateGuid(3)));
        Assert.That(description.After[0], Is.EqualTo(CreateGuid(4)));
        Assert.That(description.Order, Is.EqualTo(int.MinValue));
    }

    [Test]
    public void ConstructorRejectsDefaultArrays()
    {
        Assert.That(
            Assert.Throws<ArgumentException>(() => new SystemDescription(
                s_systemId,
                0,
                SimulationStage.Input,
                default,
                [],
                [],
                SystemThreadMode.OwnerThread))!.ParamName,
            Is.EqualTo("access"));
        Assert.That(
            Assert.Throws<ArgumentException>(() => new SystemDescription(
                s_systemId,
                0,
                SimulationStage.Input,
                [],
                default,
                [],
                SystemThreadMode.OwnerThread))!.ParamName,
            Is.EqualTo("before"));
        Assert.That(
            Assert.Throws<ArgumentException>(() => new SystemDescription(
                s_systemId,
                0,
                SimulationStage.Input,
                [],
                [],
                default,
                SystemThreadMode.OwnerThread))!.ParamName,
            Is.EqualTo("after"));
    }

    [Test]
    public void ConstructorRejectsDuplicateAccessIdentity()
    {
        var identity = CreateGuid(5);
        var access = ImmutableArray.Create(
            new SystemResourceAccess(SystemResourceKind.Component, identity, SystemAccessMode.Read),
            new SystemResourceAccess(SystemResourceKind.Component, identity, SystemAccessMode.Write, new SystemPartitionRange(0, 1)));

        var exception = Assert.Throws<ArgumentException>(() => CreateDescription(access: access));

        Assert.That(exception!.ParamName, Is.EqualTo("access"));
    }

    [Test]
    public void ConstructorRejectsDuplicateDependenciesWithinOneList()
    {
        var duplicate = CreateGuid(6);
        var exception = Assert.Throws<ArgumentException>(() => CreateDescription(before: [duplicate, duplicate]));

        Assert.That(exception!.ParamName, Is.EqualTo("before"));
    }

    [Test]
    public void ConstructorRejectsEmptyDependencyIdentity()
    {
        var exception = Assert.Throws<ArgumentException>(() => CreateDescription(after: [Guid.Empty]));

        Assert.That(exception!.ParamName, Is.EqualTo("after"));
    }

    [Test]
    public void ConstructorRejectsEmptySystemIdentity()
    {
        var exception = Assert.Throws<ArgumentException>(() => CreateDescription(systemId: Guid.Empty));

        Assert.That(exception!.ParamName, Is.EqualTo("systemId"));
    }

    [Test]
    public void ConstructorRejectsSelfDependency()
    {
        var exception = Assert.Throws<ArgumentException>(() => CreateDescription(before: [s_systemId]));

        Assert.That(exception!.ParamName, Is.EqualTo("before"));
    }

    [TestCase((SimulationStage)(-1))]
    [TestCase((SimulationStage)10)]
    [TestCase(SimulationStage.Commit)]
    public void ConstructorRejectsStagesOutsideUserExecution(SimulationStage stage)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => CreateDescription(stage: stage));

        Assert.That(exception!.ParamName, Is.EqualTo("stage"));
    }

    [TestCase(SystemThreadMode.None)]
    [TestCase((SystemThreadMode)3)]
    public void ConstructorRejectsUnsupportedThreadModes(SystemThreadMode threadMode)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => CreateDescription(threadMode: threadMode));

        Assert.That(exception!.ParamName, Is.EqualTo("threadMode"));
    }

    [Test]
    public void DependencyCountAdmitsMaximumAndRejectsFirstExcess()
    {
        var maximum = CreateDependencies(MaximumDependencyCount);
        var excess = CreateDependencies(MaximumDependencyCount + 1);

        Assert.That(CreateDescription(before: maximum).Before.Length, Is.EqualTo(MaximumDependencyCount));

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => CreateDescription(after: excess));

        Assert.That(exception!.ParamName, Is.EqualTo("after"));
    }

    [Test]
    public void UnknownForwardDependenciesAreRetainedForWorldValidation()
    {
        var dependency = CreateGuid(7);
        var description = CreateDescription(before: [dependency]);

        Assert.That(description.Before, Is.EqualTo(ImmutableArray.Create(dependency)));
    }
}
