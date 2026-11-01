// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;
using TerraFX.Numerics;
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
    public void DetachChildrenKeepWorldMovesTheChildPoseToRootStorage()
    {
        var schemas = TerraFX.Runtime.Tests.Scenes.SceneAssetReaderTests.Fixtures.CreateRegistry();
        var builder = new WorldRegistrationBuilder(schemas);
        var sceneSupport = SceneSupport.CreateRegistration();
        _ = builder.RegisterSceneSupport(sceneSupport);
        using var world = World.Create(new WorldOptions(builder.Build()));
        var participant = world.GetSceneParticipant(sceneSupport);
        var rootRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, Angle.FromDegrees(90.0f));
        var rootTransform = new SceneComponentDefinitions.LocalTransformComponent(
            Vector3.Zero,
            rootRotation,
            new Vector3(2.0f));
        var rootPosition = new SceneComponentDefinitions.RootPositionComponent(
            new SpatialPosition3(new ChunkIndex3(0, 0, 0), new Vector3(5.0f, 0.0f, 0.0f)));
        var childTransform = new SceneComponentDefinitions.LocalTransformComponent(
            new Vector3(2.0f, 0.0f, 0.0f),
            Quaternion.Identity,
            Vector3.One);
        var targetTransform = new SceneComponentDefinitions.LocalTransformComponent(
            new Vector3(1.0f, 0.0f, 0.0f),
            Quaternion.Identity,
            Vector3.One);
        var grandchildTransform = new SceneComponentDefinitions.LocalTransformComponent(
            new Vector3(3.0f, 0.0f, 0.0f),
            Quaternion.Identity,
            Vector3.One);
        var siblingTransform = new SceneComponentDefinitions.LocalTransformComponent(
            new Vector3(-1.0f, 0.0f, 0.0f),
            Quaternion.Identity,
            Vector3.One);
        var entities = world.CreateEntitiesAtomically(
            5,
            _ => [
                new EntityDescription(components: [
                    ComponentInitialization.Value(in rootPosition),
                    ComponentInitialization.Value(in rootTransform),
                ]),
                new EntityDescription(components: [ComponentInitialization.Value(in targetTransform)]),
                new EntityDescription(components: [ComponentInitialization.Value(in childTransform)]),
                new EntityDescription(components: [ComponentInitialization.Value(in grandchildTransform)]),
                new EntityDescription(components: [ComponentInitialization.Value(in siblingTransform)]),
            ]);
        var root = entities[0];
        var target = entities[1];
        var child = entities[2];
        var grandchild = entities[3];
        var sibling = entities[4];
        world.ApplyHierarchyParentChange(participant, target, root);
        world.ApplyHierarchyParentChange(participant, child, target);
        world.ApplyHierarchyParentChange(participant, grandchild, child);
        world.ApplyHierarchyParentChange(participant, sibling, target);
        var targetId = world.GetEntityId(target);
        var rootId = world.GetEntityId(root);

        world.DestroyEntity(target, HierarchyDestroyMode.DetachChildrenKeepWorld);

        Assert.That(world.TryGetEntity(rootId, out var actualRoot), Is.True);
        Assert.That(actualRoot, Is.EqualTo(root));
        Assert.That(world.TryGetEntity(targetId, out _), Is.False);
        Assert.That(world.TryGet<SceneComponentDefinitions.LocalTransformComponent>(child, out var detachedTransform), Is.True);
        Assert.That(detachedTransform.Position, Is.EqualTo(Vector3.Zero));
        Assert.That(float.Abs(detachedTransform.Scale.X - 2.0f), Is.LessThan(1.0e-5f));
        Assert.That(float.Abs(detachedTransform.Scale.Y - 2.0f), Is.LessThan(1.0e-5f));
        Assert.That(float.Abs(detachedTransform.Scale.Z - 2.0f), Is.LessThan(1.0e-5f));
        var detachedRight = Vector3.TransformDirection(
            Vector3.UnitX,
            Matrix4x4.CreateFromQuaternion(detachedTransform.Rotation));
        Assert.That(float.Abs(detachedRight.X), Is.LessThan(1.0e-5f));
        Assert.That(float.Abs(detachedRight.Y - 1.0f), Is.LessThan(1.0e-5f));
        Assert.That(world.TryGet<SceneComponentDefinitions.RootPositionComponent>(child, out var detachedPosition), Is.True);
        Assert.That(detachedPosition.Position.Chunk, Is.EqualTo(new ChunkIndex3(0, 0, 0)));
        Assert.That(float.Abs(detachedPosition.Position.Offset.X - 5.0f), Is.LessThan(1.0e-5f));
        Assert.That(float.Abs(detachedPosition.Position.Offset.Y - 6.0f), Is.LessThan(1.0e-5f));
        Assert.That(world.TryGetHierarchyParent(child, out _), Is.False);
        Assert.That(world.TryGetHierarchyParent(grandchild, out var actualParent), Is.True);
        Assert.That(actualParent, Is.EqualTo(child));
        Assert.That(world.TryGet<SceneComponentDefinitions.RootPositionComponent>(sibling, out var siblingPosition), Is.True);
        Assert.That(float.Abs(siblingPosition.Position.Offset.X - 5.0f), Is.LessThan(1.0e-5f));
        Assert.That(float.Abs(siblingPosition.Position.Offset.Y), Is.LessThan(1.0e-5f));
        Assert.That(world.TryGetHierarchyParent(sibling, out _), Is.False);
    }

    [Test]
    public void DetachChildrenKeepWorldRejectsOutOfFramePoseAtomically()
    {
        var schemas = TerraFX.Runtime.Tests.Scenes.SceneAssetReaderTests.Fixtures.CreateRegistry();
        var builder = new WorldRegistrationBuilder(schemas);
        var sceneSupport = SceneSupport.CreateRegistration();
        _ = builder.RegisterSceneSupport(sceneSupport);
        using var world = World.Create(new WorldOptions(builder.Build()));
        var participant = world.GetSceneParticipant(sceneSupport);
        var rootTransform = new SceneComponentDefinitions.LocalTransformComponent(
            Vector3.Zero,
            Quaternion.Identity,
            Vector3.One);
        var rootPosition = new SceneComponentDefinitions.RootPositionComponent(
            new SpatialPosition3(new ChunkIndex3(0, 0, 0), new Vector3(5000.0f, 0.0f, 0.0f)));
        var childTransform = new SceneComponentDefinitions.LocalTransformComponent(
            Vector3.Zero,
            Quaternion.Identity,
            Vector3.One);
        var entities = world.CreateEntitiesAtomically(
            2,
            _ => [
                new EntityDescription(components: [
                    ComponentInitialization.Value(in rootPosition),
                    ComponentInitialization.Value(in rootTransform),
                ]),
                new EntityDescription(components: [ComponentInitialization.Value(in childTransform)]),
            ]);
        var root = entities[0];
        var child = entities[1];
        world.ApplyHierarchyParentChange(participant, child, root);
        var rootId = world.GetEntityId(root);

        Assert.That(
            () => world.DestroyEntity(root, HierarchyDestroyMode.DetachChildrenKeepWorld),
            Throws.InvalidOperationException);
        Assert.That(world.TryGetEntity(rootId, out var actualRoot), Is.True);
        Assert.That(actualRoot, Is.EqualTo(root));
        Assert.That(world.TryGet<SceneComponentDefinitions.RootPositionComponent>(child, out _), Is.False);
        Assert.That(world.TryGetHierarchyParent(child, out var actualParent), Is.True);
        Assert.That(actualParent, Is.EqualTo(root));
    }

    [Test]
    public void DetachChildrenKeepWorldRejectsUnrepresentableSrtAtomically()
    {
        var schemas = TerraFX.Runtime.Tests.Scenes.SceneAssetReaderTests.Fixtures.CreateRegistry();
        var builder = new WorldRegistrationBuilder(schemas);
        var sceneSupport = SceneSupport.CreateRegistration();
        _ = builder.RegisterSceneSupport(sceneSupport);
        using var world = World.Create(new WorldOptions(builder.Build()));
        var participant = world.GetSceneParticipant(sceneSupport);
        var rootTransform = new SceneComponentDefinitions.LocalTransformComponent(
            Vector3.Zero,
            Quaternion.Identity,
            new Vector3(2.0f, 1.0f, 1.0f));
        var rootPosition = new SceneComponentDefinitions.RootPositionComponent(default);
        var childTransform = new SceneComponentDefinitions.LocalTransformComponent(
            Vector3.Zero,
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, Angle.FromDegrees(45.0f)),
            Vector3.One);
        var entities = world.CreateEntitiesAtomically(
            2,
            _ => [
                new EntityDescription(components: [
                    ComponentInitialization.Value(in rootPosition),
                    ComponentInitialization.Value(in rootTransform),
                ]),
                new EntityDescription(components: [ComponentInitialization.Value(in childTransform)]),
            ]);
        var root = entities[0];
        var child = entities[1];
        world.ApplyHierarchyParentChange(participant, child, root);
        var rootId = world.GetEntityId(root);
        var childId = world.GetEntityId(child);

        Assert.That(
            () => world.DestroyEntity(root, HierarchyDestroyMode.DetachChildrenKeepWorld),
            Throws.InvalidOperationException);
        Assert.That(world.TryGetEntity(rootId, out var actualRoot), Is.True);
        Assert.That(actualRoot, Is.EqualTo(root));
        Assert.That(world.TryGetEntity(childId, out var actualChild), Is.True);
        Assert.That(actualChild, Is.EqualTo(child));
        Assert.That(world.TryGet<SceneComponentDefinitions.RootPositionComponent>(child, out _), Is.False);
        Assert.That(world.TryGetHierarchyParent(child, out var actualParent), Is.True);
        Assert.That(actualParent, Is.EqualTo(root));
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
