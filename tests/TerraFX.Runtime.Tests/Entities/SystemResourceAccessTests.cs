// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests;

[TestFixture]
internal sealed class SystemResourceAccessTests
{
    private static readonly Guid s_resourceId = new("11111111-1111-4111-8111-111111111111");

    [Test]
    public void ConstructorRejectsEmptyResourceIdentity()
    {
        var exception = Assert.Throws<ArgumentException>(() => new SystemResourceAccess(SystemResourceKind.Component, Guid.Empty, SystemAccessMode.Read));

        Assert.That(exception!.ParamName, Is.EqualTo("resourceId"));
    }

    [TestCase(SystemAccessMode.None)]
    [TestCase((SystemAccessMode)3)]
    public void ConstructorRejectsUnsupportedAccessModes(SystemAccessMode mode)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new SystemResourceAccess(SystemResourceKind.Component, s_resourceId, mode));

        Assert.That(exception!.ParamName, Is.EqualTo("mode"));
    }

    [TestCase(SystemResourceKind.None)]
    [TestCase((SystemResourceKind)5)]
    public void ConstructorRejectsUnsupportedResourceKinds(SystemResourceKind kind)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new SystemResourceAccess(kind, s_resourceId, SystemAccessMode.Read));

        Assert.That(exception!.ParamName, Is.EqualTo("kind"));
    }

    [Test]
    public void DefaultAccessIsInvalid()
    {
        Assert.That(default(SystemResourceAccess).IsValid, Is.False);
    }

    [Test]
    public void EntityMembershipRejectsAnUnrelatedIdentity()
    {
        var exception = Assert.Throws<ArgumentException>(() => new SystemResourceAccess(SystemResourceKind.EntityMembership, s_resourceId, SystemAccessMode.Read));

        Assert.That(exception!.ParamName, Is.EqualTo("resourceId"));
    }

    [Test]
    public void EntityMembershipRejectsPartitionRanges()
    {
        var exception = Assert.Throws<ArgumentException>(() => new SystemResourceAccess(
            SystemResourceKind.EntityMembership,
            WorldSystemResources.EntityMembershipId,
            SystemAccessMode.Read,
            new SystemPartitionRange(0, 1)));

        Assert.That(exception!.ParamName, Is.EqualTo("range"));
    }

    [Test]
    public void EntityMembershipRejectsWriteAccess()
    {
        var exception = Assert.Throws<ArgumentException>(() => new SystemResourceAccess(SystemResourceKind.EntityMembership, WorldSystemResources.EntityMembershipId, SystemAccessMode.Write));

        Assert.That(exception!.ParamName, Is.EqualTo("mode"));
    }

    [Test]
    public void EntityMembershipUsesItsFixedReadOnlyIdentity()
    {
        var access = new SystemResourceAccess(SystemResourceKind.EntityMembership, WorldSystemResources.EntityMembershipId, SystemAccessMode.Read);

        Assert.That(WorldSystemResources.EntityMembershipId, Is.EqualTo(new Guid("5ae0d511-7e5f-81df-bb07-e2b259892443")));
        Assert.That(access.IsValid, Is.True);
        Assert.That(access.Range, Is.Null);
    }

    [Test]
    public void EqualityPreservesAbsentAndPresentRange()
    {
        var absent = new SystemResourceAccess(SystemResourceKind.Component, s_resourceId, SystemAccessMode.Read);
        var present = new SystemResourceAccess(SystemResourceKind.Component, s_resourceId, SystemAccessMode.Read, new SystemPartitionRange(0, 16));
        var samePresent = new SystemResourceAccess(SystemResourceKind.Component, s_resourceId, SystemAccessMode.Read, new SystemPartitionRange(0, 16));

        Assert.That(absent.Range, Is.Null);
        Assert.That(absent.IsValid, Is.True);
        Assert.That(present.Range, Is.EqualTo(new SystemPartitionRange(0, 16)));
        Assert.That(absent, Is.Not.EqualTo(present));
        Assert.That(present, Is.EqualTo(samePresent));
        Assert.That(present.GetHashCode(), Is.EqualTo(samePresent.GetHashCode()));
    }

    [Test]
    public void RangedConstructorRejectsDefaultRange()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new SystemResourceAccess(SystemResourceKind.Resource, s_resourceId, SystemAccessMode.Write, default));

        Assert.That(exception!.ParamName, Is.EqualTo("range"));
    }

    [Test]
    public void ResourceDescriptionAdmitsOnePartitionAndTheMaximum()
    {
        var single = new SystemResourceDescription(s_resourceId, 1, allowParallelAccess: false);
        var maximum = new SystemResourceDescription(s_resourceId, 1_048_576, allowParallelAccess: true);

        Assert.That(single.PartitionCount, Is.EqualTo(1));
        Assert.That(single.AllowParallelAccess, Is.False);
        Assert.That(maximum.PartitionCount, Is.EqualTo(1_048_576));
        Assert.That(maximum.AllowParallelAccess, Is.True);
    }

    [Test]
    public void ResourceDescriptionRejectsEmptyIdentity()
    {
        var exception = Assert.Throws<ArgumentException>(() => new SystemResourceDescription(Guid.Empty, 1, allowParallelAccess: true));

        Assert.That(exception!.ParamName, Is.EqualTo("resourceId"));
    }

    [Test]
    public void ResourceDescriptionRejectsEntityMembershipIdentity()
    {
        var exception = Assert.Throws<ArgumentException>(() => new SystemResourceDescription(WorldSystemResources.EntityMembershipId, 1, allowParallelAccess: true));

        Assert.That(exception!.ParamName, Is.EqualTo("resourceId"));
    }

    [TestCase(0)]
    [TestCase(1_048_577)]
    public void ResourceDescriptionRejectsPartitionCountOutsideItsDomain(int partitionCount)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new SystemResourceDescription(s_resourceId, partitionCount, allowParallelAccess: true));

        Assert.That(exception!.ParamName, Is.EqualTo("partitionCount"));
    }

    [Test]
    public void TagWriteAccessIsRejected()
    {
        var exception = Assert.Throws<ArgumentException>(() => new SystemResourceAccess(SystemResourceKind.Tag, s_resourceId, SystemAccessMode.Write));

        Assert.That(exception!.ParamName, Is.EqualTo("mode"));
    }
}
