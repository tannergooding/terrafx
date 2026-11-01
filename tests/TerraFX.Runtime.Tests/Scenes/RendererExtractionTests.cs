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
using TerraFX.Numerics;
using TerraFX.Rendering;
using TerraFX.Scenes;

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

    [Test]
    public void CpuExtractionSupportRequiresSceneSupportAndRejectsDuplicateRegistration()
    {
        var schemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
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
    [SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "Each admitted Scene activation request is pumped and completed before its PreparedScene is disposed.")]
    public void HeadlessRendererExtractsNestedSceneCamerasFromOneCommittedFrame()
    {
        const int prefabAssetId = 1;
        const int firstSceneAssetId = 3;
        const int secondSceneAssetId = 4;
        var schemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var childCamera = CreateCameraRecord(order: 5);
        var childPrefab = SceneAssetReaderTests.Fixtures.CreatePrefab(
            new AssetId(prefabAssetId),
            entityId: s_childEntityId,
            additionalEntityComponents: [childCamera]);
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
            additionalEntityComponents: [CreateCameraRecord(order: -1)]);
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
            var publication = world.Step().Publication;
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
            var nextFrame2 = nextPublication.Frame2
                ?? throw new InvalidOperationException("The enabled 2D frame was omitted from the next committed publication.");
            var currentOptions2D = new RenderExtractionOptions2D(
                nextFrame2,
                currentOptions.TargetExtent,
                currentOptions.CameraIds);
            var currentResponse2D = renderer.Extract(world, currentOptions2D);
            Assert.That(currentResponse2D.Status, Is.EqualTo(RenderExtractionStatus.Extracted));
            Assert.That(currentResponse2D.Snapshot?.TickIndex, Is.EqualTo(nextPublication.CompletedTick));
            currentResponse.Snapshot?.Dispose();
            currentResponse2D.Snapshot?.Dispose();
        }
        finally
        {
            assets.DisposeAsync().AsTask().GetAwaiter().GetResult();
            File.Delete(packagePath);
        }
    }
}
