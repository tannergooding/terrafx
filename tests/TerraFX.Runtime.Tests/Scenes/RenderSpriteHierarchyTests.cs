// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
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
    [SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "Scene activation and unload requests complete at their World owner boundaries.")]
    public void SpriteSnapshotIncludesNestedHierarchyScaleAndPosition()
    {
        const int sceneAssetId = 3;
        const int atlasAssetId = 2;
        const int textureAssetId = 1;
        const int parentPrefabAssetId = 10;
        const int childPrefabAssetId = 11;
        var schemas = CreateRendererRegistry();
        var frameId = Guid.Parse("30000000-0000-4000-8000-000000000001");
        var parentEntityId = Guid.Parse("31000000-0000-4000-8000-000000000001");
        var childEntityId = Guid.Parse("32000000-0000-4000-8000-000000000001");
        var scenePlacementId = Guid.Parse("41000000-0000-4000-8000-000000000001");
        var childPlacementId = Guid.Parse("42000000-0000-4000-8000-000000000001");
        var sceneRootEntityId = SceneAssetReaderTests.Fixtures.RootEntityId;
        var spriteRecord = CreateRenderSpriteRecord(atlasAssetId, frameId, materialAssetId: null);
        var childPlacement = SceneAssetReaderTests.Fixtures.CreatePrefabPlacement(
            childPlacementId,
            [SceneAssetReaderTests.Fixtures.CreateRootPose(
                childEntityId,
                includeRootPosition: false,
                localPositionX: 3.0f)],
            [],
            [],
            new AssetId(childPrefabAssetId),
            parentEntityId);
        var parentPrefab = SceneAssetReaderTests.Fixtures.CreatePrefabWithChildPlacements(
            new AssetId(parentPrefabAssetId),
            parentEntityId,
            [childPlacementId],
            [childPlacement],
            [spriteRecord]);
        var childPrefab = SceneAssetReaderTests.Fixtures.CreatePrefab(
            new AssetId(childPrefabAssetId),
            entityId: childEntityId,
            additionalEntityComponents: [spriteRecord]);
        var scene = SceneAssetReaderTests.Fixtures.CreateSceneWithChildPlacement(
            new AssetId(sceneAssetId),
            sceneRootEntityId,
            scenePlacementId,
            SceneAssetReaderTests.Fixtures.CreateRootPose(
                parentEntityId,
                includeRootPosition: false,
                localScaleX: 2.0f),
            placementPrefabAssetId: new AssetId(parentPrefabAssetId));
        var package = Package.CreatePackage(
            new Package.Entry(textureAssetId, Package.CreateEnvelope(textureAssetId, CreateRenderAtlasPageTextureRecord(17))),
            new Package.Entry(
                atlasAssetId,
                Package.CreateEnvelope(
                    atlasAssetId,
                    CreateRenderAtlasRecord(
                        textureAssetId,
                        CreateRenderAtlasFrameRecord(frameId, page: 0, x: 4, y: 4, width: 4, height: 4)))),
            new Package.Entry(sceneAssetId, Package.CreateEnvelope(sceneAssetId, scene)),
            new Package.Entry(parentPrefabAssetId, Package.CreateEnvelope(parentPrefabAssetId, parentPrefab)),
            new Package.Entry(childPrefabAssetId, Package.CreateEnvelope(childPrefabAssetId, childPrefab)));
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
            var cameraId = Guid.Parse("60000000-0000-4000-8000-000000000001");
            var cameraEntity = world.CreateEntity(new EntityDescription(cameraId));
            var cameraTransform = SceneComponentDefinitions.CreateAssignedLocalTransform(
                Vector3.Zero,
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, float.Pi / 2.0f),
                Vector3.One);
            var cameraRootPosition = new SceneComponentDefinitions.RootPositionComponent(
                new SpatialPosition3(default, new Vector3(-10, 0, 0)));
            var camera = new SceneComponentDefinitions.CameraComponent(
                (byte)CameraProjectionKind.Perspective,
                float.Pi / 3.0f,
                1.0f,
                0.01f,
                (byte)CameraFarMode.Finite,
                new SceneComponentDefinitions.OptionalSingle(100.0f),
                default,
                new Vector4(0, 0, 1, 1),
                (byte)(CameraClearFlags.Color | CameraClearFlags.Depth),
                new Vector4(0, 0, 0, 1),
                ulong.MaxValue,
                0);
            world.Add(cameraEntity, in cameraTransform);
            world.Add(cameraEntity, in cameraRootPosition);
            world.Add(cameraEntity, in camera);

            var loader = new SceneLoader(world, assets);
            var instance = ActivateScene(loader, world, new AssetId(sceneAssetId));
            var parentAddress = new SceneEntityAddress([scenePlacementId], parentEntityId);
            var childAddress = new SceneEntityAddress([scenePlacementId, childPlacementId], childEntityId);
            Assert.That(instance.TryGetEntity(parentAddress, out var parentEntity), Is.True);
            Assert.That(instance.TryGetEntity(childAddress, out var childEntity), Is.True);
            var parentEntityIdValue = world.GetEntityId(parentEntity);
            var childEntityIdValue = world.GetEntityId(childEntity);
            var publication = world.Step().Publication;
            var frame = publication.Frame2
                ?? throw new InvalidOperationException("The committed Sprite World omitted Frame2.");
            var response = Renderer.CreateHeadless().Extract(
                world,
                new RenderExtractionOptions2D(frame, new PixelSize(640, 480), [cameraId]));

            Assert.That(response.Status, Is.EqualTo(RenderExtractionStatus.Extracted));
            var snapshot = response.Snapshot as RenderSnapshot2D
                ?? throw new InvalidOperationException("The nested Sprite extraction omitted its snapshot.");
            using (snapshot)
            {
                var childSprite = snapshot.SpriteDraws.Single(draw => draw.EntityId == childEntityIdValue);
                Assert.That(childSprite.Position.Offset.X, Is.EqualTo(6.0f).Within(1.0e-5f));
                Assert.That(childSprite.LinearTransform.Row1.X, Is.EqualTo(2.0f).Within(1.0e-5f));
                Assert.That(childSprite.LinearTransform.Row2.Y, Is.EqualTo(1.0f).Within(1.0e-5f));
                Assert.That(childSprite.LinearTransform.Row3.Z, Is.EqualTo(1.0f).Within(1.0e-5f));
                Assert.That(snapshot.SpriteCameraOrders[0].DrawIndices, Has.Length.EqualTo(2));
                Assert.That(
                    snapshot.SpriteDraws[snapshot.SpriteCameraOrders[0].DrawIndices[0]].EntityId,
                    Is.EqualTo(childEntityIdValue));
                Assert.That(
                    snapshot.SpriteDraws[snapshot.SpriteCameraOrders[0].DrawIndices[1]].EntityId,
                    Is.EqualTo(parentEntityIdValue));
            }

            var unload = instance.UnloadAsync(HierarchyDestroyMode.DestroySubtree);
            world.PumpControlBoundary();
            Assert.That(unload.GetAwaiter().GetResult().Status, Is.EqualTo(SceneUnloadStatus.Unloaded));
        }
        finally
        {
            assets.DisposeAsync().AsTask().GetAwaiter().GetResult();
            File.Delete(packagePath);
        }
    }
}
