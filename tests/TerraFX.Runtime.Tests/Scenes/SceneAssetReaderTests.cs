// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

[TestFixture]
internal sealed class SceneAssetReaderTests
{
    [Test]
    public void ReadsSceneEntitiesHierarchyAndEmptyOptionalCollections()
    {
        var schemas = Fixtures.CreateRegistry();
        var assetId = new AssetId(3);
        var encoded = Fixtures.CreateScene(assetId);
        var workspace = new SceneHierarchyValidationWorkspace(entityCount: 1, placementCount: 0);
        var action = new ReadAction(assetId, schemas, workspace);
        var status = EbinStructuralReader.Read(
            encoded,
            EbinReadLimits.Foundation,
            EbinReadPolicy.Canonical,
            ref action,
            out var bytesConsumed,
            out var diagnostic);

        Assert.That(status, Is.EqualTo(EbinReadStatus.Complete), diagnostic?.Category);
        Assert.That(bytesConsumed, Is.EqualTo(encoded.Length));
        var scene = action.Result
            ?? throw new InvalidOperationException("The Scene reader did not produce a value.");
        Assert.That(scene.AssetId, Is.EqualTo(assetId));
        Assert.That(scene.Entities, Has.Length.EqualTo(1));
        Assert.That(scene.Entities[0].AuthoredEntityId, Is.EqualTo(Fixtures.RootEntityId));
        Assert.That(scene.Entities[0].Components, Has.Length.EqualTo(2));
        Assert.That(
            scene.Entities[0].Components[0].Schema.SchemaId,
            Is.EqualTo(new ContentSchemaIdentityAssignment("scene/root-position").SchemaId));
        Assert.That(
            scene.Entities[0].Components[1].Schema.SchemaId,
            Is.EqualTo(new ContentSchemaIdentityAssignment("scene/local-transform").SchemaId));
        Assert.That(scene.Hierarchy.Nodes, Has.Length.EqualTo(1));
        Assert.That(scene.Hierarchy.Roots, Has.Length.EqualTo(1));
        Assert.That(scene.Placements, Is.Empty);
        Assert.That(scene.References, Is.Empty);
        Assert.That(scene.Overrides, Is.Empty);
        Assert.That(scene.ExternalSlots, Is.Empty);
    }

    [Test]
    public void ReadsSceneRootPrefabPlacementAddress()
    {
        var schemas = Fixtures.CreateRegistry();
        var assetId = new AssetId(3);
        var placementId = new Guid("20000000-0000-4000-8000-000000000002");
        var encoded = Fixtures.CreateAssetReferenceScene(assetId, placementId);
        var workspace = new SceneHierarchyValidationWorkspace(entityCount: 0, placementCount: 1);
        var action = new ReadAction(assetId, schemas, workspace);
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
        Assert.That(scene.Entities, Is.Empty);
        Assert.That(scene.Placements, Has.Length.EqualTo(1));
        Assert.That(scene.Placements[0].PlacementId, Is.EqualTo(placementId));
        Assert.That(scene.Placements[0].PrefabAssetId, Is.EqualTo(new AssetId(1)));
        Assert.That(scene.Placements[0].ParentEntityId, Is.Null);
        Assert.That(
            scene.Hierarchy.Roots[0],
            Is.EqualTo(new SceneHierarchyEntry(SceneHierarchyEntryKind.PrefabPlacement, placementId)));
    }

