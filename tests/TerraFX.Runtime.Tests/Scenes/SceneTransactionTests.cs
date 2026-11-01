// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Numerics;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

internal sealed partial class SceneAssetStoreTests
{
    private static readonly Guid s_childEntityId = new("10000000-0000-4000-8000-000000000002");
    private static readonly Guid s_placementId = new("40000000-0000-4000-8000-000000000002");
    private static readonly Guid s_sceneRootEntityId = new("10000000-0000-4000-8000-000000000001");

    private static SceneTransactionFixture CreateFixture(int maximumPendingOwnerRequests)
    {
        const int prefabAssetId = 1;
        const int sceneAssetId = 3;
        const int secondSceneAssetId = 4;
        var schemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var scene = SceneAssetReaderTests.Fixtures.CreateSceneWithChildPlacement(
            new AssetId(sceneAssetId),
            s_sceneRootEntityId,
            s_placementId,
            SceneAssetReaderTests.Fixtures.CreateRootPose(
                s_childEntityId,
                localPositionX: 2.0f,
                includeRootPosition: false));
        var prefab = SceneAssetReaderTests.Fixtures.CreatePrefab(
            new AssetId(prefabAssetId),
            entityId: s_childEntityId);
        var secondScene = SceneAssetReaderTests.Fixtures.CreateScene(new AssetId(secondSceneAssetId));
        var package = Package.CreatePackage(
            new Package.Entry(prefabAssetId, Package.CreateEnvelope(prefabAssetId, prefab)),
            new Package.Entry(sceneAssetId, Package.CreateEnvelope(sceneAssetId, scene)),
            new Package.Entry(secondSceneAssetId, Package.CreateEnvelope(secondSceneAssetId, secondScene)));
        var packagePath = Package.WritePackage(package);
        AssetStore? assets = null;
        World? world = null;

        try
        {
            var assetRegistration = SceneSupport.RegisterAssetCodecs(new AssetStoreRegistrationBuilder(schemas)).Build();
            assets = AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(package), assetRegistration)).GetAwaiter().GetResult();
            var worldRegistrationBuilder = new WorldRegistrationBuilder(schemas);
            _ = worldRegistrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
            world = World.Create(new WorldOptions(worldRegistrationBuilder.Build()) {
                MaximumPendingOwnerRequests = maximumPendingOwnerRequests,
            });
            return new SceneTransactionFixture(packagePath, assets, world);
        }
        catch
        {
            world?.Dispose();

            assets?.DisposeAsync().AsTask().GetAwaiter().GetResult();

            File.Delete(packagePath);
            throw;
        }
    }

    [Test]
    public void SceneTransactionRevalidatesCapacityCancellationAndWorldPoseAdmission()
    {
        using var fixture = CreateFixture(maximumPendingOwnerRequests: 1);
        var instance = fixture.Activate(new AssetId(3));
        var secondInstance = fixture.Activate(new AssetId(4));
        Assert.That(instance.TryGetEntity(new SceneEntityAddress([s_placementId], s_childEntityId), out var child), Is.True);
        Assert.That(secondInstance.TryGetEntity(SceneAssetReaderTests.Fixtures.RootEntityId, out var secondRoot), Is.True);

        var foreignRegistrationBuilder = new WorldRegistrationBuilder(SceneAssetReaderTests.Fixtures.CreateRegistry());
        _ = foreignRegistrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());

        using (var foreignWorld = World.Create(new WorldOptions(foreignRegistrationBuilder.Build())))
        {
            var foreignRootPosition = new SceneComponentDefinitions.RootPositionComponent(default);
            var foreignRootTransform = new SceneComponentDefinitions.LocalTransformComponent(
                Vector3.Zero,
                Quaternion.Identity,
                Vector3.One);
            var foreignRoot = foreignWorld.CreateEntity(new EntityDescription(components: [
                ComponentInitialization.Value(in foreignRootPosition),
                ComponentInitialization.Value(in foreignRootTransform),
            ]));

            using var foreignParent = instance.BeginTransaction();
            foreignParent.Reparent(child, foreignRoot, ReparentMode.KeepWorld);
            Assert.That(foreignParent.Validate().Status, Is.EqualTo(SceneValidationStatus.Rejected));
            var rejectedTask = foreignParent.CommitAsync();
            fixture.World.PumpControlBoundary();
            var receipt = rejectedTask.GetAwaiter().GetResult();
            Assert.That(receipt.Status, Is.EqualTo(SceneTransactionStatus.Rejected));
        }

        var externalRootPosition = new SceneComponentDefinitions.RootPositionComponent(
            new SpatialPosition3(default, new Vector3(5.0f, 0.0f, 0.0f)));
        var externalRootTransform = new SceneComponentDefinitions.LocalTransformComponent(
            Vector3.Zero,
            Quaternion.Identity,
            Vector3.One);
        var externalRoot = fixture.World.CreateEntity(new EntityDescription(components: [
            ComponentInitialization.Value(in externalRootPosition),
            ComponentInitialization.Value(in externalRootTransform),
        ]));

        using var keepWorld = instance.BeginTransaction();
        keepWorld.Reparent(child, externalRoot, ReparentMode.KeepWorld);
        Assert.That(keepWorld.Validate().Status, Is.EqualTo(SceneValidationStatus.Valid));
        var firstCommit = keepWorld.CommitAsync();

        using (var capacityRejected = instance.BeginTransaction())
        {
            capacityRejected.Reparent(child, secondRoot, ReparentMode.KeepLocal);
            var rejectedTask = capacityRejected.CommitAsync();
            Assert.That(rejectedTask.IsCompleted, Is.True);
            var receipt = rejectedTask.GetAwaiter().GetResult();
            Assert.That(receipt.Status, Is.EqualTo(SceneTransactionStatus.Rejected));
            Assert.That(receipt.Diagnostics, Is.Not.Empty);
        }

        fixture.World.PumpControlBoundary();
        var committed = firstCommit.GetAwaiter().GetResult();
        Assert.That(committed.Status, Is.EqualTo(SceneTransactionStatus.Committed));
        Assert.That(fixture.World.TryGetHierarchyParent(child, out var actualParent), Is.True);
        Assert.That(actualParent, Is.EqualTo(externalRoot));
        Assert.That(fixture.World.TryGet<SceneComponentDefinitions.LocalTransformComponent>(child, out var keptWorldLocal), Is.True);
        Assert.That(float.Abs(keptWorldLocal.Position.X + 3.0f), Is.LessThan(1.0e-5f));

        var shearRootPosition = new SceneComponentDefinitions.RootPositionComponent(default);
        var shearRootTransform = new SceneComponentDefinitions.LocalTransformComponent(
            Vector3.Zero,
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, Angle.FromDegrees(45.0f)),
            new Vector3(2.0f, 1.0f, 1.0f));
        var shearRoot = fixture.World.CreateEntity(new EntityDescription(components: [
            ComponentInitialization.Value(in shearRootPosition),
            ComponentInitialization.Value(in shearRootTransform),
        ]));

        using (var unrepresentable = instance.BeginTransaction())
        {
            unrepresentable.Reparent(child, shearRoot, ReparentMode.KeepWorld);
            Assert.That(unrepresentable.Validate().Status, Is.EqualTo(SceneValidationStatus.Rejected));
            var rejectedTask = unrepresentable.CommitAsync();
            fixture.World.PumpControlBoundary();
            var receipt = rejectedTask.GetAwaiter().GetResult();
            Assert.That(receipt.Status, Is.EqualTo(SceneTransactionStatus.Rejected));
        }

        Assert.That(fixture.World.TryGetHierarchyParent(child, out actualParent), Is.True);
        Assert.That(actualParent, Is.EqualTo(externalRoot));

        using (var queuedCancellation = new CancellationTokenSource())
        using (var cancelled = instance.BeginTransaction())
        {
            cancelled.Reparent(child, secondRoot, ReparentMode.KeepLocal);
            var cancellationTask = cancelled.CommitAsync(queuedCancellation.Token);
            queuedCancellation.CancelAsync().GetAwaiter().GetResult();
            fixture.World.PumpControlBoundary();
            Assert.That(cancellationTask.IsCanceled, Is.True);
        }

        Assert.That(fixture.World.TryGetHierarchyParent(child, out actualParent), Is.True);
        Assert.That(actualParent, Is.EqualTo(externalRoot));

        using (var stale = instance.BeginTransaction())
        {
            stale.Reparent(child, secondRoot, ReparentMode.KeepLocal);
            var staleTask = stale.CommitAsync();
            fixture.World.DestroyEntity(child, HierarchyDestroyMode.DestroySubtree);
            fixture.World.PumpControlBoundary();
            var receipt = staleTask.GetAwaiter().GetResult();
            Assert.That(receipt.Status, Is.EqualTo(SceneTransactionStatus.Rejected));
            Assert.That(receipt.Diagnostics, Is.Not.Empty);
        }
    }

    [Test]
    public void SceneTransactionValidatesAndAppliesSequentialRootTransitionsAtomically()
    {
        using var fixture = CreateFixture(maximumPendingOwnerRequests: 8);
        var instance = fixture.Activate(new AssetId(3));
        var secondInstance = fixture.Activate(new AssetId(4));
        Assert.That(instance.TryGetEntity(s_sceneRootEntityId, out var root), Is.True);
        Assert.That(instance.TryGetEntity(new SceneEntityAddress([s_placementId], s_childEntityId), out var child), Is.True);
        Assert.That(secondInstance.TryGetEntity(SceneAssetReaderTests.Fixtures.RootEntityId, out var secondRoot), Is.True);
        Assert.That(fixture.World.TryGet<SceneComponentDefinitions.LocalTransformComponent>(root, out var rootTransform), Is.True);
        Assert.That(fixture.World.TryGet<SceneComponentDefinitions.RootPositionComponent>(root, out var initialRootPosition), Is.True);
        Assert.That(fixture.World.TryGet<SceneComponentDefinitions.LocalTransformComponent>(child, out var initialChildTransform), Is.True);

        using (var invalid = instance.BeginTransaction())
        {
            var requestedPosition = new SpatialPosition3(default, new Vector3(25.0f, 0.0f, 0.0f));
            invalid.SetRootPosition(root, requestedPosition);
            invalid.Reparent(root, child, ReparentMode.KeepLocal);

            var preview = invalid.Validate();
            Assert.That(preview.Status, Is.EqualTo(SceneValidationStatus.Rejected));
            Assert.That(preview.Diagnostics, Is.Not.Empty);
            var rejectedTask = invalid.CommitAsync();
            Assert.That(rejectedTask.IsCompleted, Is.False);
            fixture.World.PumpControlBoundary();
            var rejectedReceipt = rejectedTask.GetAwaiter().GetResult();

            Assert.That(rejectedReceipt.Status, Is.EqualTo(SceneTransactionStatus.Rejected));
            Assert.That(rejectedReceipt.CommittedStructuralRevision, Is.Null);
            Assert.That(rejectedReceipt.Diagnostics, Is.Not.Empty);
        }

        Assert.That(fixture.World.TryGet<SceneComponentDefinitions.RootPositionComponent>(root, out var unchangedRootPosition), Is.True);
        Assert.That(unchangedRootPosition.Position, Is.EqualTo(initialRootPosition.Position));
        Assert.That(fixture.World.TryGetHierarchyParent(child, out var originalParent), Is.True);
        Assert.That(originalParent, Is.EqualTo(root));

        using (var localReparent = instance.BeginTransaction())
        {
            localReparent.Reparent(child, secondRoot, ReparentMode.KeepLocal);
            var preview = localReparent.Validate();
            Assert.That(preview.Status, Is.EqualTo(SceneValidationStatus.Valid));
            Assert.That(preview.BaseStructuralRevision, Is.EqualTo(fixture.World.CanonicalMutationRevision));
            var commitTask = localReparent.CommitAsync();
            Assert.That(commitTask.IsCompleted, Is.False);
            fixture.World.PumpControlBoundary();
            var receipt = commitTask.GetAwaiter().GetResult();

            Assert.That(receipt.Status, Is.EqualTo(SceneTransactionStatus.Committed));
            Assert.That(receipt.CommittedStructuralRevision, Is.Not.Null);
            Assert.That(receipt.Diagnostics, Is.Empty);
        }

        Assert.That(fixture.World.TryGetHierarchyParent(child, out var committedParent), Is.True);
        Assert.That(committedParent, Is.EqualTo(secondRoot));
        Assert.That(fixture.World.TryGet<SceneComponentDefinitions.LocalTransformComponent>(child, out var retainedLocal), Is.True);
        Assert.That(retainedLocal.Position, Is.EqualTo(initialChildTransform.Position));
        Assert.That(retainedLocal.Rotation, Is.EqualTo(initialChildTransform.Rotation));
        Assert.That(retainedLocal.Scale, Is.EqualTo(initialChildTransform.Scale));
        Assert.That(fixture.World.TryGet<SceneComponentDefinitions.RootPositionComponent>(child, out _), Is.False);

        var requestedRootPosition = new SpatialPosition3(
            new ChunkIndex3(0, 0, 0),
            new Vector3(12.0f, -3.0f, 5.0f));
        var normalizedRootPosition = SpatialMath.Normalize(requestedRootPosition, fixture.World.SpatialSettings);

        using (var rootTransition = instance.BeginTransaction())
        {
            rootTransition.Reparent(child, null, ReparentMode.KeepLocal);
            rootTransition.SetRootPosition(child, requestedRootPosition);
            Assert.That(rootTransition.Validate().Status, Is.EqualTo(SceneValidationStatus.Valid));
            var commitTask = rootTransition.CommitAsync();
            fixture.World.PumpControlBoundary();
            var receipt = commitTask.GetAwaiter().GetResult();
            Assert.That(receipt.Status, Is.EqualTo(SceneTransactionStatus.Committed));
        }

        Assert.That(fixture.World.TryGetHierarchyParent(child, out _), Is.False);
        Assert.That(fixture.World.TryGet<SceneComponentDefinitions.LocalTransformComponent>(child, out var rootLocal), Is.True);
        Assert.That(rootLocal.Position, Is.EqualTo(Vector3.Zero));
        Assert.That(rootLocal.Rotation, Is.EqualTo(initialChildTransform.Rotation));
        Assert.That(rootLocal.Scale, Is.EqualTo(initialChildTransform.Scale));
        Assert.That(fixture.World.TryGet<SceneComponentDefinitions.RootPositionComponent>(child, out var newRootPosition), Is.True);
        Assert.That(newRootPosition.Position.Chunk.X, Is.EqualTo(normalizedRootPosition.Chunk.X));
        Assert.That(newRootPosition.Position.Chunk.Y, Is.EqualTo(normalizedRootPosition.Chunk.Y));
        Assert.That(newRootPosition.Position.Chunk.Z, Is.EqualTo(normalizedRootPosition.Chunk.Z));
        Assert.That(newRootPosition.Position.Offset.X, Is.EqualTo(normalizedRootPosition.Offset.X));
        Assert.That(newRootPosition.Position.Offset.Y, Is.EqualTo(normalizedRootPosition.Offset.Y));
        Assert.That(newRootPosition.Position.Offset.Z, Is.EqualTo(normalizedRootPosition.Offset.Z));
        Assert.That(fixture.World.TryGet<SceneComponentDefinitions.LocalTransformComponent>(root, out var unchangedRootTransform), Is.True);
        Assert.That(unchangedRootTransform, Is.EqualTo(rootTransform));
    }

    private sealed class SceneTransactionFixture : IDisposable
    {
        private readonly AssetStore _assets;
        private readonly string _packagePath;

        internal SceneTransactionFixture(string packagePath, AssetStore assets, World world)
        {
            _packagePath = packagePath;
            _assets = assets;
            World = world;
            Loader = new SceneLoader(world, assets);
        }

        internal SceneLoader Loader { get; }

        internal World World { get; }

        [SuppressMessage(
            "Reliability",
            "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
            Justification = "The owner boundary completes the admitted activation request before the prepared scene is disposed.")]
        public SceneInstance Activate(AssetId assetId)
        {
            using var prepared = Loader.PrepareAsync(assetId, new SceneLoadOptions()).GetAwaiter().GetResult();
            var activation = Loader.ActivateAsync(prepared);
            World.PumpControlBoundary();
            var receipt = activation.GetAwaiter().GetResult();

            if (receipt.Status != SceneActivationStatus.Activated || receipt.Instance is null)
            {
                throw new InvalidOperationException("The Scene transaction test fixture failed to activate its Scene.");
            }

            return receipt.Instance;
        }

        public void Dispose()
        {
            World.Dispose();
            _assets.DisposeAsync().AsTask().GetAwaiter().GetResult();
            File.Delete(_packagePath);
        }
    }
}
