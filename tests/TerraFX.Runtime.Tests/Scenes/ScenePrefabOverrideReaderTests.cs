// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

[TestFixture]
internal sealed class ScenePrefabOverrideReaderTests
{
    private static DataSchema CreateEntityReferenceComponentSchema()
    {
        var identityAssignment = new ContentSchemaIdentityAssignment("test/prefab-entity-reference");
        var schema = new ContentSchemaReference(
            identityAssignment.SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var field = new ContentFieldDescriptor(
            tag: 1,
            wireKind: 11,
            elementKind: 0,
            isRequired: true,
            minimumCount: 1,
            maximumCount: 1,
            nestedSchemas: [],
            nestedRoleMask: 0,
            unit: ContentUnit.None,
            reference: ContentReferenceKind.Entity,
            authoringFlags: ContentAuthoring.PrefabEditable,
            defaultValue: null);
        return new DataSchema(
            schema,
            identityAssignment,
            ContentSchemaRole.Component,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.Persistent,
            ContentTransientRestore.NotApplicable,
            [field]);
    }

    private static byte[] CreateEntitySelector(Guid entityId)
        => SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("native/entity-selector").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(1, (byte)SceneEntitySelectorScope.OccurrenceRelative),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFixedGuidArrayField(2),
            SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(3, entityId));

