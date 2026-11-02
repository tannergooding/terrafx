// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
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
using RenderFrameDimension = TerraFX.Rendering.RenderFrameDimension;

namespace TerraFX.Runtime.Tests.Scenes;

internal sealed partial class SceneAssetStoreTests
{
    [SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "The owner boundary completes the admitted activation request before the prepared scene is disposed.")]
    private static SceneInstance ActivateScene(
        SceneLoader loader,
        World world,
        AssetId assetId)
    {
        using var prepared = loader.PrepareAsync(assetId, new SceneLoadOptions()).GetAwaiter().GetResult();
        var activation = loader.ActivateAsync(prepared);
        world.PumpControlBoundary();
        var receipt = activation.GetAwaiter().GetResult();

        if (receipt.Status != SceneActivationStatus.Activated || receipt.Instance is null)
        {
            throw new InvalidOperationException("The Renderer extraction fixture failed to activate its Scene.");
        }

        return receipt.Instance;
    }

    private static byte[] CreateCameraRecord(int order)
    {
        var layerMask = new byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(layerMask, ulong.MaxValue);
        var orderBytes = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(orderBytes, order);
        var farBytes = new byte[sizeof(float)];
        BinaryPrimitives.WriteInt32LittleEndian(farBytes, BitConverter.SingleToInt32Bits(1000.0f));
        return SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("scene/camera").SchemaId,
            1,
            0,
            new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(
                1,
                1,
                1,
                1,
                [(byte)CameraProjectionKind.Perspective]),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(2, 10, float.Pi / 3.0f),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(3, 10, 10.0f),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(4, 10, 0.1f),
            new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(5, 1, 1, 1, [(byte)CameraFarMode.Finite]),
            new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(6, 10, 0, 1, farBytes),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(8, 16, 0.0f, 0.0f, 1.0f, 1.0f),
            new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(
                9,
                1,
                1,
                1,
                [(byte)(CameraClearFlags.Color | CameraClearFlags.Depth)]),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(10, 16, 0.0f, 0.0f, 0.0f, 1.0f),
            new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(11, 4, 1, 1, layerMask),
            new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(12, 7, 1, 1, orderBytes));
    }

    private static byte[] CreateDirectionalLightRecord(ulong layerMask, bool enabled, int priority)
    {
        var layerMaskBytes = new byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(layerMaskBytes, layerMask);
        var priorityBytes = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(priorityBytes, priority);
        return SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("render/directional-light").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(1, 15, 0.5f, 0.75f, 1.0f),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(2, 10, 5000.0f),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(3, 15, 0.0f, -1.0f, 0.0f),
            new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(4, 4, 1, 1, layerMaskBytes),
            new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(5, 9, 1, 1, [enabled ? (byte)1 : (byte)0]),
            new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(6, 7, 1, 1, priorityBytes));
    }

    private static byte[] CreatePointLightRecord(ulong layerMask)
    {
        var layerMaskBytes = new byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(layerMaskBytes, layerMask);
        return SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("render/point-light").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(1, 15, 0.25f, 0.5f, 1.0f),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(2, 10, 750.0f),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(3, 10, 12.0f),
            new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(4, 4, 1, 1, layerMaskBytes));
    }

    private static CompiledContentSchemaRegistry CreateRendererRegistry()
        => new([
            .. CoreContentSchemaCatalogue.PrimitiveSchemas,
            .. CoreContentSchemaCatalogue.FoundationSchemas,
            .. NativeAssetSchemaCatalogue.Schemas,
            .. SceneSchemaCatalogue.Schemas,
            .. RenderingSchemaCatalogue.Schemas,
        ]);

    private static byte[] CreateSpotLightRecord(ulong layerMask, bool enabled, int priority)
    {
        var layerMaskBytes = new byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(layerMaskBytes, layerMask);
        var priorityBytes = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(priorityBytes, priority);
        return SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("render/spot-light").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(1, 15, 0.5f, 0.75f, 1.0f),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(2, 10, 650.0f),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(3, 10, 8.0f),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(4, 15, 0.0f, -1.0f, 0.0f),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(5, 10, 0.4f),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(6, 10, 0.7f),
            new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(7, 4, 1, 1, layerMaskBytes),
            new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(8, 9, 1, 1, [enabled ? (byte)1 : (byte)0]),
            new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(9, 7, 1, 1, priorityBytes));
    }

    [Test]
    public void CpuExtractionSupportRequiresSceneSupportAndRejectsDuplicateRegistration()
    {
        var schemas = CreateRendererRegistry();
        var registrationBuilder = new WorldRegistrationBuilder(schemas);
        _ = registrationBuilder.RegisterCpuExtractionSupport(Renderer.CreateCpuExtractionSupportRegistration());

        Assert.That(() => registrationBuilder.Build(), Throws.InvalidOperationException);

        _ = registrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
        Assert.That(
            () => registrationBuilder.RegisterCpuExtractionSupport(Renderer.CreateCpuExtractionSupportRegistration()),
            Throws.ArgumentException);
        Assert.That(registrationBuilder.Build(), Is.Not.Null);
    }

    [Test]
    public void DirectionalAndSpotLightsUseWorldDefaultsAndRejectInvalidMutation()
    {
        var schemas = CreateRendererRegistry();
        var registrationBuilder = new WorldRegistrationBuilder(schemas);
        _ = registrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
        _ = registrationBuilder.RegisterCpuExtractionSupport(Renderer.CreateCpuExtractionSupportRegistration());
        using var world = World.Create(new WorldOptions(registrationBuilder.Build()));
        var entity = world.CreateEntity(new EntityDescription());
        world.Add<RenderComponentDefinitions.DirectionalLightComponent>(entity);
        world.Add<RenderComponentDefinitions.SpotLightComponent>(entity);
        Assert.That(
            world.TryGet<RenderComponentDefinitions.DirectionalLightComponent>(entity, out var directionalLight),
            Is.True);
        Assert.That(directionalLight.Color, Is.EqualTo(Vector3.One));
        Assert.That(directionalLight.Illuminance, Is.EqualTo(10000.0f));
        Assert.That(directionalLight.Direction, Is.EqualTo(new Vector3(0, -1, 0)));
        Assert.That(directionalLight.Enabled, Is.False);
        Assert.That(directionalLight.LayerMask, Is.EqualTo(ulong.MaxValue));
        Assert.That(directionalLight.Priority, Is.Zero);
        Assert.That(
            world.TryGet<RenderComponentDefinitions.SpotLightComponent>(entity, out var spotLight),
            Is.True);
        Assert.That(spotLight.Color, Is.EqualTo(Vector3.One));
        Assert.That(spotLight.LuminousFlux, Is.EqualTo(1000.0f));
        Assert.That(
            spotLight.Range,
            Is.EqualTo(SpatialMath.ConvertQuantity(
                10,
                1,
                0,
                world.SpatialSettings.MetersPerUnitExponent,
                1)));
        Assert.That(spotLight.Direction, Is.EqualTo(new Vector3(0, -1, 0)));
        Assert.That(spotLight.InnerConeAngle, Is.EqualTo(BitConverter.Int32BitsToSingle(0x3F060A92)));
        Assert.That(spotLight.OuterConeAngle, Is.EqualTo(BitConverter.Int32BitsToSingle(0x3F490FDB)));
        Assert.That(spotLight.Enabled, Is.False);

        var invalidDirectional = new RenderComponentDefinitions.DirectionalLightComponent(
            Vector3.One,
            float.NaN,
            new Vector3(0, -1, 0),
            ulong.MaxValue,
            enabled: false,
            priority: 0);
        var invalidSpot = new RenderComponentDefinitions.SpotLightComponent(
            Vector3.One,
            1000.0f,
            spotLight.Range,
            new Vector3(0, -1, 0),
            0.8f,
            0.7f,
            ulong.MaxValue,
            enabled: false,
            priority: 0);
        Assert.That(() => world.Set(entity, invalidDirectional), Throws.ArgumentException);
        Assert.That(() => world.Set(entity, invalidSpot), Throws.ArgumentException);
        Assert.That(
            world.TryGet<RenderComponentDefinitions.DirectionalLightComponent>(entity, out directionalLight),
            Is.True);
        Assert.That(world.TryGet<RenderComponentDefinitions.SpotLightComponent>(entity, out spotLight), Is.True);
        Assert.That(directionalLight.Illuminance, Is.EqualTo(10000.0f));
        Assert.That(spotLight.InnerConeAngle, Is.EqualTo(BitConverter.Int32BitsToSingle(0x3F060A92)));
    }

    [Test]
    [SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "Each admitted Scene activation request is pumped and completed before its PreparedScene is disposed.")]
    public void HeadlessRendererExtractsNestedSceneCamerasFromOneCommittedFrame()
    {
        const int prefabAssetId = 1;
        const int firstSceneAssetId = 3;
        const int secondSceneAssetId = 4;
        var schemas = CreateRendererRegistry();
        var childCamera = CreateCameraRecord(order: 5);
        var childPrefab = SceneAssetReaderTests.Fixtures.CreatePrefab(
            new AssetId(prefabAssetId),
            entityId: s_childEntityId,
            additionalEntityComponents: [
                childCamera,
                CreateDirectionalLightRecord(ulong.MaxValue, enabled: true, priority: 2),
                CreatePointLightRecord(ulong.MaxValue),
                CreateSpotLightRecord(ulong.MaxValue, enabled: true, priority: 1),
            ]);
        var firstScene = SceneAssetReaderTests.Fixtures.CreateSceneWithChildPlacement(
            new AssetId(firstSceneAssetId),
            s_sceneRootEntityId,
            s_placementId,
            SceneAssetReaderTests.Fixtures.CreateRootPose(
                s_childEntityId,
                localPositionX: 2.0f,
                includeRootPosition: false));
        var secondScene = SceneAssetReaderTests.Fixtures.CreateScene(
            new AssetId(secondSceneAssetId),
            additionalEntityComponents: [
                CreateCameraRecord(order: -1),
                CreatePointLightRecord(layerMask: 0),
                CreateDirectionalLightRecord(layerMask: ulong.MaxValue, enabled: false, priority: 100),
                CreateSpotLightRecord(layerMask: 0, enabled: true, priority: 100),
            ]);
        var package = Package.CreatePackage(
            new Package.Entry(prefabAssetId, Package.CreateEnvelope(prefabAssetId, childPrefab)),
            new Package.Entry(firstSceneAssetId, Package.CreateEnvelope(firstSceneAssetId, firstScene)),
            new Package.Entry(secondSceneAssetId, Package.CreateEnvelope(secondSceneAssetId, secondScene)));
        var packagePath = Package.WritePackage(package);
        var assetRegistration = SceneSupport.RegisterAssetCodecs(new AssetStoreRegistrationBuilder(schemas)).Build();
        var assets = AssetStore.CreateAsync(
            new AssetStoreOptions(packagePath, SHA256.HashData(package), assetRegistration)).GetAwaiter().GetResult();

        try
        {
            var registrationBuilder = new WorldRegistrationBuilder(schemas);
            var sceneSupport = SceneSupport.CreateRegistration();
            _ = registrationBuilder.RegisterSceneSupport(sceneSupport);
            _ = registrationBuilder.RegisterCpuExtractionSupport(Renderer.CreateCpuExtractionSupportRegistration());
            using var world = World.Create(new WorldOptions(registrationBuilder.Build()));
            var loader = new SceneLoader(world, assets);
            var firstInstance = ActivateScene(loader, world, new AssetId(firstSceneAssetId));
            var secondInstance = ActivateScene(loader, world, new AssetId(secondSceneAssetId));
            Assert.That(
                firstInstance.TryGetEntity(new SceneEntityAddress([s_placementId], s_childEntityId), out var child),
                Is.True);
            Assert.That(secondInstance.TryGetEntity(SceneAssetReaderTests.Fixtures.RootEntityId, out var rootCamera), Is.True);

            var externalRootTransform = new SceneComponentDefinitions.LocalTransformComponent(
                Vector3.Zero,
                Quaternion.CreateFromAxisAngle(Vector3.UnitZ, Angle.FromDegrees(90.0f)),
                new Vector3(2.0f, 1.0f, 1.0f));
            var externalRootPosition = new SceneComponentDefinitions.RootPositionComponent(
                new SpatialPosition3(default, new Vector3(5.0f, 0.0f, 0.0f)));
            var externalRoot = world.CreateEntity(new EntityDescription(components: [
                ComponentInitialization.Value(in externalRootPosition),
                ComponentInitialization.Value(in externalRootTransform),
            ]));
            world.ApplyHierarchyParentChange(world.GetSceneParticipant(sceneSupport), child, externalRoot);
            Assert.That(world.TryGetHierarchyParent(child, out var actualParent), Is.True);
            Assert.That(actualParent, Is.EqualTo(externalRoot));
            Assert.That(
                world.TryGet<SceneComponentDefinitions.LocalTransformComponent>(child, out var childTransform),
                Is.True);
            Assert.That(childTransform.Position.X, Is.EqualTo(2.0f));
            var childCameraId = world.GetEntityId(child);
            var rootCameraId = world.GetEntityId(rootCamera);
            var externalRootId = world.GetEntityId(externalRoot);
            var publication = world.Step().Publication;

            var foundChildScene = false;
            var foundRootScene = false;
            var foundExternalRoot = false;

            using (var captureLease = world.AcquirePersistenceCaptureLease())
            {
                var entities = captureLease.GetEntityStateEnumerator();

                while (entities.MoveNext())
                {
                    var entityId = entities.CurrentEntityId;

                    if (entityId == childCameraId)
                    {
                        Assert.That(entities.CurrentSceneInstanceId, Is.EqualTo(firstInstance.SceneInstanceId));
                        foundChildScene = true;
                    }
                    else if (entityId == rootCameraId)
                    {
                        Assert.That(entities.CurrentSceneInstanceId, Is.EqualTo(secondInstance.SceneInstanceId));
                        foundRootScene = true;
                    }
                    else if (entityId == externalRootId)
                    {
                        Assert.That(entities.CurrentSceneInstanceId, Is.Null);
                        foundExternalRoot = true;
                    }
                }
            }

            Assert.That(foundChildScene, Is.True);
            Assert.That(foundRootScene, Is.True);
            Assert.That(foundExternalRoot, Is.True);

            var renderer = Renderer.CreateHeadless();
            var options = new RenderExtractionOptions3D(
                publication.Frame3,
                new PixelSize(640, 480),
                [childCameraId, rootCameraId]);

            var response = renderer.Extract(world, options);

            Assert.That(response.Status, Is.EqualTo(RenderExtractionStatus.Extracted));
            Assert.That(response.LimitDiagnostic.HasValue, Is.False);
            Assert.That(response.Diagnostics, Is.Empty);
            Assert.That(response.Snapshot, Is.TypeOf<RenderSnapshot3D>());
            var snapshot = (RenderSnapshot3D)(response.Snapshot
                ?? throw new InvalidOperationException("The successful CPU extraction omitted its snapshot."));
            Assert.That(snapshot.SnapshotId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(snapshot.WorldGuid, Is.EqualTo(publication.WorldGuid));
            Assert.That(snapshot.TickIndex, Is.EqualTo(publication.CompletedTick));
            Assert.That(snapshot.FrameRevision, Is.EqualTo(publication.Frame3.Revision));
            Assert.That(snapshot.FrameDimension, Is.EqualTo(RenderFrameDimension.ThreeDimensional));
            Assert.That(snapshot.TargetExtent, Is.EqualTo(new PixelSize(640, 480)));
            Assert.That(snapshot.CameraIds, Is.EqualTo(ImmutableArray.Create(rootCameraId, childCameraId)));
            Assert.That(snapshot.Cameras[0].Order, Is.EqualTo(-1));
            Assert.That(snapshot.Cameras[0].CameraId, Is.EqualTo(rootCameraId));
            Assert.That(snapshot.Cameras[1].CameraId, Is.EqualTo(childCameraId));
            Assert.That(snapshot.Cameras[1].Position.Chunk.X, Is.Zero);
            Assert.That(snapshot.Cameras[1].Position.Chunk.Y, Is.Zero);
            Assert.That(snapshot.Cameras[1].Position.Chunk.Z, Is.Zero);
            Assert.That(float.Abs(snapshot.Cameras[1].Position.Offset.X - 5.0f), Is.LessThan(1.0e-5f));
            Assert.That(float.Abs(snapshot.Cameras[1].Position.Offset.Y - 4.0f), Is.LessThan(1.0e-5f));
            Assert.That(snapshot.PointLightIds, Has.Length.EqualTo(1));
            Assert.That(snapshot.PointLightIds[0], Is.Not.EqualTo(Guid.Empty));
            Assert.That(snapshot.PointLights[0].LightId, Is.EqualTo(snapshot.PointLightIds[0]));
            Assert.That(snapshot.PointLights, Has.Length.EqualTo(1));
            Assert.That(snapshot.PointLights[0].EntityId, Is.EqualTo(childCameraId));
            Assert.That(snapshot.PointLights[0].LuminousFlux, Is.EqualTo(750.0f));
            Assert.That(snapshot.PointLights[0].Range, Is.EqualTo(12.0f));
            Assert.That(snapshot.PointLights[0].Position, Is.EqualTo(snapshot.Cameras[1].Position));
            Assert.That(snapshot.DirectionalLightIds, Has.Length.EqualTo(1));
            Assert.That(snapshot.DirectionalLights[0].Illuminance, Is.EqualTo(5000.0f));
            Assert.That(snapshot.DirectionalLights[0].Priority, Is.EqualTo(2));
            Assert.That(float.Abs(snapshot.DirectionalLights[0].Direction.X - 1.0f), Is.LessThan(1.0e-5f));
            Assert.That(float.Abs(snapshot.DirectionalLights[0].Direction.Y), Is.LessThan(1.0e-5f));
            Assert.That(snapshot.SpotLightIds, Has.Length.EqualTo(1));
            Assert.That(snapshot.SpotLights[0].LuminousFlux, Is.EqualTo(650.0f));
            Assert.That(snapshot.SpotLights[0].InnerConeAngle, Is.EqualTo(0.4f));
            Assert.That(snapshot.SpotLights[0].OuterConeAngle, Is.EqualTo(0.7f));
            Assert.That(snapshot.SpotLights[0].Position, Is.EqualTo(snapshot.Cameras[1].Position));
            Assert.That(float.Abs(snapshot.SpotLights[0].Direction.X - 1.0f), Is.LessThan(1.0e-5f));
            Assert.That(float.Abs(snapshot.SpotLights[0].Direction.Y), Is.LessThan(1.0e-5f));

            var cameraRight = Vector3.TransformDirection(
                Vector3.UnitX,
                Matrix4x4.CreateFromQuaternion(snapshot.Cameras[1].Rotation));
            Assert.That(float.Abs(cameraRight.X), Is.LessThan(1.0e-5f));
            Assert.That(float.Abs(cameraRight.Y - 1.0f), Is.LessThan(1.0e-5f));

            var frame2 = publication.Frame2
                ?? throw new InvalidOperationException("The enabled 2D frame was omitted from the committed publication.");
            var options2D = new RenderExtractionOptions2D(frame2, options.TargetExtent, options.CameraIds);
            var response2D = renderer.Extract(world, options2D);

            Assert.That(response2D.Status, Is.EqualTo(RenderExtractionStatus.Extracted));
            Assert.That(response2D.Snapshot, Is.TypeOf<RenderSnapshot2D>());
            var snapshot2D = (RenderSnapshot2D)(response2D.Snapshot
                ?? throw new InvalidOperationException("The successful 2D extraction omitted its snapshot."));
            Assert.That(snapshot2D.FrameDimension, Is.EqualTo(RenderFrameDimension.TwoDimensional));
            Assert.That(snapshot2D.FrameRevision, Is.EqualTo(frame2.Revision));
            Assert.That(snapshot2D.Cameras[1].Position, Is.EqualTo(snapshot.Cameras[1].Position));
            Assert.That(snapshot2D.PointLightIds, Is.EqualTo(snapshot.PointLightIds));
            Assert.That(snapshot2D.PointLights[0].Position, Is.EqualTo(snapshot.PointLights[0].Position));
            Assert.That(snapshot2D.PointLights[0].LuminousFlux, Is.EqualTo(snapshot.PointLights[0].LuminousFlux));
            Assert.That(snapshot2D.DirectionalLightIds, Is.EqualTo(snapshot.DirectionalLightIds));
            Assert.That(snapshot2D.SpotLightIds, Is.EqualTo(snapshot.SpotLightIds));
            snapshot2D.Dispose();

            snapshot.Dispose();
            Assert.That(snapshot.Cameras, Has.Length.EqualTo(2));
            var changedRootTransform = new SceneComponentDefinitions.LocalTransformComponent(
                Vector3.Zero,
                Quaternion.Identity,
                Vector3.One);
            world.Set(externalRoot, in changedRootTransform);
            Assert.That(() => renderer.Extract(world, options), Throws.InvalidOperationException);
            var nextPublication = world.Step().Publication;
            var currentOptions = new RenderExtractionOptions3D(
                nextPublication.Frame3,
                options.TargetExtent,
                options.CameraIds);
            var currentResponse = renderer.Extract(world, currentOptions);
            Assert.That(currentResponse.Status, Is.EqualTo(RenderExtractionStatus.Extracted));
            Assert.That(currentResponse.Snapshot?.TickIndex, Is.EqualTo(nextPublication.CompletedTick));
            var currentSnapshot = (RenderSnapshot3D)(currentResponse.Snapshot
                ?? throw new InvalidOperationException("The successful current 3D extraction omitted its snapshot."));
            Assert.That(currentSnapshot.PointLights[0].LightId, Is.EqualTo(snapshot.PointLights[0].LightId));
            Assert.That(currentSnapshot.DirectionalLightIds, Is.EqualTo(snapshot.DirectionalLightIds));
            Assert.That(currentSnapshot.SpotLightIds, Is.EqualTo(snapshot.SpotLightIds));
            var nextFrame2 = nextPublication.Frame2
                ?? throw new InvalidOperationException("The enabled 2D frame was omitted from the next committed publication.");
            var currentOptions2D = new RenderExtractionOptions2D(
                nextFrame2,
                currentOptions.TargetExtent,
                currentOptions.CameraIds);
            var currentResponse2D = renderer.Extract(world, currentOptions2D);
            Assert.That(currentResponse2D.Status, Is.EqualTo(RenderExtractionStatus.Extracted));
            Assert.That(currentResponse2D.Snapshot?.TickIndex, Is.EqualTo(nextPublication.CompletedTick));
            Assert.That(
                currentSnapshot.PointLights[0].Position.Offset,
                Is.EqualTo(new Vector3(7.0f, 0.0f, 0.0f)));
            currentResponse.Snapshot?.Dispose();
            currentResponse2D.Snapshot?.Dispose();

            var lightTransform = new SceneComponentDefinitions.LocalTransformComponent(
                Vector3.Zero,
                Quaternion.Identity,
                Vector3.One);
            var lightRootPosition = new SceneComponentDefinitions.RootPositionComponent(default);
            var capacityLight = new RenderComponentDefinitions.PointLightComponent(
                Vector3.One,
                1000.0f,
                10.0f,
                ulong.MaxValue);
            ImmutableArray<ComponentInitialization> lightComponents = [
                ComponentInitialization.Value(in lightTransform),
                ComponentInitialization.Value(in lightRootPosition),
                ComponentInitialization.Value(in capacityLight),
            ];

            for (var index = 0; index < 253; index++)
            {
                _ = world.CreateEntity(new EntityDescription(components: lightComponents));
            }

            var atLimitPublication = world.Step().Publication;
            var atLimitOptions = new RenderExtractionOptions3D(
                atLimitPublication.Frame3,
                options.TargetExtent,
                options.CameraIds);
            var atLimitResponse = renderer.Extract(world, atLimitOptions);
            Assert.That(atLimitResponse.Status, Is.EqualTo(RenderExtractionStatus.Extracted));
            var atLimitSnapshot = (RenderSnapshot3D)(atLimitResponse.Snapshot
                ?? throw new InvalidOperationException("The successful light-limit extraction omitted its snapshot."));
            Assert.That(
                atLimitSnapshot.DirectionalLights.Length
                    + atLimitSnapshot.PointLights.Length
                    + atLimitSnapshot.SpotLights.Length,
                Is.EqualTo(256));
            atLimitSnapshot.Dispose();

            _ = world.CreateEntity(new EntityDescription(components: lightComponents));
            var capacityPublication = world.Step().Publication;
            var capacityOptions3D = new RenderExtractionOptions3D(
                capacityPublication.Frame3,
                options.TargetExtent,
                options.CameraIds);
            var capacityResponse3D = renderer.Extract(world, capacityOptions3D);
            Assert.That(capacityResponse3D.Status, Is.EqualTo(RenderExtractionStatus.CapacityExceeded));
            Assert.That(capacityResponse3D.Snapshot, Is.Null);
            Assert.That(capacityResponse3D.LimitDiagnostic?.LimitKind, Is.EqualTo(RenderExtractionLimitKind.Lights));
            Assert.That(capacityResponse3D.LimitDiagnostic?.Requested, Is.EqualTo(257));
            Assert.That(capacityResponse3D.LimitDiagnostic?.Limit, Is.EqualTo(256));
            Assert.That(capacityResponse3D.Diagnostics, Is.Empty);

            var capacityFrame2 = capacityPublication.Frame2
                ?? throw new InvalidOperationException("The enabled 2D frame was omitted from the capacity publication.");
            var capacityOptions2D = new RenderExtractionOptions2D(
                capacityFrame2,
                options.TargetExtent,
                options.CameraIds);
            var capacityResponse2D = renderer.Extract(world, capacityOptions2D);
            Assert.That(capacityResponse2D.Status, Is.EqualTo(RenderExtractionStatus.CapacityExceeded));
            Assert.That(capacityResponse2D.Snapshot, Is.Null);
            Assert.That(capacityResponse2D.LimitDiagnostic?.LimitKind, Is.EqualTo(RenderExtractionLimitKind.Lights));
            Assert.That(capacityResponse2D.LimitDiagnostic?.Requested, Is.EqualTo(257));
            Assert.That(capacityResponse2D.LimitDiagnostic?.Limit, Is.EqualTo(256));
        }
        finally
        {
            assets.DisposeAsync().AsTask().GetAwaiter().GetResult();
            File.Delete(packagePath);
        }
    }

    [Test]
    public void PointLightUsesWorldDefaultsAndRejectsInvalidMutation()
    {
        var schemas = CreateRendererRegistry();
        var registrationBuilder = new WorldRegistrationBuilder(schemas);
        _ = registrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
        _ = registrationBuilder.RegisterCpuExtractionSupport(Renderer.CreateCpuExtractionSupportRegistration());
        using var world = World.Create(new WorldOptions(registrationBuilder.Build()));
        var entity = world.CreateEntity(new EntityDescription());
        world.Add<RenderComponentDefinitions.PointLightComponent>(entity);

        Assert.That(
            world.TryGet<RenderComponentDefinitions.PointLightComponent>(entity, out var light),
            Is.True);
        Assert.That(light.Color, Is.EqualTo(Vector3.One));
        Assert.That(light.LuminousFlux, Is.EqualTo(1000.0f));
        Assert.That(
            light.Range,
            Is.EqualTo(SpatialMath.ConvertQuantity(
                10,
                1,
                0,
                world.SpatialSettings.MetersPerUnitExponent,
                1)));
        Assert.That(light.LayerMask, Is.EqualTo(ulong.MaxValue));

        var invalidLights = new[]
        {
            new RenderComponentDefinitions.PointLightComponent(
                new Vector3(1.0f, -1.0f, 1.0f),
                1000.0f,
                light.Range,
                ulong.MaxValue),
            new RenderComponentDefinitions.PointLightComponent(
                Vector3.One,
                float.NaN,
                light.Range,
                ulong.MaxValue),
            new RenderComponentDefinitions.PointLightComponent(
                Vector3.One,
                1000.0f,
                0.0f,
                ulong.MaxValue),
        };

        foreach (var invalidLight in invalidLights)
        {
            Assert.That(() => world.Set(entity, invalidLight), Throws.ArgumentException);
        }

        Assert.That(
            world.TryGet<RenderComponentDefinitions.PointLightComponent>(entity, out var unchangedLight),
            Is.True);
        Assert.That(unchangedLight, Is.EqualTo(light));
    }
}