    [Test]
    public void RejectsEnclosingAssetIdMismatch()
    {
        var schemas = Fixtures.CreateRegistry();
        var encoded = Fixtures.CreateScene(new AssetId(4));
        var workspace = new SceneHierarchyValidationWorkspace(entityCount: 1, placementCount: 0);

        void ReadMismatchedScene()
        {
            var action = new ReadAction(new AssetId(3), schemas, workspace);
            _ = EbinStructuralReader.Read(
                encoded,
                EbinReadLimits.Foundation,
                EbinReadPolicy.Canonical,
                ref action,
                out _,
                out _);
        }

        Assert.That(ReadMismatchedScene, Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RejectsRootEntityWithoutRootPosition()
    {
        var schemas = Fixtures.CreateRegistry();
        var assetId = new AssetId(3);
        var encoded = Fixtures.CreateScene(assetId, includeRootPosition: false);
        var workspace = new SceneHierarchyValidationWorkspace(entityCount: 1, placementCount: 0);

        void ReadScene()
        {
            var action = new ReadAction(assetId, schemas, workspace);
            _ = EbinStructuralReader.Read(
                encoded,
                EbinReadLimits.Foundation,
                EbinReadPolicy.Canonical,
                ref action,
                out _,
                out _);
        }

        Assert.That(ReadScene, Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RejectsTranslatedRootLocalPosition()
    {
        var schemas = Fixtures.CreateRegistry();
        var assetId = new AssetId(3);
        var encoded = Fixtures.CreateScene(assetId, localPositionX: 1.0f);
        var workspace = new SceneHierarchyValidationWorkspace(entityCount: 1, placementCount: 0);

        void ReadScene()
        {
            var action = new ReadAction(assetId, schemas, workspace);
            _ = EbinStructuralReader.Read(
                encoded,
                EbinReadLimits.Foundation,
                EbinReadPolicy.Canonical,
                ref action,
                out _,
                out _);
        }

        Assert.That(ReadScene, Throws.TypeOf<InvalidDataException>());
    }

    private static class Fixtures
    {
        internal static Guid RootEntityId { get; } = new("10000000-0000-4000-8000-000000000001");

        internal static byte[] CreateAssetReferenceScene(AssetId assetId, Guid placementId)
        {
            var prefabReference = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
                new ContentSchemaIdentityAssignment("native/asset-reference").SchemaId,
                1,
                0,
                SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(1, 1),
                SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(
                    2,
                    new ContentSchemaIdentityAssignment("native/prefab").SchemaId),
                SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(3, 2));
            var placement = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
                new ContentSchemaIdentityAssignment("native/prefab-placement").SchemaId,
                1,
                0,
                SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(1, placementId),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(2, prefabReference),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(4),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(5),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(6));
            var hierarchy = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
                new ContentSchemaIdentityAssignment("native/hierarchy").SchemaId,
                2,
                0,
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(1),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(
                    2,
                    SceneHierarchyCodecTests.FixtureBuilder.EncodeEntry(
                        (byte)SceneHierarchyEntryKind.PrefabPlacement,
                        placementId)));

            return SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
                new ContentSchemaIdentityAssignment("native/scene").SchemaId,
                2,
                0,
                SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(1, checked((uint)assetId.Value)),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(2),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(3, hierarchy),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(4, placement),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(6),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(7),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(8));
        }

        internal static CompiledContentSchemaRegistry CreateRegistry()
            => new CompiledContentSchemaRegistry([
                .. CoreContentSchemaCatalogue.PrimitiveSchemas,
                .. CoreContentSchemaCatalogue.FoundationSchemas,
                .. NativeAssetSchemaCatalogue.Schemas,
                .. SceneSchemaCatalogue.Schemas,
            ]);

        internal static byte[] CreateScene(
            AssetId assetId,
            bool includeRootPosition = true,
            float localPositionX = 0.0f)
        {
            var localTransformSchema = new ContentSchemaIdentityAssignment("scene/local-transform").SchemaId;
            var rootPositionSchema = new ContentSchemaIdentityAssignment("scene/root-position").SchemaId;
            var rootPosition = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
                rootPositionSchema,
                1,
                0,
                SceneHierarchyCodecTests.FixtureBuilder.CreatePosition3Field(1, 0, 0, 0, 0, 0, 0));
            var localTransform = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
                localTransformSchema,
                1,
                0,
                SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(1, 15, localPositionX, 0, 0),
                SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(2, 17, 0, 0, 0, 1),
                SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(3, 15, 1, 1, 1));
            var entity = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
                new ContentSchemaIdentityAssignment("native/entity").SchemaId,
                1,
                0,
                SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(1, RootEntityId),
                includeRootPosition
                    ? SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(2, rootPosition, localTransform)
                    : SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(2, localTransform));
            var hierarchyNode = SceneHierarchyCodecTests.FixtureBuilder.EncodeNode(RootEntityId, parentEntityId: null);
            var hierarchyEntry = SceneHierarchyCodecTests.FixtureBuilder.EncodeEntry(
                (byte)SceneHierarchyEntryKind.Entity,
                RootEntityId);
            var hierarchy = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
                new ContentSchemaIdentityAssignment("native/hierarchy").SchemaId,
                2,
                0,
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(1, hierarchyNode),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(2, hierarchyEntry));

            return SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
                new ContentSchemaIdentityAssignment("native/scene").SchemaId,
                2,
                0,
                SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(1, checked((uint)assetId.Value)),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(2, entity),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(3, hierarchy),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(4),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(6),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(7),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(8));
        }
    }

    private struct ReadAction : IAction<EbinRecordReadView>
    {
        private readonly AssetId _expectedSceneId;
        private readonly CompiledContentSchemaRegistry _schemas;
        private readonly SceneHierarchyValidationWorkspace _workspace;

        internal ReadAction(
            AssetId expectedSceneId,
            CompiledContentSchemaRegistry schemas,
            SceneHierarchyValidationWorkspace workspace)
        {
            _expectedSceneId = expectedSceneId;
            _workspace = workspace;
            _schemas = schemas;
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
