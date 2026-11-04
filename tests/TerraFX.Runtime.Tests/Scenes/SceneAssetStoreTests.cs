// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Animation;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Graphics;
using TerraFX.Hosting;
using TerraFX.Rendering;
using TerraFX.Runtime.Tests.Entities;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

[TestFixture]
internal sealed partial class SceneAssetStoreTests
{
    private static async Task AssertExpansionRejectedAsync(byte[] scene, byte[] prefab)
    {
        var schemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var registration = SceneSupport.RegisterAssetCodecs(new AssetStoreRegistrationBuilder(schemas)).Build();
        var package = Package.CreatePackage(
            new Package.Entry(1, Package.CreateEnvelope(1, prefab)),
            new Package.Entry(3, Package.CreateEnvelope(3, scene)));
        var packagePath = Package.WritePackage(package);

        try
        {
            var store = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(package), registration)).ConfigureAwait(false);

            try
            {
                _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
                    await ScenePrefabExpansionPlanner.PrepareAsync(
                        store,
                        new AssetId(3),
                        SceneExpansionLimits.Default).ConfigureAwait(false));
            }
            finally
            {
                await store.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    private static uint[] CaptureSceneAssetIds(World world)
    {
        using var lease = world.AcquirePersistenceCaptureLease();
        var assetIds = new uint[lease.GetSceneAssetIdCount()];
        lease.CopySceneAssetIds(assetIds);
        return assetIds;
    }

    private static byte[] CreateEntityReferenceBindingToExternalSlot(
        Guid entityId,
        ContentSchemaReference componentSchema,
        Guid externalSlotId)
    {
        var source = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("native/entity-selector").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(1, (byte)SceneEntitySelectorScope.OccurrenceRelative),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFixedGuidArrayField(2),
            SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(3, entityId));
        var propertySegment = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("content/property-segment").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(1, (byte)ScenePropertyPathSegmentKind.FieldTag),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(2, 1));
        var target = SceneAssetReaderTests.Fixtures.CreateReferenceTarget(
            SceneReferenceTargetKind.ExternalSlot,
            externalSlotId: externalSlotId);

        return SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("native/entity-reference-binding").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(1, source),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(
                2,
                CreateSchemaReference("test/scene-expanded-entity-reference")),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(3, propertySegment),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(4, target));
    }

    private static byte[] CreateEntityReferenceComponent(
        Guid schemaId,
        Guid externalSlotId,
        bool isRequired = true)
        => SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            schemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(1, externalSlotId, isRequired));

    private static DataSchema CreateEntityReferenceComponentSchema(bool optional = false)
    {
        var identity = new ContentSchemaIdentityAssignment("test/scene-expanded-entity-reference");
        return new DataSchema(
            new ContentSchemaReference(identity.SchemaId, 1, 0, ContentSchemaScope.Content),
            identity,
            ContentSchemaRole.Component,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.Persistent,
            ContentTransientRestore.NotApplicable,
            [
                new ContentFieldDescriptor(
                    tag: 1,
                    wireKind: 11,
                    elementKind: 0,
                    isRequired: !optional,
                    minimumCount: 1,
                    maximumCount: 1,
                    nestedSchemas: [],
                    nestedRoleMask: 0,
                    unit: ContentUnit.None,
                    reference: ContentReferenceKind.Entity,
                    authoringFlags: ContentAuthoring.PrefabEditable,
                    defaultValue: null),
            ]);
    }

    private static byte[] CreateEntityReferenceInternalOverride(
        Guid entityId,
        Guid[] placementPath,
        Guid componentSchemaId,
        Guid targetEntityId)
    {
        var targetEntity = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("native/entity-selector").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(1, (byte)SceneEntitySelectorScope.OccurrenceRelative),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFixedGuidArrayField(2, placementPath),
            SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(3, entityId));
        var propertySegment = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("content/property-segment").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(1, (byte)ScenePropertyPathSegmentKind.FieldTag),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(2, 1));
        var literal = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("content/literal/guid").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(1, targetEntityId));
        var referenceValue = SceneAssetReaderTests.Fixtures.CreateReferenceTarget(
            SceneReferenceTargetKind.Internal,
            entityId: targetEntityId);

        return SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("native/prefab-override").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(1, targetEntity),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(
                2,
                CreateSchemaReference("test/scene-expanded-entity-reference")),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(3, propertySegment),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(4, literal, isRequired: false),
            new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(5, 1, 0, 1, [(byte)ScenePrefabOverrideOperation.Set]),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(6, referenceValue, isRequired: false));
    }

    private static byte[] CreateEntityReferenceOverride(
        Guid entityId,
        Guid[] placementPath,
        Guid componentSchemaId,
        Guid externalSlotId,
        bool omit = false)
    {
        var targetEntity = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("native/entity-selector").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(1, (byte)SceneEntitySelectorScope.OccurrenceRelative),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFixedGuidArrayField(2, placementPath),
            SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(3, entityId));
        var propertySegment = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("content/property-segment").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(1, (byte)ScenePropertyPathSegmentKind.FieldTag),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(2, 1));
        List<SceneHierarchyCodecTests.FixtureBuilder.FixtureField> fields = [
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(1, targetEntity),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(
                2,
                CreateSchemaReference("test/scene-expanded-entity-reference")),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(3, propertySegment),
        ];

        if (!omit)
        {
            var literal = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
                new ContentSchemaIdentityAssignment("content/literal/guid").SchemaId,
                1,
                0,
                SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(1, externalSlotId));
            var referenceValue = SceneAssetReaderTests.Fixtures.CreateReferenceTarget(
                SceneReferenceTargetKind.ExternalSlot,
                externalSlotId: externalSlotId);
            fields.Add(SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(4, literal, isRequired: false));
            fields.Add(new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(5, 1, 0, 1, [(byte)ScenePrefabOverrideOperation.Set]));
            fields.Add(SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(6, referenceValue, isRequired: false));
        }
        else
        {
            fields.Add(new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(5, 1, 0, 1, [(byte)ScenePrefabOverrideOperation.Omit]));
        }

        return SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("native/prefab-override").SchemaId,
            1,
            0,
            [.. fields]);
    }

    private static byte[] CreateExternalSlot(
        Guid slotId,
        bool required,
        bool allowNull,
        byte[][] requiredComponents)
        => SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("native/external-slot").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(1, slotId),
            new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(2, 9, 1, 1, [required ? (byte)1 : (byte)0]),
            new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(3, 9, 1, 1, [allowNull ? (byte)1 : (byte)0]),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(4, requiredComponents));

    private static byte[] CreateLocalTransformPositionOverride(
        Guid targetEntityId,
        Guid[] placementPath,
        byte scope,
        float positionX)
        => CreateLocalTransformValueOverride(
            targetEntityId,
            placementPath,
            scope,
            fieldTag: 1,
            x: positionX,
            y: 0.0f,
            z: 0.0f);

    private static byte[] CreateLocalTransformRecord(float positionX, float scaleX)
        => SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("scene/local-transform").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(1, 15, positionX, 0, 0),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(2, 17, 0, 0, 0, 1),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(3, 15, scaleX, 1, 1));

    private static byte[] CreateLocalTransformScaleOverride(
        Guid targetEntityId,
        Guid[] placementPath,
        byte scope,
        float scaleX)
        => CreateLocalTransformValueOverride(
            targetEntityId,
            placementPath,
            scope,
            fieldTag: 3,
            x: scaleX,
            y: 1.0f,
            z: 1.0f);

    private static byte[] CreateLocalTransformValueOverride(
        Guid targetEntityId,
        Guid[] placementPath,
        byte scope,
        uint fieldTag,
        float x,
        float y,
        float z)
    {
        var target = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("native/entity-selector").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(1, scope),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFixedGuidArrayField(2, placementPath),
            SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(3, targetEntityId));
        var propertySegment = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("content/property-segment").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(1, (byte)ScenePropertyPathSegmentKind.FieldTag),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(2, fieldTag));
        var literal = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("content/literal/vector3").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(1, 15, x, y, z));

        return SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("native/prefab-override").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(1, target),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(2, CreateSchemaReference("scene/local-transform")),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(3, propertySegment),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(4, literal, isRequired: false),
            new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(5, 1, 0, 1, [(byte)ScenePrefabOverrideOperation.Set]));
    }

    private static byte[] CreateNestedExpansionPackage(
        Guid outerPlacementId,
        Guid innerPlacementId,
        Guid entityId,
        AssetId innerTargetAssetId,
        bool includeInnerEntity = true,
        bool includeOuterEntity = false)
    {
        byte[][] innerRootPoses = includeInnerEntity
            ? [SceneAssetReaderTests.Fixtures.CreateRootPose(entityId, localScaleX: 2.0f)]
            : [];
        List<byte[]> sceneRootPoses = [];

        if (includeOuterEntity)
        {
            sceneRootPoses.Add(SceneAssetReaderTests.Fixtures.CreateRootPose(entityId, localScaleX: 3.0f, rootChunkX: 1));
        }

        if (includeInnerEntity)
        {
            sceneRootPoses.Add(
                SceneAssetReaderTests.Fixtures.CreateRootPose(
                    entityId,
                    placementPath: [innerPlacementId],
                    localScaleX: 4.0f,
                    rootChunkX: 2));
        }

        var outerPrefab = SceneAssetReaderTests.Fixtures.CreatePrefab(
            new AssetId(1),
            entityId: includeOuterEntity ? entityId : null,
            placementId: innerPlacementId,
            placementRootPoses: innerRootPoses,
            placementPrefabAssetId: innerTargetAssetId);
        var scene = SceneAssetReaderTests.Fixtures.CreateAssetReferenceScene(
            new AssetId(3),
            outerPlacementId,
            rootPoses: [.. sceneRootPoses]);
        var outerEntry = new Package.Entry(1, Package.CreateEnvelope(1, outerPrefab));
        var sceneEntry = new Package.Entry(3, Package.CreateEnvelope(3, scene));

        if (innerTargetAssetId == new AssetId(1))
        {
            return Package.CreatePackage(outerEntry, sceneEntry);
        }

        var innerPrefab = SceneAssetReaderTests.Fixtures.CreatePrefab(
            new AssetId(2),
            entityId: includeInnerEntity ? entityId : null);
        return Package.CreatePackage(
            outerEntry,
            new Package.Entry(2, Package.CreateEnvelope(2, innerPrefab)),
            sceneEntry);
    }

    private static byte[] CreateRootPositionRecord(int chunkX, float offsetX = 0.0f)
        => SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("scene/root-position").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreatePosition3Field(1, chunkX, 0, 0, offsetX, 0, 0));

    private static byte[] CreateSchemaReference(string schemaName)
        => SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("content/schema-ref").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(1, new ContentSchemaIdentityAssignment(schemaName).SchemaId),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(2, 1),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(3, 0),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(4, (byte)ContentSchemaScope.Content));

    private static async Task DeleteTemporaryPackageAsync(string packagePath)
    {
        ArgumentNullException.ThrowIfNull(packagePath);

        for (var retry = 0; ; retry++)
        {
            try
            {
                File.Delete(packagePath);
                return;
            }
            catch (IOException exception) when (
                (retry < 299)
                && ((exception.HResult == unchecked((int)0x80070020))
                    || (exception.HResult == unchecked((int)0x80070021))))
            {
                await Task.Delay(50).ConfigureAwait(false);
            }
        }
    }

    private static SceneAuthoredComponentRecord GetComponentRecord(SceneExpandedEntity entity, Guid schemaId)
    {
        for (var index = 0; index < entity.Components.Length; index++)
        {
            if (entity.Components[index].Schema.SchemaId == schemaId)
            {
                return entity.Components[index];
            }
        }

        throw new AssertionException($"Expanded Entity {entity.AuthoredEntityId:N} has no component {schemaId:N}.");
    }

    private static bool HasComponent(SceneExpandedEntity entity, Guid schemaId)
    {
        for (var index = 0; index < entity.Components.Length; index++)
        {
            if (entity.Components[index].Schema.SchemaId == schemaId)
            {
                return true;
            }
        }

        return false;
    }

    [Test]
    public async Task AppliesChildPlacementRootPoseAndRemovesTemplateRootPosition()
    {
        var schemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var registration = SceneSupport.RegisterAssetCodecs(new AssetStoreRegistrationBuilder(schemas)).Build();
        var placementId = new Guid("20000000-0000-4000-8000-000000000045");
        var rootEntityId = SceneAssetReaderTests.Fixtures.RootEntityId;
        var childRootPose = SceneAssetReaderTests.Fixtures.CreateRootPose(
            rootEntityId,
            localPositionX: 2.0f,
            includeRootPosition: false);
        var scene = SceneAssetReaderTests.Fixtures.CreateSceneWithChildPlacement(
            new AssetId(3),
            rootEntityId,
            placementId,
            childRootPose);
        var prefab = SceneAssetReaderTests.Fixtures.CreatePrefab(
            new AssetId(1),
            entityId: rootEntityId);
        var package = Package.CreatePackage(
            new Package.Entry(1, Package.CreateEnvelope(1, prefab)),
            new Package.Entry(3, Package.CreateEnvelope(3, scene)));
        var packagePath = Package.WritePackage(package);

        try
        {
            var store = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(package), registration)).ConfigureAwait(false);

            try
            {
                using var expansion = await ScenePrefabExpansionPlanner.PrepareAsync(
                    store,
                    new AssetId(3),
                    SceneExpansionLimits.Default).ConfigureAwait(false);
                var rootPositionSchemaId = new ContentSchemaIdentityAssignment("scene/root-position").SchemaId;
                var localTransformSchemaId = new ContentSchemaIdentityAssignment("scene/local-transform").SchemaId;
                var rootComponents = expansion.Entities[0].Components;
                var childComponents = expansion.Entities[1].Components;

                Assert.That(
                    GetComponentRecord(expansion.Entities[0], rootPositionSchemaId).EncodedRecord.ToArray(),
                    Is.EqualTo(CreateRootPositionRecord(chunkX: 0)));
                Assert.That(HasComponent(expansion.Entities[1], rootPositionSchemaId), Is.False);
                Assert.That(
                    GetComponentRecord(expansion.Entities[1], localTransformSchemaId).EncodedRecord.ToArray(),
                    Is.EqualTo(CreateLocalTransformRecord(positionX: 2.0f, scaleX: 1.0f)));
                Assert.That(rootComponents.Length, Is.EqualTo(2));
                Assert.That(childComponents.Length, Is.EqualTo(1));
            }
            finally
            {
                await store.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Test]
    public async Task AppliesNestedInternalReferenceOverrideFromSelectedOccurrence()
    {
        var rootEntityId = SceneAssetReaderTests.Fixtures.RootEntityId;
        var componentSchema = CreateEntityReferenceComponentSchema();
        var registeredSchemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var schemas = new CompiledContentSchemaRegistry([.. registeredSchemas.Schemas, componentSchema]);
        var registration = SceneSupport.RegisterAssetCodecs(new AssetStoreRegistrationBuilder(schemas)).Build();
        var outerPlacementId = new Guid("20000000-0000-4000-8000-00000000004f");
        var innerPlacementId = new Guid("20000000-0000-4000-8000-000000000050");
        var externalSlotId = new Guid("30000000-0000-4000-8000-000000000054");
        var externalSlot = CreateExternalSlot(externalSlotId, required: false, allowNull: true, []);
        var innerBinding = CreateEntityReferenceBindingToExternalSlot(
            rootEntityId,
            componentSchema.Schema,
            externalSlotId);
        var innerPrefab = SceneAssetReaderTests.Fixtures.CreatePrefab(
            new AssetId(2),
            entityId: rootEntityId,
            references: [innerBinding],
            externalSlots: [externalSlot],
            additionalEntityComponents: [
                CreateEntityReferenceComponent(componentSchema.Schema.SchemaId, externalSlotId),
            ]);
        var outerPrefab = SceneAssetReaderTests.Fixtures.CreatePrefab(
            new AssetId(1),
            placementId: innerPlacementId,
            placementRootPoses: [SceneAssetReaderTests.Fixtures.CreateRootPose(rootEntityId)],
            placementPrefabAssetId: new AssetId(2));
        var scene = SceneAssetReaderTests.Fixtures.CreateAssetReferenceScene(
            new AssetId(3),
            outerPlacementId,
            rootPoses: [
                SceneAssetReaderTests.Fixtures.CreateRootPose(
                    rootEntityId,
                    placementPath: [innerPlacementId]),
            ],
            overrides: [
                CreateEntityReferenceInternalOverride(
                    rootEntityId,
                    [innerPlacementId],
                    componentSchema.Schema.SchemaId,
                    rootEntityId),
            ]);
        var package = Package.CreatePackage(
            new Package.Entry(1, Package.CreateEnvelope(1, outerPrefab)),
            new Package.Entry(2, Package.CreateEnvelope(2, innerPrefab)),
            new Package.Entry(3, Package.CreateEnvelope(3, scene)));
        var packagePath = Package.WritePackage(package);

        try
        {
            var store = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(package), registration)).ConfigureAwait(false);

            try
            {
                using var expansion = await ScenePrefabExpansionPlanner.PrepareAsync(
                    store,
                    new AssetId(3),
                    SceneExpansionLimits.Default).ConfigureAwait(false);
                Assert.That(expansion.ReferenceBindings, Has.Length.EqualTo(1));
                var referenceBinding = expansion.ReferenceBindings[0];
                Assert.That(referenceBinding.OwnerOccurrenceIndex, Is.Zero);
                Assert.That(referenceBinding.TargetAnchorOccurrenceIndex, Is.EqualTo(2));
                Assert.That(referenceBinding.Target.Kind, Is.EqualTo(SceneReferenceTargetKind.Internal));
                Assert.That(referenceBinding.Target.Entity?.AuthoredEntityId, Is.EqualTo(rootEntityId));
                Assert.That(referenceBinding.ResolvedTargetExpandedEntityIndex, Is.EqualTo(0));
                Assert.That(expansion.Entities[0].OccurrenceIndex, Is.EqualTo(2));
                Assert.That(expansion.Entities[0].AuthoredEntityId, Is.EqualTo(rootEntityId));
            }
            finally
            {
                await store.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Test]
    public async Task AppliesNestedOverridesFromInnerToOuterAfterRootPoses()
    {
        var schemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var registration = SceneSupport.RegisterAssetCodecs(new AssetStoreRegistrationBuilder(schemas)).Build();
        var outerPlacementId = new Guid("20000000-0000-4000-8000-000000000049");
        var innerPlacementId = new Guid("20000000-0000-4000-8000-00000000004a");
        var rootEntityId = SceneAssetReaderTests.Fixtures.RootEntityId;
        var innerRootPose = SceneAssetReaderTests.Fixtures.CreateRootPose(rootEntityId);
        var innerOverride = CreateLocalTransformScaleOverride(rootEntityId, [], 1, 2.0f);
        var outerPrefab = SceneAssetReaderTests.Fixtures.CreatePrefab(
            new AssetId(1),
            entityId: rootEntityId,
            placementId: innerPlacementId,
            placementRootPoses: [innerRootPose],
            overrides: [innerOverride],
            placementPrefabAssetId: new AssetId(2));
        var innerPrefab = SceneAssetReaderTests.Fixtures.CreatePrefab(
            new AssetId(2),
            entityId: rootEntityId);
        var scene = SceneAssetReaderTests.Fixtures.CreateAssetReferenceScene(
            new AssetId(3),
            outerPlacementId,
            rootPoses: [
                SceneAssetReaderTests.Fixtures.CreateRootPose(
                    rootEntityId,
                    localScaleX: 4.0f,
                    rootChunkX: 1),
                SceneAssetReaderTests.Fixtures.CreateRootPose(
                    rootEntityId,
                    placementPath: [innerPlacementId],
                    localScaleX: 4.0f,
                    rootChunkX: 2),
            ],
            overrides: [
                CreateLocalTransformScaleOverride(rootEntityId, [], 1, 3.0f),
            ]);
        var package = Package.CreatePackage(
            new Package.Entry(1, Package.CreateEnvelope(1, outerPrefab)),
            new Package.Entry(2, Package.CreateEnvelope(2, innerPrefab)),
            new Package.Entry(3, Package.CreateEnvelope(3, scene)));
        var packagePath = Package.WritePackage(package);

        try
        {
            var store = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(package), registration)).ConfigureAwait(false);

            try
            {
                using var expansion = await ScenePrefabExpansionPlanner.PrepareAsync(
                    store,
                    new AssetId(3),
                    SceneExpansionLimits.Default).ConfigureAwait(false);

                Assert.That(expansion.Entities, Has.Length.EqualTo(2));
                Assert.That(
                    GetComponentRecord(
                        expansion.Entities[0],
                        new ContentSchemaIdentityAssignment("scene/local-transform").SchemaId).EncodedRecord.ToArray(),
                    Is.EqualTo(CreateLocalTransformRecord(positionX: 0.0f, scaleX: 3.0f)));
                Assert.That(
                    GetComponentRecord(
                        expansion.Entities[1],
                        new ContentSchemaIdentityAssignment("scene/local-transform").SchemaId).EncodedRecord.ToArray(),
                    Is.EqualTo(CreateLocalTransformRecord(positionX: 0.0f, scaleX: 4.0f)));
                Assert.That(
                    GetComponentRecord(
                        expansion.Entities[1],
                        new ContentSchemaIdentityAssignment("scene/root-position").SchemaId).EncodedRecord.ToArray(),
                    Is.EqualTo(CreateRootPositionRecord(chunkX: 2)));
            }
            finally
            {
                await store.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Test]
    public async Task AppliesRootEntityReferenceOverrideToEffectiveBinding()
    {
        var rootEntityId = SceneAssetReaderTests.Fixtures.RootEntityId;
        var componentSchema = CreateEntityReferenceComponentSchema();
        var registeredSchemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var schemas = new CompiledContentSchemaRegistry([.. registeredSchemas.Schemas, componentSchema]);
        var registration = SceneSupport.RegisterAssetCodecs(new AssetStoreRegistrationBuilder(schemas)).Build();
        var placementId = new Guid("20000000-0000-4000-8000-00000000004d");
        var originalSlotId = new Guid("30000000-0000-4000-8000-000000000051");
        var replacementSlotId = new Guid("30000000-0000-4000-8000-000000000052");
        var prefab = SceneAssetReaderTests.Fixtures.CreatePrefab(
            new AssetId(1),
            entityId: rootEntityId,
            references: [
                CreateEntityReferenceBindingToExternalSlot(
                    rootEntityId,
                    componentSchema.Schema,
                    originalSlotId),
            ],
            externalSlots: [
                CreateExternalSlot(originalSlotId, required: false, allowNull: true, []),
                CreateExternalSlot(replacementSlotId, required: false, allowNull: true, []),
            ],
            additionalEntityComponents: [
                CreateEntityReferenceComponent(componentSchema.Schema.SchemaId, originalSlotId),
            ]);
        var scene = SceneAssetReaderTests.Fixtures.CreateAssetReferenceScene(
            new AssetId(3),
            placementId,
            rootPoses: [SceneAssetReaderTests.Fixtures.CreateRootPose(rootEntityId)],
            overrides: [
                CreateEntityReferenceOverride(
                    rootEntityId,
                    [],
                    componentSchema.Schema.SchemaId,
                    replacementSlotId),
            ],
            externalSlots: [
                CreateExternalSlot(replacementSlotId, required: false, allowNull: true, []),
            ]);
        var package = Package.CreatePackage(
            new Package.Entry(1, Package.CreateEnvelope(1, prefab)),
            new Package.Entry(3, Package.CreateEnvelope(3, scene)));
        var packagePath = Package.WritePackage(package);

        try
        {
            var store = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(package), registration)).ConfigureAwait(false);

            try
            {
                using var expansion = await ScenePrefabExpansionPlanner.PrepareAsync(
                    store,
                    new AssetId(3),
                    SceneExpansionLimits.Default).ConfigureAwait(false);

                Assert.That(expansion.ReferenceBindings, Has.Length.EqualTo(1));
                Assert.That(expansion.ReferenceBindings[0].ExpandedEntityIndex, Is.Zero);
                Assert.That(expansion.ReferenceBindings[0].OwnerOccurrenceIndex, Is.Zero);
                Assert.That(expansion.ReferenceBindings[0].TargetAnchorOccurrenceIndex, Is.EqualTo(1));
                Assert.That(expansion.ReferenceBindings[0].Target.Kind, Is.EqualTo(SceneReferenceTargetKind.ExternalSlot));
                Assert.That(expansion.ReferenceBindings[0].Target.ExternalSlotId, Is.EqualTo(replacementSlotId));
                Assert.That(expansion.ReferenceBindings[0].ResolvedTargetExpandedEntityIndex, Is.Null);
                Assert.That(
                    GetComponentRecord(expansion.Entities[0], componentSchema.Schema.SchemaId).EncodedRecord.ToArray(),
                    Is.EqualTo(CreateEntityReferenceComponent(componentSchema.Schema.SchemaId, replacementSlotId)));
            }
            finally
            {
                await store.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Test]
    public async Task AppliesRootSceneOverridesAfterPlacementLayers()
    {
        var schemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var registration = SceneSupport.RegisterAssetCodecs(new AssetStoreRegistrationBuilder(schemas)).Build();
        var placementId = new Guid("20000000-0000-4000-8000-00000000004b");
        var rootEntityId = SceneAssetReaderTests.Fixtures.RootEntityId;
        var prefab = SceneAssetReaderTests.Fixtures.CreatePrefab(
            new AssetId(1),
            entityId: rootEntityId);
        var placementOverride = CreateLocalTransformScaleOverride(rootEntityId, [], 1, 3.0f);
        var sceneOverride = CreateLocalTransformScaleOverride(
            rootEntityId,
            [placementId],
            (byte)SceneEntitySelectorScope.InstanceAbsolute,
            5.0f);
        var scene = SceneAssetReaderTests.Fixtures.CreateAssetReferenceScene(
            new AssetId(3),
            placementId,
            rootPoses: [SceneAssetReaderTests.Fixtures.CreateRootPose(rootEntityId)],
            overrides: [placementOverride],
            sceneOverrides: [sceneOverride]);
        var package = Package.CreatePackage(
            new Package.Entry(1, Package.CreateEnvelope(1, prefab)),
            new Package.Entry(3, Package.CreateEnvelope(3, scene)));
        var packagePath = Package.WritePackage(package);

        try
        {
            var store = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(package), registration)).ConfigureAwait(false);

            try
            {
                using var expansion = await ScenePrefabExpansionPlanner.PrepareAsync(
                    store,
                    new AssetId(3),
                    SceneExpansionLimits.Default).ConfigureAwait(false);

                Assert.That(
                    GetComponentRecord(
                        expansion.Entities[0],
                        new ContentSchemaIdentityAssignment("scene/local-transform").SchemaId).EncodedRecord.ToArray(),
                    Is.EqualTo(CreateLocalTransformRecord(positionX: 0.0f, scaleX: 5.0f)));
            }
            finally
            {
                await store.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Test]
    public async Task ExpandsEmptyPrefabPlacementsAsOccurrences()
    {
        var schemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var registration = SceneSupport.RegisterAssetCodecs(new AssetStoreRegistrationBuilder(schemas)).Build();
        var package = CreateNestedExpansionPackage(
            new Guid("20000000-0000-4000-8000-000000000043"),
            new Guid("20000000-0000-4000-8000-000000000044"),
            SceneAssetReaderTests.Fixtures.RootEntityId,
            new AssetId(2),
            includeInnerEntity: false);
        var packagePath = Package.WritePackage(package);

        try
        {
            var store = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(package), registration)).ConfigureAwait(false);

            try
            {
                using var expansion = await ScenePrefabExpansionPlanner.PrepareAsync(
                    store,
                    new AssetId(3),
                    SceneExpansionLimits.Default).ConfigureAwait(false);

                Assert.That(expansion.Occurrences, Has.Length.EqualTo(3));
                Assert.That(expansion.Entities, Is.Empty);
                Assert.That(expansion.GetRootEntityIndices(0).Length, Is.Zero);
            }
            finally
            {
                await store.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Test]
    public async Task ExpandsNestedPrefabsWithAssetStoreLeasesAndStableOccurrences()
    {
        var schemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var registration = SceneSupport.RegisterAssetCodecs(new AssetStoreRegistrationBuilder(schemas)).Build();
        var outerPlacementId = new Guid("20000000-0000-4000-8000-000000000041");
        var innerPlacementId = new Guid("20000000-0000-4000-8000-000000000042");
        var rootEntityId = SceneAssetReaderTests.Fixtures.RootEntityId;
        var sceneRecord = SceneAssetReaderTests.Fixtures.CreateAssetReferenceScene(
            new AssetId(3),
            outerPlacementId,
            rootPoses: [
                SceneAssetReaderTests.Fixtures.CreateRootPose(
                    rootEntityId,
                    localScaleX: 3.0f,
                    rootChunkX: 1,
                    rootOffsetX: 1.5f),
                SceneAssetReaderTests.Fixtures.CreateRootPose(
                    rootEntityId,
                    placementPath: [innerPlacementId],
                    localScaleX: 4.0f,
                    rootChunkX: 2,
                    rootOffsetX: -2.25f),
            ]);
        var outerPrefabRecord = SceneAssetReaderTests.Fixtures.CreatePrefab(
            new AssetId(1),
            entityId: rootEntityId,
            placementId: innerPlacementId,
            placementRootPoses: [
                SceneAssetReaderTests.Fixtures.CreateRootPose(rootEntityId, localScaleX: 2.0f),
            ],
            placementPrefabAssetId: new AssetId(2));
        var innerPrefabRecord = SceneAssetReaderTests.Fixtures.CreatePrefab(
            new AssetId(2),
            entityId: rootEntityId);
        var package = Package.CreatePackage(
            new Package.Entry(1, Package.CreateEnvelope(1, outerPrefabRecord)),
            new Package.Entry(2, Package.CreateEnvelope(2, innerPrefabRecord)),
            new Package.Entry(3, Package.CreateEnvelope(3, sceneRecord)));
        var packagePath = Package.WritePackage(package);

        try
        {
            var store = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(package), registration)).ConfigureAwait(false);

            try
            {
                using var expansion = await ScenePrefabExpansionPlanner.PrepareAsync(
                    store,
                    new AssetId(3),
                    SceneExpansionLimits.Default).ConfigureAwait(false);

                var rootOccurrenceId = SceneOccurrenceIdentity.CreateRootOccurrence(new AssetId(3));
                var outerOccurrenceId = SceneOccurrenceIdentity.CreatePrefabOccurrence(
                    rootOccurrenceId,
                    outerPlacementId,
                    new AssetId(1));
                var innerOccurrenceId = SceneOccurrenceIdentity.CreatePrefabOccurrence(
                    outerOccurrenceId,
                    innerPlacementId,
                    new AssetId(2));

                Assert.That(expansion.Occurrences, Has.Length.EqualTo(3));
                Assert.That(expansion.Entities, Has.Length.EqualTo(2));
                Assert.That(expansion.Occurrences[0].OccurrenceId, Is.EqualTo(rootOccurrenceId));
                Assert.That(expansion.Occurrences[1].OccurrenceId, Is.EqualTo(outerOccurrenceId));
                Assert.That(expansion.Occurrences[2].OccurrenceId, Is.EqualTo(innerOccurrenceId));
                Assert.That(expansion.Entities[0].ExpandedEntityKey, Is.EqualTo(
                    SceneOccurrenceIdentity.CreateExpandedEntityKey(outerOccurrenceId, rootEntityId)));
                Assert.That(expansion.Entities[1].ExpandedEntityKey, Is.EqualTo(
                    SceneOccurrenceIdentity.CreateExpandedEntityKey(innerOccurrenceId, rootEntityId)));
                var assignmentKeys = expansion.EntityAssignmentKeys.ToArray();
                var assignmentIndices = expansion.EntityIndicesByAssignmentOrdinal.ToArray();
                var sortedAssignmentKeys = (Guid[])assignmentKeys.Clone();
                SceneOccurrenceIdentity.SortExpandedEntityKeysForAssignment(sortedAssignmentKeys);
                Assert.That(assignmentKeys, Is.EqualTo(sortedAssignmentKeys));
                Assert.That(assignmentIndices, Has.Length.EqualTo(assignmentKeys.Length));

                for (var index = 0; index < assignmentKeys.Length; index++)
                {
                    Assert.That(
                        expansion.Entities[assignmentIndices[index]].ExpandedEntityKey,
                        Is.EqualTo(assignmentKeys[index]));
                }

                Assert.That(expansion.GetRootEntityIndices(0).Length, Is.EqualTo(2));
                Assert.That(expansion.GetSourceEntity(0).AuthoredEntityId, Is.EqualTo(rootEntityId));
                Assert.That(expansion.GetSourceEntity(1).AuthoredEntityId, Is.EqualTo(rootEntityId));
                Assert.That(
                    GetComponentRecord(expansion.Entities[0], new ContentSchemaIdentityAssignment("scene/local-transform").SchemaId)
                        .EncodedRecord.ToArray(),
                    Is.EqualTo(CreateLocalTransformRecord(positionX: 0.0f, scaleX: 3.0f)));
                Assert.That(
                    GetComponentRecord(expansion.Entities[1], new ContentSchemaIdentityAssignment("scene/local-transform").SchemaId)
                        .EncodedRecord.ToArray(),
                    Is.EqualTo(CreateLocalTransformRecord(positionX: 0.0f, scaleX: 4.0f)));
                Assert.That(
                    GetComponentRecord(expansion.Entities[0], new ContentSchemaIdentityAssignment("scene/root-position").SchemaId)
                        .EncodedRecord.ToArray(),
                    Is.EqualTo(CreateRootPositionRecord(chunkX: 1, offsetX: 1.5f)));
                Assert.That(
                    GetComponentRecord(expansion.Entities[1], new ContentSchemaIdentityAssignment("scene/root-position").SchemaId)
                        .EncodedRecord.ToArray(),
                    Is.EqualTo(CreateRootPositionRecord(chunkX: 2, offsetX: -2.25f)));

                var selector = new SceneEntitySelector(
                    SceneEntitySelectorScope.OccurrenceRelative,
                    [outerPlacementId, innerPlacementId],
                    rootEntityId);
                Assert.That(expansion.ResolveEntity(selector, relativeAnchorOccurrenceIndex: 0), Is.EqualTo(1));
            }
            finally
            {
                await store.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Test]
    public async Task LoadsSceneAndPrefabThroughRegisteredCodecs()
    {
        var schemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var registration = SceneSupport.RegisterAssetCodecs(new AssetStoreRegistrationBuilder(schemas)).Build();
        var placementId = new Guid("20000000-0000-4000-8000-000000000003");
        var rootEntityId = SceneAssetReaderTests.Fixtures.RootEntityId;
        var childSlotId = new Guid("30000000-0000-4000-8000-000000000031");
        var containingSlotId = new Guid("30000000-0000-4000-8000-000000000032");
        var requiredComponents = new[] { CreateSchemaReference("scene/camera") };
        var childSlot = CreateExternalSlot(
            childSlotId,
            required: false,
            allowNull: true,
            requiredComponents: requiredComponents);
        var containingSlot = CreateExternalSlot(
            containingSlotId,
            required: false,
            allowNull: true,
            requiredComponents: requiredComponents);
        var slotTarget = SceneAssetReaderTests.Fixtures.CreateReferenceTarget(
            SceneReferenceTargetKind.ExternalSlot,
            externalSlotId: containingSlotId);
        var slotBinding = SceneAssetReaderTests.Fixtures.CreatePlacementSlotBinding(childSlotId, slotTarget);
        var prefabRecord = SceneAssetReaderTests.Fixtures.CreatePrefab(
            new AssetId(1),
            entityId: rootEntityId,
            externalSlots: [childSlot]);
        var sceneRecord = SceneAssetReaderTests.Fixtures.CreateAssetReferenceScene(
            new AssetId(3),
            placementId,
            rootPoses: [SceneAssetReaderTests.Fixtures.CreateRootPose(rootEntityId)],
            slotBindings: [slotBinding],
            externalSlots: [containingSlot]);
        var package = Package.CreatePackage(
            new Package.Entry(1, Package.CreateEnvelope(1, prefabRecord)),
            new Package.Entry(3, Package.CreateEnvelope(3, sceneRecord)));
        var packagePath = Package.WritePackage(package);

        try
        {
            var store = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(package), registration)).ConfigureAwait(false);

            try
            {
                using var prefabLease = await store.LoadAsync<ScenePrefabAsset>(new AssetId(1)).ConfigureAwait(false);
                using var sceneLease = await store.LoadAsync<SceneAsset>(new AssetId(3)).ConfigureAwait(false);

                Assert.That(prefabLease.Value.AssetId, Is.EqualTo(new AssetId(1)));
                Assert.That(sceneLease.Value.AssetId, Is.EqualTo(new AssetId(3)));
                Assert.That(sceneLease.Value.Placements, Has.Length.EqualTo(1));
                Assert.That(sceneLease.Value.Placements[0].PrefabAssetId, Is.EqualTo(prefabLease.AssetId));
                Assert.That(sceneLease.Value.Placements[0].RootPoses, Has.Length.EqualTo(1));
                var resolvedBindings = ScenePrefabSlotBindingResolver.Resolve(
                    sceneLease.Value.Placements[0],
                    prefabLease.Value,
                    sceneLease.Value.ExternalSlots,
                    static _ => ImmutableArray<SceneAuthoredComponentRecord>.Empty,
                    allowInstanceAbsoluteInternalTargets: true);
                Assert.That(resolvedBindings, Has.Length.EqualTo(1));
                Assert.That(resolvedBindings[0].SlotId, Is.EqualTo(childSlotId));
                Assert.That(resolvedBindings[0].Target?.Kind, Is.EqualTo(SceneReferenceTargetKind.ExternalSlot));
                Assert.That(resolvedBindings[0].Target?.ExternalSlotId, Is.EqualTo(containingSlotId));
            }

            finally
            {
                await store.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Test]
    public async Task OmitsEntityReferenceAndRemovesEffectiveBinding()
    {
        var rootEntityId = SceneAssetReaderTests.Fixtures.RootEntityId;
        var componentSchema = CreateEntityReferenceComponentSchema(optional: true);
        var registeredSchemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var schemas = new CompiledContentSchemaRegistry([.. registeredSchemas.Schemas, componentSchema]);
        var registration = SceneSupport.RegisterAssetCodecs(new AssetStoreRegistrationBuilder(schemas)).Build();
        var placementId = new Guid("20000000-0000-4000-8000-00000000004e");
        var externalSlotId = new Guid("30000000-0000-4000-8000-000000000053");
        var externalSlot = CreateExternalSlot(externalSlotId, required: false, allowNull: true, []);
        var prefab = SceneAssetReaderTests.Fixtures.CreatePrefab(
            new AssetId(1),
            entityId: rootEntityId,
            references: [
                CreateEntityReferenceBindingToExternalSlot(
                    rootEntityId,
                    componentSchema.Schema,
                    externalSlotId),
            ],
            externalSlots: [externalSlot],
            additionalEntityComponents: [
                CreateEntityReferenceComponent(
                    componentSchema.Schema.SchemaId,
                    externalSlotId,
                    isRequired: false),
            ]);
        var scene = SceneAssetReaderTests.Fixtures.CreateAssetReferenceScene(
            new AssetId(3),
            placementId,
            rootPoses: [SceneAssetReaderTests.Fixtures.CreateRootPose(rootEntityId)],
            overrides: [
                CreateEntityReferenceOverride(
                    rootEntityId,
                    [],
                    componentSchema.Schema.SchemaId,
                    externalSlotId,
                    omit: true),
            ]);
        var package = Package.CreatePackage(
            new Package.Entry(1, Package.CreateEnvelope(1, prefab)),
            new Package.Entry(3, Package.CreateEnvelope(3, scene)));
        var packagePath = Package.WritePackage(package);

        try
        {
            var store = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(package), registration)).ConfigureAwait(false);

            try
            {
                using var expansion = await ScenePrefabExpansionPlanner.PrepareAsync(
                    store,
                    new AssetId(3),
                    SceneExpansionLimits.Default).ConfigureAwait(false);

                Assert.That(expansion.ReferenceBindings, Is.Empty);
                Assert.That(
                    GetComponentRecord(expansion.Entities[0], componentSchema.Schema.SchemaId).EncodedRecord.ToArray(),
                    Is.EqualTo(SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
                        componentSchema.Schema.SchemaId,
                        1,
                        0)));
            }
            finally
            {
                await store.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Test]
    public async Task RejectsExpandedEntityLimitBeforePublishingAPlan()
    {
        var schemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var registration = SceneSupport.RegisterAssetCodecs(new AssetStoreRegistrationBuilder(schemas)).Build();
        var package = CreateNestedExpansionPackage(
            new Guid("20000000-0000-4000-8000-000000000057"),
            new Guid("20000000-0000-4000-8000-000000000058"),
            SceneAssetReaderTests.Fixtures.RootEntityId,
            new AssetId(2),
            includeOuterEntity: true);
        var packagePath = Package.WritePackage(package);
        var limits = new SceneExpansionLimits(
            maximumExpandedEntities: 1,
            maximumPrefabEntities: 65_536,
            maximumExpandedOccurrences: 1_048_576,
            maximumPrefabDepth: 32,
            maximumHierarchyDepth: 256,
            maximumExpandedBytes: WorldSchedulingLimits.Default.MaximumOwnedStateBytes);

        try
        {
            var store = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(package), registration)).ConfigureAwait(false);

            try
            {
                _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
                    await ScenePrefabExpansionPlanner.PrepareAsync(store, new AssetId(3), limits).ConfigureAwait(false));
            }
            finally
            {
                await store.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Test]
    public async Task RejectsExpandedOccurrenceLimitBeforePublishingAPlan()
    {
        var schemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var registration = SceneSupport.RegisterAssetCodecs(new AssetStoreRegistrationBuilder(schemas)).Build();
        var package = CreateNestedExpansionPackage(
            new Guid("20000000-0000-4000-8000-000000000053"),
            new Guid("20000000-0000-4000-8000-000000000054"),
            SceneAssetReaderTests.Fixtures.RootEntityId,
            new AssetId(2));
        var packagePath = Package.WritePackage(package);
        var limits = new SceneExpansionLimits(
            maximumExpandedEntities: 1_048_576,
            maximumPrefabEntities: 65_536,
            maximumExpandedOccurrences: 2,
            maximumPrefabDepth: 32,
            maximumHierarchyDepth: 256,
            maximumExpandedBytes: WorldSchedulingLimits.Default.MaximumOwnedStateBytes);

        try
        {
            var store = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(package), registration)).ConfigureAwait(false);

            try
            {
                _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
                    await ScenePrefabExpansionPlanner.PrepareAsync(store, new AssetId(3), limits).ConfigureAwait(false));
            }

            finally
            {
                await store.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Test]
    public async Task RejectsExpansionByteReservationBeforePublishingAPlan()
    {
        var schemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var registration = SceneSupport.RegisterAssetCodecs(new AssetStoreRegistrationBuilder(schemas)).Build();
        var package = CreateNestedExpansionPackage(
            new Guid("20000000-0000-4000-8000-000000000055"),
            new Guid("20000000-0000-4000-8000-000000000056"),
            SceneAssetReaderTests.Fixtures.RootEntityId,
            new AssetId(2));
        var packagePath = Package.WritePackage(package);
        var limits = new SceneExpansionLimits(
            maximumExpandedEntities: 1_048_576,
            maximumPrefabEntities: 65_536,
            maximumExpandedOccurrences: 1_048_576,
            maximumPrefabDepth: 32,
            maximumHierarchyDepth: 256,
            maximumExpandedBytes: 1024);

        try
        {
            var store = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(package), registration)).ConfigureAwait(false);

            try
            {
                _ = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await ScenePrefabExpansionPlanner.PrepareAsync(store, new AssetId(3), limits).ConfigureAwait(false));
            }
            finally
            {
                await store.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Test]
    public async Task RejectsIncompleteRootPoseTable()
    {
        var rootEntityId = SceneAssetReaderTests.Fixtures.RootEntityId;
        var placementId = new Guid("20000000-0000-4000-8000-000000000046");
        var scene = SceneAssetReaderTests.Fixtures.CreateAssetReferenceScene(
            new AssetId(3),
            placementId);
        var prefab = SceneAssetReaderTests.Fixtures.CreatePrefab(
            new AssetId(1),
            entityId: rootEntityId);

        await AssertExpansionRejectedAsync(scene, prefab).ConfigureAwait(false);
    }

    [Test]
    public async Task RejectsPrefabChildPlacementRootPosition()
    {
        var rootEntityId = SceneAssetReaderTests.Fixtures.RootEntityId;
        var placementId = new Guid("20000000-0000-4000-8000-000000000047");
        var scene = SceneAssetReaderTests.Fixtures.CreateSceneWithChildPlacement(
            new AssetId(3),
            rootEntityId,
            placementId,
            SceneAssetReaderTests.Fixtures.CreateRootPose(rootEntityId));
        var prefab = SceneAssetReaderTests.Fixtures.CreatePrefab(
            new AssetId(1),
            entityId: rootEntityId);

        await AssertExpansionRejectedAsync(scene, prefab).ConfigureAwait(false);
    }

    [Test]
    public async Task RejectsPrefabCycleDuringExpansion()
    {
        var schemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var registration = SceneSupport.RegisterAssetCodecs(new AssetStoreRegistrationBuilder(schemas)).Build();
        var package = CreateNestedExpansionPackage(
            new Guid("20000000-0000-4000-8000-000000000051"),
            new Guid("20000000-0000-4000-8000-000000000052"),
            SceneAssetReaderTests.Fixtures.RootEntityId,
            new AssetId(1));
        var packagePath = Package.WritePackage(package);

        try
        {
            var store = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(package), registration)).ConfigureAwait(false);

            try
            {
                _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
                    await ScenePrefabExpansionPlanner.PrepareAsync(
                        store,
                        new AssetId(3),
                        SceneExpansionLimits.Default).ConfigureAwait(false));
            }
            finally
            {
                await store.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Test]
    public async Task RejectsPrefabDepthAboveSelectedLimit()
    {
        var schemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var registration = SceneSupport.RegisterAssetCodecs(new AssetStoreRegistrationBuilder(schemas)).Build();
        var package = CreateNestedExpansionPackage(
            new Guid("20000000-0000-4000-8000-000000000059"),
            new Guid("20000000-0000-4000-8000-00000000005a"),
            SceneAssetReaderTests.Fixtures.RootEntityId,
            new AssetId(2));
        var packagePath = Package.WritePackage(package);
        var limits = new SceneExpansionLimits(
            maximumExpandedEntities: 1_048_576,
            maximumPrefabEntities: 65_536,
            maximumExpandedOccurrences: 1_048_576,
            maximumPrefabDepth: 1,
            maximumHierarchyDepth: 256,
            maximumExpandedBytes: WorldSchedulingLimits.Default.MaximumOwnedStateBytes);

        try
        {
            var store = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(package), registration)).ConfigureAwait(false);

            try
            {
                _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
                    await ScenePrefabExpansionPlanner.PrepareAsync(store, new AssetId(3), limits).ConfigureAwait(false));
            }
            finally
            {
                await store.DisposeAsync().ConfigureAwait(false);
            }
        }

        finally
        {
            File.Delete(packagePath);
        }
    }

    [Test]
    public async Task RejectsResolvedRootOverrideConflictWithinOneLayer()
    {
        var rootEntityId = SceneAssetReaderTests.Fixtures.RootEntityId;
        var placementId = new Guid("20000000-0000-4000-8000-00000000004c");
        var relativeOverride = CreateLocalTransformScaleOverride(rootEntityId, [placementId], 1, 2.0f);
        var absoluteOverride = CreateLocalTransformScaleOverride(
            rootEntityId,
            [placementId],
            (byte)SceneEntitySelectorScope.InstanceAbsolute,
            3.0f);
        var scene = SceneAssetReaderTests.Fixtures.CreateAssetReferenceScene(
            new AssetId(3),
            placementId,
            rootPoses: [SceneAssetReaderTests.Fixtures.CreateRootPose(rootEntityId)],
            sceneOverrides: [relativeOverride, absoluteOverride]);
        var prefab = SceneAssetReaderTests.Fixtures.CreatePrefab(
            new AssetId(1),
            entityId: rootEntityId);

        await AssertExpansionRejectedAsync(scene, prefab).ConfigureAwait(false);
    }

    [Test]
    public async Task RejectsRootPlacementRootPoseWithoutPosition()
    {
        var rootEntityId = SceneAssetReaderTests.Fixtures.RootEntityId;
        var placementId = new Guid("20000000-0000-4000-8000-000000000048");
        var scene = SceneAssetReaderTests.Fixtures.CreateAssetReferenceScene(
            new AssetId(3),
            placementId,
            rootPoses: [
                SceneAssetReaderTests.Fixtures.CreateRootPose(
                    rootEntityId,
                    includeRootPosition: false),
            ]);
        var prefab = SceneAssetReaderTests.Fixtures.CreatePrefab(
            new AssetId(1),
            entityId: rootEntityId);

        await AssertExpansionRejectedAsync(scene, prefab).ConfigureAwait(false);
    }

    [Test]
    public async Task RejectsRootSceneOverrideWithTranslatedRootLocalPosition()
    {
        var rootEntityId = SceneAssetReaderTests.Fixtures.RootEntityId;
        var overrideRecord = CreateLocalTransformPositionOverride(
            rootEntityId,
            [],
            (byte)SceneEntitySelectorScope.InstanceAbsolute,
            1.0f);
        var scene = SceneAssetReaderTests.Fixtures.CreateScene(
            new AssetId(3),
            overrides: [overrideRecord]);
        var prefab = SceneAssetReaderTests.Fixtures.CreatePrefab(new AssetId(1));

        await AssertExpansionRejectedAsync(scene, prefab).ConfigureAwait(false);
    }

    [Test]
    public void ReservesEveryNestedOverrideRewriteAndFixedArrayCopy()
    {
        var fieldTag = new ScenePropertyPathSegment(ScenePropertyPathSegmentKind.FieldTag, 1);
        var nestedFieldTag = new ScenePropertyPathSegment(ScenePropertyPathSegmentKind.FieldTag, 2);
        var fixedArrayIndex = new ScenePropertyPathSegment(ScenePropertyPathSegmentKind.FixedArrayIndex, 0);

        Assert.That(
            ScenePrefabExpansionPlanner.CalculateOverrideRewriteReservation(128, [fieldTag]),
            Is.EqualTo(384));
        Assert.That(
            ScenePrefabExpansionPlanner.CalculateOverrideRewriteReservation(128, [fieldTag, nestedFieldTag]),
            Is.EqualTo(768));
        Assert.That(
            ScenePrefabExpansionPlanner.CalculateOverrideRewriteReservation(128, [fieldTag, fixedArrayIndex]),
            Is.EqualTo(512));
    }

    [Test]
    public async Task ResolvesInternalSlotTargetToExpandedEntity()
    {
        var outerEntityId = SceneAssetReaderTests.Fixtures.RootEntityId;
        var innerEntityId = new Guid("10000000-0000-4000-8000-000000000002");
        var innerPlacementId = new Guid("20000000-0000-4000-8000-000000000064");
        var outerPlacementId = new Guid("20000000-0000-4000-8000-000000000065");
        var innerSlotId = new Guid("30000000-0000-4000-8000-000000000064");
        var innerPrefab = SceneAssetReaderTests.Fixtures.CreatePrefab(
            new AssetId(2),
            entityId: innerEntityId,
            externalSlots: [
                CreateExternalSlot(
                    innerSlotId,
                    required: true,
                    allowNull: false,
                    [CreateSchemaReference("scene/local-transform")]),
            ]);
        var outerPrefab = SceneAssetReaderTests.Fixtures.CreatePrefab(
            new AssetId(1),
            entityId: outerEntityId,
            placementId: innerPlacementId,
            placementRootPoses: [
                SceneAssetReaderTests.Fixtures.CreateRootPose(
                    innerEntityId,
                    includeRootPosition: false),
            ],
            slotBindings: [
                SceneAssetReaderTests.Fixtures.CreatePlacementSlotBinding(
                    innerSlotId,
                    SceneAssetReaderTests.Fixtures.CreateReferenceTarget(
                        SceneReferenceTargetKind.Internal,
                        entityId: outerEntityId)),
            ],
            placementPrefabAssetId: new AssetId(2),
            placementParentEntityId: outerEntityId);
        var scene = SceneAssetReaderTests.Fixtures.CreateAssetReferenceScene(
            new AssetId(3),
            outerPlacementId,
            rootPoses: [SceneAssetReaderTests.Fixtures.CreateRootPose(outerEntityId)]);
        var schemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var registration = SceneSupport.RegisterAssetCodecs(new AssetStoreRegistrationBuilder(schemas)).Build();
        var package = Package.CreatePackage(
            new Package.Entry(1, Package.CreateEnvelope(1, outerPrefab)),
            new Package.Entry(2, Package.CreateEnvelope(2, innerPrefab)),
            new Package.Entry(3, Package.CreateEnvelope(3, scene)));
        var packagePath = Package.WritePackage(package);

        try
        {
            var store = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(package), registration)).ConfigureAwait(false);

            try
            {
                using var expansion = await ScenePrefabExpansionPlanner.PrepareAsync(
                    store,
                    new AssetId(3),
                    SceneExpansionLimits.Default).ConfigureAwait(false);
                Assert.That(expansion.ExternalSlotBindings, Has.Length.EqualTo(1));
                Assert.That(expansion.ExternalSlotBindings[0].OccurrenceIndex, Is.EqualTo(2));
                Assert.That(expansion.ExternalSlotBindings[0].SlotId, Is.EqualTo(innerSlotId));
                Assert.That(expansion.ExternalSlotBindings[0].TargetExpandedEntityIndex, Is.Zero);
                Assert.That(expansion.ExternalSlotBindings[0].RootExternalSlotId, Is.Null);
            }
            finally
            {
                await store.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Test]
    public async Task ResolvesNestedExternalSlotForwardingToRootSceneSlot()
    {
        var rootEntityId = SceneAssetReaderTests.Fixtures.RootEntityId;
        var componentSchema = CreateEntityReferenceComponentSchema();
        var registeredSchemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var schemas = new CompiledContentSchemaRegistry([.. registeredSchemas.Schemas, componentSchema]);
        var registration = SceneSupport.RegisterAssetCodecs(new AssetStoreRegistrationBuilder(schemas)).Build();
        var outerPlacementId = new Guid("20000000-0000-4000-8000-000000000061");
        var innerPlacementId = new Guid("20000000-0000-4000-8000-000000000062");
        var innerSlotId = new Guid("30000000-0000-4000-8000-000000000061");
        var outerSlotId = new Guid("30000000-0000-4000-8000-000000000062");
        var rootSlotId = new Guid("30000000-0000-4000-8000-000000000063");
        var innerPrefab = SceneAssetReaderTests.Fixtures.CreatePrefab(
            new AssetId(2),
            entityId: rootEntityId,
            references: [
                CreateEntityReferenceBindingToExternalSlot(
                    rootEntityId,
                    componentSchema.Schema,
                    innerSlotId),
            ],
            externalSlots: [
                CreateExternalSlot(innerSlotId, required: false, allowNull: true, []),
            ],
            additionalEntityComponents: [
                CreateEntityReferenceComponent(componentSchema.Schema.SchemaId, innerSlotId),
            ]);
        var outerPrefab = SceneAssetReaderTests.Fixtures.CreatePrefab(
            new AssetId(1),
            placementId: innerPlacementId,
            placementRootPoses: [SceneAssetReaderTests.Fixtures.CreateRootPose(rootEntityId)],
            slotBindings: [
                SceneAssetReaderTests.Fixtures.CreatePlacementSlotBinding(
                    innerSlotId,
                    SceneAssetReaderTests.Fixtures.CreateReferenceTarget(
                        SceneReferenceTargetKind.ExternalSlot,
                        externalSlotId: outerSlotId)),
            ],
            externalSlots: [
                CreateExternalSlot(outerSlotId, required: false, allowNull: true, []),
            ],
            placementPrefabAssetId: new AssetId(2));
        var scene = SceneAssetReaderTests.Fixtures.CreateAssetReferenceScene(
            new AssetId(3),
            outerPlacementId,
            rootPoses: [
                SceneAssetReaderTests.Fixtures.CreateRootPose(
                    rootEntityId,
                    placementPath: [innerPlacementId]),
            ],
            slotBindings: [
                SceneAssetReaderTests.Fixtures.CreatePlacementSlotBinding(
                    outerSlotId,
                    SceneAssetReaderTests.Fixtures.CreateReferenceTarget(
                        SceneReferenceTargetKind.ExternalSlot,
                        externalSlotId: rootSlotId)),
            ],
            externalSlots: [
                CreateExternalSlot(rootSlotId, required: false, allowNull: true, []),
            ]);
        var package = Package.CreatePackage(
            new Package.Entry(1, Package.CreateEnvelope(1, outerPrefab)),
            new Package.Entry(2, Package.CreateEnvelope(2, innerPrefab)),
            new Package.Entry(3, Package.CreateEnvelope(3, scene)));
        var packagePath = Package.WritePackage(package);

        try
        {
            var store = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(package), registration)).ConfigureAwait(false);

            try
            {
                using var expansion = await ScenePrefabExpansionPlanner.PrepareAsync(
                    store,
                    new AssetId(3),
                    SceneExpansionLimits.Default).ConfigureAwait(false);
                Assert.That(expansion.ExternalSlotBindings, Has.Length.EqualTo(3));
                Assert.That(expansion.ExternalSlotBindings[0].OccurrenceIndex, Is.Zero);
                Assert.That(expansion.ExternalSlotBindings[0].SlotId, Is.EqualTo(rootSlotId));
                Assert.That(expansion.ExternalSlotBindings[0].RootExternalSlotId, Is.EqualTo(rootSlotId));
                Assert.That(expansion.ExternalSlotBindings[1].OccurrenceIndex, Is.EqualTo(1));
                Assert.That(expansion.ExternalSlotBindings[1].SlotId, Is.EqualTo(outerSlotId));
                Assert.That(expansion.ExternalSlotBindings[1].RootExternalSlotId, Is.EqualTo(rootSlotId));
                Assert.That(expansion.ExternalSlotBindings[2].OccurrenceIndex, Is.EqualTo(2));
                Assert.That(expansion.ExternalSlotBindings[2].SlotId, Is.EqualTo(innerSlotId));
                Assert.That(expansion.ExternalSlotBindings[2].RootExternalSlotId, Is.EqualTo(rootSlotId));
                Assert.That(expansion.ReferenceBindings, Has.Length.EqualTo(1));
                Assert.That(expansion.ReferenceBindings[0].ResolvedExternalSlotBindingIndex, Is.EqualTo(2));
                Assert.That(expansion.ReferenceBindings[0].ResolvedTargetExpandedEntityIndex, Is.Null);
            }
            finally
            {
                await store.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Test]
    [SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "ActivateAsync detaches the expansion plan before returning; the awaited request owns it independently of PreparedScene.")]
    public async Task SceneLoaderActivatesAtTheWorldOwnerControlBoundary()
    {
        const int sceneAssetIdValue = 3;
        var schemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var scene = SceneAssetReaderTests.Fixtures.CreateScene(new AssetId(sceneAssetIdValue));
        var package = Package.CreatePackage(new Package.Entry(
            sceneAssetIdValue,
            Package.CreateEnvelope(sceneAssetIdValue, scene)));
        var packagePath = Package.WritePackage(package);

        try
        {
            var storeRegistration = SceneSupport.RegisterAssetCodecs(new AssetStoreRegistrationBuilder(schemas)).Build();
            var assets = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(package), storeRegistration)).ConfigureAwait(false);

            try
            {
                var worldRegistrationBuilder = new WorldRegistrationBuilder(schemas);
                _ = worldRegistrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
                using var world = World.Create(new WorldOptions(worldRegistrationBuilder.Build()));
                var loader = new SceneLoader(world, assets);
                var initialWorldBytes = world.GetWorldOwnedStateBytes();
                using var prepared = loader
                    .PrepareAsync(new AssetId(sceneAssetIdValue), new SceneLoadOptions())
                    .GetAwaiter()
                    .GetResult();
                var preparedWorldBytes = world.GetWorldOwnedStateBytes();
                Assert.That(preparedWorldBytes, Is.GreaterThan(initialWorldBytes));
                var activation = loader.ActivateAsync(prepared);

                Assert.That(activation.IsCompleted, Is.False);
                world.PumpControlBoundary();
                var receipt = await activation.ConfigureAwait(false);

                Assert.That(receipt.Status, Is.EqualTo(SceneActivationStatus.Activated));
                Assert.That(world.GetWorldOwnedStateBytes(), Is.GreaterThan(preparedWorldBytes));
                Assert.That(receipt.Instance, Is.Not.Null);
                Assert.That(receipt.CommittedStructuralRevision, Is.Not.Null);
                Assert.That(receipt.Diagnostics, Is.Empty);
                var instance = receipt.Instance
                    ?? throw new InvalidOperationException("An activated Scene receipt omitted its instance.");
                Assert.That(instance.TryGetEntity(SceneAssetReaderTests.Fixtures.RootEntityId, out var entity), Is.True);
                var address = new SceneEntityAddress([], SceneAssetReaderTests.Fixtures.RootEntityId);
                Assert.That(instance.TryGetEntity(address, out var addressedEntity), Is.True);
                Assert.That(addressedEntity, Is.EqualTo(entity));
                Assert.That(instance.TryGetProvenance(entity, out var provenance), Is.True);
                var source = provenance
                    ?? throw new InvalidOperationException("A live Scene Entity omitted its provenance.");
                Assert.That(source.SceneInstanceId, Is.EqualTo(instance.SceneInstanceId));
                Assert.That(source.SourceAssetId, Is.EqualTo(new AssetId(sceneAssetIdValue)));
                Assert.That(source.PlacementPath, Is.Empty);
                Assert.That(source.WorldIdentity, Is.EqualTo(world.GetEntityIdentity(entity)));
                Assert.That(world.IdentityLedger.TryFind(instance.SceneInstanceId, out var sceneOrdinal), Is.True);
                Assert.That(world.IdentityLedger.TryFind(world.GetEntityId(entity), out var entityOrdinal), Is.True);
                Assert.That(world.TryGet<SceneComponentDefinitions.LocalTransformComponent>(entity, out var localTransform), Is.True);
                Assert.That(localTransform.Position, Is.EqualTo(TerraFX.Numerics.Vector3.Zero));
                Assert.That(world.TryGet<SceneComponentDefinitions.RootPositionComponent>(entity, out _), Is.True);
                Assert.That(sceneOrdinal, Is.EqualTo(1));
                Assert.That(entityOrdinal, Is.EqualTo(2));

                var activeWorldBytes = world.GetWorldOwnedStateBytes();
                var unload = instance.UnloadAsync(HierarchyDestroyMode.DestroySubtree);
                Assert.That(unload.IsCompleted, Is.False);
                world.PumpControlBoundary();
                var unloadReceipt = unload.GetAwaiter().GetResult();
                Assert.That(unloadReceipt.Status, Is.EqualTo(SceneUnloadStatus.Unloaded));
                Assert.That(unloadReceipt.StructuralRevision, Is.Not.Null);
                var unloadedWorldBytes = world.GetWorldOwnedStateBytes();
                Assert.That(unloadedWorldBytes, Is.LessThan(activeWorldBytes));

                var abandoned = loader.PrepareAsync(
                    new AssetId(sceneAssetIdValue),
                    new SceneLoadOptions()).GetAwaiter().GetResult();
                Assert.That(world.GetWorldOwnedStateBytes(), Is.GreaterThan(unloadedWorldBytes));
                abandoned.Dispose();
                Assert.That(world.GetWorldOwnedStateBytes(), Is.EqualTo(unloadedWorldBytes));
                Assert.That(unloadReceipt.Diagnostics, Is.Empty);
                Assert.That(world.IdentityLedger.SceneInstanceCount, Is.Zero);
                Assert.That(instance.TryGetEntity(SceneAssetReaderTests.Fixtures.RootEntityId, out _), Is.False);
                Assert.That(instance.TryGetProvenance(entity, out _), Is.False);

                var alreadyUnloaded = instance.UnloadAsync(HierarchyDestroyMode.DetachChildrenKeepWorld);
                world.PumpControlBoundary();
                var alreadyUnloadedReceipt = alreadyUnloaded.GetAwaiter().GetResult();
                Assert.That(alreadyUnloadedReceipt.Status, Is.EqualTo(SceneUnloadStatus.AlreadyUnloaded));
                Assert.That(alreadyUnloadedReceipt.StructuralRevision, Is.Not.Null);
            }
            finally
            {
                await assets.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [TestCase(true, false)]
    [TestCase(false, false)]
    [TestCase(true, true)]
    [TestCase(false, true)]
    public async Task SceneLoaderActivationHandlesExplicitNullEntityReference(bool optional, bool hosted)
    {
        const int sceneAssetIdValue = 3;
        var componentSchema = CreateEntityReferenceComponentSchema(optional);
        var registeredSchemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var schemas = new CompiledContentSchemaRegistry([.. registeredSchemas.Schemas, componentSchema]);
        var externalSlotId = new Guid("30000000-0000-4000-8000-000000000091");
        var rootEntityId = SceneAssetReaderTests.Fixtures.RootEntityId;
        var scene = SceneAssetReaderTests.Fixtures.CreateScene(
            new AssetId(sceneAssetIdValue),
            externalSlots: [CreateExternalSlot(externalSlotId, required: false, allowNull: true, [])],
            references: [
                CreateEntityReferenceBindingToExternalSlot(
                    rootEntityId,
                    componentSchema.Schema,
                    externalSlotId),
            ],
            additionalEntityComponents: [
                CreateEntityReferenceComponent(
                    componentSchema.Schema.SchemaId,
                    externalSlotId,
                    isRequired: !optional),
            ]);
        var package = Package.CreatePackage(new Package.Entry(
            sceneAssetIdValue,
            Package.CreateEnvelope(sceneAssetIdValue, scene)));
        var packagePath = Package.WritePackage(package);

        try
        {
            DirectoryInfo? repositoryDirectory = new(AppContext.BaseDirectory);

            while (repositoryDirectory is not null
                && !File.Exists(Path.Combine(repositoryDirectory.FullName, "TerraFX.slnx")))
            {
                repositoryDirectory = repositoryDirectory.Parent;
            }

            var repositoryRoot = repositoryDirectory?.FullName
                ?? throw new DirectoryNotFoundException("The Scene activation public consumer could not locate the repository root.");
            var consumerProject = Path.Combine(
                repositoryRoot,
                "tests",
                "TerraFX.Runtime.ComponentConsumer",
                "TerraFX.Runtime.ComponentConsumer.csproj");
            var sceneActivationConsumer = Environment.GetEnvironmentVariable("TERRAFX_SCENE_ACTIVATION_CONSUMER");

            if (sceneActivationConsumer is not null
                && !Path.IsPathFullyQualified(sceneActivationConsumer))
            {
                throw new InvalidOperationException(
                    "TERRAFX_SCENE_ACTIVATION_CONSUMER must be an absolute executable path.");
            }

            var startInfo = new ProcessStartInfo(sceneActivationConsumer ?? "dotnet") {
                WorkingDirectory = repositoryRoot,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };

            if (sceneActivationConsumer is null)
            {
#if DEBUG
                const string configuration = "Debug";
#else
                const string configuration = "Release";
#endif
                startInfo.ArgumentList.Add("run");
                startInfo.ArgumentList.Add("--project");
                startInfo.ArgumentList.Add(consumerProject);
                startInfo.ArgumentList.Add("--configuration");
                startInfo.ArgumentList.Add(configuration);
                startInfo.ArgumentList.Add("--no-build");
                startInfo.ArgumentList.Add("--");
            }

            startInfo.ArgumentList.Add(hosted ? "--scene-activation-host" : "--scene-activation");
            startInfo.ArgumentList.Add(packagePath);
            startInfo.ArgumentList.Add(optional ? "optional" : "required");
            startInfo.ArgumentList.Add(externalSlotId.ToString("N"));
            startInfo.ArgumentList.Add(rootEntityId.ToString("N"));
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The Scene activation public consumer did not start.");
            var standardOutputTask = process.StandardOutput.ReadToEndAsync();
            var standardErrorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().ConfigureAwait(false);
            var standardOutput = await standardOutputTask.ConfigureAwait(false);
            var standardError = await standardErrorTask.ConfigureAwait(false);

            Assert.That(
                process.ExitCode,
                Is.Zero,
                $"The Scene activation public consumer failed.{Environment.NewLine}{standardOutput}{standardError}");
            Assert.That(
                standardOutput,
                Does.Contain(optional ? "optional-null activation passed" : "required-null rejection passed"));
        }
        finally
        {
            await DeleteTemporaryPackageAsync(packagePath).ConfigureAwait(false);
        }
    }

    [Test]
    [SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "ActivateAsync detaches the expansion plan before returning; the awaited request owns it independently of PreparedScene.")]
    public async Task SceneLoaderAllowsExplicitNullForOptionalRootSlot()
    {
        const int sceneAssetIdValue = 3;
        var schemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var slotId = new Guid("30000000-0000-4000-8000-000000000091");
        var scene = SceneAssetReaderTests.Fixtures.CreateScene(
            new AssetId(sceneAssetIdValue),
            externalSlots: [CreateExternalSlot(slotId, required: false, allowNull: true, [])]);
        var package = Package.CreatePackage(new Package.Entry(
            sceneAssetIdValue,
            Package.CreateEnvelope(sceneAssetIdValue, scene)));
        var packagePath = Package.WritePackage(package);

        try
        {
            var storeRegistration = SceneSupport.RegisterAssetCodecs(new AssetStoreRegistrationBuilder(schemas)).Build();
            var assets = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(package), storeRegistration)).ConfigureAwait(false);

            try
            {
                var worldRegistrationBuilder = new WorldRegistrationBuilder(schemas);
                _ = worldRegistrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
                using var world = World.Create(new WorldOptions(worldRegistrationBuilder.Build()));
                var loader = new SceneLoader(world, assets);
                var options = new SceneLoadOptions {
                    ExternalBindings = [new SceneExternalBinding(slotId, target: null)],
                };
                using var prepared = loader
                    .PrepareAsync(new AssetId(sceneAssetIdValue), options)
                    .GetAwaiter()
                    .GetResult();

                Assert.That(prepared.AssetId, Is.EqualTo(new AssetId(sceneAssetIdValue)));
                var activation = loader.ActivateAsync(prepared);
                world.PumpControlBoundary();
                var receipt = await activation.ConfigureAwait(false);
                Assert.That(receipt.Status, Is.EqualTo(SceneActivationStatus.Activated));
                var instance = receipt.Instance
                    ?? throw new InvalidOperationException("The explicit-null Scene activation omitted its instance.");
                _ = world.Step();
                using var captureLease = world.AcquirePersistenceCaptureLease();
                var externalBindings = instance.GetExternalBindingCaptureRows(captureLease);
                Assert.That(externalBindings.Length, Is.EqualTo(1));
                Assert.That(externalBindings[0].BindingId, Is.EqualTo(slotId));
                Assert.That(externalBindings[0].TargetEntityId, Is.Null);
                Assert.That(
                    ScenePersistenceExternalBindingArrayWriter.Measure(
                        externalBindings,
                        EbinReadLimits.Foundation,
                        out var recordStorageBytes,
                        out var candidateBytes,
                        out var payloadBytes,
                        out var diagnostic),
                    Is.EqualTo(EbinWriteStatus.Complete),
                    diagnostic?.Category);
                Assert.That(diagnostic, Is.Null);
                var slices = new EbinPayloadSlice[externalBindings.Length];
                var recordStorage = new byte[recordStorageBytes];
                var candidateStorage = new byte[candidateBytes];
                var payload = new byte[payloadBytes];
                Assert.That(
                    ScenePersistenceExternalBindingArrayWriter.Write(
                        externalBindings,
                        EbinReadLimits.Foundation,
                        slices,
                        recordStorage,
                        candidateStorage,
                        payload,
                        out var payloadBytesWritten,
                        out diagnostic),
                    Is.EqualTo(EbinWriteStatus.Complete),
                    diagnostic?.Category);
                Assert.That(diagnostic, Is.Null);
                Assert.That(payloadBytesWritten, Is.EqualTo(payloadBytes));
                Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(payload), Is.EqualTo(1));
            }
            finally
            {
                await assets.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Test]
    [SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "The canceled request releases its detached plan at the owner boundary before PreparedScene disposal.")]
    public async Task SceneLoaderCancelsBeforeOwnerBoundaryWithoutConsumingIdentity()
    {
        const int sceneAssetIdValue = 3;
        var schemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var scene = SceneAssetReaderTests.Fixtures.CreateScene(new AssetId(sceneAssetIdValue));
        var package = Package.CreatePackage(new Package.Entry(
            sceneAssetIdValue,
            Package.CreateEnvelope(sceneAssetIdValue, scene)));
        var packagePath = Package.WritePackage(package);

        try
        {
            var storeRegistration = SceneSupport.RegisterAssetCodecs(new AssetStoreRegistrationBuilder(schemas)).Build();
            var assets = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(package), storeRegistration)).ConfigureAwait(false);

            try
            {
                var worldRegistrationBuilder = new WorldRegistrationBuilder(schemas);
                _ = worldRegistrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
                using var world = World.Create(new WorldOptions(worldRegistrationBuilder.Build()));
                var loader = new SceneLoader(world, assets);
                using var prepared = loader
                    .PrepareAsync(new AssetId(sceneAssetIdValue), new SceneLoadOptions())
                    .GetAwaiter()
                    .GetResult();
                using var cancellation = new CancellationTokenSource();
                var activation = loader.ActivateAsync(prepared, cancellation.Token);
                await cancellation.CancelAsync().ConfigureAwait(false);

                world.PumpControlBoundary();

                Assert.That(world.IdentityLedger.SceneInstanceCount, Is.Zero);
                Assert.That(world.IdentityLedger.AssignmentCount, Is.Zero);
                _ = Assert.ThrowsAsync<TaskCanceledException>(async () =>
                    await activation.ConfigureAwait(false));
            }
            finally
            {
                await assets.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Test]
    public async Task SceneLoaderPreparesAgainstExactWorldSchemaRegistry()
    {
        const int sceneAssetIdValue = 3;
        var schemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var package = Package.CreatePackage(
            new Package.Entry(
                sceneAssetIdValue,
                Package.CreateEnvelope(
                    sceneAssetIdValue,
                    SceneAssetReaderTests.Fixtures.CreateScene(new AssetId(sceneAssetIdValue)))));
        var packagePath = Package.WritePackage(package);

        try
        {
            var storeRegistration = SceneSupport.RegisterAssetCodecs(new AssetStoreRegistrationBuilder(schemas)).Build();
            var assets = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(package), storeRegistration)).ConfigureAwait(false);

            try
            {
                var worldRegistrationBuilder = new WorldRegistrationBuilder(schemas);
                _ = worldRegistrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
                using var world = World.Create(new WorldOptions(worldRegistrationBuilder.Build()));
                using var prepared = new SceneLoader(world, assets)
                    .PrepareAsync(new AssetId(sceneAssetIdValue), new SceneLoadOptions())
                    .GetAwaiter()
                    .GetResult();

                Assert.That(prepared.AssetId, Is.EqualTo(new AssetId(sceneAssetIdValue)));
            }
            finally
            {
                await assets.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Test]
    [SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "ActivateAsync detaches the expansion plan before returning; the awaited request owns it independently of PreparedScene.")]
    public async Task SceneLoaderRejectsNonliveRootBindingsAtActivationBoundary()
    {
        const int sceneAssetIdValue = 3;
        var schemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var slotId = new Guid("30000000-0000-4000-8000-000000000093");
        var scene = SceneAssetReaderTests.Fixtures.CreateScene(
            new AssetId(sceneAssetIdValue),
            externalSlots: [CreateExternalSlot(slotId, required: false, allowNull: false, [])]);
        var package = Package.CreatePackage(new Package.Entry(
            sceneAssetIdValue,
            Package.CreateEnvelope(sceneAssetIdValue, scene)));
        var packagePath = Package.WritePackage(package);

        try
        {
            var storeRegistration = SceneSupport.RegisterAssetCodecs(new AssetStoreRegistrationBuilder(schemas)).Build();
            var assets = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(package), storeRegistration)).ConfigureAwait(false);

            try
            {
                var worldRegistrationBuilder = new WorldRegistrationBuilder(schemas);
                _ = worldRegistrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
                using var world = World.Create(new WorldOptions(worldRegistrationBuilder.Build()));
                var staleTarget = world.CreateEntity(new EntityDescription());
                world.DestroyEntity(staleTarget, HierarchyDestroyMode.DetachChildrenKeepWorld);
                var initialAssignmentCount = world.IdentityLedger.AssignmentCount;
                var loader = new SceneLoader(world, assets);
                using var prepared = loader
                    .PrepareAsync(
                        new AssetId(sceneAssetIdValue),
                        new SceneLoadOptions {
                            ExternalBindings = [new SceneExternalBinding(slotId, staleTarget)],
                        })
                    .GetAwaiter()
                    .GetResult();
                var activation = loader.ActivateAsync(prepared);

                world.PumpControlBoundary();
                var receipt = await activation.ConfigureAwait(false);

                Assert.That(receipt.Status, Is.EqualTo(SceneActivationStatus.Rejected));
                Assert.That(receipt.Instance, Is.Null);
                Assert.That(receipt.CommittedStructuralRevision, Is.Null);
                Assert.That(receipt.Diagnostics, Is.Not.Empty);
                Assert.That(world.IdentityLedger.SceneInstanceCount, Is.Zero);
                Assert.That(world.IdentityLedger.AssignmentCount, Is.EqualTo(initialAssignmentCount));
            }
            finally
            {
                await assets.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Test]
    [SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "Accepted activation requests own detached plans; the rejected queue attempt retains its PreparedScene for retry.")]
    public async Task SceneLoaderRejectsOwnerQueueFullWithoutConsumingPreparedScene()
    {
        const int sceneAssetIdValue = 3;
        var schemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var scene = SceneAssetReaderTests.Fixtures.CreateScene(new AssetId(sceneAssetIdValue));
        var package = Package.CreatePackage(new Package.Entry(
            sceneAssetIdValue,
            Package.CreateEnvelope(sceneAssetIdValue, scene)));
        var packagePath = Package.WritePackage(package);

        try
        {
            var storeRegistration = SceneSupport.RegisterAssetCodecs(new AssetStoreRegistrationBuilder(schemas)).Build();
            var assets = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(package), storeRegistration)).ConfigureAwait(false);

            try
            {
                var worldRegistrationBuilder = new WorldRegistrationBuilder(schemas);
                _ = worldRegistrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
                var worldOptions = new WorldOptions(worldRegistrationBuilder.Build()) {
                    MaximumPendingOwnerRequests = 1,
                };
                using var world = World.Create(worldOptions);
                var loader = new SceneLoader(world, assets);
                using var first = loader
                    .PrepareAsync(new AssetId(sceneAssetIdValue), new SceneLoadOptions())
                    .GetAwaiter()
                    .GetResult();
                using var second = loader
                    .PrepareAsync(new AssetId(sceneAssetIdValue), new SceneLoadOptions())
                    .GetAwaiter()
                    .GetResult();
                var firstActivation = loader.ActivateAsync(first);
                var refusedActivation = loader.ActivateAsync(second);
                var refusedReceipt = await refusedActivation.ConfigureAwait(false);

                Assert.That(refusedReceipt.Status, Is.EqualTo(SceneActivationStatus.Rejected));
                Assert.That(refusedReceipt.Diagnostics, Is.Not.Empty);
                Assert.That(firstActivation.IsCompleted, Is.False);

                world.PumpControlBoundary();
                var firstReceipt = firstActivation.GetAwaiter().GetResult();
                Assert.That(firstReceipt.Status, Is.EqualTo(SceneActivationStatus.Activated));

                var retriedActivation = loader.ActivateAsync(second);
                world.PumpControlBoundary();
                var retriedReceipt = retriedActivation.GetAwaiter().GetResult();
                Assert.That(retriedReceipt.Status, Is.EqualTo(SceneActivationStatus.Activated));
                Assert.That(world.IdentityLedger.SceneInstanceCount, Is.EqualTo(2));
            }
            finally
            {
                await assets.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Test]
    public async Task SceneLoaderRejectsUnknownRootExternalBinding()
    {
        const int sceneAssetIdValue = 3;
        var schemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var package = Package.CreatePackage(
            new Package.Entry(
                sceneAssetIdValue,
                Package.CreateEnvelope(
                    sceneAssetIdValue,
                    SceneAssetReaderTests.Fixtures.CreateScene(new AssetId(sceneAssetIdValue)))));
        var packagePath = Package.WritePackage(package);

        try
        {
            var storeRegistration = SceneSupport.RegisterAssetCodecs(new AssetStoreRegistrationBuilder(schemas)).Build();
            var assets = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(package), storeRegistration)).ConfigureAwait(false);

            try
            {
                var worldRegistrationBuilder = new WorldRegistrationBuilder(schemas);
                _ = worldRegistrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
                using var world = World.Create(new WorldOptions(worldRegistrationBuilder.Build()));
                var options = new SceneLoadOptions {
                    ExternalBindings = [new SceneExternalBinding(Guid.NewGuid(), target: null)],
                };
                _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
                    await new SceneLoader(world, assets)
                        .PrepareAsync(new AssetId(sceneAssetIdValue), options)
                        .ConfigureAwait(false));
            }
            finally
            {
                await assets.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Test]
    public async Task SceneLoaderRejectsWorldMissingRequiredRootSlot()
    {
        const int sceneAssetIdValue = 3;
        var schemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var scene = SceneAssetReaderTests.Fixtures.CreateScene(
            new AssetId(sceneAssetIdValue),
            externalSlots: [
                CreateExternalSlot(
                    new Guid("30000000-0000-4000-8000-000000000092"),
                    required: true,
                    allowNull: false,
                    []),
            ]);
        var package = Package.CreatePackage(new Package.Entry(
            sceneAssetIdValue,
            Package.CreateEnvelope(sceneAssetIdValue, scene)));
        var packagePath = Package.WritePackage(package);

        try
        {
            var storeRegistration = SceneSupport.RegisterAssetCodecs(new AssetStoreRegistrationBuilder(schemas)).Build();
            var assets = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(package), storeRegistration)).ConfigureAwait(false);

            try
            {
                var worldRegistrationBuilder = new WorldRegistrationBuilder(schemas);
                _ = worldRegistrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
                using var world = World.Create(new WorldOptions(worldRegistrationBuilder.Build()));
                _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
                    await new SceneLoader(world, assets)
                        .PrepareAsync(new AssetId(sceneAssetIdValue), new SceneLoadOptions())
                        .ConfigureAwait(false));
            }
            finally
            {
                await assets.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Test]
    public async Task SceneLoaderRejectsWorldSchemaRegistryMismatch()
    {
        const int sceneAssetIdValue = 3;
        var assetSchemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var worldSchemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var package = Package.CreatePackage(
            new Package.Entry(
                sceneAssetIdValue,
                Package.CreateEnvelope(
                    sceneAssetIdValue,
                    SceneAssetReaderTests.Fixtures.CreateScene(new AssetId(sceneAssetIdValue)))));
        var packagePath = Package.WritePackage(package);

        try
        {
            var storeRegistration = SceneSupport.RegisterAssetCodecs(new AssetStoreRegistrationBuilder(assetSchemas)).Build();
            var assets = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(package), storeRegistration)).ConfigureAwait(false);

            try
            {
                var worldRegistrationBuilder = new WorldRegistrationBuilder(worldSchemas);
                _ = worldRegistrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
                using var world = World.Create(new WorldOptions(worldRegistrationBuilder.Build()));
                _ = Assert.ThrowsAsync<ArgumentException>(async () =>
                    await new SceneLoader(world, assets)
                        .PrepareAsync(new AssetId(sceneAssetIdValue), new SceneLoadOptions())
                        .ConfigureAwait(false));
            }
            finally
            {
                await assets.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Test]
    [SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "ActivateAsync detaches the expansion plan before returning; the awaited request owns it independently of PreparedScene.")]
    public async Task SceneLoaderResolvesNestedAddressesAndProvenance()
    {
        const int sceneAssetIdValue = 3;
        const int prefabAssetIdValue = 1;
        const int atlasAssetIdValue = 2;
        const int textureAssetIdValue = 5;
        var schemas = CreateRendererRegistry();
        var rootEntityId = SceneAssetReaderTests.Fixtures.RootEntityId;
        var childEntityId = new Guid("10000000-0000-4000-8000-000000000002");
        var placementId = new Guid("40000000-0000-4000-8000-000000000002");
        var boundExternalSlotId = new Guid("30000000-0000-4000-8000-000000000091");
        var externalSlotId = new Guid("30000000-0000-4000-8000-000000000092");
        var frameId = new Guid("30000000-0000-4000-8000-000000000093");
        var spriteRecord = CreateRenderSpriteRecord(atlasAssetIdValue, frameId, materialAssetId: null);
        var textureRecord = CreateRenderAtlasPageTextureRecord(17);
        var atlasRecord = CreateRenderAtlasRecord(
            textureAssetIdValue,
            CreateRenderAtlasFrameRecord(frameId, page: 0, x: 4, y: 4, width: 4, height: 4));
        var scene = SceneAssetReaderTests.Fixtures.CreateSceneWithChildPlacement(
            new AssetId(sceneAssetIdValue),
            rootEntityId,
            placementId,
            SceneAssetReaderTests.Fixtures.CreateRootPose(childEntityId, includeRootPosition: false),
            externalSlots: [
                CreateExternalSlot(boundExternalSlotId, required: false, allowNull: true, []),
                CreateExternalSlot(externalSlotId, required: false, allowNull: true, []),
            ],
            additionalEntityComponents: [spriteRecord]);
        var prefab = SceneAssetReaderTests.Fixtures.CreatePrefab(
            new AssetId(prefabAssetIdValue),
            entityId: childEntityId);
        var package = Package.CreatePackage(
            new Package.Entry(prefabAssetIdValue, Package.CreateEnvelope(prefabAssetIdValue, prefab)),
            new Package.Entry(atlasAssetIdValue, Package.CreateEnvelope(atlasAssetIdValue, atlasRecord)),
            new Package.Entry(sceneAssetIdValue, Package.CreateEnvelope(sceneAssetIdValue, scene)),
            new Package.Entry(textureAssetIdValue, Package.CreateEnvelope(textureAssetIdValue, textureRecord)));
        var packagePath = Package.WritePackage(package);

        try
        {
            var storeRegistrationBuilder = new AssetStoreRegistrationBuilder(schemas);
            _ = SceneSupport.RegisterAssetCodecs(storeRegistrationBuilder);
            _ = Renderer.RegisterAssetCodecs(storeRegistrationBuilder);
            var storeRegistration = storeRegistrationBuilder.Build();
            var assets = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(package), storeRegistration)).ConfigureAwait(false);

            try
            {
                var worldRegistrationBuilder = new WorldRegistrationBuilder(schemas);
                var sceneSupport = SceneSupport.CreateRegistration();
                _ = worldRegistrationBuilder.RegisterSceneSupport(sceneSupport);
                _ = worldRegistrationBuilder.RegisterCpuExtractionSupport(Renderer.CreateCpuExtractionSupportRegistration());
                var worldRegistration = worldRegistrationBuilder.Build();
                using var world = World.Create(new WorldOptions(worldRegistration));
                var externalTarget = world.CreateEntity(new EntityDescription());
                var externalTargetId = world.GetEntityId(externalTarget);
                var loader = new SceneLoader(world, assets);
                var options = new SceneLoadOptions {
                    ExternalBindings = [
                        new SceneExternalBinding(boundExternalSlotId, target: externalTarget),
                        new SceneExternalBinding(externalSlotId, target: null),
                    ],
                };
                using var prepared = loader
                    .PrepareAsync(new AssetId(sceneAssetIdValue), options)
                    .GetAwaiter()
                    .GetResult();
                var activation = loader.ActivateAsync(prepared);
                world.PumpControlBoundary();
                var receipt = await activation.ConfigureAwait(false);
                var instance = receipt.Instance
                    ?? throw new InvalidOperationException("The nested Scene activation did not return its instance.");

                Assert.That(receipt.Status, Is.EqualTo(SceneActivationStatus.Activated));
                world.DestroyEntity(externalTarget, HierarchyDestroyMode.DetachChildrenKeepWorld);
                _ = world.Step();
                uint[] expectedSceneAssetIds = [checked((uint)sceneAssetIdValue), checked((uint)prefabAssetIdValue)];
                var rootOccurrence = SceneOccurrenceIdentity.CreateRootOccurrence(new AssetId(sceneAssetIdValue));
                var childOccurrence = SceneOccurrenceIdentity.CreatePrefabOccurrence(
                    rootOccurrence,
                    placementId,
                    new AssetId(prefabAssetIdValue));
                _ = world.Step();
                Assert.That(
                    CaptureSceneAssetIds(world),
                    Is.EqualTo(expectedSceneAssetIds));
                using (var captureLease = world.AcquirePersistenceCaptureLease())
                {
                    var sceneRegistry = sceneSupport.GetParticipant(world) as SceneWorldRegistry
                        ?? throw new InvalidOperationException("The active Scene participant is not a Scene registry.");
                    var activeSceneInstances = new SceneInstance[sceneRegistry.GetPersistenceInstanceCount(captureLease)];
                    sceneRegistry.CopyPersistenceInstances(captureLease, activeSceneInstances);
                    Assert.That(activeSceneInstances, Has.Length.EqualTo(1));
                    Assert.That(activeSceneInstances[0], Is.SameAs(instance));
                    Assert.That(instance.OccurrenceCount, Is.EqualTo(2));
                    var capturedChildAddress = new SceneEntityAddress([placementId], childEntityId);
                    Assert.That(instance.TryGetEntity(rootEntityId, out var capturedRootEntity), Is.True);
                    Assert.That(instance.TryGetEntity(capturedChildAddress, out var capturedChildEntity), Is.True);
                    Assert.That(
                        world.TryGet<RenderComponentDefinitions.SpriteComponent>(capturedRootEntity, out var capturedSprite),
                        Is.True);
                    Assert.That(capturedSprite.AtlasAssetId, Is.EqualTo(atlasAssetIdValue));
                    Assert.That(world.TryGetLiveEntityIdentityOrdinal(capturedRootEntity, out var rootAssignmentOrdinal), Is.True);
                    Assert.That(world.TryGetLiveEntityIdentityOrdinal(capturedChildEntity, out var childAssignmentOrdinal), Is.True);
                    Guid[] expectedLiveEntityIds = (rootAssignmentOrdinal < childAssignmentOrdinal)
                        ? [world.GetEntityId(capturedRootEntity), world.GetEntityId(capturedChildEntity)]
                        : [world.GetEntityId(capturedChildEntity), world.GetEntityId(capturedRootEntity)];
                    var liveEntityCount = instance.GetPersistenceEntityCount(captureLease);
                    var capturedEntityIds = new Guid[liveEntityCount];
                    instance.CopyPersistenceEntityIds(captureLease, capturedEntityIds);
                    Assert.That(capturedEntityIds, Is.EqualTo(expectedLiveEntityIds));
                    Assert.That(capturedEntityIds[0], Is.Not.EqualTo(capturedEntityIds[1]));

                    var occurrenceRecords = new byte[instance.OccurrenceCount][];

                    for (var occurrenceIndex = 0; occurrenceIndex < instance.OccurrenceCount; occurrenceIndex++)
                    {
                        Assert.That(
                            instance.MeasurePersistenceOccurrenceRecord(
                                captureLease,
                                occurrenceIndex,
                                out var recordBytes,
                                out var occurrenceDiagnostic),
                            Is.EqualTo(EbinWriteStatus.Complete),
                            occurrenceDiagnostic?.Category);
                        Assert.That(occurrenceDiagnostic, Is.Null);
                        var candidateStorage = new byte[recordBytes];
                        var destination = new byte[recordBytes];
                        Assert.That(
                            instance.WritePersistenceOccurrenceRecord(
                                captureLease,
                                occurrenceIndex,
                                candidateStorage,
                                destination,
                                out var bytesWritten,
                                out occurrenceDiagnostic),
                            Is.EqualTo(EbinWriteStatus.Complete),
                            occurrenceDiagnostic?.Category);
                        Assert.That(occurrenceDiagnostic, Is.Null);
                        Assert.That(bytesWritten, Is.EqualTo(recordBytes));
                        occurrenceRecords[occurrenceIndex] = destination;
                        var assertions = (occurrenceIndex == 0)
                            ? new SceneOccurrenceRecordAssertions(
                                rootOccurrence,
                                checked((uint)sceneAssetIdValue),
                                new ContentSchemaIdentityAssignment("native/scene").SchemaId,
                                placementPathLength: 0,
                                placementPathTail: null,
                                parentOccurrenceId: null,
                                placementId: null)
                            : new SceneOccurrenceRecordAssertions(
                                childOccurrence,
                                checked((uint)prefabAssetIdValue),
                                new ContentSchemaIdentityAssignment("native/prefab").SchemaId,
                                placementPathLength: 1,
                                placementPathTail: placementId,
                                parentOccurrenceId: rootOccurrence,
                                placementId: placementId);
                        Assert.That(
                            EbinStructuralReader.Read(
                                destination,
                                EbinReadLimits.Foundation,
                                EbinReadPolicy.Canonical,
                                ref assertions,
                                out var bytesConsumed,
                                out var readDiagnostic),
                            Is.EqualTo(EbinReadStatus.Complete));
                        Assert.That(readDiagnostic, Is.Null);
                        Assert.That(assertions.IsMatch, Is.True);
                        Assert.That(bytesConsumed, Is.EqualTo(destination.Length));
                    }

                    var occurrenceArrayPayload = SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayPayload(occurrenceRecords);
                    Assert.That(liveEntityCount, Is.EqualTo(2));

                    var bindingRecords = new byte[liveEntityCount][];
                    var bindingRecordIndex = 0;

                    for (var expandedEntityIndex = 0; expandedEntityIndex < instance.EntityCount; expandedEntityIndex++)
                    {
                        if (!instance.TryGetPersistenceEntityId(captureLease, expandedEntityIndex, out _))
                        {
                            continue;
                        }

                        Assert.That(
                            instance.MeasurePersistenceEntityBindingRecord(
                                captureLease,
                                expandedEntityIndex,
                                out var recordBytes,
                                out var bindingDiagnostic),
                            Is.EqualTo(EbinWriteStatus.Complete),
                            bindingDiagnostic?.Category);
                        Assert.That(bindingDiagnostic, Is.Null);
                        var candidateStorage = new byte[recordBytes];
                        var destination = new byte[recordBytes];
                        Assert.That(
                            instance.WritePersistenceEntityBindingRecord(
                                captureLease,
                                expandedEntityIndex,
                                candidateStorage,
                                destination,
                                out var bytesWritten,
                                out bindingDiagnostic),
                            Is.EqualTo(EbinWriteStatus.Complete),
                            bindingDiagnostic?.Category);
                        Assert.That(bindingDiagnostic, Is.Null);
                        Assert.That(bytesWritten, Is.EqualTo(recordBytes));
                        bindingRecords[bindingRecordIndex++] = destination;
                        var bindingAssertions = new SceneEntityBindingRecordAssertions();
                        Assert.That(
                            EbinStructuralReader.Read(
                                destination,
                                EbinReadLimits.Foundation,
                                EbinReadPolicy.Canonical,
                                ref bindingAssertions,
                                out var bindingBytesConsumed,
                                out var bindingReadDiagnostic),
                            Is.EqualTo(EbinReadStatus.Complete));
                        Assert.That(bindingReadDiagnostic, Is.Null);
                        Assert.That(bindingAssertions.IsMatch, Is.True);
                        Assert.That(bindingBytesConsumed, Is.EqualTo(destination.Length));
                    }

                    Assert.That(bindingRecordIndex, Is.EqualTo(bindingRecords.Length));
                    var bindingsArrayPayload = SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayPayload(bindingRecords);
                    var externalBindings = instance.GetExternalBindingCaptureRows(captureLease);
                    Assert.That(externalBindings.Length, Is.EqualTo(2));
                    Assert.That(externalBindings[0].BindingId, Is.EqualTo(boundExternalSlotId));
                    Assert.That(externalBindings[0].TargetEntityId, Is.EqualTo(externalTargetId));
                    Assert.That(externalBindings[1].BindingId, Is.EqualTo(externalSlotId));
                    Assert.That(externalBindings[1].TargetEntityId, Is.Null);
                    Assert.That(
                        ScenePersistenceExternalBindingArrayWriter.Measure(
                            externalBindings,
                            EbinReadLimits.Foundation,
                            out var externalRecordStorageBytes,
                            out var externalCandidateBytes,
                            out var externalArrayPayloadBytes,
                            out var externalDiagnostic),
                        Is.EqualTo(EbinWriteStatus.Complete),
                        externalDiagnostic?.Category);
                    Assert.That(externalDiagnostic, Is.Null);
                    var externalSlices = new EbinPayloadSlice[externalBindings.Length];
                    var externalRecordStorage = new byte[externalRecordStorageBytes];
                    var externalCandidateStorage = new byte[externalCandidateBytes];
                    var externalBindingsArrayPayload = new byte[externalArrayPayloadBytes];
                    Assert.That(
                        ScenePersistenceExternalBindingArrayWriter.Write(
                            externalBindings,
                            EbinReadLimits.Foundation,
                            externalSlices,
                            externalRecordStorage,
                            externalCandidateStorage,
                            externalBindingsArrayPayload,
                            out var externalPayloadBytesWritten,
                            out externalDiagnostic),
                        Is.EqualTo(EbinWriteStatus.Complete),
                        externalDiagnostic?.Category);
                    Assert.That(externalDiagnostic, Is.Null);
                    Assert.That(externalPayloadBytesWritten, Is.EqualTo(externalArrayPayloadBytes));

                    var entityIdsArrayPayload = new byte[checked(32 + (capturedEntityIds.Length * 16))];
                    Assert.That(
                        EbinPayloadWriter.WriteFixedArray(
                            capturedEntityIds.AsSpan(),
                            EbinReadLimits.Foundation,
                            entityIdsArrayPayload,
                            out var entityIdsArrayPayloadBytes,
                            out var entityIdsDiagnostic),
                        Is.EqualTo(EbinWriteStatus.Complete),
                        entityIdsDiagnostic?.Category);
                    Assert.That(entityIdsDiagnostic, Is.Null);
                    entityIdsArrayPayload = entityIdsArrayPayload[..entityIdsArrayPayloadBytes];

                    var plan = instance.GetPersistencePlan(captureLease);
                    Assert.That(
                        ScenePersistenceOccurrenceWriter.MeasureSourceAssetReference(
                            plan,
                            0,
                            EbinReadLimits.Foundation,
                            out var sourceRecordBytes,
                            out var sourceDiagnostic),
                        Is.EqualTo(EbinWriteStatus.Complete),
                        sourceDiagnostic?.Category);
                    Assert.That(sourceDiagnostic, Is.Null);
                    var rootPayloadStorage = new byte[checked(
                        16
                        + sourceRecordBytes
                        + entityIdsArrayPayload.Length
                        + bindingsArrayPayload.Length
                        + externalBindingsArrayPayload.Length
                        + sizeof(long)
                        + occurrenceArrayPayload.Length)];
                    Assert.That(
                        ScenePersistenceSceneInstanceWriter.Measure(
                            instance,
                            captureLease,
                            entityIdsArrayPayload,
                            bindingsArrayPayload,
                            externalBindingsArrayPayload,
                            occurrenceArrayPayload,
                            EbinReadLimits.Foundation,
                            rootPayloadStorage,
                            out var instanceWorkspace,
                            out var instanceDiagnostic),
                        Is.EqualTo(EbinWriteStatus.Complete),
                        instanceDiagnostic?.Category);
                    Assert.That(instanceDiagnostic, Is.Null);
                    Assert.That(instanceWorkspace.SourceRecordBytes, Is.EqualTo(sourceRecordBytes));
                    Assert.That(captureLease.TryReserveAdmissionBytes(instanceWorkspace.WorkspaceBytes, out var reservation), Is.True);
                    using (reservation)
                    {
                        var instanceCandidateStorage = new byte[instanceWorkspace.RecordBytes];
                        var instanceRecord = new byte[instanceWorkspace.RecordBytes];
                        Assert.That(
                            ScenePersistenceSceneInstanceWriter.Write(
                                instance,
                                captureLease,
                                instanceWorkspace,
                                entityIdsArrayPayload,
                                bindingsArrayPayload,
                                externalBindingsArrayPayload,
                                occurrenceArrayPayload,
                                EbinReadLimits.Foundation,
                                rootPayloadStorage,
                                instanceCandidateStorage,
                                instanceRecord,
                                out var instanceBytesWritten,
                                out instanceDiagnostic),
                            Is.EqualTo(EbinWriteStatus.Complete),
                            instanceDiagnostic?.Category);
                        Assert.That(instanceDiagnostic, Is.Null);
                        Assert.That(instanceBytesWritten, Is.EqualTo(instanceRecord.Length));
                        var instanceAssertions = new SceneInstanceRecordAssertions(
                            instance.SceneInstanceId,
                            checked((uint)sceneAssetIdValue),
                            new ContentSchemaIdentityAssignment("native/scene").SchemaId,
                            capturedEntityIds,
                            bindingsArrayPayload,
                            externalBindingsArrayPayload,
                            externalBindings.Length,
                            occurrenceArrayPayload,
                            instance.OccurrenceCount,
                            instance.GetPersistenceAssignmentOrdinal(captureLease));
                        Assert.That(
                            EbinStructuralReader.Read(
                                instanceRecord,
                                EbinReadLimits.Foundation,
                                EbinReadPolicy.Canonical,
                                ref instanceAssertions,
                                out var instanceBytesConsumed,
                                out var instanceReadDiagnostic),
                            Is.EqualTo(EbinReadStatus.Complete));
                        Assert.That(instanceReadDiagnostic, Is.Null);
                        Assert.That(instanceAssertions.IsMatch, Is.True);
                        Assert.That(instanceBytesConsumed, Is.EqualTo(instanceRecord.Length));

                        var sceneInstanceRecordSlices = new EbinPayloadSlice[activeSceneInstances.Length];
                        sceneInstanceRecordSlices[0] = new EbinPayloadSlice(0, instanceRecord.Length);
                        var sceneInstanceOrderScratch = new int[activeSceneInstances.Length];
                        var orderedSceneInstanceRecords = new EbinPayloadSlice[activeSceneInstances.Length];
                        Assert.That(
                            ScenePersistenceSceneInstanceArrayWriter.Measure(
                                activeSceneInstances,
                                sceneInstanceRecordSlices,
                                instanceRecord,
                                EbinReadLimits.Foundation,
                                sceneInstanceOrderScratch,
                                orderedSceneInstanceRecords,
                                out var sceneInstanceArrayWorkspace,
                                out instanceDiagnostic),
                            Is.EqualTo(EbinWriteStatus.Complete),
                            instanceDiagnostic?.Category);
                        Assert.That(instanceDiagnostic, Is.Null);
                        Assert.That(
                            captureLease.TryReserveAdmissionBytes(
                                checked(sceneInstanceArrayWorkspace.WorkspaceBytes - instanceRecord.Length),
                                out var sceneInstanceArrayReservation),
                            Is.True);
                        Assert.That(sceneInstanceArrayReservation, Is.Not.Null);

                        using (sceneInstanceArrayReservation)
                        {
                            var sceneInstancesArrayPayload = new byte[sceneInstanceArrayWorkspace.ArrayPayloadBytes];
                            Assert.That(
                                ScenePersistenceSceneInstanceArrayWriter.Write(
                                    activeSceneInstances,
                                    sceneInstanceRecordSlices,
                                    instanceRecord,
                                    EbinReadLimits.Foundation,
                                    sceneInstanceArrayWorkspace,
                                    sceneInstanceOrderScratch,
                                    orderedSceneInstanceRecords,
                                    sceneInstancesArrayPayload,
                                    out var sceneInstancesArrayBytesWritten,
                                    out instanceDiagnostic),
                                Is.EqualTo(EbinWriteStatus.Complete),
                                instanceDiagnostic?.Category);
                            Assert.That(instanceDiagnostic, Is.Null);
                            Assert.That(sceneInstancesArrayBytesWritten, Is.EqualTo(sceneInstancesArrayPayload.Length));
                            Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(sceneInstancesArrayPayload), Is.EqualTo(1));

                            var worldHierarchyRecord = SceneInstanceRecordFixtureBuilder.CreateWorldHierarchyRecord(world, captureLease);
                            var scenesRootPayload = new byte[sceneInstancesArrayPayload.Length + worldHierarchyRecord.Length];
                            Assert.That(
                                ScenePersistenceScenesWriter.Measure(
                                    activeSceneInstances.Length,
                                    sceneInstancesArrayPayload,
                                    worldHierarchyRecord,
                                    EbinReadLimits.Foundation,
                                    scenesRootPayload,
                                    out var scenesWorkspace,
                                    out var scenesDiagnostic),
                                Is.EqualTo(EbinWriteStatus.Complete),
                                scenesDiagnostic?.Category);
                            Assert.That(scenesDiagnostic, Is.Null);
                            var scenesCandidateStorage = new byte[scenesWorkspace.RecordBytes];
                            var scenesRecord = new byte[scenesWorkspace.RecordBytes];
                            Assert.That(
                                ScenePersistenceScenesWriter.Write(
                                    scenesWorkspace,
                                    activeSceneInstances.Length,
                                    sceneInstancesArrayPayload,
                                    worldHierarchyRecord,
                                    EbinReadLimits.Foundation,
                                    scenesRootPayload,
                                    scenesCandidateStorage,
                                    scenesRecord,
                                    out var scenesBytesWritten,
                                    out scenesDiagnostic),
                                Is.EqualTo(EbinWriteStatus.Complete),
                                scenesDiagnostic?.Category);
                            Assert.That(scenesDiagnostic, Is.Null);
                            Assert.That(scenesBytesWritten, Is.EqualTo(scenesRecord.Length));

                            var ecsRecord = WorldPersistenceEcsTestHelper.CopyEcsRecord(world, in captureLease);
                            var scanSchemas = new CompiledContentSchemaRegistry([
                                .. schemas.Schemas,
                                .. StandaloneContinuationSchemaCatalogue.Schemas,
                                .. AnimationSchemaCatalogue.Schemas,
                                WorldPersistenceWorldRecordWriterTests.CreateApplicationSchema(
                                    "application/world-root-test/tag/v1",
                                    ContentSchemaRole.EntityTag,
                                    0),
                                WorldPersistenceWorldRecordWriterTests.CreateApplicationSchema(
                                    "application/world-root-test/system-state/v1",
                                    ContentSchemaRole.SystemState,
                                    7),
                                WorldPersistenceWorldRecordWriterTests.CreateApplicationSchema(
                                    "application/world-root-test/resource-state/v1",
                                    ContentSchemaRole.ResourceState,
                                    23,
                                    4),
                                WorldPersistenceWorldRecordWriterTests.CreateApplicationSchema(
                                    "application/world-root-test/attachment-state/v1",
                                    ContentSchemaRole.AttachmentState,
                                    8),
                            ]);
                            var persistenceLimits = WorldPersistenceEbinLimits.Create(world.Options);
                            var referenceCount = WorldPersistenceAssetIdSetWriter.MeasureReferenceCount(
                                scanSchemas,
                                ecsRecord,
                                scenesRecord,
                                persistenceLimits);
                            var assetIds = new uint[referenceCount];
                            var uniqueAssetCount = WorldPersistenceAssetIdSetWriter.CopySortedUniqueAssetIds(
                                scanSchemas,
                                ecsRecord,
                                scenesRecord,
                                persistenceLimits,
                                assetIds);
                            uint[] expectedAssetIds = [
                                checked((uint)prefabAssetIdValue),
                                checked((uint)atlasAssetIdValue),
                                checked((uint)sceneAssetIdValue),
                            ];
                            Array.Sort(expectedAssetIds);
                            var uniqueAssetIds = assetIds.AsSpan(0, uniqueAssetCount).ToArray();
                            Assert.That(uniqueAssetIds, Is.EqualTo(expectedAssetIds));
                            Assert.That(uniqueAssetIds, Does.Not.Contain(checked((uint)textureAssetIdValue)));

                            var worldRecord = WorldPersistenceWorldRecordWriterTests.ComposeWorldRecord(
                                world,
                                in captureLease,
                                scanSchemas,
                                uniqueAssetIds);
                            var assetIdScratch = new uint[referenceCount];
                            var identityScratch = new WorldPersistenceIdentityScratchEntry[
                                WorldPersistenceWorldRecordReader.MeasureIdentityAssignmentCount(worldRecord, persistenceLimits)];
                            var worldAssertions = new WorldPersistenceWorldRecordWriterTests.WorldRecordAssertions(
                                captureLease.Frame2.HasValue,
                                controlRecordCount: captureLease.ControlRecordCount,
                                assetIds: uniqueAssetIds);
                            Assert.That(
                                EbinStructuralReader.Read(
                                    worldRecord,
                                    persistenceLimits,
                                    EbinReadPolicy.Canonical,
                                    ref worldAssertions,
                                    out var worldBytesConsumed,
                                    out var worldReadDiagnostic),
                                Is.EqualTo(EbinReadStatus.Complete),
                                worldReadDiagnostic?.ToString());
                            Assert.That(worldBytesConsumed, Is.EqualTo(worldRecord.Length));
                            var decodedWorld = WorldPersistenceWorldRecordReader.Read(
                                worldRecord,
                                persistenceLimits,
                                worldRegistration,
                                scanSchemas,
                                assetIdScratch,
                                identityScratch);
                            Assert.That(decodedWorld.Scenes.InstanceCount, Is.EqualTo(1));
                            Assert.That(decodedWorld.Scenes.HierarchyNodeCount, Is.EqualTo(2));
                            Assert.That(decodedWorld.Scenes.RootCount, Is.EqualTo(1));
                            Assert.That(decodedWorld.Ecs.EntityCount, Is.EqualTo(2));
                            Assert.That(decodedWorld.Ecs.LiveEntityCount, Is.EqualTo(2));
                            Assert.That(decodedWorld.Ecs.LiveSceneInstanceCount, Is.EqualTo(1));
                            Assert.That(decodedWorld.AssetIdCount, Is.EqualTo(uniqueAssetCount));

                            var mismatchedAssetIdWorldRecord = new byte[worldRecord.Length];
                            worldRecord.CopyTo(mismatchedAssetIdWorldRecord, 0);
                            var assetIdsFieldIndex = -1;

                            for (var fieldIndex = 0; fieldIndex < 7; fieldIndex++)
                            {
                                var fieldEntryOffset = checked(64 + (fieldIndex * 32));

                                if (BinaryPrimitives.ReadUInt32LittleEndian(
                                        mismatchedAssetIdWorldRecord.AsSpan(fieldEntryOffset, sizeof(uint)))
                                    == 7)
                                {
                                    assetIdsFieldIndex = fieldIndex;
                                    break;
                                }
                            }

                            Assert.That(assetIdsFieldIndex, Is.GreaterThanOrEqualTo(0));
                            var assetIdsFieldEntryOffset = checked(64 + (assetIdsFieldIndex * 32));
                            var assetIdsPayloadOffset = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(
                                mismatchedAssetIdWorldRecord.AsSpan(assetIdsFieldEntryOffset + 8, sizeof(ulong))));
                            var finalAssetIdOffset = checked(assetIdsPayloadOffset + 32 + ((uniqueAssetCount - 1) * sizeof(uint)));
                            var finalAssetId = BinaryPrimitives.ReadUInt32LittleEndian(
                                mismatchedAssetIdWorldRecord.AsSpan(finalAssetIdOffset, sizeof(uint)));
                            BinaryPrimitives.WriteUInt32LittleEndian(
                                mismatchedAssetIdWorldRecord.AsSpan(finalAssetIdOffset, sizeof(uint)),
                                checked(finalAssetId + 1));
                            _ = Assert.Throws<ArgumentException>(
                                () => WorldPersistenceWorldRecordReader.Read(
                                    mismatchedAssetIdWorldRecord,
                                    persistenceLimits,
                                    worldRegistration,
                                    scanSchemas,
                                    assetIdScratch,
                                    identityScratch));

                            var mismatchedExternalTargetRecord = (byte[])worldRecord.Clone();
                            Span<byte> externalTargetIdBytes = stackalloc byte[16];
                            Span<byte> unknownTargetIdBytes = stackalloc byte[16];
                            _ = externalTargetId.TryWriteBytes(externalTargetIdBytes, bigEndian: true, out _);
                            _ = Guid.Parse("50000000-0000-0000-0000-000000000001").TryWriteBytes(
                                unknownTargetIdBytes,
                                bigEndian: true,
                                out _);
                            static (int Offset, int Length) GetFieldPayloadRange(ReadOnlySpan<byte> record, uint tag)
                            {
                                var fieldCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(record[12..]));

                                for (var fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
                                {
                                    var entry = record.Slice(64 + (fieldIndex * 32), 32);

                                    if (BinaryPrimitives.ReadUInt32LittleEndian(entry) == tag)
                                    {
                                        return (
                                            checked((int)BinaryPrimitives.ReadUInt64LittleEndian(entry[8..])),
                                            checked((int)BinaryPrimitives.ReadUInt64LittleEndian(entry[16..])));
                                    }
                                }

                                throw new AssertionException($"Record field {tag} is absent.");
                            }

                            static (int Offset, int Length) GetRecordArrayElementRange(ReadOnlySpan<byte> array, int index)
                            {
                                var start = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(array[(8 + (index * 8))..]));
                                var end = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(array[(16 + (index * 8))..]));
                                return (start, checked(end - start));
                            }

                            var worldBytes = mismatchedExternalTargetRecord.AsSpan();
                            var (scenesOffset, scenesLength) = GetFieldPayloadRange(worldBytes, 6);
                            var persistedScenesRecord = worldBytes.Slice(scenesOffset, scenesLength);
                            var (instanceArrayOffset, instanceArrayLength) = GetFieldPayloadRange(persistedScenesRecord, 1);
                            var instanceArray = persistedScenesRecord.Slice(instanceArrayOffset, instanceArrayLength);
                            var (instanceOffset, instanceLength) = GetRecordArrayElementRange(instanceArray, 0);
                            Assert.That(
                                (instanceOffset >= 0)
                                && (instanceLength >= 0)
                                && (instanceOffset <= instanceArray.Length)
                                && (instanceLength <= (instanceArray.Length - instanceOffset)),
                                Is.True,
                                $"Instance range {instanceOffset}+{instanceLength} exceeds array length {instanceArray.Length}.");
                            var persistedInstanceRecord = instanceArray.Slice(instanceOffset, instanceLength);
                            var (externalBindingsOffset, externalBindingsLength) = GetFieldPayloadRange(
                                persistedInstanceRecord,
                                5);
                            var externalBindingsArray = persistedInstanceRecord.Slice(
                                externalBindingsOffset,
                                externalBindingsLength);
                            var (bindingOffset, bindingLength) = GetRecordArrayElementRange(externalBindingsArray, 0);
                            var persistedBindingRecord = externalBindingsArray.Slice(bindingOffset, bindingLength);
                            var (targetOffset, targetLength) = GetFieldPayloadRange(persistedBindingRecord, 2);
                            Assert.That(targetLength, Is.EqualTo(externalTargetIdBytes.Length));
                            var externalTargetOffset = checked(
                                scenesOffset
                                + instanceArrayOffset
                                + instanceOffset
                                + externalBindingsOffset
                                + bindingOffset
                                + targetOffset);
                            Assert.That(
                                mismatchedExternalTargetRecord.AsSpan(externalTargetOffset, targetLength).ToArray(),
                                Is.EqualTo(externalTargetIdBytes.ToArray()));

                            _ = WorldPersistenceWorldRecordReader.Read(
                                worldRecord,
                                persistenceLimits,
                                worldRegistration,
                                scanSchemas,
                                assetIdScratch,
                                identityScratch);

                            var sceneInstanceIdBytes = new byte[16];
                            _ = instance.SceneInstanceId.TryWriteBytes(sceneInstanceIdBytes, bigEndian: true, out _);
                            var sceneInstanceTargetRecord = (byte[])worldRecord.Clone();
                            sceneInstanceIdBytes.CopyTo(
                                sceneInstanceTargetRecord.AsSpan(externalTargetOffset, sceneInstanceIdBytes.Length));
                            _ = Assert.Throws<ArgumentException>(
                                () => WorldPersistenceWorldRecordReader.Read(
                                    sceneInstanceTargetRecord,
                                    persistenceLimits,
                                    worldRegistration,
                                    scanSchemas,
                                    assetIdScratch,
                                    identityScratch));

                            unknownTargetIdBytes.CopyTo(
                                mismatchedExternalTargetRecord.AsSpan(externalTargetOffset, unknownTargetIdBytes.Length));
                            _ = Assert.Throws<ArgumentException>(
                                () => WorldPersistenceWorldRecordReader.Read(
                                    mismatchedExternalTargetRecord,
                                    persistenceLimits,
                                    worldRegistration,
                                    scanSchemas,
                                    assetIdScratch,
                                    identityScratch));
                        }
                    }
                }

                Assert.That(instance.TryGetEntity(childEntityId, out _), Is.False);
                Assert.That(instance.TryGetEntity(rootEntityId, out var root), Is.True);
                var address = new SceneEntityAddress([placementId], childEntityId);
                Assert.That(instance.TryGetEntity(address, out var child), Is.True);
                Assert.That(instance.TryGetProvenance(child, out var provenance), Is.True);
                Assert.That(world.TryGetHierarchyParent(root, out _), Is.False);
                Assert.That(world.TryGetHierarchyParent(child, out var parent), Is.True);
                Assert.That(parent, Is.EqualTo(root));
                var externalTransform = new SceneComponentDefinitions.LocalTransformComponent(
                    new TerraFX.Numerics.Vector3(3.0f, 0.0f, 0.0f),
                    TerraFX.Numerics.Quaternion.Identity,
                    TerraFX.Numerics.Vector3.One);
                var external = world.CreateEntity(new EntityDescription(
                    components: [ComponentInitialization.Value(in externalTransform)]));
                var externalIdentity = world.GetEntityId(external);
                world.ApplyHierarchyParentChange(world.GetSceneParticipant(sceneSupport), external, root);

                var childProvenance = provenance
                    ?? throw new InvalidOperationException("The nested Scene Entity has no provenance.");
                Guid[] expectedPlacementPath = [placementId];

                Assert.That(childProvenance.SceneInstanceId, Is.EqualTo(instance.SceneInstanceId));
                Assert.That(childProvenance.OccurrenceId, Is.EqualTo(childOccurrence));
                Assert.That(childProvenance.SourceAssetId, Is.EqualTo(new AssetId(prefabAssetIdValue)));
                Assert.That(childProvenance.AuthoredEntityId, Is.EqualTo(childEntityId));
                Assert.That(
                    childProvenance.ExpandedEntityKey,
                    Is.EqualTo(SceneOccurrenceIdentity.CreateExpandedEntityKey(childOccurrence, childEntityId)));
                Assert.That(childProvenance.PlacementPath, Is.EqualTo(expectedPlacementPath));
                Assert.That(childProvenance.WorldIdentity, Is.EqualTo(world.GetEntityIdentity(child)));

                var rootIdentity = world.GetEntityId(root);
                var childIdentity = world.GetEntityId(child);
                var unload = instance.UnloadAsync(HierarchyDestroyMode.DetachChildrenKeepWorld);
                world.PumpControlBoundary();
                var unloadReceipt = await unload.ConfigureAwait(false);

                Assert.That(unloadReceipt.Status, Is.EqualTo(SceneUnloadStatus.Unloaded));
                Assert.That(world.TryGetEntity(rootIdentity, out _), Is.False);
                Assert.That(world.TryGetEntity(childIdentity, out _), Is.False);
                Assert.That(world.TryGetEntity(externalIdentity, out var liveExternal), Is.True);
                Assert.That(liveExternal, Is.EqualTo(external));
                Assert.That(world.TryGetHierarchyParent(external, out _), Is.False);
                Assert.That(
                    world.TryGet<SceneComponentDefinitions.RootPositionComponent>(external, out var externalPosition),
                    Is.True);
                Assert.That(externalPosition.Position.Offset, Is.EqualTo(new TerraFX.Numerics.Vector3(3.0f, 0.0f, 0.0f)));
            }
            finally
            {
                await assets.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Test]
    [SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "ActivateAsync detaches the expansion plan before returning; the awaited request owns it independently of PreparedScene.")]
    public async Task SceneLoaderResolvesOnlyLiveEntityIdsInAssignmentOrder()
    {
        const int sceneAssetIdValue = 3;
        const int prefabAssetIdValue = 1;
        var schemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var rootEntityId = SceneAssetReaderTests.Fixtures.RootEntityId;
        var childEntityId = new Guid("10000000-0000-4000-8000-000000000002");
        var placementId = new Guid("40000000-0000-4000-8000-000000000002");
        var scene = SceneAssetReaderTests.Fixtures.CreateSceneWithChildPlacement(
            new AssetId(sceneAssetIdValue),
            rootEntityId,
            placementId,
            SceneAssetReaderTests.Fixtures.CreateRootPose(childEntityId, includeRootPosition: false));
        var prefab = SceneAssetReaderTests.Fixtures.CreatePrefab(
            new AssetId(prefabAssetIdValue),
            entityId: childEntityId);
        var package = Package.CreatePackage(
            new Package.Entry(prefabAssetIdValue, Package.CreateEnvelope(prefabAssetIdValue, prefab)),
            new Package.Entry(sceneAssetIdValue, Package.CreateEnvelope(sceneAssetIdValue, scene)));
        var packagePath = Package.WritePackage(package);

        try
        {
            var storeRegistration = SceneSupport.RegisterAssetCodecs(new AssetStoreRegistrationBuilder(schemas)).Build();
            var assets = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(package), storeRegistration)).ConfigureAwait(false);

            try
            {
                var worldRegistrationBuilder = new WorldRegistrationBuilder(schemas);
                _ = worldRegistrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
                using var world = World.Create(new WorldOptions(worldRegistrationBuilder.Build()));
                var loader = new SceneLoader(world, assets);

                using var prepared = loader
                    .PrepareAsync(new AssetId(sceneAssetIdValue), new SceneLoadOptions())
                    .GetAwaiter()
                    .GetResult();
                var activation = loader.ActivateAsync(prepared);
                world.PumpControlBoundary();
                var receipt = await activation.ConfigureAwait(false);
                var instance = receipt.Instance
                    ?? throw new InvalidOperationException("The nested Scene activation did not return its instance.");

                Assert.That(receipt.Status, Is.EqualTo(SceneActivationStatus.Activated));
                _ = world.Step();
                var childAddress = new SceneEntityAddress([placementId], childEntityId);
                Assert.That(instance.TryGetEntity(rootEntityId, out var root), Is.True);
                Assert.That(instance.TryGetEntity(childAddress, out var child), Is.True);
                world.DestroyEntity(root, HierarchyDestroyMode.DetachChildrenKeepWorld);
                _ = world.Step();

                using var lease = world.AcquirePersistenceCaptureLease();
                Assert.That(instance.GetPersistenceEntityCount(lease), Is.EqualTo(1));
                var capturedEntityIds = new Guid[1];
                instance.CopyPersistenceEntityIds(lease, capturedEntityIds);
                var expectedEntityIds = new[] { world.GetEntityId(child) };
                Assert.That(capturedEntityIds, Is.EqualTo(expectedEntityIds));
                Assert.That(SceneInstanceRecordFixtureBuilder.Create(instance, lease), Is.Not.Empty);
            }
            finally
            {
                await assets.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    internal static class Package
    {
        private static readonly byte[] s_profileRecord = Convert.FromHexString("4542494E0100000040000000090000003B9760CE468A8AABBB89F38DB9E3BC0020020000000000002001000000000000020000000000000000000000000000000100000003000100600100000000000004000000000000000100000000000000020000000300010070010000000000000400000000000000010000000000000003000000030001008001000000000000040000000000000001000000000000000500000001000100900100000000000001000000000000000100000000000000060000000D000100A00100000000000020000000000000002000000000000000070000000D000100C00100000000000020000000000000002000000000000000080000000D000100E00100000000000020000000000000002000000000000000090000000B0001000002000000000000100000000000000001000000000000000A000000070001001002000000000000040000000000000001000000000000000100000000000000000000000000000002000000000000000000000000000000020000000000000000000000000000000C0000000000000000000000000000009EF6534033621925D39355058D26782EB3B1A41A20690EFD829EF6D3EFE0EB1CF21833596ED8BEDEAA7E366505A2ADE8AD349AB35F7BF0554AD2D8CE5AEDA43F494F69B9559DE1C609B2C51A11454FCDC3A0BE8B108FDC91F242D89B38D18105C2DDC5D89EE68676BDEFDFFD36A56D8900000000000000000000000000000000");

        internal static byte[] CreateEnvelope(uint assetId, byte[] payload)
        {
            var envelope = new byte[checked((int)Align16(144UL + checked((ulong)payload.Length)))];
            WriteRecordHeader(
                envelope,
                new ContentSchemaIdentityAssignment("native/envelope").SchemaId,
                major: 2,
                minor: 0,
                fieldCount: 2);
            WriteFieldHeader(envelope.AsSpan(64, 32), tag: 1, kind: 3, payloadOffset: 128, payloadLength: sizeof(uint), count: 1);
            WriteFieldHeader(
                envelope.AsSpan(96, 32),
                tag: 2,
                kind: 22,
                payloadOffset: 144,
                payloadLength: checked((ulong)payload.Length),
                count: 1);
            BinaryPrimitives.WriteUInt32LittleEndian(envelope.AsSpan(128), assetId);
            payload.CopyTo(envelope.AsSpan(144));
            return envelope;
        }

        internal static byte[] CreatePackage(params Entry[] entries)
        {
            var directoryOffset = Align16(checked((ulong)(64 + s_profileRecord.Length)));
            var directoryLength = checked(64UL + (checked((ulong)entries.Length) * 32));
            var payloadEnd = Align16(directoryOffset + directoryLength);
            var payloadOffsets = new ulong[entries.Length];

            for (var index = 0; index < entries.Length; index++)
            {
                payloadOffsets[index] = Align16(payloadEnd);
                payloadEnd = checked(payloadOffsets[index] + checked((ulong)entries[index].Payload.Length));
            }

            var package = new byte[checked((int)Align16(payloadEnd))];
            "EPKG"u8.CopyTo(package);
            BinaryPrimitives.WriteUInt16LittleEndian(package.AsSpan(4), 2);
            BinaryPrimitives.WriteUInt64LittleEndian(package.AsSpan(8), checked((ulong)package.Length));
            BinaryPrimitives.WriteUInt64LittleEndian(package.AsSpan(16), 64);
            BinaryPrimitives.WriteUInt64LittleEndian(package.AsSpan(24), checked((ulong)s_profileRecord.Length));
            BinaryPrimitives.WriteUInt64LittleEndian(package.AsSpan(32), directoryOffset);
            BinaryPrimitives.WriteUInt64LittleEndian(package.AsSpan(40), directoryLength);
            BinaryPrimitives.WriteUInt32LittleEndian(package.AsSpan(48), checked((uint)entries.Length));
            s_profileRecord.CopyTo(package.AsSpan(64));

            var directory = package.AsSpan(checked((int)directoryOffset), checked((int)directoryLength));
            "EIDX"u8.CopyTo(directory);
            BinaryPrimitives.WriteUInt16LittleEndian(directory[4..], 1);
            BinaryPrimitives.WriteUInt16LittleEndian(directory[6..], 16);
            BinaryPrimitives.WriteUInt32LittleEndian(directory[8..], checked((uint)entries.Length));
            BinaryPrimitives.WriteUInt64LittleEndian(directory[16..], 64);
            BinaryPrimitives.WriteUInt64LittleEndian(directory[24..], checked((ulong)entries.Length * 32));
            BinaryPrimitives.WriteUInt64LittleEndian(directory[32..], directoryLength);

            for (var index = 0; index < entries.Length; index++)
            {
                var entry = entries[index];
                var encodedEntry = directory.Slice(64 + (index * 32), 32);
                BinaryPrimitives.WriteUInt32LittleEndian(encodedEntry, entry.AssetId);
                BinaryPrimitives.WriteUInt64LittleEndian(encodedEntry[8..], payloadOffsets[index]);
                BinaryPrimitives.WriteUInt64LittleEndian(encodedEntry[16..], checked((ulong)entry.Payload.Length));
                BinaryPrimitives.WriteUInt64LittleEndian(encodedEntry[24..], checked((ulong)entry.Payload.Length));
                entry.Payload.CopyTo(package.AsSpan(checked((int)payloadOffsets[index])));
            }

            return package;
        }

        internal static string WritePackage(byte[] package)
        {
            var packagePath = Path.Combine(Path.GetTempPath(), $"terrafx-{Guid.NewGuid():N}.epkg");
            File.WriteAllBytes(packagePath, package);
            return packagePath;
        }

        private static ulong Align16(ulong value) => (value + 15) & ~15UL;

        private static void WriteFieldHeader(
            Span<byte> destination,
            uint tag,
            ushort kind,
            ulong payloadOffset,
            ulong payloadLength,
            ulong count)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination, tag);
            BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], kind);
            BinaryPrimitives.WriteUInt16LittleEndian(destination[6..], 1);
            BinaryPrimitives.WriteUInt64LittleEndian(destination[8..], payloadOffset);
            BinaryPrimitives.WriteUInt64LittleEndian(destination[16..], payloadLength);
            BinaryPrimitives.WriteUInt64LittleEndian(destination[24..], count);
        }

        private static void WriteRecordHeader(
            byte[] destination,
            Guid schemaId,
            int major,
            int minor,
            uint fieldCount)
        {
            "EBIN"u8.CopyTo(destination);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.AsSpan(4), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(8), 64);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(12), fieldCount);
            _ = schemaId.TryWriteBytes(destination.AsSpan(16, 16), bigEndian: true, out _);
            BinaryPrimitives.WriteUInt64LittleEndian(destination.AsSpan(32), checked((ulong)destination.Length));
            BinaryPrimitives.WriteUInt64LittleEndian(destination.AsSpan(40), checked((ulong)fieldCount * 32));
            BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(48), checked((uint)major));
            BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(52), checked((uint)minor));
        }

        internal readonly record struct Entry(uint AssetId, byte[] Payload);
    }
}
