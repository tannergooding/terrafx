// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using NUnit.Framework;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldOwnerCommandTests
{
    [Test]
    public void EntityBatchCommitsAsOneIdentityTransaction()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        using var world = World.Create(new WorldOptions(registration));
        var initialAssignmentCount = world.IdentityLedger.AssignmentCount;
        var duplicateId = new Guid("b2000000-0000-4000-8000-000000000001");

        Assert.That(
            () => world.CreateEntitiesAtomically(
                2,
                _ => [
                    new EntityDescription(persistentId: duplicateId),
                    new EntityDescription(persistentId: duplicateId),
                ]),
            Throws.ArgumentException);
        Assert.That(world.IdentityLedger.AssignmentCount, Is.EqualTo(initialAssignmentCount));

        var entities = world.CreateEntitiesAtomically(
            2,
            static _ => [
                new EntityDescription(),
                new EntityDescription(),
            ]);

        Assert.That(entities, Has.Length.EqualTo(2));
        Assert.That(entities[0], Is.Not.EqualTo(entities[1]));
        Assert.That(world.GetEntityId(entities[0]), Is.Not.EqualTo(Guid.Empty));
        Assert.That(world.GetEntityId(entities[1]), Is.Not.EqualTo(Guid.Empty));
        Assert.That(world.IdentityLedger.AssignmentCount, Is.EqualTo(initialAssignmentCount + 2));
    }
}
