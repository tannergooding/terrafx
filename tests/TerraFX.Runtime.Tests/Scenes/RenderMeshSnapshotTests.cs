// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Animation;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Graphics;
using TerraFX.Numerics;
using TerraFX.Rendering;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

internal sealed partial class SceneAssetStoreTests
{
    private static EbinRecord CreateMeshAssetReferenceRecord(uint assetId, Guid schemaId)
        => new(
            new ContentSchemaIdentityAssignment("native/asset-reference").SchemaId,
            1,
            0,
            [
                EbinPrimitives.UInt32(1, assetId),
                EbinPrimitives.Guid(2, schemaId),
                EbinPrimitives.UInt32(3, 1),
            ]);

    private static byte[] CreateMeshComponentRecord(int meshAssetId, ulong layerMask)
    {
        var layerMaskBytes = new byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(layerMaskBytes, layerMask);
        return SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("render/mesh").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(
                1,
                CreateRenderSpriteAssetReferenceRecord(
                    meshAssetId,
                    new ContentSchemaIdentityAssignment("native/mesh").SchemaId)),
            new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(2, 4, 1, 1, layerMaskBytes),
            new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(3, 1, 1, 1, [0]));
    }

    private static EbinField CreateMeshFixedArrayField(
        uint tag,
        ushort elementKind,
        int stride,
        scoped ReadOnlySpan<byte> elements)
    {
        var elementCount = checked((ulong)(elements.Length / stride));
        var payload = new byte[checked(32 + elements.Length)];
        var status = EbinPrimitives.WriteFixedArray(
            elementKind,
            elementCount,
            elements,
            payload,
            out var payloadBytes,
            out var diagnostic);

        if ((status is not EbinStatus.Complete) || (payloadBytes != payload.Length))
        {
            throw new InvalidOperationException($"The Mesh scene fixture could not be encoded: {diagnostic.Category}.");
        }

        return new EbinField(tag, 23, flags: 1, elementCount, payload);
    }

    private static EbinRecord CreateMeshIndexRecord(int groupCount, bool reverseWinding = false)
    {
        var indices = new byte[checked(groupCount * 6)];

        for (var groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            var offset = groupIndex * 6;
            BinaryPrimitives.WriteUInt16LittleEndian(indices.AsSpan(offset), 0);
            BinaryPrimitives.WriteUInt16LittleEndian(indices.AsSpan(offset + 2), reverseWinding ? (ushort)2 : (ushort)1);
            BinaryPrimitives.WriteUInt16LittleEndian(indices.AsSpan(offset + 4), reverseWinding ? (ushort)1 : (ushort)2);
        }

        return new EbinRecord(
            new ContentSchemaIdentityAssignment("native/mesh-indices16").SchemaId,
            1,
            0,
            [CreateMeshFixedArrayField(1, elementKind: 2, stride: sizeof(ushort), indices)]);
    }

    private static EbinRecord CreateMeshJointRecord(Guid jointId, int parentIndex)
    {
        var localTransform = new EbinRecord(
            new ContentSchemaIdentityAssignment("scene/local-transform").SchemaId,
            1,
            0,
            [
                EbinPrimitives.Geometry(1, Vector3.Zero),
                EbinPrimitives.Geometry(2, Quaternion.Identity),
                EbinPrimitives.Geometry(3, Vector3.One),
            ]);
        return new EbinRecord(
            new ContentSchemaIdentityAssignment("native/joint").SchemaId,
            1,
            0,
            [
                EbinPrimitives.Guid(1, jointId),
                EbinPrimitives.Int32(2, parentIndex),
                EbinPrimitives.Record(3, localTransform),
                EbinPrimitives.Geometry(4, Matrix4x4.Identity),
            ]);
    }

    private static EbinRecord CreateMeshPositionOrNormalStream(
        string schemaName,
        bool unitNormals = false,
        Vector3? normalDirection = null,
        Vector3? positionOffset = null)
    {
        var elements = new byte[36];
        var offset = positionOffset ?? Vector3.Zero;
        WriteMeshVector3(elements, unitNormals ? (normalDirection ?? Vector3.UnitZ) : offset);
        WriteMeshVector3(
            elements.AsSpan(12),
            unitNormals ? (normalDirection ?? Vector3.UnitZ) : offset + Vector3.UnitX);
        WriteMeshVector3(
            elements.AsSpan(24),
            unitNormals ? (normalDirection ?? Vector3.UnitZ) : offset + Vector3.UnitY);
        return new EbinRecord(
            new ContentSchemaIdentityAssignment(schemaName).SchemaId,
            1,
            0,
            [
                EbinPrimitives.UInt32(1, 12),
                CreateMeshFixedArrayField(2, elementKind: 15, stride: 12, elements),
            ]);
    }

    private static EbinRecord CreateMeshRecord(
        uint materialAssetId,
        uint? skeletonAssetId,
        int groupCount = 2,
        bool reverseWinding = false,
        bool includeUv0 = false,
        Vector3? positionOffset = null)
    {
        var offset = positionOffset ?? Vector3.Zero;
        var positions = CreateMeshPositionOrNormalStream(
            "native/mesh-position",
            positionOffset: offset);
        var normals = CreateMeshPositionOrNormalStream(
            "native/mesh-normal",
            unitNormals: true,
            normalDirection: reverseWinding ? -Vector3.UnitZ : Vector3.UnitZ);
        var streams = new System.Collections.Generic.List<EbinRecord> { positions, normals };

        if (includeUv0)
        {
            var uvElements = new byte[24];
            BinaryPrimitives.WriteInt32LittleEndian(uvElements.AsSpan(8), BitConverter.SingleToInt32Bits(1.0f));
            BinaryPrimitives.WriteInt32LittleEndian(uvElements.AsSpan(20), BitConverter.SingleToInt32Bits(1.0f));
            streams.Add(new EbinRecord(
                new ContentSchemaIdentityAssignment("native/mesh-uv").SchemaId,
                1,
                0,
                [
                    EbinPrimitives.UInt8(1, 0),
                    EbinPrimitives.UInt32(2, 8),
                    CreateMeshFixedArrayField(3, elementKind: 14, stride: 8, uvElements),
                ]));
        }

        if (skeletonAssetId is not null)
        {
            var jointIndices = new byte[24];
            var jointStream = new EbinRecord(
                new ContentSchemaIdentityAssignment("native/mesh-joints").SchemaId,
                1,
                0,
                [
                    EbinPrimitives.UInt32(1, 8),
                    CreateMeshFixedArrayField(2, elementKind: 2, stride: sizeof(ushort), jointIndices),
                ]);
            var weights = new byte[48];

            for (var index = 0; index < 3; index++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(weights.AsSpan(index * 16), BitConverter.SingleToInt32Bits(1.0f));
            }

            var weightStream = new EbinRecord(
                new ContentSchemaIdentityAssignment("native/mesh-weights").SchemaId,
                1,
                0,
                [
                    EbinPrimitives.UInt32(1, 16),
                    CreateMeshFixedArrayField(2, elementKind: 16, stride: 16, weights),
                ]);
            streams.Add(jointStream);
            streams.Add(weightStream);
        }

        var groups = new EbinRecord[groupCount];

        for (var groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            groups[groupIndex] = new EbinRecord(
                new ContentSchemaIdentityAssignment("native/mesh-group").SchemaId,
                1,
                0,
                [
                    EbinPrimitives.UInt32(1, checked((uint)(groupIndex * 3))),
                    EbinPrimitives.UInt32(2, 3),
                    EbinPrimitives.UInt32(3, 0),
                ]);
        }

        var bounds = new EbinRecord(
            new ContentSchemaIdentityAssignment("native/bounds3").SchemaId,
            1,
            0,
            [
                EbinPrimitives.Geometry(1, offset),
                EbinPrimitives.Geometry(2, Vector3.One + offset),
                EbinPrimitives.Geometry(3, new Vector3(0.5f) + offset),
                EbinPrimitives.Binary32(4, 1.0f),
            ]);
        var fields = new System.Collections.Generic.List<EbinField> {
            EbinPrimitives.UInt32(1, 3),
            EbinPrimitives.RecordArray(2, [.. streams]),
            EbinPrimitives.Record(3, CreateMeshIndexRecord(groupCount, reverseWinding)),
            EbinPrimitives.RecordArray(4, groups),
            EbinPrimitives.RecordArray(5, [
                CreateMeshAssetReferenceRecord(
                    materialAssetId,
                    new ContentSchemaIdentityAssignment("native/material").SchemaId),
            ]),
            EbinPrimitives.Record(6, bounds),
        };

        if (skeletonAssetId is { } selectedSkeletonAssetId)
        {
            var skeletonReference = CreateMeshAssetReferenceRecord(
                selectedSkeletonAssetId,
                new ContentSchemaIdentityAssignment("native/skeleton").SchemaId);
            fields.Add(new EbinField(
                7,
                22,
                flags: 0,
                count: 1,
                EbinPrimitives.Encode(skeletonReference)));
        }

        return new EbinRecord(
            new ContentSchemaIdentityAssignment("native/mesh").SchemaId,
            1,
            0,
            fields.ToArray());
    }

    private static EbinRecord CreateMeshSkeletonRecord(Guid jointId, int jointCount = 1)
    {
        var joints = new EbinRecord[jointCount];

        for (var index = 0; index < jointCount; index++)
        {
            var selectedJointId = index == 0
                ? jointId
                : Guid.Parse($"70000000-0000-4000-8000-{index + 1:x12}");
            joints[index] = CreateMeshJointRecord(selectedJointId, index - 1);
        }

        return new EbinRecord(
            new ContentSchemaIdentityAssignment("native/skeleton").SchemaId,
            1,
            0,
            [EbinPrimitives.RecordArray(1, joints)]);
    }

    private static void WriteMeshVector3(scoped Span<byte> destination, Vector3 value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination, BitConverter.SingleToInt32Bits(value.X));
        BinaryPrimitives.WriteInt32LittleEndian(destination[4..], BitConverter.SingleToInt32Bits(value.Y));
        BinaryPrimitives.WriteInt32LittleEndian(destination[8..], BitConverter.SingleToInt32Bits(value.Z));
    }

    [Test]
    public async Task MeshExtractionCopiesGroupedBindPoseDrawsFromCommittedWorldAndRetainsAssets()
    {
        var schemas = new CompiledContentSchemaRegistry([
            .. CreateRendererRegistry().Schemas,
            .. AnimationSchemaCatalogue.Schemas,
        ]);
        var meshAssetId = 1u;
        var materialAssetId = 1000u;
        var skeletonAssetId = 1001u;
        var sceneAssetId = new AssetId(2);
        var jointId = new Guid("70000000-0000-4000-8000-000000000001");
        var sceneRecord = SceneAssetReaderTests.Fixtures.CreateScene(
            sceneAssetId,
            additionalEntityComponents: [
                CreateCameraRecord(order: 0),
                CreateMeshComponentRecord(checked((int)meshAssetId), layerMask: 1),
            ]);
        var packageBytes = Package.CreatePackage(
            new Package.Entry(meshAssetId, Package.CreateEnvelope(meshAssetId, EbinPrimitives.Encode(CreateMeshRecord(materialAssetId, skeletonAssetId)))),
            new Package.Entry(checked((uint)sceneAssetId.Value), Package.CreateEnvelope(checked((uint)sceneAssetId.Value), sceneRecord)),
            new Package.Entry(materialAssetId, Package.CreateEnvelope(materialAssetId, CreateRenderMaterialAssetRecord(
                isSprite: false,
                CreateRenderMaterialPbrParametersRecord(),
                alphaMode: 0,
                doubleSided: false,
                depthTest: true,
                depthWrite: true,
                cullMode: 1,
                outputAlpha: 0,
                requiredCapabilities: 0x191))),
            new Package.Entry(skeletonAssetId, Package.CreateEnvelope(skeletonAssetId, EbinPrimitives.Encode(CreateMeshSkeletonRecord(jointId)))));
        var packagePath = Package.WritePackage(packageBytes);
        var assetRegistrationBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = SceneSupport.RegisterAssetCodecs(assetRegistrationBuilder);
        _ = Renderer.RegisterAssetCodecs(assetRegistrationBuilder);
        _ = AnimationSupport.RegisterAssetCodecs(assetRegistrationBuilder);
        var assets = await AssetStore.CreateAsync(new AssetStoreOptions(
            packagePath,
            SHA256.HashData(packageBytes),
            assetRegistrationBuilder.Build())).ConfigureAwait(false);

        try
        {
            var worldRegistrationBuilder = new WorldRegistrationBuilder(schemas);
            _ = worldRegistrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
            _ = worldRegistrationBuilder.RegisterCpuExtractionSupport(Renderer.CreateCpuExtractionSupportRegistration());
            using var world = World.Create(new WorldOptions(worldRegistrationBuilder.Build()) {
                CpuWorkerCount = 0,
                InitialReplayLineageId = Guid.NewGuid(),
                WorldGuid = Guid.NewGuid(),
            });
            var loader = new SceneLoader(world, assets);
            var instance = ActivateScene(loader, world, sceneAssetId);

            Assert.That(instance.TryGetEntity(SceneAssetReaderTests.Fixtures.RootEntityId, out var rootEntity), Is.True);
            var cameraId = world.GetEntityId(rootEntity);
            var publication = world.Step().Publication;
            var response = Renderer.CreateHeadless().Extract(
                world,
                new RenderExtractionOptions3D(publication.Frame3, new PixelSize(640, 480), [cameraId]));

            Assert.That(response.Status, Is.EqualTo(RenderExtractionStatus.Extracted));
            using var snapshot = (RenderSnapshot3D)(response.Snapshot
                ?? throw new InvalidOperationException("The successful Mesh extraction omitted its snapshot."));
            Assert.That(snapshot.MeshDraws, Has.Length.EqualTo(2));
            Assert.That(snapshot.MeshDraws[0].MeshAssetId, Is.EqualTo(checked((int)meshAssetId)));
            var groupIndices = new[] { snapshot.MeshDraws[0].GroupIndex, snapshot.MeshDraws[1].GroupIndex };
            Array.Sort(groupIndices);
            Assert.That(groupIndices, Is.EqualTo([0, 1]));
            Assert.That(snapshot.MeshDraws[0].MaterialSlot, Is.Zero);
            Assert.That(snapshot.MeshDraws[1].MaterialSlot, Is.Zero);
            Assert.That(snapshot.MeshDraws[0].MaterialAssetId, Is.EqualTo(checked((int)materialAssetId)));
            Assert.That(snapshot.MeshDraws[1].MaterialAssetId, Is.EqualTo(checked((int)materialAssetId)));
            Assert.That(snapshot.MeshDraws[0].PaletteIndex, Is.Zero);
            Assert.That(snapshot.MeshDraws[1].PaletteIndex, Is.Zero);
            var entityId = world.GetEntityId(rootEntity);

            foreach (var meshDraw in snapshot.MeshDraws)
            {
                Assert.That(
                    meshDraw.DrawId,
                    Is.EqualTo(FoundationNameUuid.Create(entityId, $"mesh-group/{meshDraw.GroupIndex:x8}")));
                Assert.That(meshDraw.HasPreviousTransform, Is.False);
            }

            Assert.That(snapshot.MeshPalettes, Has.Length.EqualTo(1));
            Assert.That(snapshot.MeshPalettes[0].EntityId, Is.EqualTo(entityId));
            Assert.That(snapshot.MeshPalettes[0].SkeletonAssetId, Is.EqualTo(checked((int)skeletonAssetId)));
            Assert.That(snapshot.MeshPalettes[0].SkinMatrices, Is.EqualTo([Matrix4x4.Identity]));
            Assert.That(snapshot.MeshCameraOrders[0].DrawIndices, Has.Length.EqualTo(2));

            var sceneInstanceId = instance.SceneInstanceId;
            var assetPins = snapshot.AssetPins
                ?? throw new InvalidOperationException("The Mesh snapshot did not retain its Scene asset pins.");
            var unload = instance.UnloadAsync(HierarchyDestroyMode.DestroySubtree);
            world.PumpControlBoundary();
            Assert.That(unload.GetAwaiter().GetResult().Status, Is.EqualTo(SceneUnloadStatus.Unloaded));
            Assert.That(
                assetPins.GetSceneAssets(sceneInstanceId).GetMesh(checked((int)meshAssetId)).VertexCount,
                Is.EqualTo(3));
            Assert.That(
                assetPins.GetSceneAssets(sceneInstanceId).GetSkeleton(checked((int)meshAssetId))?.Joints[0].JointId,
                Is.EqualTo(jointId));
        }
        finally
        {
            await assets.DisposeAsync().ConfigureAwait(false);
            File.Delete(packagePath);
        }
    }

    [Test]
    public async Task MeshExtractionFiltersCameraLayersAndPreflightsAllGroupInstances()
    {
        const uint meshAssetId = 1;
        const uint secondMeshAssetId = 2;
        const uint meshSceneAssetId = 3;
        const uint secondMeshSceneAssetId = 4;
        const uint cameraSceneAssetId = 5;
        const uint materialAssetId = 1000;
        const int groupCount = 200;
        const int repeatedSceneCount = 655;
        var schemas = new CompiledContentSchemaRegistry([
            .. CreateRendererRegistry().Schemas,
            .. AnimationSchemaCatalogue.Schemas,
        ]);
        var meshScene = SceneAssetReaderTests.Fixtures.CreateScene(
            new AssetId(checked((int)meshSceneAssetId)),
            additionalEntityComponents: [CreateMeshComponentRecord(checked((int)meshAssetId), layerMask: 2)]);
        var secondMeshScene = SceneAssetReaderTests.Fixtures.CreateScene(
            new AssetId(checked((int)secondMeshSceneAssetId)),
            additionalEntityComponents: [CreateMeshComponentRecord(checked((int)secondMeshAssetId), layerMask: 2)]);
        var cameraScene = SceneAssetReaderTests.Fixtures.CreateScene(
            new AssetId(checked((int)cameraSceneAssetId)),
            additionalEntityComponents: [CreateCameraRecord(order: 0)]);
        var packageBytes = Package.CreatePackage(
            new Package.Entry(meshAssetId, Package.CreateEnvelope(
                meshAssetId,
                EbinPrimitives.Encode(CreateMeshRecord(materialAssetId, skeletonAssetId: null, groupCount: groupCount)))),
            new Package.Entry(secondMeshAssetId, Package.CreateEnvelope(
                secondMeshAssetId,
                EbinPrimitives.Encode(CreateMeshRecord(materialAssetId, skeletonAssetId: null, groupCount: 73)))),
            new Package.Entry(meshSceneAssetId, Package.CreateEnvelope(meshSceneAssetId, meshScene)),
            new Package.Entry(secondMeshSceneAssetId, Package.CreateEnvelope(secondMeshSceneAssetId, secondMeshScene)),
            new Package.Entry(cameraSceneAssetId, Package.CreateEnvelope(cameraSceneAssetId, cameraScene)),
            new Package.Entry(materialAssetId, Package.CreateEnvelope(materialAssetId, CreateRenderMaterialAssetRecord(
                isSprite: false,
                CreateRenderMaterialPbrParametersRecord(),
                alphaMode: 0,
                doubleSided: false,
                depthTest: true,
                depthWrite: true,
                cullMode: 1,
                outputAlpha: 0,
                requiredCapabilities: 0x191))));
        var packagePath = Package.WritePackage(packageBytes);
        var assetRegistrationBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = SceneSupport.RegisterAssetCodecs(assetRegistrationBuilder);
        _ = Renderer.RegisterAssetCodecs(assetRegistrationBuilder);
        var assets = await AssetStore.CreateAsync(new AssetStoreOptions(
            packagePath,
            SHA256.HashData(packageBytes),
            assetRegistrationBuilder.Build())).ConfigureAwait(false);

        try
        {
            var worldRegistrationBuilder = new WorldRegistrationBuilder(schemas);
            _ = worldRegistrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
            _ = worldRegistrationBuilder.RegisterCpuExtractionSupport(Renderer.CreateCpuExtractionSupportRegistration());
            using var world = World.Create(new WorldOptions(worldRegistrationBuilder.Build()) {
                CpuWorkerCount = 0,
                InitialReplayLineageId = Guid.NewGuid(),
                WorldGuid = Guid.NewGuid(),
            });
            var loader = new SceneLoader(world, assets);

            for (var index = 0; index < repeatedSceneCount; index++)
            {
                _ = ActivateScene(loader, world, new AssetId(checked((int)meshSceneAssetId)));
            }

            _ = ActivateScene(loader, world, new AssetId(checked((int)secondMeshSceneAssetId)));
            var cameraInstance = ActivateScene(loader, world, new AssetId(checked((int)cameraSceneAssetId)));

            Assert.That(cameraInstance.TryGetEntity(SceneAssetReaderTests.Fixtures.RootEntityId, out var cameraEntity), Is.True);
            var cameraId = world.GetEntityId(cameraEntity);
            var camera = new SceneComponentDefinitions.CameraComponent(
                (byte)CameraProjectionKind.Perspective,
                float.Pi / 3.0f,
                10.0f,
                0.1f,
                (byte)CameraFarMode.Finite,
                new SceneComponentDefinitions.OptionalSingle(1000.0f),
                default,
                new Vector4(0.0f, 0.0f, 1.0f, 1.0f),
                (byte)(CameraClearFlags.Color | CameraClearFlags.Depth),
                new Vector4(0.0f, 0.0f, 0.0f, 1.0f),
                layerMask: 1,
                order: 0);
            world.Set(cameraEntity, in camera);
            var hiddenPublication = world.Step().Publication;
            var renderer = Renderer.CreateHeadless();
            var hiddenResponse = renderer.Extract(
                world,
                new RenderExtractionOptions3D(
                    hiddenPublication.Frame3,
                    new PixelSize(640, 480),
                    [cameraId]));
            Assert.That(hiddenResponse.Status, Is.EqualTo(RenderExtractionStatus.Extracted));
            using (var hiddenSnapshot = (RenderSnapshot3D)(hiddenResponse.Snapshot
                       ?? throw new InvalidOperationException("The selected-layer extraction omitted its snapshot.")))
            {
                Assert.That(hiddenSnapshot.MeshDraws, Is.Empty);
                Assert.That(hiddenSnapshot.AssetPins, Is.Null);
            }

            camera = new SceneComponentDefinitions.CameraComponent(
                (byte)CameraProjectionKind.Perspective,
                float.Pi / 3.0f,
                10.0f,
                0.1f,
                (byte)CameraFarMode.Finite,
                new SceneComponentDefinitions.OptionalSingle(1000.0f),
                default,
                new Vector4(0.0f, 0.0f, 1.0f, 1.0f),
                (byte)(CameraClearFlags.Color | CameraClearFlags.Depth),
                new Vector4(0.0f, 0.0f, 0.0f, 1.0f),
                layerMask: 2,
                order: 0);
            world.Set(cameraEntity, in camera);
            var capacityPublication = world.Step().Publication;
            var capacityResponse = renderer.Extract(
                world,
                new RenderExtractionOptions3D(
                    capacityPublication.Frame3,
                    new PixelSize(640, 480),
                    [cameraId]));

            Assert.That(capacityResponse.Status, Is.EqualTo(RenderExtractionStatus.CapacityExceeded));
            Assert.That(capacityResponse.Snapshot, Is.Null);
            Assert.That(capacityResponse.LimitDiagnostic?.LimitKind, Is.EqualTo(RenderExtractionLimitKind.DrawInstances));
            Assert.That(capacityResponse.LimitDiagnostic?.Requested, Is.EqualTo(131073));
            Assert.That(capacityResponse.LimitDiagnostic?.Limit, Is.EqualTo(131072));
        }
        finally
        {
            await assets.DisposeAsync().ConfigureAwait(false);
            File.Delete(packagePath);
        }
    }

    [Test]
    public async Task MeshExtractionPreflightsStaticBindPosePaletteEntries()
    {
        const uint meshAssetId = 1;
        const uint meshSceneAssetId = 2;
        const uint cameraSceneAssetId = 3;
        const uint materialAssetId = 1000;
        const uint skeletonAssetId = 1001;
        const int meshInstanceCount = 17;
        const int jointCount = 128;
        var schemas = new CompiledContentSchemaRegistry([
            .. CreateRendererRegistry().Schemas,
            .. AnimationSchemaCatalogue.Schemas,
        ]);
        var meshScene = SceneAssetReaderTests.Fixtures.CreateScene(
            new AssetId(checked((int)meshSceneAssetId)),
            additionalEntityComponents: [CreateMeshComponentRecord(checked((int)meshAssetId), layerMask: ulong.MaxValue)]);
        var cameraScene = SceneAssetReaderTests.Fixtures.CreateScene(
            new AssetId(checked((int)cameraSceneAssetId)),
            additionalEntityComponents: [CreateCameraRecord(order: 0)]);
        var packageBytes = Package.CreatePackage(
            new Package.Entry(meshAssetId, Package.CreateEnvelope(
                meshAssetId,
                EbinPrimitives.Encode(CreateMeshRecord(materialAssetId, skeletonAssetId)))),
            new Package.Entry(meshSceneAssetId, Package.CreateEnvelope(meshSceneAssetId, meshScene)),
            new Package.Entry(cameraSceneAssetId, Package.CreateEnvelope(cameraSceneAssetId, cameraScene)),
            new Package.Entry(materialAssetId, Package.CreateEnvelope(materialAssetId, CreateRenderMaterialAssetRecord(
                isSprite: false,
                CreateRenderMaterialPbrParametersRecord(),
                alphaMode: 0,
                doubleSided: false,
                depthTest: true,
                depthWrite: true,
                cullMode: 1,
                outputAlpha: 0,
                requiredCapabilities: 0x191))),
            new Package.Entry(skeletonAssetId, Package.CreateEnvelope(
                skeletonAssetId,
                EbinPrimitives.Encode(CreateMeshSkeletonRecord(
                    new Guid("70000000-0000-4000-8000-000000000001"),
                    jointCount)))));
        var packagePath = Package.WritePackage(packageBytes);
        var assetRegistrationBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = SceneSupport.RegisterAssetCodecs(assetRegistrationBuilder);
        _ = Renderer.RegisterAssetCodecs(assetRegistrationBuilder);
        _ = AnimationSupport.RegisterAssetCodecs(assetRegistrationBuilder);
        var assets = await AssetStore.CreateAsync(new AssetStoreOptions(
            packagePath,
            SHA256.HashData(packageBytes),
            assetRegistrationBuilder.Build())).ConfigureAwait(false);

        try
        {
            var worldRegistrationBuilder = new WorldRegistrationBuilder(schemas);
            _ = worldRegistrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
            _ = worldRegistrationBuilder.RegisterCpuExtractionSupport(Renderer.CreateCpuExtractionSupportRegistration());
            using var world = World.Create(new WorldOptions(worldRegistrationBuilder.Build()) {
                CpuWorkerCount = 0,
                InitialReplayLineageId = Guid.NewGuid(),
                WorldGuid = Guid.NewGuid(),
            });
            var loader = new SceneLoader(world, assets);

            for (var index = 0; index < meshInstanceCount; index++)
            {
                _ = ActivateScene(loader, world, new AssetId(checked((int)meshSceneAssetId)));
            }

            var cameraInstance = ActivateScene(loader, world, new AssetId(checked((int)cameraSceneAssetId)));
            Assert.That(cameraInstance.TryGetEntity(SceneAssetReaderTests.Fixtures.RootEntityId, out var cameraEntity), Is.True);
            var cameraId = world.GetEntityId(cameraEntity);
            var publication = world.Step().Publication;
            var response = Renderer.CreateHeadless().Extract(
                world,
                new RenderExtractionOptions3D(
                    publication.Frame3,
                    new PixelSize(640, 480),
                    [cameraId]));

            Assert.That(response.Status, Is.EqualTo(RenderExtractionStatus.CapacityExceeded));
            Assert.That(response.Snapshot, Is.Null);
            Assert.That(response.LimitDiagnostic?.LimitKind, Is.EqualTo(RenderExtractionLimitKind.BonePaletteEntries));
            Assert.That(response.LimitDiagnostic?.Requested, Is.EqualTo(meshInstanceCount * jointCount));
            Assert.That(response.LimitDiagnostic?.Limit, Is.EqualTo(2048));
        }
        finally
        {
            await assets.DisposeAsync().ConfigureAwait(false);
            File.Delete(packagePath);
        }
    }
}
