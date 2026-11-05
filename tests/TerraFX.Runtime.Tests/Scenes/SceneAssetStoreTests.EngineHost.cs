// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Hosting;
using TerraFX.Platform;
using TerraFX.Rendering;
using TerraFX.Runtime.Tests.Hosting;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

internal sealed partial class SceneAssetStoreTests
{
    [Test]
    public async Task HostedWorldExtractsLiveCameraFromHostRendererAsync()
    {
        const int SceneAssetId = 3;
        var schemas = CreateRendererRegistry();
        var sceneBytes = SceneAssetReaderTests.Fixtures.CreateScene(
            new AssetId(SceneAssetId),
            additionalEntityComponents: [CreateCameraRecord(order: 0)]);
        var packageBytes = Package.CreatePackage(
            new Package.Entry(SceneAssetId, Package.CreateEnvelope(SceneAssetId, sceneBytes)));
        var packagePath = Package.WritePackage(packageBytes);
        var assetRegistration = SceneSupport.RegisterAssetCodecs(new AssetStoreRegistrationBuilder(schemas)).Build();
        var hostOptions = new EngineHostOptions(
            HostProfile.Headless,
            PlatformBackend.Headless,
            graphicsBackend: null,
            graphicsBackendRegistration: null,
            assetStoreOptions: new AssetStoreOptions(packagePath, SHA256.HashData(packageBytes), assetRegistration)) {
            CpuWorkerCount = 0,
            PersistenceMetadata = WorldPersistenceHeaderWriterTests.CreateMetadata(),
        };
        EngineHost? host = null;

        try
        {
            host = await EngineHost.StartAsync(hostOptions).ConfigureAwait(false);
            var sceneSupport = SceneSupport.CreateRegistration();
            var worldRegistrationBuilder = new WorldRegistrationBuilder(schemas);
            _ = worldRegistrationBuilder.RegisterSceneSupport(sceneSupport);
            _ = worldRegistrationBuilder.RegisterCpuExtractionSupport(Renderer.CreateCpuExtractionSupportRegistration());
            var worldRegistration = worldRegistrationBuilder.Build();
            World? world = null;
            SceneLoader? loader = null;

            await host.InvokeAsync(context => {
                world = context.CreateWorld(new WorldOptions(worldRegistration) {
                    CpuWorkerCount = 0,
                    InitialReplayLineageId = Guid.NewGuid(),
                    WorldGuid = Guid.NewGuid(),
                });
                loader = new SceneLoader(world, host.Assets);
            }).ConfigureAwait(false);

            using var prepared = await loader!.PrepareAsync(new AssetId(SceneAssetId), new SceneLoadOptions())
                .ConfigureAwait(false);
            var activation = await loader.ActivateAsync(prepared).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(activation.Status, Is.EqualTo(SceneActivationStatus.Activated));
            Assert.That(activation.Instance, Is.Not.Null);
            await Task.Delay(100).ConfigureAwait(false);

            RenderExtractionResponse? response = null;
            var cameraId = Guid.Empty;

            await host.InvokeAsync(_ => {
                if (!activation.Instance!.TryGetEntity(SceneAssetReaderTests.Fixtures.RootEntityId, out var cameraEntity))
                {
                    throw new InvalidOperationException("The activated Scene did not retain its camera Entity.");
                }

                cameraId = world!.GetEntityId(cameraEntity);
                var currentFrame = world.CurrentFrame3;
                response = host.Renderer.Extract(
                    world,
                    new RenderExtractionOptions3D(
                        currentFrame,
                        new PixelSize(640, 480),
                        [cameraId]));
            }).ConfigureAwait(false);

            Assert.That(response!.Status, Is.EqualTo(RenderExtractionStatus.Extracted));
            Assert.That(response.Snapshot, Is.TypeOf<RenderSnapshot3D>());

            using var snapshot = (RenderSnapshot3D)(response.Snapshot
                ?? throw new InvalidOperationException("The host renderer omitted its extracted snapshot."));
            Assert.That(snapshot.WorldGuid, Is.EqualTo(world!.WorldGuid));
            Assert.That(snapshot.CameraIds, Is.EqualTo([cameraId]));
            Assert.That(snapshot.TargetExtent, Is.EqualTo(new PixelSize(640, 480)));
            var originalFrame = snapshot.Frame;
            SpatialFrame3 nextFrame = default;

            await host.InvokeAsync(_ => nextFrame = world!.CreateNextFrame3(
                    originalFrame.Origin,
                    originalFrame.Minimum,
                    originalFrame.Maximum)).ConfigureAwait(false);

            var frameReceipt = await host.ChangeFrameAsync(world, nextFrame).ConfigureAwait(false);
            Assert.That(frameReceipt.Status, Is.EqualTo(FrameChangeStatus.Applied));
            var committedFrame = frameReceipt.CommittedFrame3
                ?? throw new InvalidOperationException("The applied frame change omitted its committed frame.");
            Assert.That(committedFrame.Equals(nextFrame), Is.True);
            RenderExtractionResponse? changedFrameResponse = null;

            await host.InvokeAsync(_ => changedFrameResponse = host.Renderer.Extract(
                    world!,
                    new RenderExtractionOptions3D(
                        world!.CurrentFrame3,
                        new PixelSize(640, 480),
                        [cameraId]))).ConfigureAwait(false);

            Assert.That(changedFrameResponse!.Status, Is.EqualTo(RenderExtractionStatus.Extracted));
            using var changedFrameSnapshot = (RenderSnapshot3D)(changedFrameResponse.Snapshot
                ?? throw new InvalidOperationException("The changed-frame extraction omitted its snapshot."));
            Assert.That(changedFrameSnapshot.Frame.Equals(committedFrame), Is.True);
            Assert.That(snapshot.Frame.Equals(originalFrame), Is.True);
            snapshot.Dispose();
            changedFrameSnapshot.Dispose();
            await host.StopAsync().ConfigureAwait(false);
            Assert.That(
                activation.Instance!.TryGetEntity(SceneAssetReaderTests.Fixtures.RootEntityId, out _),
                Is.False);
            _ = Assert.Throws<ObjectDisposedException>(
                () => _ = host.Renderer.Extract(
                    world!,
                    new RenderExtractionOptions3D(
                        committedFrame,
                        new PixelSize(640, 480),
                        [cameraId])));
        }
        finally
        {
            try
            {
                if (host is not null)
                {
                    await host.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                File.Delete(packagePath);
            }
        }
    }

    [Test]
    [SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "Both saves and package replacement complete before the host and its AssetStore are disposed.")]
    public async Task HostedWorldSaveValidatesActiveSceneOccurrencesAgainstTheSelectedPackageAsync()
    {
        var schemas = CreatePersistenceScanSchemas(CreateRendererRegistry());
        var prefabEntityId = new Guid("31000000-0000-0000-0000-000000000001");
        var rootPlacementId = new Guid("31000000-0000-0000-0000-000000000002");
        var nestedPlacementId = new Guid("31000000-0000-0000-0000-000000000003");
        var prefab = SceneAssetReaderTests.Fixtures.CreatePrefab(
            new AssetId(1),
            entityId: prefabEntityId);
        var scene = SceneAssetReaderTests.Fixtures.CreateSceneWithChildPlacement(
            new AssetId(3),
            SceneAssetReaderTests.Fixtures.RootEntityId,
            rootPlacementId,
            SceneAssetReaderTests.Fixtures.CreateRootPose(prefabEntityId, includeRootPosition: false));
        var originalPackage = Package.CreatePackage(
            new Package.Entry(1, Package.CreateEnvelope(1, prefab)),
            new Package.Entry(3, Package.CreateEnvelope(3, scene)));
        var replacementPrefab = SceneAssetReaderTests.Fixtures.CreatePrefab(
            new AssetId(1),
            entityId: prefabEntityId,
            placementId: nestedPlacementId,
            placementPrefabAssetId: new AssetId(2),
            placementParentEntityId: prefabEntityId);
        var replacementPackage = Package.CreatePackage(
            new Package.Entry(1, Package.CreateEnvelope(1, replacementPrefab)),
            new Package.Entry(2, Package.CreateEnvelope(2, SceneAssetReaderTests.Fixtures.CreatePrefab(new AssetId(2)))),
            new Package.Entry(3, Package.CreateEnvelope(3, scene)));
        var originalPackagePath = Package.WritePackage(originalPackage);
        var replacementPackagePath = Package.WritePackage(replacementPackage);
        var validSavePath = Path.Combine(Path.GetTempPath(), $"terrafx-{Guid.NewGuid():N}.scene-valid.state");
        var mismatchedSavePath = Path.Combine(Path.GetTempPath(), $"terrafx-{Guid.NewGuid():N}.scene-mismatch.state");
        var assetRegistration = SceneSupport.RegisterAssetCodecs(new AssetStoreRegistrationBuilder(schemas)).Build();
        var hostOptions = new EngineHostOptions(
            HostProfile.Headless,
            PlatformBackend.Headless,
            graphicsBackend: null,
            graphicsBackendRegistration: null,
            assetStoreOptions: new AssetStoreOptions(
                originalPackagePath,
                SHA256.HashData(originalPackage),
                assetRegistration)) {
            CpuWorkerCount = 0,
            PersistenceMetadata = WorldPersistenceHeaderWriterTests.CreateMetadata(),
        };
        EngineHost? host = null;

        try
        {
            host = await EngineHost.StartAsync(hostOptions).ConfigureAwait(false);
            var sceneSupport = SceneSupport.CreateRegistration();
            var worldRegistrationBuilder = new WorldRegistrationBuilder(schemas);
            _ = worldRegistrationBuilder.RegisterSceneSupport(sceneSupport);
            _ = worldRegistrationBuilder.RegisterCpuExtractionSupport(Renderer.CreateCpuExtractionSupportRegistration());
            World? world = null;
            SceneLoader? loader = null;

            await host.InvokeAsync(context => {
                world = context.CreateWorld(new WorldOptions(worldRegistrationBuilder.Build()) {
                    CpuWorkerCount = 0,
                    InitialReplayLineageId = Guid.NewGuid(),
                    WorldGuid = Guid.NewGuid(),
                });
                loader = new SceneLoader(world, host.Assets);
            }).ConfigureAwait(false);

            using var prepared = await loader!.PrepareAsync(new AssetId(3), new SceneLoadOptions()).ConfigureAwait(false);
            var activation = await loader.ActivateAsync(prepared).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(activation.Status, Is.EqualTo(SceneActivationStatus.Activated));
            await Task.Delay(50).ConfigureAwait(false);

            Task<SaveReceipt>? saveTask = null;
            await host.InvokeAsync(_ => saveTask = host.Persistence.SaveAsync(
                world!,
                new SaveOptions(validSavePath, SaveDurabilityPolicy.BestEffort))).ConfigureAwait(false);
            var validReceipt = await (saveTask
                ?? throw new InvalidOperationException("The active Scene Save operation was not admitted.")).ConfigureAwait(false);

            Assert.That(
                validReceipt.Status,
                Is.AnyOf(SaveStatus.DurableCommitted, SaveStatus.BestEffortCommitted),
                $"Unexpected Save rejection: {validReceipt.Diagnostic?.Code}: {validReceipt.Diagnostic?.Message}");
            Assert.That(File.Exists(validSavePath), Is.True);

            await host.Assets.ReplacePackageAsync(
                new AssetStoreOptions(replacementPackagePath, SHA256.HashData(replacementPackage), assetRegistration),
                System.Threading.CancellationToken.None).ConfigureAwait(false);
            await host.InvokeAsync(_ => saveTask = host.Persistence.SaveAsync(
                world!,
                new SaveOptions(mismatchedSavePath, SaveDurabilityPolicy.BestEffort))).ConfigureAwait(false);
            var mismatchedReceipt = await (saveTask
                ?? throw new InvalidOperationException("The mismatched active Scene Save operation was not admitted.")).ConfigureAwait(false);

            Assert.That(mismatchedReceipt.Status, Is.EqualTo(SaveStatus.Rejected));
            Assert.That(mismatchedReceipt.Diagnostic?.Code, Is.EqualTo("save-scene-source-mismatch"));
            Assert.That(File.Exists(mismatchedSavePath), Is.False);
        }
        finally
        {
            if (host is not null)
            {
                await host.DisposeAsync().ConfigureAwait(false);
            }

            File.Delete(originalPackagePath);
            File.Delete(replacementPackagePath);

            if (File.Exists(validSavePath))
            {
                File.Delete(validSavePath);
            }

            if (File.Exists(mismatchedSavePath))
            {
                File.Delete(mismatchedSavePath);
            }
        }
    }
}
