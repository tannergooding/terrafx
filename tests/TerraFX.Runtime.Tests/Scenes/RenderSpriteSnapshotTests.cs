// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Graphics;
using TerraFX.Numerics;
using TerraFX.Rendering;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

internal sealed partial class SceneAssetStoreTests
{
    [Test]
    public void SpriteSnapshotsOrderDrawsAndRetainAssetsAfterSceneUnload()
    {
        var schemas = CreateRendererRegistry();
        var frameId = new Guid("30000000-0000-4000-8000-000000000001");
        var textureRecord = CreateRenderAtlasPageTextureRecord(17);
        var atlasRecord = CreateRenderAtlasRecord(
            1,
            CreateRenderAtlasFrameRecord(frameId, page: 0, x: 4, y: 4, width: 4, height: 4));
        var materialRecord = CreateRenderMaterialAssetRecord(
            isSprite: true,
            CreateRenderMaterialSpriteParametersRecord(),
            alphaMode: 2,
            doubleSided: false,
            depthTest: false,
            depthWrite: false,
            cullMode: 0,
            outputAlpha: 1,
            requiredCapabilities: 0x009);
        var sceneRecords = new[]
        {
            SceneAssetReaderTests.Fixtures.CreateScene(
                new AssetId(4),
                additionalEntityComponents: [
                    CreateCameraRecord(order: 0),
                    CreateRenderSpriteRecord(2, frameId, materialAssetId: 3),
                ]),
            SceneAssetReaderTests.Fixtures.CreateScene(
                new AssetId(5),
                additionalEntityComponents: [CreateRenderSpriteRecord(2, frameId, materialAssetId: 3)]),
            SceneAssetReaderTests.Fixtures.CreateScene(
                new AssetId(6),
                additionalEntityComponents: [CreateRenderSpriteRecord(2, frameId, materialAssetId: 3)]),
            SceneAssetReaderTests.Fixtures.CreateScene(
                new AssetId(7),
                additionalEntityComponents: [
                    CreateRenderSpriteRecord(
                        2,
                        frameId,
                        materialAssetId: 3,
                        sortingLayer: -1,
                        sortingOrder: 20),
                ]),
            SceneAssetReaderTests.Fixtures.CreateScene(
                new AssetId(8),
                additionalEntityComponents: [
                    CreateRenderSpriteRecord(
                        2,
                        frameId,
                        materialAssetId: 3,
                        sortingOrder: -4),
                ]),
            SceneAssetReaderTests.Fixtures.CreateScene(
                new AssetId(9),
                additionalEntityComponents: [CreateRenderSpriteRecord(2, frameId, materialAssetId: 3)]),
        };
        var packageEntries = new Package.Entry[sceneRecords.Length + 3];
        packageEntries[0] = new Package.Entry(1, Package.CreateEnvelope(1, textureRecord));
        packageEntries[1] = new Package.Entry(2, Package.CreateEnvelope(2, atlasRecord));
        packageEntries[2] = new Package.Entry(3, Package.CreateEnvelope(3, materialRecord));

        for (var index = 0; index < sceneRecords.Length; index++)
        {
            var assetId = checked((uint)(index + 4));
            packageEntries[index + 3] = new Package.Entry(
                assetId,
                Package.CreateEnvelope(assetId, sceneRecords[index]));
        }

        var package = Package.CreatePackage(packageEntries);
        var packagePath = Package.WritePackage(package);
        var assetRegistrationBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = SceneSupport.RegisterAssetCodecs(assetRegistrationBuilder);
        _ = Renderer.RegisterAssetCodecs(assetRegistrationBuilder);
        var assets = AssetStore.CreateAsync(
            new AssetStoreOptions(packagePath, SHA256.HashData(package), assetRegistrationBuilder.Build()))
            .GetAwaiter()
            .GetResult();

        try
        {
            var worldRegistrationBuilder = new WorldRegistrationBuilder(schemas);
            _ = worldRegistrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
            _ = worldRegistrationBuilder.RegisterCpuExtractionSupport(Renderer.CreateCpuExtractionSupportRegistration());
            using var world = World.Create(new WorldOptions(worldRegistrationBuilder.Build()));
            var loader = new SceneLoader(world, assets);
            var sceneInstances = new SceneInstance[sceneRecords.Length];
            var sceneEntities = new Entity[sceneRecords.Length];

            for (var index = 0; index < sceneInstances.Length; index++)
            {
                sceneInstances[index] = ActivateScene(loader, world, new AssetId(index + 4));
                Assert.That(
                    sceneInstances[index].TryGetEntity(SceneAssetReaderTests.Fixtures.RootEntityId, out sceneEntities[index]),
                    Is.True);
            }

            var rootPositionOffsets = new[] { 0.0f, 9.0f, 2.0f, 0.0f, 100.0f, 8.0f };

            for (var index = 0; index < sceneEntities.Length; index++)
            {
                var rootPosition = new SceneComponentDefinitions.RootPositionComponent(
                    new SpatialPosition3(default, new Vector3(0.0f, 0.0f, rootPositionOffsets[index])));
                world.Set(sceneEntities[index], in rootPosition);
            }

            var cameraId = world.GetEntityId(sceneEntities[0]);
            var spriteEntityIds = new Guid[sceneEntities.Length];

            for (var index = 0; index < sceneEntities.Length; index++)
            {
                spriteEntityIds[index] = world.GetEntityId(sceneEntities[index]);
            }

            var publication = world.Step().Publication;
            var options = new RenderExtractionOptions3D(
                publication.Frame3,
                new PixelSize(640, 480),
                [cameraId]);
            var renderer = Renderer.CreateHeadless();
            var response = renderer.Extract(world, options);
            Assert.That(response.Status, Is.EqualTo(RenderExtractionStatus.Extracted));
            using var snapshot = (RenderSnapshot3D)(response.Snapshot
                ?? throw new InvalidOperationException("The successful Sprite extraction omitted its snapshot."));
            var spriteDraws = snapshot.SpriteDraws;
            Assert.That(spriteDraws, Has.Length.EqualTo(6));

            var expectedEntityIds = new[]
            {
                spriteEntityIds[3],
                spriteEntityIds[4],
                spriteEntityIds[1],
                spriteEntityIds[5],
                spriteEntityIds[2],
                spriteEntityIds[0],
            };
            var cameraOrder = snapshot.SpriteCameraOrders[0];
            Assert.That(cameraOrder.CameraId, Is.EqualTo(cameraId));
            Assert.That(cameraOrder.DrawIndices, Has.Length.EqualTo(expectedEntityIds.Length));

            for (var index = 0; index < expectedEntityIds.Length; index++)
            {
                var sprite = spriteDraws[cameraOrder.DrawIndices[index]];
                Assert.That(sprite.EntityId, Is.EqualTo(expectedEntityIds[index]));
                Assert.That(sprite.DrawId, Is.Not.EqualTo(Guid.Empty));
                Assert.That(sprite.HasPreviousTransform, Is.False);
            }

            var frame2 = publication.Frame2
                ?? throw new InvalidOperationException("The enabled 2D frame was omitted from the committed publication.");
            var response2D = renderer.Extract(
                world,
                new RenderExtractionOptions2D(frame2, options.TargetExtent, options.CameraIds));
            Assert.That(response2D.Status, Is.EqualTo(RenderExtractionStatus.Extracted));
            using var snapshot2D = (RenderSnapshot2D)(response2D.Snapshot
                ?? throw new InvalidOperationException("The successful 2D Sprite extraction omitted its snapshot."));
            var cameraOrder2D = snapshot2D.SpriteCameraOrders[0];
            Assert.That(snapshot2D.SpriteDraws, Has.Length.EqualTo(expectedEntityIds.Length));

            for (var index = 0; index < expectedEntityIds.Length; index++)
            {
                Assert.That(
                    snapshot2D.SpriteDraws[cameraOrder2D.DrawIndices[index]].EntityId,
                    Is.EqualTo(expectedEntityIds[index]));
            }

            snapshot2D.Dispose();

            var retainedSceneInstanceId = sceneInstances[0].SceneInstanceId;
            var assetPins = snapshot.AssetPins
                ?? throw new InvalidOperationException("The Sprite snapshot did not retain its Scene asset pins.");
            Assert.That(
                assetPins.GetSceneAssets(retainedSceneInstanceId).GetAtlasPageSet(2).GetPage(0).Mips[0].Pixels[0],
                Is.EqualTo(17));
            Assert.That(assetPins.GetSceneAssets(retainedSceneInstanceId).GetMaterial(3).IsSprite, Is.True);

            foreach (var sceneInstance in sceneInstances)
            {
                var unload = sceneInstance.UnloadAsync(HierarchyDestroyMode.DestroySubtree);
                world.PumpControlBoundary();
                Assert.That(unload.GetAwaiter().GetResult().Status, Is.EqualTo(SceneUnloadStatus.Unloaded));
            }

            Assert.That(
                assetPins.GetSceneAssets(retainedSceneInstanceId).GetAtlasPageSet(2).GetPage(0).Mips[0].Pixels[0],
                Is.EqualTo(17));
            Assert.That(assetPins.GetSceneAssets(retainedSceneInstanceId).GetMaterial(3).IsSprite, Is.True);
            snapshot.Dispose();
            Assert.That(snapshot.AssetPins, Is.Null);
        }
        finally
        {
            assets.DisposeAsync().AsTask().GetAwaiter().GetResult();
            File.Delete(packagePath);
        }
    }
}
