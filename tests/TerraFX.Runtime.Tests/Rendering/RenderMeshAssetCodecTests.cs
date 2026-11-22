// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Animation;
using TerraFX.Assets;
using TerraFX.Graphics;
using TerraFX.Numerics;
using TerraFX.Rendering;
using TerraFX.Runtime.Tests.Scenes;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Rendering;

[TestFixture]
internal sealed class RenderMeshAssetCodecTests
{
    private const uint MaterialAssetId = 1000;
    private const uint SkeletonAssetId = 1001;

    private static EbinRecord CreateAssetReference(uint assetId, Guid schemaId)
        => new(
            new ContentSchemaIdentityAssignment("native/asset-reference").SchemaId,
            1,
            0,
            [
                EbinPrimitives.UInt32(1, assetId),
                EbinPrimitives.Guid(2, schemaId),
                EbinPrimitives.UInt32(3, 1),
            ]);

    private static EbinRecord CreateBounds(Vector3 minimum, Vector3 maximum, Vector3 sphereCenter, float sphereRadius)
        => new(
            new ContentSchemaIdentityAssignment("native/bounds3").SchemaId,
            1,
            0,
            [
                EbinPrimitives.Geometry(1, minimum),
                EbinPrimitives.Geometry(2, maximum),
                EbinPrimitives.Geometry(3, sphereCenter),
                EbinPrimitives.Binary32(4, sphereRadius),
            ]);

    private static EbinField CreateFixedArrayField(
        uint tag,
        ushort elementKind,
        int stride,
        scoped ReadOnlySpan<byte> elements)
    {
        if ((elements.Length % stride) != 0)
        {
            throw new ArgumentException("The fixed-array bytes must be stride-aligned.", nameof(elements));
        }

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
            throw new InvalidOperationException($"The fixed-array fixture could not be encoded: {diagnostic.Category}.");
        }

