// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests;

[TestFixture]
internal sealed class WorldEntityIdentityTests
{
    private static readonly Guid s_branchId = new("baac4901-3bef-4e07-92c9-f14c88ea5293");
    private static readonly Guid s_entityId = new("5c81e833-50cc-4429-802e-4de9a460f7fa");
    private static readonly Guid s_lineageId = new("28657578-3ea4-4ff7-8a8f-bca092421206");
    private static readonly Guid s_worldGuid = new("d44281f7-2dbc-49c7-8bd1-57d2573d0c32");

    private static WorldEntityIdentity CreateIdentity(int emptyPart = -1)
        => new(
            emptyPart == 0 ? Guid.Empty : s_worldGuid,
            emptyPart == 1 ? Guid.Empty : s_lineageId,
            emptyPart == 2 ? Guid.Empty : s_branchId,
            emptyPart == 3 ? Guid.Empty : s_entityId);

    [TestCase(0, "worldGuid")]
    [TestCase(1, "creationLineageId")]
    [TestCase(2, "creationBranchId")]
    [TestCase(3, "entityId")]
    public void ConstructorRejectsEmptyIdentityComponents(int emptyPart, string parameterName)
    {
        var exception = Assert.Throws<ArgumentException>(() => CreateIdentity(emptyPart));

        Assert.That(exception!.ParamName, Is.EqualTo(parameterName));
    }

    [Test]
    public void DefaultIdentityIsInvalid()
    {
        Assert.That(default(WorldEntityIdentity).IsValid, Is.False);
    }

    [Test]
    public void EqualityUsesTheCompleteWorldLineageBranchEntityTuple()
    {
        var identity = CreateIdentity();
        var sameIdentity = CreateIdentity();

        Assert.That(identity.IsValid, Is.True);
        Assert.That(identity, Is.EqualTo(sameIdentity));
        Assert.That(identity == sameIdentity, Is.True);
        Assert.That(identity.GetHashCode(), Is.EqualTo(sameIdentity.GetHashCode()));

        Assert.That(
            identity == new WorldEntityIdentity(new Guid("d44281f7-2dbc-49c7-8bd1-57d2573d0c33"), s_lineageId, s_branchId, s_entityId),
            Is.False);
        Assert.That(
            identity == new WorldEntityIdentity(s_worldGuid, new Guid("28657578-3ea4-4ff7-8a8f-bca092421207"), s_branchId, s_entityId),
            Is.False);
        Assert.That(
            identity == new WorldEntityIdentity(s_worldGuid, s_lineageId, new Guid("baac4901-3bef-4e07-92c9-f14c88ea5294"), s_entityId),
            Is.False);
        Assert.That(
            identity == new WorldEntityIdentity(s_worldGuid, s_lineageId, s_branchId, new Guid("5c81e833-50cc-4429-802e-4de9a460f7fb")),
            Is.False);
        Assert.That(identity != sameIdentity, Is.False);
    }
}