    private static byte[] CreateLiteralGuid(Guid value)
        => SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("content/literal/guid").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(1, value));

    private static byte[] CreateLiteralUInt8(byte value)
        => SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("content/literal/u8").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(1, value));

    private static byte[] CreateLiteralUtf8()
        => SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("content/literal/utf8").SchemaId,
            1,
            0,
            new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(1, 12, 1, 0, []));

    private static byte[] CreatePrefabOverride(
        Guid componentSchemaId,
        byte[][] propertyPath,
        byte[]? value = null,
        byte? operation = null,
        byte[]? referenceValue = null,
        Guid? targetEntityId = null)
    {
        List<SceneHierarchyCodecTests.FixtureBuilder.FixtureField> fields = [
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(1, CreateEntitySelector(targetEntityId ?? SceneAssetReaderTests.Fixtures.RootEntityId)),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(2, CreateSchemaReference(componentSchemaId)),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(3, propertyPath),
        ];

        if (value is not null)
        {
            fields.Add(SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(4, value, isRequired: false));
        }

        if (operation is byte operationValue)
        {
            fields.Add(new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(5, 1, 0, 1, [operationValue]));
        }

        if (referenceValue is not null)
        {
            fields.Add(SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(6, referenceValue, isRequired: false));
        }

        return SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("native/prefab-override").SchemaId,
            1,
            0,
            [.. fields]);
    }

    private static byte[] CreatePropertySegment(byte kind, uint value)
        => SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("content/property-segment").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(1, kind),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(2, value));

    private static CompiledContentSchemaRegistry CreateRegistry(params DataSchema[] additionalSchemas)
    {
        var schemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        return new CompiledContentSchemaRegistry([.. schemas.Schemas, .. additionalSchemas]);
    }

    private static byte[] CreateSchemaReference(Guid schemaId)
        => SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("content/schema-ref").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(1, schemaId),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(2, 1),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(3, 0),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(4, (byte)ContentSchemaScope.Content));

    private static ScenePrefabAsset ReadPrefabAsset(
        byte[] encoded,
        AssetId assetId,
        CompiledContentSchemaRegistry schemas)
    {
        var workspace = new SceneHierarchyValidationWorkspace(entityCount: 0, placementCount: 1);
        var action = new PrefabReadAction(assetId, schemas, workspace);
        var status = EbinStructuralReader.Read(
            encoded,
            EbinReadLimits.Foundation,
            EbinReadPolicy.Canonical,
            ref action,
            out _,
            out var diagnostic);

        if (status != EbinReadStatus.Complete)
        {
            throw new InvalidOperationException($"The Prefab reader failed: {diagnostic?.Category ?? status.ToString()}.");
        }

        return action.Result
            ?? throw new InvalidOperationException("The Prefab reader did not produce a value.");
    }

    [Test]
    public void ReadsPrefabPlacementOverrideAsTypedValue()
    {
        var schemas = CreateRegistry();
        var assetId = new AssetId(3);
        var placementId = new Guid("20000000-0000-4000-8000-000000000009");
        var componentSchemaId = new ContentSchemaIdentityAssignment("scene/camera").SchemaId;
        var overrideRecord = CreatePrefabOverride(
            componentSchemaId,
            [CreatePropertySegment((byte)ScenePropertyPathSegmentKind.FieldTag, 1)],
            value: CreateLiteralUInt8(1));
        var encoded = SceneAssetReaderTests.Fixtures.CreatePrefab(assetId, placementId: placementId, overrides: [overrideRecord]);
        var prefab = ReadPrefabAsset(encoded, assetId, schemas);
        var prefabOverride = prefab.Placements[0].Overrides[0];

        Assert.That(prefabOverride.Target.Scope, Is.EqualTo(SceneEntitySelectorScope.OccurrenceRelative));
        Assert.That(prefabOverride.ComponentSchema.SchemaId, Is.EqualTo(componentSchemaId));
        Assert.That(prefabOverride.PropertyPath, Is.EqualTo([
            new ScenePropertyPathSegment(ScenePropertyPathSegmentKind.FieldTag, 1),
        ]));
        Assert.That(prefabOverride.Operation, Is.EqualTo(ScenePrefabOverrideOperation.Set));
        Assert.That(
            prefabOverride.Value?.Schema.SchemaId,
            Is.EqualTo(new ContentSchemaIdentityAssignment("content/literal/u8").SchemaId));
        Assert.That(prefabOverride.ReferenceValue, Is.Null);
    }

    [Test]
    public void ReadsPrefabPlacementOverrideOmission()
    {
        var schemas = CreateRegistry();
        var assetId = new AssetId(3);
        var placementId = new Guid("20000000-0000-4000-8000-000000000010");
        var overrideRecord = CreatePrefabOverride(
            new ContentSchemaIdentityAssignment("scene/camera").SchemaId,
            [CreatePropertySegment((byte)ScenePropertyPathSegmentKind.FieldTag, 6)],
            operation: (byte)ScenePrefabOverrideOperation.Omit);
        var encoded = SceneAssetReaderTests.Fixtures.CreatePrefab(assetId, placementId: placementId, overrides: [overrideRecord]);
        var prefab = ReadPrefabAsset(encoded, assetId, schemas);

        Assert.That(prefab.Placements[0].Overrides[0].Operation, Is.EqualTo(ScenePrefabOverrideOperation.Omit));
        Assert.That(prefab.Placements[0].Overrides[0].Value, Is.Null);
        Assert.That(prefab.Placements[0].Overrides[0].ReferenceValue, Is.Null);
    }

    [Test]
    public void ReadsSceneAuthoredInternalOverrideReference()
    {
        var entityReferenceSchema = CreateEntityReferenceComponentSchema();
        var schemas = CreateRegistry(entityReferenceSchema);
        var assetId = new AssetId(3);
        var targetId = new Guid("40000000-0000-4000-8000-000000000014");
        var target = SceneAssetReaderTests.Fixtures.CreateReferenceTarget(
            SceneReferenceTargetKind.Internal,
            entityId: targetId);
        var overrideRecord = CreatePrefabOverride(
            entityReferenceSchema.Schema.SchemaId,
            [CreatePropertySegment((byte)ScenePropertyPathSegmentKind.FieldTag, 1)],
            value: CreateLiteralGuid(targetId),
            referenceValue: target);
        var placementId = new Guid("20000000-0000-4000-8000-000000000014");
        var encoded = SceneAssetReaderTests.Fixtures.CreateAssetReferenceScene(assetId, placementId, overrides: [overrideRecord]);
        var action = new SceneReadAction(
            assetId,
            schemas,
            new SceneHierarchyValidationWorkspace(entityCount: 0, placementCount: 1));
        var status = EbinStructuralReader.Read(
            encoded,
            EbinReadLimits.Foundation,
            EbinReadPolicy.Canonical,
            ref action,
            out _,
            out var diagnostic);

        Assert.That(status, Is.EqualTo(EbinReadStatus.Complete), diagnostic?.Category);
        var scene = action.Result
            ?? throw new InvalidOperationException("The Scene reader did not produce a value.");
        Assert.That(scene.Placements[0].Overrides[0].ReferenceValue?.Kind, Is.EqualTo(SceneReferenceTargetKind.Internal));
        Assert.That(
            scene.Placements[0].Overrides[0].Value?.Schema.SchemaId,
            Is.EqualTo(new ContentSchemaIdentityAssignment("content/literal/guid").SchemaId));
    }

    [Test]
    public void RejectsNonFixedPrefabOverrideLiteral()
    {
        var schemas = CreateRegistry();
        var assetId = new AssetId(3);
        var overrideRecord = CreatePrefabOverride(
            new ContentSchemaIdentityAssignment("scene/camera").SchemaId,
            [CreatePropertySegment((byte)ScenePropertyPathSegmentKind.FieldTag, 1)],
            value: CreateLiteralUtf8());
        var encoded = SceneAssetReaderTests.Fixtures.CreatePrefab(
            assetId,
            placementId: new Guid("20000000-0000-4000-8000-000000000017"),
            overrides: [overrideRecord]);

        Assert.That(() => ReadPrefabAsset(encoded, assetId, schemas), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RejectsOmitPrefabOverrideWithValue()
    {
        var schemas = CreateRegistry();
        var assetId = new AssetId(3);
        var overrideRecord = CreatePrefabOverride(
            new ContentSchemaIdentityAssignment("scene/camera").SchemaId,
            [CreatePropertySegment((byte)ScenePropertyPathSegmentKind.FieldTag, 6)],
            value: CreateLiteralUInt8(1),
            operation: (byte)ScenePrefabOverrideOperation.Omit);
        var encoded = SceneAssetReaderTests.Fixtures.CreatePrefab(
            assetId,
            placementId: new Guid("20000000-0000-4000-8000-000000000012"),
            overrides: [overrideRecord]);

        Assert.That(() => ReadPrefabAsset(encoded, assetId, schemas), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RejectsPrefabAuthoredInternalOverrideReference()
    {
        var entityReferenceSchema = CreateEntityReferenceComponentSchema();
        var schemas = CreateRegistry(entityReferenceSchema);
        var assetId = new AssetId(3);
        var target = SceneAssetReaderTests.Fixtures.CreateReferenceTarget(
            SceneReferenceTargetKind.Internal,
            entityId: SceneAssetReaderTests.Fixtures.RootEntityId);
        var overrideRecord = CreatePrefabOverride(
            entityReferenceSchema.Schema.SchemaId,
            [CreatePropertySegment((byte)ScenePropertyPathSegmentKind.FieldTag, 1)],
            value: CreateLiteralGuid(SceneAssetReaderTests.Fixtures.RootEntityId),
            referenceValue: target);
        var encoded = SceneAssetReaderTests.Fixtures.CreatePrefab(
            assetId,
            placementId: new Guid("20000000-0000-4000-8000-000000000013"),
            overrides: [overrideRecord]);

        Assert.That(() => ReadPrefabAsset(encoded, assetId, schemas), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RejectsPrefabOverrideReferenceWhenLiteralDoesNotMatchTarget()
    {
        var entityReferenceSchema = CreateEntityReferenceComponentSchema();
        var schemas = CreateRegistry(entityReferenceSchema);
        var assetId = new AssetId(3);
        var target = SceneAssetReaderTests.Fixtures.CreateReferenceTarget(
            SceneReferenceTargetKind.ExternalSlot,
            externalSlotId: new Guid("40000000-0000-4000-8000-000000000015"));
        var overrideRecord = CreatePrefabOverride(
            entityReferenceSchema.Schema.SchemaId,
            [CreatePropertySegment((byte)ScenePropertyPathSegmentKind.FieldTag, 1)],
            value: CreateLiteralGuid(SceneAssetReaderTests.Fixtures.RootEntityId),
            referenceValue: target);
        var encoded = SceneAssetReaderTests.Fixtures.CreatePrefab(
            assetId,
            placementId: new Guid("20000000-0000-4000-8000-000000000015"),
            overrides: [overrideRecord]);

        Assert.That(() => ReadPrefabAsset(encoded, assetId, schemas), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RejectsSetPrefabOverrideWithoutValue()
    {
        var schemas = CreateRegistry();
        var assetId = new AssetId(3);
        var overrideRecord = CreatePrefabOverride(
            new ContentSchemaIdentityAssignment("scene/camera").SchemaId,
            [CreatePropertySegment((byte)ScenePropertyPathSegmentKind.FieldTag, 1)]);
        var encoded = SceneAssetReaderTests.Fixtures.CreatePrefab(
            assetId,
            placementId: new Guid("20000000-0000-4000-8000-000000000011"),
            overrides: [overrideRecord]);

        Assert.That(() => ReadPrefabAsset(encoded, assetId, schemas), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RejectsZeroPrefabOverrideFieldTag()
    {
        var schemas = CreateRegistry();
        var assetId = new AssetId(3);
        var overrideRecord = CreatePrefabOverride(
            new ContentSchemaIdentityAssignment("scene/camera").SchemaId,
            [CreatePropertySegment((byte)ScenePropertyPathSegmentKind.FieldTag, 0)],
            value: CreateLiteralUInt8(1));
        var encoded = SceneAssetReaderTests.Fixtures.CreatePrefab(
            assetId,
            placementId: new Guid("20000000-0000-4000-8000-000000000016"),
            overrides: [overrideRecord]);

        Assert.That(() => ReadPrefabAsset(encoded, assetId, schemas), Throws.TypeOf<InvalidDataException>());
    }

    private struct PrefabReadAction : IAction<EbinRecordReadView>
    {
        private readonly AssetId _expectedPrefabId;
        private readonly CompiledContentSchemaRegistry _schemas;
        private readonly SceneHierarchyValidationWorkspace _workspace;

        internal PrefabReadAction(
            AssetId expectedPrefabId,
            CompiledContentSchemaRegistry schemas,
            SceneHierarchyValidationWorkspace workspace)
        {
            _expectedPrefabId = expectedPrefabId;
            _schemas = schemas;
            _workspace = workspace;
            Result = null;
        }

        internal ScenePrefabAsset? Result { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
            => Result = SceneAssetReader.ReadPrefab(
                record,
                _expectedPrefabId,
                _schemas,
                EbinReadLimits.Foundation,
                _workspace,
                maximumHierarchyDepth: 256);
    }

    private struct SceneReadAction : IAction<EbinRecordReadView>
    {
        private readonly AssetId _expectedSceneId;
        private readonly CompiledContentSchemaRegistry _schemas;
        private readonly SceneHierarchyValidationWorkspace _workspace;

        internal SceneReadAction(
            AssetId expectedSceneId,
            CompiledContentSchemaRegistry schemas,
            SceneHierarchyValidationWorkspace workspace)
        {
            _expectedSceneId = expectedSceneId;
            _schemas = schemas;
            _workspace = workspace;
            Result = null;
        }

        internal SceneAsset? Result { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
            => Result = SceneAssetReader.Read(
                record,
                _expectedSceneId,
                _schemas,
                EbinReadLimits.Foundation,
                _workspace,
                maximumHierarchyDepth: 256);
    }
}
