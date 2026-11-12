// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
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
    public void RenderExtractionOptionsAdmitOneThroughSixteenCamerasOnly()
    {
        var schemas = CreateRendererRegistry();
        var registrationBuilder = new WorldRegistrationBuilder(schemas);
        _ = registrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
        _ = registrationBuilder.RegisterCpuExtractionSupport(Renderer.CreateCpuExtractionSupportRegistration());
        using var world = World.Create(new WorldOptions(registrationBuilder.Build()));
        var frame = world.CurrentFrame2
            ?? throw new InvalidOperationException("The selected World omitted its initial 2D frame.");
        var cameraIds = new Guid[17];

        for (var index = 0; index < cameraIds.Length; index++)
        {
            cameraIds[index] = Guid.Parse($"60000000-0000-4000-8000-{index + 1:x12}");
        }

        Assert.That(
            () => new RenderExtractionOptions2D(frame, new PixelSize(1, 1), [cameraIds[0]]),
            Throws.Nothing);
        Assert.That(
            () => new RenderExtractionOptions2D(frame, new PixelSize(1, 1), [.. cameraIds[..16]]),
            Throws.Nothing);
        Assert.That(
            () => new RenderExtractionOptions2D(frame, new PixelSize(1, 1), [.. cameraIds]),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    [SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "Scene activation and unload requests complete at their World owner boundaries.")]
    public void SpriteExtractionEnforcesExactDefaultLogicalDrawLimit()
    {
        const int maximumDrawInstances = 131072;
        const int firstSceneAssetId = 2000;
        const int secondSceneAssetId = 2001;
        const int additionalSceneAssetId = 2002;
        const int prefabAssetIdBase = 100;
        const int prefabDepth = 15;
        var schemas = CreateRendererRegistry();
        var frameId = Guid.Parse("30000000-0000-4000-8000-000000000001");
        var textureRecord = CreateRenderAtlasPageTextureRecord(17);
        var atlasRecord = CreateRenderAtlasRecord(
            1,
            CreateRenderAtlasFrameRecord(frameId, page: 0, x: 4, y: 4, width: 4, height: 4));
        var spriteRecord = CreateRenderSpriteRecord(2, frameId, materialAssetId: null);
        var prefabAssetIds = new AssetId[prefabDepth + 1];
        var prefabEntityIds = new Guid[prefabDepth + 1];
        var prefabRecords = new byte[prefabDepth + 1][];

        for (var level = 0; level <= prefabDepth; level++)
        {
            prefabAssetIds[level] = new AssetId(prefabAssetIdBase + level);
            prefabEntityIds[level] = Guid.Parse($"30000000-0000-4000-8000-{level + 2:x12}");

            if (level == 0)
            {
                prefabRecords[level] = SceneAssetReaderTests.Fixtures.CreatePrefab(
                    prefabAssetIds[level],
                    entityId: prefabEntityIds[level],
                    additionalEntityComponents: [spriteRecord]);
                continue;
            }

            var childRootPose = SceneAssetReaderTests.Fixtures.CreateRootPose(
                prefabEntityIds[level - 1],
                includeRootPosition: false);
            var childPlacementIds = new Guid[2];
            var childPlacements = new byte[2][];

            for (var branch = 0; branch < childPlacements.Length; branch++)
            {
                childPlacementIds[branch] = Guid.Parse(
                    $"40000000-0000-4000-8000-{(level * 2) + branch + 1:x12}");
                childPlacements[branch] = SceneAssetReaderTests.Fixtures.CreatePrefabPlacement(
                    childPlacementIds[branch],
                    [childRootPose],
                    [],
                    [],
                    prefabAssetIds[level - 1],
                    prefabEntityIds[level]);
            }

            prefabRecords[level] = SceneAssetReaderTests.Fixtures.CreatePrefabWithChildPlacements(
                prefabAssetIds[level],
                prefabEntityIds[level],
                childPlacementIds,
                childPlacements,
                [spriteRecord]);
        }

        var rootEntityId = SceneAssetReaderTests.Fixtures.RootEntityId;
        var firstSceneRecord = SceneAssetReaderTests.Fixtures.CreateSceneWithChildPlacement(
            new AssetId(firstSceneAssetId),
            rootEntityId,
            Guid.Parse("50000000-0000-4000-8000-000000000001"),
            SceneAssetReaderTests.Fixtures.CreateRootPose(
                prefabEntityIds[^1],
                includeRootPosition: false),
            additionalEntityComponents: [spriteRecord],
            placementPrefabAssetId: prefabAssetIds[^1]);
        var secondSceneRecord = SceneAssetReaderTests.Fixtures.CreateSceneWithChildPlacement(
            new AssetId(secondSceneAssetId),
            rootEntityId,
            Guid.Parse("50000000-0000-4000-8000-000000000002"),
            SceneAssetReaderTests.Fixtures.CreateRootPose(
                prefabEntityIds[^1],
                includeRootPosition: false),
            additionalEntityComponents: [spriteRecord],
            placementPrefabAssetId: prefabAssetIds[^1]);
        var additionalSceneRecord = SceneAssetReaderTests.Fixtures.CreateScene(
            new AssetId(additionalSceneAssetId),
            additionalEntityComponents: [spriteRecord]);
        var packageEntries = new Package.Entry[2 + prefabRecords.Length + 3];
        packageEntries[0] = new Package.Entry(1, Package.CreateEnvelope(1, textureRecord));
        packageEntries[1] = new Package.Entry(2, Package.CreateEnvelope(2, atlasRecord));

        for (var index = 0; index < prefabRecords.Length; index++)
        {
            var assetId = checked((uint)prefabAssetIds[index].Value);
            packageEntries[index + 2] = new Package.Entry(
                assetId,
                Package.CreateEnvelope(assetId, prefabRecords[index]));
        }

        packageEntries[^3] = new Package.Entry(
            firstSceneAssetId,
            Package.CreateEnvelope(firstSceneAssetId, firstSceneRecord));
        packageEntries[^2] = new Package.Entry(
            secondSceneAssetId,
            Package.CreateEnvelope(secondSceneAssetId, secondSceneRecord));
        packageEntries[^1] = new Package.Entry(
            additionalSceneAssetId,
            Package.CreateEnvelope(additionalSceneAssetId, additionalSceneRecord));
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
            var cameraIds = new[] {
                Guid.Parse("60000000-0000-4000-8000-000000000001"),
                Guid.Parse("60000000-0000-4000-8000-000000000002"),
            };

            foreach (var cameraId in cameraIds)
            {
                var cameraEntity = world.CreateEntity(new EntityDescription(cameraId));
                var localTransform = SceneComponentDefinitions.CreateAssignedLocalTransform(
                    Vector3.Zero,
                    Quaternion.Identity,
                    Vector3.One);
                var rootPosition = new SceneComponentDefinitions.RootPositionComponent(
                    new SpatialPosition3(default, new Vector3(0, 0, -5)));
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
                world.Add(cameraEntity, in localTransform);
                world.Add(cameraEntity, in rootPosition);
                world.Add(cameraEntity, in camera);
            }

            var loader = new SceneLoader(world, assets);
            var firstInstance = ActivateScene(loader, world, new AssetId(firstSceneAssetId));
            var secondInstance = ActivateScene(loader, world, new AssetId(secondSceneAssetId));
            Assert.That(firstInstance.EntityCount, Is.EqualTo(maximumDrawInstances / 2));
            Assert.That(secondInstance.EntityCount, Is.EqualTo(maximumDrawInstances / 2));
            var renderer = Renderer.CreateHeadless();
            var publication = world.Step().Publication;
            var frame = publication.Frame2
                ?? throw new InvalidOperationException("The committed Sprite World omitted Frame2.");
            var extent = new PixelSize(640, 480);
            var options = new RenderExtractionOptions2D(frame, extent, [.. cameraIds]);
            var response = renderer.Extract(world, options);

            Assert.That(response.Status, Is.EqualTo(RenderExtractionStatus.Extracted));
            var snapshot = response.Snapshot as RenderSnapshot2D
                ?? throw new InvalidOperationException("The at-limit extraction omitted its 2D snapshot.");
            using (snapshot)
            {
                Assert.That(snapshot.SpriteDraws, Has.Length.EqualTo(maximumDrawInstances));
                Assert.That(snapshot.SpriteCameraOrders, Has.Length.EqualTo(cameraIds.Length));
                Assert.That(
                    snapshot.SpriteCameraOrders[0].DrawIndices,
                    Has.Length.EqualTo(maximumDrawInstances));
                Assert.That(
                    snapshot.SpriteCameraOrders[1].DrawIndices,
                    Has.Length.EqualTo(maximumDrawInstances));

                var additionalInstance = ActivateScene(loader, world, new AssetId(additionalSceneAssetId));
                Assert.That(() => renderer.Extract(world, options), Throws.InvalidOperationException);
                var overLimitPublication = world.Step().Publication;
                var overLimitFrame = overLimitPublication.Frame2
                    ?? throw new InvalidOperationException("The over-limit Sprite World omitted Frame2.");
                var overLimitResponse = renderer.Extract(
                    world,
                    new RenderExtractionOptions2D(overLimitFrame, extent, [.. cameraIds]));

                Assert.That(overLimitResponse.Status, Is.EqualTo(RenderExtractionStatus.CapacityExceeded));
                Assert.That(overLimitResponse.Snapshot, Is.Null);
                Assert.That(overLimitResponse.LimitDiagnostic?.LimitKind, Is.EqualTo(RenderExtractionLimitKind.DrawInstances));
                Assert.That(overLimitResponse.LimitDiagnostic?.Requested, Is.EqualTo(maximumDrawInstances + 1));
                Assert.That(overLimitResponse.LimitDiagnostic?.Limit, Is.EqualTo(maximumDrawInstances));

                var unloadFirst = firstInstance.UnloadAsync(HierarchyDestroyMode.DestroySubtree);
                var unloadSecond = secondInstance.UnloadAsync(HierarchyDestroyMode.DestroySubtree);
                var unloadAdditional = additionalInstance.UnloadAsync(HierarchyDestroyMode.DestroySubtree);
                world.PumpControlBoundary();
                Assert.That(unloadFirst.GetAwaiter().GetResult().Status, Is.EqualTo(SceneUnloadStatus.Unloaded));
                Assert.That(unloadSecond.GetAwaiter().GetResult().Status, Is.EqualTo(SceneUnloadStatus.Unloaded));
                Assert.That(unloadAdditional.GetAwaiter().GetResult().Status, Is.EqualTo(SceneUnloadStatus.Unloaded));
                var assetPins = snapshot.AssetPins
                    ?? throw new InvalidOperationException("The Sprite snapshot omitted its package asset pins.");
                Assert.That(assetPins.GetSceneAssets(firstInstance.SceneInstanceId).GetAtlasPageSet(2).GetPage(0).Mips[0].Pixels[0], Is.EqualTo(17));
            }
        }
        finally
        {
            assets.DisposeAsync().AsTask().GetAwaiter().GetResult();
            File.Delete(packagePath);
        }
    }
}
