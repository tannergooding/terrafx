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
    [SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the Scene snapshot is disposed",
        Justification = "The snapshot intentionally retains its package-backed Atlas page after Scene unload.")]
    public async Task HeadlessEngineHostExtractsPackageBackedSpriteForActiveSceneAsync()
    {
        const int SceneAssetId = 3;
        var schemas = CreateRendererRegistry();
        var frameId = new Guid("30000000-0000-4000-8000-000000000001");
        var sceneBytes = SceneAssetReaderTests.Fixtures.CreateScene(
            new AssetId(SceneAssetId),
            additionalEntityComponents: [
                CreateCameraRecord(order: 0),
                CreateRenderSpriteRecord(2, frameId, materialAssetId: null),
            ]);
        var packageBytes = Package.CreatePackage(
            new Package.Entry(1, Package.CreateEnvelope(1, CreateRenderAtlasPageTextureRecord(17))),
            new Package.Entry(
                2,
                Package.CreateEnvelope(
                    2,
                    CreateRenderAtlasRecord(
                        1,
                        CreateRenderAtlasFrameRecord(frameId, page: 0, x: 4, y: 4, width: 4, height: 4)))),
            new Package.Entry(SceneAssetId, Package.CreateEnvelope(SceneAssetId, sceneBytes)));
        var packagePath = Package.WritePackage(packageBytes);
        var assetRegistrationBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = SceneSupport.RegisterAssetCodecs(assetRegistrationBuilder);
        _ = Renderer.RegisterAssetCodecs(assetRegistrationBuilder);
        var hostOptions = new EngineHostOptions(
            HostProfile.Headless,
            PlatformBackend.Headless,
            graphicsBackend: null,
            graphicsBackendRegistration: null,
            assetStoreOptions: new AssetStoreOptions(
                packagePath,
                SHA256.HashData(packageBytes),
                assetRegistrationBuilder.Build())) {
            CpuWorkerCount = 0,
            PersistenceMetadata = WorldPersistenceHeaderWriterTests.CreateMetadata(),
        };
        EngineHost? host = null;

        try
        {
            host = await EngineHost.StartAsync(hostOptions).ConfigureAwait(false);
            var worldRegistrationBuilder = new WorldRegistrationBuilder(schemas);
            _ = worldRegistrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
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

            using var prepared = await loader!.PrepareAsync(
                    new AssetId(SceneAssetId),
                    new SceneLoadOptions())
                .ConfigureAwait(false);
            var activation = await loader.ActivateAsync(prepared)
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
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
                response = host.Renderer.Extract(
                    world,
                    new RenderExtractionOptions3D(
                        world.CurrentFrame3,
                        new PixelSize(640, 480),
                        [cameraId]));
            }).ConfigureAwait(false);

            Assert.That(response!.Status, Is.EqualTo(RenderExtractionStatus.Extracted));
            using var snapshot = response.Snapshot as RenderSnapshot3D
                ?? throw new InvalidOperationException("The host Renderer omitted its extracted 3D snapshot.");
            Assert.That(snapshot.WorldGuid, Is.EqualTo(world!.WorldGuid));
            Assert.That(snapshot.CameraIds, Is.EqualTo([cameraId]));
            Assert.That(snapshot.TargetExtent, Is.EqualTo(new PixelSize(640, 480)));
            Assert.That(snapshot.SpriteDraws, Has.Length.EqualTo(1));

            var sprite = snapshot.SpriteDraws[0];
            var sceneInstance = activation.Instance
                ?? throw new InvalidOperationException("The activated Scene omitted its instance.");
            Assert.That(sprite.SceneInstanceId, Is.EqualTo(sceneInstance.SceneInstanceId));
            Assert.That(sprite.EntityId, Is.EqualTo(cameraId));
            Assert.That(sprite.AtlasAssetId, Is.EqualTo(2));
            Assert.That(sprite.FrameId, Is.EqualTo(frameId));
            Assert.That(sprite.HasPreviousTransform, Is.False);

            var assetPins = snapshot.AssetPins
                ?? throw new InvalidOperationException("The extracted Sprite snapshot omitted its package asset pins.");
            var sceneAssets = assetPins.GetSceneAssets(sceneInstance.SceneInstanceId);
            Assert.That(
                sceneAssets.GetAtlasPageSet(sprite.AtlasAssetId).GetPage(0).Mips[0].Pixels[0],
                Is.EqualTo(17));

            var unloadTask = sceneInstance.UnloadAsync(HierarchyDestroyMode.DestroySubtree);
            var unloadReceipt = await unloadTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(unloadReceipt.Status, Is.EqualTo(SceneUnloadStatus.Unloaded));
            Assert.That(sceneAssets.GetAtlasPageSet(sprite.AtlasAssetId).GetPage(0).Mips[0].Pixels[0], Is.EqualTo(17));
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
}
