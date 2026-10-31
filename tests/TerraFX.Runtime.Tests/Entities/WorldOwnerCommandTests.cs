// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using NUnit.Framework;
using TerraFX.Entities;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldOwnerCommandTests
{
    [Test]
    public void DestroySubtreeRetiresItsLiveDescendants()
    {
        var schemas = TerraFX.Runtime.Tests.Scenes.SceneAssetReaderTests.Fixtures.CreateRegistry();
        var builder = new WorldRegistrationBuilder(schemas);
        var sceneSupport = SceneSupport.CreateRegistration();
        _ = builder.RegisterSceneSupport(sceneSupport);
        using var world = World.Create(new WorldOptions(builder.Build()));
        var participant = world.GetSceneParticipant(sceneSupport);
        var entities = world.CreateEntitiesAtomically(
            3,
            static _ => [
                new EntityDescription(),
                new EntityDescription(),
                new EntityDescription(),
            ]);
        var root = entities[0];
        var child = entities[1];
        var grandchild = entities[2];
        world.ApplyHierarchyParentChange(participant, child, root);
        world.ApplyHierarchyParentChange(participant, grandchild, child);
        var rootId = world.GetEntityId(root);
        var childId = world.GetEntityId(child);
        var grandchildId = world.GetEntityId(grandchild);

        Assert.That(world.TryGetHierarchyParent(child, out var actualParent), Is.True);
        Assert.That(actualParent, Is.EqualTo(root));
        Assert.That(world.TryGetHierarchyParent(grandchild, out actualParent), Is.True);
        Assert.That(actualParent, Is.EqualTo(child));

        Assert.That(
            () => world.DestroyEntity(root, HierarchyDestroyMode.DetachChildrenKeepWorld),
            Throws.InvalidOperationException);
        Assert.That(world.TryGetEntity(rootId, out var stillLiveRoot), Is.True);
        Assert.That(stillLiveRoot, Is.EqualTo(root));
        Assert.That(world.TryGetEntity(childId, out _), Is.True);
        Assert.That(world.TryGetEntity(grandchildId, out _), Is.True);

        world.DestroyEntity(root, HierarchyDestroyMode.DestroySubtree);

        Assert.That(world.TryGetEntity(rootId, out _), Is.False);
        Assert.That(world.TryGetEntity(childId, out _), Is.False);
        Assert.That(world.TryGetEntity(grandchildId, out _), Is.False);
    }

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

    [Test]
    public void SceneEntityBatchAssignsSceneIdentityBeforeEntityIdentities()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        using var world = World.Create(new WorldOptions(registration));
        var commit = world.CreateSceneEntitiesAtomically(
            2,
            requestedSceneInstanceId: null,
            static _ => [
                new EntityDescription(),
                new EntityDescription(),
            ]);

        Assert.That(commit.SceneInstanceId, Is.Not.EqualTo(Guid.Empty));
        Assert.That(commit.Entities, Has.Length.EqualTo(2));
        Assert.That(world.IdentityLedger.TryFind(commit.SceneInstanceId, out var sceneOrdinal), Is.True);
        Assert.That(sceneOrdinal, Is.EqualTo(1));
        Assert.That(world.IdentityLedger.TryFind(world.GetEntityId(commit.Entities[0]), out var firstEntityOrdinal), Is.True);
        Assert.That(world.IdentityLedger.TryFind(world.GetEntityId(commit.Entities[1]), out var secondEntityOrdinal), Is.True);
        Assert.That(firstEntityOrdinal, Is.EqualTo(2));
        Assert.That(secondEntityOrdinal, Is.EqualTo(3));

        var emptyCommit = world.CreateSceneEntitiesAtomically(
            0,
            requestedSceneInstanceId: null,
            static _ => []);

        Assert.That(emptyCommit.SceneInstanceId, Is.Not.EqualTo(Guid.Empty));
        Assert.That(emptyCommit.Entities, Is.Empty);
        Assert.That(world.IdentityLedger.SceneInstanceCount, Is.EqualTo(2));
        Assert.That(world.IdentityLedger.AssignmentCount, Is.EqualTo(4));
        Assert.That(
            () => world.CreateSceneEntitiesAtomically(
                0,
                requestedSceneInstanceId: commit.SceneInstanceId,
                static _ => []),
            Throws.ArgumentException);
        Assert.That(world.IdentityLedger.SceneInstanceCount, Is.EqualTo(2));
        Assert.That(world.IdentityLedger.AssignmentCount, Is.EqualTo(4));
    }

    [Test]
    public void SceneEntityBatchUsesExpandedIndexMapForIdentityOrder()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        using var world = World.Create(new WorldOptions(registration));
        var assignmentOrder = ImmutableArray.Create(1, 0);
        var commit = world.CreateSceneEntitiesAtomically(
            2,
            requestedSceneInstanceId: null,
            entityIndicesByAssignmentOrdinal: assignmentOrder.AsSpan(),
            static _ => [
                new EntityDescription(),
                new EntityDescription(),
            ]);

        Assert.That(world.IdentityLedger.TryFind(world.GetEntityId(commit.Entities[1]), out var firstAssignedOrdinal), Is.True);
        Assert.That(world.IdentityLedger.TryFind(world.GetEntityId(commit.Entities[0]), out var secondAssignedOrdinal), Is.True);
        Assert.That(firstAssignedOrdinal, Is.EqualTo(2));
        Assert.That(secondAssignedOrdinal, Is.EqualTo(3));
    }
}