        return new EbinField(tag, 23, flags: 1, elementCount, payload);
    }

    private static EbinRecord CreateIndex16Record(params ushort[] indices)
    {
        var elements = new byte[checked(indices.Length * sizeof(ushort))];

        for (var index = 0; index < indices.Length; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(elements.AsSpan(index * sizeof(ushort)), indices[index]);
        }

        return new EbinRecord(
            new ContentSchemaIdentityAssignment("native/mesh-indices16").SchemaId,
            1,
            0,
            [CreateFixedArrayField(1, elementKind: 2, stride: sizeof(ushort), elements)]);
    }

    private static EbinRecord CreateJointRecord(Guid jointId)
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
                EbinPrimitives.Int32(2, -1),
                EbinPrimitives.Record(3, localTransform),
                EbinPrimitives.Geometry(4, Matrix4x4.Identity),
            ]);
    }

    private static EbinRecord CreateJointStream(ushort[] jointIndices)
    {
        var elements = new byte[checked(jointIndices.Length * sizeof(ushort))];

        for (var index = 0; index < jointIndices.Length; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(elements.AsSpan(index * sizeof(ushort)), jointIndices[index]);
        }

        return CreateStream(
            "native/mesh-joints",
            EbinPrimitives.UInt32(1, 8),
            CreateFixedArrayField(2, elementKind: 2, stride: sizeof(ushort), elements));
    }

    private static EbinRecord CreateMaterialRecord(uint? textureAssetId = null)
    {
        var parameterFields = new List<EbinField> {
            EbinPrimitives.Geometry(1, Vector4.One),
        };

        if (textureAssetId is { } selectedTextureAssetId)
        {
            parameterFields.Add(new EbinField(
                2,
                22,
                flags: 0,
                count: 1,
                EbinPrimitives.Encode(CreateTextureBinding(selectedTextureAssetId))));
        }

        var parameters = new EbinRecord(
            new ContentSchemaIdentityAssignment("native/material-sprite-parameters").SchemaId,
            1,
            0,
            parameterFields.ToArray());
        return new EbinRecord(
            new ContentSchemaIdentityAssignment("native/material").SchemaId,
            1,
            0,
            [
                EbinPrimitives.Guid(1, new ContentSchemaIdentityAssignment("material/profile/sprite-premul/v1").SchemaId),
                EbinPrimitives.Guid(2, new ContentSchemaIdentityAssignment("material/shader/sprite-unlit/v1").SchemaId),
                EbinPrimitives.Guid(3, new ContentSchemaIdentityAssignment("material/pass/sprite/v1").SchemaId),
                EbinPrimitives.Guid(4, new ContentSchemaIdentityAssignment("material/layout/sprite/v1").SchemaId),
                EbinPrimitives.Record(5, parameters),
                EbinPrimitives.UInt8(6, 2),
                EbinPrimitives.Binary32(7, 0.5f),
                EbinPrimitives.Boolean(8, value: false),
                EbinPrimitives.Boolean(9, value: false),
                EbinPrimitives.Boolean(10, value: false),
                EbinPrimitives.UInt8(11, 0),
                EbinPrimitives.UInt8(12, 1),
                EbinPrimitives.UInt64(13, 9),
            ]);
    }

    private static EbinRecord CreateMeshGroup(uint firstIndex, uint indexCount, uint materialSlot)
        => new(
            new ContentSchemaIdentityAssignment("native/mesh-group").SchemaId,
            1,
            0,
            [
                EbinPrimitives.UInt32(1, firstIndex),
                EbinPrimitives.UInt32(2, indexCount),
                EbinPrimitives.UInt32(3, materialSlot),
            ]);

    private static EbinRecord CreateMeshRecord(
        Vector3[] positions,
        EbinRecord[] streams,
        ushort[] indices,
        EbinRecord[] groups,
        RenderMeshBounds bounds,
        bool includeSkeleton = false,
        EbinRecord[]? materialSlots = null)
    {
        var fields = new List<EbinField>
        {
            EbinPrimitives.UInt32(1, checked((uint)positions.Length)),
            EbinPrimitives.RecordArray(2, streams),
            EbinPrimitives.Record(3, CreateIndex16Record(indices)),
            EbinPrimitives.RecordArray(4, groups),
            EbinPrimitives.RecordArray(
                5,
                materialSlots ?? [CreateAssetReference(MaterialAssetId, new ContentSchemaIdentityAssignment("native/material").SchemaId)]),
            EbinPrimitives.Record(6, CreateBounds(bounds.Minimum, bounds.Maximum, bounds.SphereCenter, bounds.SphereRadius)),
        };

        if (includeSkeleton)
        {
            fields.Add(new EbinField(
                7,
                22,
                flags: 0,
                count: 1,
                EbinPrimitives.Encode(CreateAssetReference(
                    SkeletonAssetId,
                    new ContentSchemaIdentityAssignment("native/skeleton").SchemaId))));
        }

        return new EbinRecord(
            new ContentSchemaIdentityAssignment("native/mesh").SchemaId,
            1,
            0,
            fields.ToArray());
    }

    private static EbinRecord CreateNormalStream(Vector3[] normals)
        => CreateVector3Stream("native/mesh-normal", normals);

    private static byte[] CreatePackage(
        scoped ReadOnlySpan<EbinRecord> meshRecords,
        EbinRecord? skeletonRecord = null,
        EbinRecord? materialRecord = null)
    {
        var entries = new List<SceneAssetStoreTests.Package.Entry>(meshRecords.Length + 2 + (skeletonRecord is null ? 0 : 1));

        for (var index = 0; index < meshRecords.Length; index++)
        {
            var assetId = checked((uint)(index + 1));
            entries.Add(new SceneAssetStoreTests.Package.Entry(
                assetId,
                SceneAssetStoreTests.Package.CreateEnvelope(assetId, EbinPrimitives.Encode(meshRecords[index]))));
        }

        entries.Add(new SceneAssetStoreTests.Package.Entry(
            MaterialAssetId,
            SceneAssetStoreTests.Package.CreateEnvelope(
                MaterialAssetId,
                EbinPrimitives.Encode(materialRecord ?? CreateMaterialRecord()))));

        if (skeletonRecord is { } selectedSkeletonRecord)
        {
            entries.Add(new SceneAssetStoreTests.Package.Entry(
                SkeletonAssetId,
                SceneAssetStoreTests.Package.CreateEnvelope(SkeletonAssetId, EbinPrimitives.Encode(selectedSkeletonRecord))));
        }

        return SceneAssetStoreTests.Package.CreatePackage([.. entries]);
    }

    private static EbinRecord CreatePbrMaterialRecord()
    {
        var parameters = new EbinRecord(
            new ContentSchemaIdentityAssignment("native/material-pbr-parameters").SchemaId,
            1,
            0,
            [
                EbinPrimitives.Geometry(1, Vector4.One),
                EbinPrimitives.Binary32(2, 0.0f),
                EbinPrimitives.Binary32(3, 1.0f),
                EbinPrimitives.Binary32(4, 1.0f),
                EbinPrimitives.Geometry(5, Vector3.Zero),
            ]);
        return new EbinRecord(
            new ContentSchemaIdentityAssignment("native/material").SchemaId,
            1,
            0,
            [
                EbinPrimitives.Guid(1, new ContentSchemaIdentityAssignment("material/profile/pbr-metallic-roughness/v1").SchemaId),
                EbinPrimitives.Guid(2, new ContentSchemaIdentityAssignment("material/shader/pbr-metallic-roughness/v1").SchemaId),
                EbinPrimitives.Guid(3, new ContentSchemaIdentityAssignment("material/pass/pbr/v1").SchemaId),
                EbinPrimitives.Guid(4, new ContentSchemaIdentityAssignment("material/layout/pbr/v1").SchemaId),
                EbinPrimitives.Record(5, parameters),
                EbinPrimitives.UInt8(6, 0),
                EbinPrimitives.Binary32(7, 0.5f),
                EbinPrimitives.Boolean(8, value: false),
                EbinPrimitives.Boolean(9, value: false),
                EbinPrimitives.Boolean(10, value: false),
                EbinPrimitives.UInt8(11, 1),
                EbinPrimitives.UInt8(12, 0),
                EbinPrimitives.UInt64(13, 0x191),
            ]);
    }

    private static EbinRecord CreatePositionStream(Vector3[] positions)
        => CreateVector3Stream("native/mesh-position", positions);

    private static CompiledContentSchemaRegistry CreateRegistry()
        => new([
            .. CoreContentSchemaCatalogue.PrimitiveSchemas,
            .. CoreContentSchemaCatalogue.FoundationSchemas,
            .. NativeAssetSchemaCatalogue.Schemas,
            .. SceneSchemaCatalogue.Schemas,
            .. RenderingSchemaCatalogue.Schemas,
            .. AnimationSchemaCatalogue.Schemas,
        ]);

    private static EbinRecord CreateSkeletonRecord(Guid jointId)
        => new(
            new ContentSchemaIdentityAssignment("native/skeleton").SchemaId,
            1,
            0,
            [EbinPrimitives.RecordArray(1, [CreateJointRecord(jointId)])]);

    private static async Task<(AssetStore Store, string PackagePath)> CreateStoreAsync(
        byte[] packageBytes,
        AssetStoreRegistration registration)
    {
        var packagePath = SceneAssetStoreTests.Package.WritePackage(packageBytes);

        try
        {
            var store = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(packageBytes), registration)).ConfigureAwait(false);
            return (store, packagePath);
        }
        catch
        {
            File.Delete(packagePath);
            throw;
        }
    }

    private static EbinRecord CreateStream(string schemaName, params EbinField[] fields)
        => new(new ContentSchemaIdentityAssignment(schemaName).SchemaId, 1, 0, fields);

    private static EbinRecord CreateTextureBinding(uint textureAssetId)
        => new(
            new ContentSchemaIdentityAssignment("native/texture-binding").SchemaId,
            1,
            0,
            [
                EbinPrimitives.Record(1, CreateAssetReference(
                    textureAssetId,
                    new ContentSchemaIdentityAssignment("native/texture").SchemaId)),
                EbinPrimitives.Record(2, new EbinRecord(
                    new ContentSchemaIdentityAssignment("native/sampler").SchemaId,
                    1,
                    0,
                    [
                        EbinPrimitives.UInt8(1, 1),
                        EbinPrimitives.UInt8(2, 1),
                        EbinPrimitives.UInt8(3, 0),
                        EbinPrimitives.UInt8(4, 0),
                        EbinPrimitives.UInt8(5, 0),
                    ])),
                EbinPrimitives.UInt8(3, 0),
            ]);

    private static EbinRecord CreateUvStream(byte set, Vector2[] values)
    {
        var elements = new byte[checked(values.Length * 8)];

        for (var index = 0; index < values.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(elements.AsSpan(index * 8), BitConverter.SingleToInt32Bits(values[index].X));
            BinaryPrimitives.WriteInt32LittleEndian(elements.AsSpan((index * 8) + 4), BitConverter.SingleToInt32Bits(values[index].Y));
        }

        return CreateStream(
            "native/mesh-uv",
            EbinPrimitives.UInt8(1, set),
            EbinPrimitives.UInt32(2, 8),
            CreateFixedArrayField(3, elementKind: 14, stride: 8, elements));
    }

    private static EbinRecord CreateVector3Stream(string schemaName, Vector3[] values)
    {
        var elements = new byte[checked(values.Length * 12)];

        for (var index = 0; index < values.Length; index++)
        {
            WriteVector3(elements.AsSpan(index * 12), values[index]);
        }

        return new EbinRecord(
            new ContentSchemaIdentityAssignment(schemaName).SchemaId,
            1,
            0,
            [
                EbinPrimitives.UInt32(1, 12),
                CreateFixedArrayField(2, elementKind: 15, stride: 12, elements),
            ]);
    }

    private static EbinRecord CreateVector4Stream(string schemaName, ushort elementKind, uint stride, Vector4[] values)
    {
        var elements = new byte[checked(values.Length * (int)stride)];

        for (var index = 0; index < values.Length; index++)
        {
            var offset = checked(index * (int)stride);
            BinaryPrimitives.WriteInt32LittleEndian(elements.AsSpan(offset), BitConverter.SingleToInt32Bits(values[index].X));
            BinaryPrimitives.WriteInt32LittleEndian(elements.AsSpan(offset + 4), BitConverter.SingleToInt32Bits(values[index].Y));
            BinaryPrimitives.WriteInt32LittleEndian(elements.AsSpan(offset + 8), BitConverter.SingleToInt32Bits(values[index].Z));
            BinaryPrimitives.WriteInt32LittleEndian(elements.AsSpan(offset + 12), BitConverter.SingleToInt32Bits(values[index].W));
        }

        return CreateStream(
            schemaName,
            EbinPrimitives.UInt32(1, stride),
            CreateFixedArrayField(2, elementKind, checked((int)stride), elements));
    }

    private static EbinRecord CreateWeightStream(Vector4[] values)
        => CreateVector4Stream("native/mesh-weights", elementKind: 16, stride: 16, values);

    private static void WriteVector3(scoped Span<byte> destination, Vector3 value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination, BitConverter.SingleToInt32Bits(value.X));
        BinaryPrimitives.WriteInt32LittleEndian(destination[4..], BitConverter.SingleToInt32Bits(value.Y));
        BinaryPrimitives.WriteInt32LittleEndian(destination[8..], BitConverter.SingleToInt32Bits(value.Z));
    }

    [Test]
    public void BoundedStaticMeshAssetDecoderAcceptsTheSealedStaticMeshPair()
    {
        var registration = BoundedStaticMeshAssetRegistrationFactory.Create();
        var positions = new[] {
            Vector3.Zero,
            Vector3.UnitX,
            Vector3.UnitY,
        };
        var meshRecord = CreateMeshRecord(
            positions,
            [CreatePositionStream(positions), CreateNormalStream([Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ])],
            [0, 1, 2],
            [CreateMeshGroup(0, 3, 0)],
            new RenderMeshBounds(
                Vector3.Zero,
                new Vector3(1.0f, 1.0f, 0.0f),
                new Vector3(0.5f, 0.5f, 0.0f),
                1.0f));
        var meshEnvelope = SceneAssetStoreTests.Package.CreateEnvelope(
            1,
            EbinPrimitives.Encode(meshRecord));
        var materialEnvelope = SceneAssetStoreTests.Package.CreateEnvelope(
            MaterialAssetId,
            EbinPrimitives.Encode(CreatePbrMaterialRecord()));
        var referenceWorkspace = new AssetReference[257];
        var referenceIndexWorkspace = new int[257];
        var uniqueReferences = new AssetReference[257];
        var failure = BoundedStaticMeshAssetDecoder.Decode(
            registration,
            new AssetId(1),
            new AssetId(checked((int)MaterialAssetId)),
            SpatialProfileId.Default,
            SpatialSettings.Default,
            EbinReadLimits.Foundation,
            meshEnvelope,
            materialEnvelope,
            referenceWorkspace,
            referenceIndexWorkspace,
            uniqueReferences,
            out var mesh,
            out var material,
            out var referenceCount);

        Assert.Multiple(() => {
            Assert.That(failure.Kind, Is.EqualTo(AssetBoundedFailureKind.None));
            Assert.That(mesh, Is.Not.Null);
            Assert.That(mesh?.Groups.Length, Is.EqualTo(1));
            Assert.That(mesh?.MaterialSlots[0], Is.EqualTo(new AssetId(checked((int)MaterialAssetId))));
            Assert.That(mesh?.SkeletonAssetId, Is.Null);
            Assert.That(material, Is.Not.Null);
            Assert.That(material?.IsSprite, Is.False);
            Assert.That(material?.Parameters.BaseTexture, Is.Null);
            Assert.That(referenceCount, Is.EqualTo(1));
            Assert.That(uniqueReferences[0], Is.EqualTo(new AssetReference(
                new AssetId(checked((int)MaterialAssetId)),
                new ContentSchemaIdentityAssignment("native/material").SchemaId,
                1)));
        });
    }

    [Test]
    public void BoundedStaticMeshAssetDecoderRejectsMeshesOutsideTheStaticShape()
    {
        var registration = BoundedStaticMeshAssetRegistrationFactory.Create();
        var positions = new[] {
            Vector3.Zero,
            Vector3.UnitX,
            new Vector3(1.0f, 1.0f, 0.0f),
            Vector3.UnitY,
        };
        var normals = new[] { Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ };
        var meshRecord = CreateMeshRecord(
            positions,
            [CreatePositionStream(positions), CreateNormalStream(normals)],
            [0, 1, 2, 0, 2, 3],
            [CreateMeshGroup(0, 3, 0), CreateMeshGroup(3, 3, 0)],
            new RenderMeshBounds(
                Vector3.Zero,
                new Vector3(1.0f, 1.0f, 0.0f),
                new Vector3(0.5f, 0.5f, 0.0f),
                1.0f));
        var meshEnvelope = SceneAssetStoreTests.Package.CreateEnvelope(
            1,
            EbinPrimitives.Encode(meshRecord));
        var materialEnvelope = SceneAssetStoreTests.Package.CreateEnvelope(
            MaterialAssetId,
            EbinPrimitives.Encode(CreatePbrMaterialRecord()));
        var failure = BoundedStaticMeshAssetDecoder.Decode(
            registration,
            new AssetId(1),
            new AssetId(checked((int)MaterialAssetId)),
            SpatialProfileId.Default,
            SpatialSettings.Default,
            EbinReadLimits.Foundation,
            meshEnvelope,
            materialEnvelope,
            new AssetReference[257],
            new int[257],
            new AssetReference[257],
            out var mesh,
            out var material,
            out var referenceCount);

        Assert.Multiple(() => {
            Assert.That(failure.Stage, Is.EqualTo(AssetBoundedFailureStage.Decode));
            Assert.That(failure.Kind, Is.EqualTo(AssetBoundedFailureKind.TypedRecord));
            Assert.That(mesh, Is.Null);
            Assert.That(material, Is.Null);
            Assert.That(referenceCount, Is.Zero);
        });
    }

    [Test]
    public void BoundedStaticMeshAssetDecoderRejectsUnexpectedMaterialReferences()
    {
        var registration = BoundedStaticMeshAssetRegistrationFactory.Create();
        var positions = new[] {
            Vector3.Zero,
            Vector3.UnitX,
            Vector3.UnitY,
        };
        var meshRecord = CreateMeshRecord(
            positions,
            [CreatePositionStream(positions), CreateNormalStream([Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ])],
            [0, 1, 2],
            [CreateMeshGroup(0, 3, 0)],
            new RenderMeshBounds(
                Vector3.Zero,
                new Vector3(1.0f, 1.0f, 0.0f),
                new Vector3(0.5f, 0.5f, 0.0f),
                1.0f));
        var meshEnvelope = SceneAssetStoreTests.Package.CreateEnvelope(
            1,
            EbinPrimitives.Encode(meshRecord));
        var materialEnvelope = SceneAssetStoreTests.Package.CreateEnvelope(
            MaterialAssetId + 1,
            EbinPrimitives.Encode(CreatePbrMaterialRecord()));
        var failure = BoundedStaticMeshAssetDecoder.Decode(
            registration,
            new AssetId(1),
            new AssetId(checked((int)MaterialAssetId + 1)),
            SpatialProfileId.Default,
            SpatialSettings.Default,
            EbinReadLimits.Foundation,
            meshEnvelope,
            materialEnvelope,
            new AssetReference[257],
            new int[257],
            new AssetReference[257],
            out var mesh,
            out var material,
            out var referenceCount);

        Assert.Multiple(() => {
            Assert.That(failure.Stage, Is.EqualTo(AssetBoundedFailureStage.Reference));
            Assert.That(failure.Kind, Is.EqualTo(AssetBoundedFailureKind.Reference));
            Assert.That(mesh, Is.Null);
            Assert.That(material, Is.Null);
            Assert.That(referenceCount, Is.Zero);
        });
    }

    [Test]
    public async Task MeshAssetBinderChecksJointIndicesAgainstTheSelectedPackageSkeleton()
    {
        var schemas = CreateRegistry();
        var builder = new AssetStoreRegistrationBuilder(schemas);
        _ = Renderer.RegisterAssetCodecs(builder);
        _ = AnimationSupport.RegisterAssetCodecs(builder);
        var positions = new[] {
            Vector3.Zero,
            Vector3.UnitX,
            Vector3.UnitY,
        };
        var unitWeights = CreateWeightStream([
            new Vector4(1.0f, 0.0f, 0.0f, 0.0f),
            new Vector4(1.0f, 0.0f, 0.0f, 0.0f),
            new Vector4(1.0f, 0.0f, 0.0f, 0.0f),
        ]);
        var skeletonJointId = new Guid("70000000-0000-4000-8000-000000000001");
        var skeletonRecord = CreateSkeletonRecord(skeletonJointId);
        var validMesh = CreateMeshRecord(
            positions,
            [
                CreatePositionStream(positions),
                CreateJointStream(new ushort[12]),
                unitWeights,
            ],
            [0, 1, 2],
            [CreateMeshGroup(0, 3, 0)],
            new RenderMeshBounds(Vector3.Zero, new Vector3(1.0f, 1.0f, 0.0f), new Vector3(0.5f, 0.5f, 0.0f), 1.0f),
            includeSkeleton: true);
        var invalidMesh = CreateMeshRecord(
            positions,
            [
                CreatePositionStream(positions),
                CreateJointStream([1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]),
                unitWeights,
            ],
            [0, 1, 2],
            [CreateMeshGroup(0, 3, 0)],
            new RenderMeshBounds(Vector3.Zero, new Vector3(1.0f, 1.0f, 0.0f), new Vector3(0.5f, 0.5f, 0.0f), 1.0f),
            includeSkeleton: true);
        var (store, packagePath) = await CreateStoreAsync(
            CreatePackage([validMesh, invalidMesh], skeletonRecord),
            builder.Build()).ConfigureAwait(false);

        try
        {
            using var validMeshLease = await store.LoadAsync<RenderMeshAsset>(new AssetId(1)).ConfigureAwait(false);
            using var skeletonLease = await RenderMeshAssetBinding.LoadSkeletonAsync(validMeshLease).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The selected skinned Mesh should retain its Skeleton binding.");

            Assert.That(skeletonLease.Value.Joints, Has.Length.EqualTo(1));
            Assert.That(skeletonLease.Value.Joints[0].JointId, Is.EqualTo(skeletonJointId));

            using var invalidMeshLease = await store.LoadAsync<RenderMeshAsset>(new AssetId(2)).ConfigureAwait(false);
            _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await RenderMeshAssetBinding.LoadSkeletonAsync(invalidMeshLease).ConfigureAwait(false));
        }
        finally
        {
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(packagePath);
        }
    }

    [Test]
    public async Task MeshAssetBinderChecksMaterialUvRequirementsAgainstTheSelectedMesh()
    {
        var schemas = CreateRegistry();
        var builder = new AssetStoreRegistrationBuilder(schemas);
        _ = Renderer.RegisterAssetCodecs(builder);
        var positions = new[] {
            Vector3.Zero,
            Vector3.UnitX,
            Vector3.UnitY,
        };
        var meshRecord = CreateMeshRecord(
            positions,
            [CreatePositionStream(positions)],
            [0, 1, 2],
            [CreateMeshGroup(0, 3, 0)],
            new RenderMeshBounds(Vector3.Zero, new Vector3(1.0f, 1.0f, 0.0f), new Vector3(0.5f, 0.5f, 0.0f), 1.0f));
        var (store, packagePath) = await CreateStoreAsync(CreatePackage([meshRecord]), builder.Build()).ConfigureAwait(false);

        try
        {
            using var meshLease = await store.LoadAsync<RenderMeshAsset>(new AssetId(1)).ConfigureAwait(false);
            _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await RenderMeshAssetBinding.ValidatePackageBindingsAsync(meshLease).ConfigureAwait(false));
        }
        finally
        {
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(packagePath);
        }
    }

    [Test]
    public async Task MeshAssetBinderChecksPbrNormalRequirementsAgainstTheSelectedMesh()
    {
        var schemas = CreateRegistry();
        var builder = new AssetStoreRegistrationBuilder(schemas);
        _ = Renderer.RegisterAssetCodecs(builder);
        var positions = new[] {
            Vector3.Zero,
            Vector3.UnitX,
            Vector3.UnitY,
        };
        var normals = new[] { Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ };
        var validMesh = CreateMeshRecord(
            positions,
            [CreatePositionStream(positions), CreateNormalStream(normals)],
            [0, 1, 2],
            [CreateMeshGroup(0, 3, 0)],
            new RenderMeshBounds(Vector3.Zero, new Vector3(1.0f, 1.0f, 0.0f), new Vector3(0.5f, 0.5f, 0.0f), 1.0f));
        var invalidMesh = CreateMeshRecord(
            positions,
            [CreatePositionStream(positions)],
            [0, 1, 2],
            [CreateMeshGroup(0, 3, 0)],
            new RenderMeshBounds(Vector3.Zero, new Vector3(1.0f, 1.0f, 0.0f), new Vector3(0.5f, 0.5f, 0.0f), 1.0f));
        var (store, packagePath) = await CreateStoreAsync(
            CreatePackage([validMesh, invalidMesh], materialRecord: CreatePbrMaterialRecord()),
            builder.Build()).ConfigureAwait(false);

        try
        {
            using var validMeshLease = await store.LoadAsync<RenderMeshAsset>(new AssetId(1)).ConfigureAwait(false);
            await RenderMeshAssetBinding.ValidatePackageBindingsAsync(validMeshLease).ConfigureAwait(false);

            using var invalidMeshLease = await store.LoadAsync<RenderMeshAsset>(new AssetId(2)).ConfigureAwait(false);
            _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await RenderMeshAssetBinding.ValidatePackageBindingsAsync(invalidMeshLease).ConfigureAwait(false));
        }
        finally
        {
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(packagePath);
        }
    }

    [Test]
    public async Task MeshAssetBinderResolvesMaterialTexturesFromTheSelectedPackage()
    {
        var schemas = CreateRegistry();
        var builder = new AssetStoreRegistrationBuilder(schemas);
        _ = Renderer.RegisterAssetCodecs(builder);
        var positions = new[] {
            Vector3.Zero,
            Vector3.UnitX,
            Vector3.UnitY,
        };
        var meshRecord = CreateMeshRecord(
            positions,
            [
                CreatePositionStream(positions),
                CreateUvStream(0, [Vector2.Zero, Vector2.UnitX, Vector2.UnitY]),
            ],
            [0, 1, 2],
            [CreateMeshGroup(0, 3, 0)],
            new RenderMeshBounds(Vector3.Zero, new Vector3(1.0f, 1.0f, 0.0f), new Vector3(0.5f, 0.5f, 0.0f), 1.0f));
        var (store, packagePath) = await CreateStoreAsync(
            CreatePackage([meshRecord], materialRecord: CreateMaterialRecord(textureAssetId: 1002)),
            builder.Build()).ConfigureAwait(false);

        try
        {
            using var meshLease = await store.LoadAsync<RenderMeshAsset>(new AssetId(1)).ConfigureAwait(false);
            _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await RenderMeshAssetBinding.ValidatePackageBindingsAsync(meshLease).ConfigureAwait(false));
        }
        finally
        {
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(packagePath);
        }
    }

    [Test]
    public async Task MeshAssetCodecAccepts256MaterialSlotsAndRejects257()
    {
        var schemas = CreateRegistry();
        var builder = new AssetStoreRegistrationBuilder(schemas);
        _ = Renderer.RegisterAssetCodecs(builder);
        var positions = new[] {
            Vector3.Zero,
            Vector3.UnitX,
            Vector3.UnitY,
        };
        var materialReference = CreateAssetReference(
            MaterialAssetId,
            new ContentSchemaIdentityAssignment("native/material").SchemaId);
        var maximumSlots = new EbinRecord[256];
        Array.Fill(maximumSlots, materialReference);
        var excessSlots = new EbinRecord[257];
        Array.Fill(excessSlots, materialReference);
        var validMesh = CreateMeshRecord(
            positions,
            [CreatePositionStream(positions)],
            [0, 1, 2],
            [CreateMeshGroup(0, 3, 0)],
            new RenderMeshBounds(Vector3.Zero, new Vector3(1.0f, 1.0f, 0.0f), new Vector3(0.5f, 0.5f, 0.0f), 1.0f),
            materialSlots: maximumSlots);
        var invalidMesh = CreateMeshRecord(
            positions,
            [CreatePositionStream(positions)],
            [0, 1, 2],
            [CreateMeshGroup(0, 3, 0)],
            new RenderMeshBounds(Vector3.Zero, new Vector3(1.0f, 1.0f, 0.0f), new Vector3(0.5f, 0.5f, 0.0f), 1.0f),
            materialSlots: excessSlots);
        var (store, packagePath) = await CreateStoreAsync(
            CreatePackage([validMesh, invalidMesh]),
            builder.Build()).ConfigureAwait(false);

        try
        {
            using var meshLease = await store.LoadAsync<RenderMeshAsset>(new AssetId(1)).ConfigureAwait(false);
            Assert.That(meshLease.Value.MaterialSlots, Has.Length.EqualTo(256));
            _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.LoadAsync<RenderMeshAsset>(new AssetId(2)).ConfigureAwait(false));
        }
        finally
        {
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(packagePath);
        }
    }

    [Test]
    public async Task MeshAssetCodecLoadsTypedStreamsAndContiguousGroupsFromPackage()
    {
        var schemas = CreateRegistry();
        var builder = new AssetStoreRegistrationBuilder(schemas);
        _ = Renderer.RegisterAssetCodecs(builder);
        var positions = new[] {
            Vector3.Zero,
            Vector3.UnitX,
            Vector3.UnitY,
        };
        var meshRecord = CreateMeshRecord(
            positions,
            [
                CreatePositionStream(positions),
                CreateNormalStream([Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ]),
                CreateUvStream(0, [Vector2.Zero, Vector2.UnitX, Vector2.UnitY]),
                CreateVector4Stream("native/mesh-color", 16, 16, [Vector4.One, Vector4.One, Vector4.One]),
            ],
            [0, 1, 2],
            [CreateMeshGroup(0, 3, 0)],
            new RenderMeshBounds(Vector3.Zero, new Vector3(1.0f, 1.0f, 0.0f), new Vector3(0.5f, 0.5f, 0.0f), 1.0f));
        var (store, packagePath) = await CreateStoreAsync(CreatePackage([meshRecord]), builder.Build()).ConfigureAwait(false);

        try
        {
            using var meshLease = await store.LoadAsync<RenderMeshAsset>(new AssetId(1)).ConfigureAwait(false);
            await RenderMeshAssetBinding.ValidatePackageBindingsAsync(meshLease).ConfigureAwait(false);
            var mesh = meshLease.Value;

            Assert.That(mesh.VertexCount, Is.EqualTo(3));
            Assert.That(mesh.Positions, Is.EqualTo(positions));
            Assert.That(mesh.Normals, Has.Length.EqualTo(3));
            Assert.That(mesh.Normals[0], Is.EqualTo(Vector3.UnitZ));
            Assert.That(mesh.Uv0, Is.EqualTo([Vector2.Zero, Vector2.UnitX, Vector2.UnitY]));
            Assert.That(mesh.Uv1.IsDefault, Is.True);
            Assert.That(mesh.Colors, Has.Length.EqualTo(3));
            Assert.That(mesh.IndexFormat, Is.EqualTo(RenderMeshIndexFormat.UInt16));
            Assert.That(mesh.Indices, Is.EqualTo([0u, 1u, 2u]));
            Assert.That(mesh.Groups, Is.EqualTo([new RenderMeshGroup(0, 3, 0)]));
            Assert.That(mesh.MaterialSlots, Is.EqualTo([new AssetId(checked((int)MaterialAssetId))]));
            Assert.That(mesh.Bounds.SphereRadius, Is.EqualTo(1.0f));
        }
        finally
        {
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(packagePath);
        }
    }

    [Test]
    public async Task MeshAssetCodecPreservesExactDyadicPredicatesAcrossBinary32Range()
    {
        var schemas = CreateRegistry();
        var builder = new AssetStoreRegistrationBuilder(schemas);
        _ = Renderer.RegisterAssetCodecs(builder);
        var maximumCoordinate = float.MaxValue / 2.0f;
        var positions = new[] {
            Vector3.Zero,
            new Vector3(0.0f, float.Epsilon, 0.0f),
            new Vector3(maximumCoordinate, 0.0f, 0.0f),
        };
        var streams = new[] { CreatePositionStream(positions) };
        var indices = new ushort[] { 0, 1, 2 };
        var groups = new[] { CreateMeshGroup(0, 3, 0) };
        var minimum = Vector3.Zero;
        var maximum = new Vector3(maximumCoordinate, float.Epsilon, 0.0f);
        var center = new Vector3(-float.Epsilon, 0.0f, 0.0f);
        var validBounds = new RenderMeshBounds(minimum, maximum, center, MathF.BitIncrement(maximumCoordinate));
        var boundaryBounds = new RenderMeshBounds(minimum, maximum, center, maximumCoordinate);
        var validMesh = CreateMeshRecord(positions, streams, indices, groups, validBounds);
        var boundaryMesh = CreateMeshRecord(positions, streams, indices, groups, boundaryBounds);
        var maximumFinite = float.MaxValue;
        var largeProductPositions = new[] {
            new Vector3(-maximumFinite, -maximumFinite, 0.0f),
            new Vector3(maximumFinite, maximumFinite, 0.0f),
            new Vector3(-maximumFinite, maximumFinite, 0.0f),
        };
        var largeProductMesh = CreateMeshRecord(
            largeProductPositions,
            [CreatePositionStream(largeProductPositions)],
            indices,
            groups,
            new RenderMeshBounds(
                new Vector3(-maximumFinite, -maximumFinite, 0.0f),
                new Vector3(maximumFinite, maximumFinite, 0.0f),
                Vector3.Zero,
                maximumFinite));
        var (store, packagePath) = await CreateStoreAsync(
            CreatePackage([validMesh, boundaryMesh, largeProductMesh]),
            builder.Build()).ConfigureAwait(false);

        try
        {
            using var validMeshLease = await store.LoadAsync<RenderMeshAsset>(new AssetId(1)).ConfigureAwait(false);
            Assert.That(validMeshLease.Value.Positions, Is.EqualTo(positions));
            _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.LoadAsync<RenderMeshAsset>(new AssetId(2)).ConfigureAwait(false));
            _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.LoadAsync<RenderMeshAsset>(new AssetId(3)).ConfigureAwait(false));
        }
        finally
        {
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(packagePath);
        }
    }

    [Test]
    public async Task MeshAssetCodecRejectsDegenerateTrianglesAndUnconservativeBounds()
    {
        var schemas = CreateRegistry();
        var builder = new AssetStoreRegistrationBuilder(schemas);
        _ = Renderer.RegisterAssetCodecs(builder);
        var collinearPositions = new[] {
            Vector3.Zero,
            Vector3.UnitX,
            new Vector3(2.0f, 0.0f, 0.0f),
        };
        var degenerateTriangle = CreateMeshRecord(
            collinearPositions,
            [CreatePositionStream(collinearPositions)],
            [0, 1, 2],
            [CreateMeshGroup(0, 3, 0)],
            new RenderMeshBounds(Vector3.Zero, new Vector3(2.0f, 0.0f, 0.0f), Vector3.Zero, 2.0f));
        var validPositions = new[] {
            Vector3.Zero,
            Vector3.UnitX,
            Vector3.UnitY,
        };
        var outOfBoundsMesh = CreateMeshRecord(
            validPositions,
            [CreatePositionStream(validPositions)],
            [0, 1, 2],
            [CreateMeshGroup(0, 3, 0)],
            new RenderMeshBounds(Vector3.Zero, new Vector3(0.5f, 0.5f, 0.0f), Vector3.Zero, 1.0f));
        var (store, packagePath) = await CreateStoreAsync(
            CreatePackage([degenerateTriangle, outOfBoundsMesh]),
            builder.Build()).ConfigureAwait(false);

        try
        {
            _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.LoadAsync<RenderMeshAsset>(new AssetId(1)).ConfigureAwait(false));
            _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.LoadAsync<RenderMeshAsset>(new AssetId(2)).ConfigureAwait(false));
        }
        finally
        {
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(packagePath);
        }
    }

    [Test]
    public async Task MeshAssetCodecRejectsInvalidTangentWeightsAndGroupRanges()
    {
        var schemas = CreateRegistry();
        var builder = new AssetStoreRegistrationBuilder(schemas);
        _ = Renderer.RegisterAssetCodecs(builder);
        var positions = new[] {
            Vector3.Zero,
            Vector3.UnitX,
            Vector3.UnitY,
        };
        var invalidTangent = CreateVector4Stream(
            "native/mesh-tangent",
            16,
            16,
            [new Vector4(0.0f, 0.0f, 1.0f, 0.0f), new Vector4(0.0f, 0.0f, 1.0f, 1.0f), new Vector4(0.0f, 0.0f, 1.0f, 1.0f)]);
        var badTangentMesh = CreateMeshRecord(
            positions,
            [CreatePositionStream(positions), invalidTangent],
            [0, 1, 2],
            [CreateMeshGroup(0, 3, 0)],
            new RenderMeshBounds(Vector3.Zero, new Vector3(1.0f, 1.0f, 0.0f), new Vector3(0.5f, 0.5f, 0.0f), 1.0f));
        var badWeights = CreateWeightStream([
            new Vector4(0.25f, 0.25f, 0.25f, 0.125f),
            new Vector4(0.25f, 0.25f, 0.25f, 0.25f),
            new Vector4(0.25f, 0.25f, 0.25f, 0.25f),
        ]);
        var jointElements = new byte[3 * 4 * sizeof(ushort)];
        var jointArray = CreateFixedArrayField(2, elementKind: 2, stride: sizeof(ushort), jointElements);
        var jointStream = CreateStream(
            "native/mesh-joints",
            EbinPrimitives.UInt32(1, 8),
            jointArray);
        var badWeightsMesh = CreateMeshRecord(
            positions,
            [CreatePositionStream(positions), jointStream, badWeights],
            [0, 1, 2],
            [CreateMeshGroup(0, 3, 0)],
            new RenderMeshBounds(Vector3.Zero, new Vector3(1.0f, 1.0f, 0.0f), new Vector3(0.5f, 0.5f, 0.0f), 1.0f),
            includeSkeleton: true);
        var badGroupMesh = CreateMeshRecord(
            positions,
            [CreatePositionStream(positions)],
            [0, 1, 2],
            [CreateMeshGroup(1, 3, 0)],
            new RenderMeshBounds(Vector3.Zero, new Vector3(1.0f, 1.0f, 0.0f), new Vector3(0.5f, 0.5f, 0.0f), 1.0f));
        var (store, packagePath) = await CreateStoreAsync(
            CreatePackage(
                [badTangentMesh, badWeightsMesh, badGroupMesh],
                CreateSkeletonRecord(new Guid("70000000-0000-4000-8000-000000000002"))),
            builder.Build()).ConfigureAwait(false);

        try
        {
            _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.LoadAsync<RenderMeshAsset>(new AssetId(1)).ConfigureAwait(false));
            _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.LoadAsync<RenderMeshAsset>(new AssetId(2)).ConfigureAwait(false));
            _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.LoadAsync<RenderMeshAsset>(new AssetId(3)).ConfigureAwait(false));
        }
        finally
        {
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(packagePath);
        }
    }

    [Test]
    public async Task MeshAssetCodecUsesExactWeightToleranceAroundTheBoundary()
    {
        var schemas = CreateRegistry();
        var builder = new AssetStoreRegistrationBuilder(schemas);
        _ = Renderer.RegisterAssetCodecs(builder);
        _ = AnimationSupport.RegisterAssetCodecs(builder);
        // Below 0.25, adjacent binary32 values are 2^-26 apart; these steps bracket 1/100000.
        var acceptedWeight = BitConverter.UInt32BitsToSingle(0x3E80_0000u - 671u);
        var rejectedWeight = BitConverter.UInt32BitsToSingle(0x3E80_0000u - 672u);
        var positions = new[] {
            Vector3.Zero,
            Vector3.UnitX,
            Vector3.UnitY,
        };
        var jointStream = CreateJointStream(new ushort[12]);
        var validWeights = CreateWeightStream([
            new Vector4(0.25f, 0.25f, 0.25f, acceptedWeight),
            new Vector4(0.25f, 0.25f, 0.25f, acceptedWeight),
            new Vector4(0.25f, 0.25f, 0.25f, acceptedWeight),
        ]);
        var invalidWeights = CreateWeightStream([
            new Vector4(0.25f, 0.25f, 0.25f, rejectedWeight),
            new Vector4(0.25f, 0.25f, 0.25f, rejectedWeight),
            new Vector4(0.25f, 0.25f, 0.25f, rejectedWeight),
        ]);
        var bounds = new RenderMeshBounds(
            Vector3.Zero,
            new Vector3(1.0f, 1.0f, 0.0f),
            new Vector3(0.5f, 0.5f, 0.0f),
            1.0f);
        var validMesh = CreateMeshRecord(
            positions,
            [CreatePositionStream(positions), jointStream, validWeights],
            [0, 1, 2],
            [CreateMeshGroup(0, 3, 0)],
            bounds,
            includeSkeleton: true);
        var invalidMesh = CreateMeshRecord(
            positions,
            [CreatePositionStream(positions), jointStream, invalidWeights],
            [0, 1, 2],
            [CreateMeshGroup(0, 3, 0)],
            bounds,
            includeSkeleton: true);
        var skeleton = CreateSkeletonRecord(new Guid("70000000-0000-4000-8000-000000000003"));
        var (store, packagePath) = await CreateStoreAsync(
            CreatePackage([validMesh, invalidMesh], skeleton),
            builder.Build()).ConfigureAwait(false);
        AssetLease<RenderMeshAsset>? unexpectedInvalidLease = null;

        try
        {
            using var validMeshLease = await store.LoadAsync<RenderMeshAsset>(new AssetId(1)).ConfigureAwait(false);
            Assert.That(validMeshLease.Value.Weights[0].W, Is.EqualTo(acceptedWeight));
            _ = Assert.ThrowsAsync<InvalidDataException>(
                async () => unexpectedInvalidLease = await store.LoadAsync<RenderMeshAsset>(new AssetId(2)).ConfigureAwait(false));
        }
        finally
        {
            unexpectedInvalidLease?.Dispose();
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(packagePath);
        }
    }
}
