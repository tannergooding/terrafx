// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

[TestFixture]
internal sealed class SceneExternalSlotReaderTests
{
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

    private static byte[] CreateSchemaReference(string schemaName)
        => SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("content/schema-ref").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(1, new ContentSchemaIdentityAssignment(schemaName).SchemaId),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(2, 1),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(3, 0),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(4, (byte)ContentSchemaScope.Content));

    private static ScenePrefabAsset ReadPrefabAsset(
        byte[] encoded,
        AssetId assetId,
        CompiledContentSchemaRegistry schemas)
    {
        var action = new PrefabReadAction(
            assetId,
            schemas,
            new SceneHierarchyValidationWorkspace(entityCount: 0, placementCount: 0));
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

    private static SceneAsset ReadSceneAsset(
        byte[] encoded,
        AssetId assetId,
        CompiledContentSchemaRegistry schemas)
    {
        var action = new SceneReadAction(
            assetId,
            schemas,
            new SceneHierarchyValidationWorkspace(entityCount: 1, placementCount: 0));
        var status = EbinStructuralReader.Read(
            encoded,
            EbinReadLimits.Foundation,
            EbinReadPolicy.Canonical,
            ref action,
            out _,
            out var diagnostic);

        if (status != EbinReadStatus.Complete)
        {
            throw new InvalidOperationException($"The Scene reader failed: {diagnostic?.Category ?? status.ToString()}.");
        }

        return action.Result
            ?? throw new InvalidOperationException("The Scene reader did not produce a value.");
    }

    [Test]
    public void ReadsPrefabExternalSlotDeclaration()
    {
        var schemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var assetId = new AssetId(3);
        var slotId = new Guid("30000000-0000-4000-8000-000000000021");
        var externalSlot = CreateExternalSlot(
            slotId,
            required: true,
            allowNull: false,
            requiredComponents: [CreateSchemaReference("scene/camera")]);
        var encoded = SceneAssetReaderTests.Fixtures.CreatePrefab(assetId, externalSlots: [externalSlot]);
        var prefab = ReadPrefabAsset(encoded, assetId, schemas);
        var slot = prefab.ExternalSlots[0];

        Assert.That(prefab.ExternalSlots, Has.Length.EqualTo(1));
        Assert.That(slot.SlotId, Is.EqualTo(slotId));
        Assert.That(slot.Required, Is.True);
        Assert.That(slot.AllowNull, Is.False);
        Assert.That(
            slot.RequiredComponents,
            Is.EqualTo([
                new ContentSchemaReference(
                    new ContentSchemaIdentityAssignment("scene/camera").SchemaId,
                    1,
                    0,
                    ContentSchemaScope.Content),
            ]));
    }

    [Test]
    public void ReadsSceneExternalSlotDeclaration()
    {
        var schemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var assetId = new AssetId(3);
        var slotId = new Guid("30000000-0000-4000-8000-000000000022");
        var externalSlot = CreateExternalSlot(slotId, required: false, allowNull: true, requiredComponents: []);
        var encoded = SceneAssetReaderTests.Fixtures.CreateScene(assetId, externalSlots: [externalSlot]);
        var scene = ReadSceneAsset(encoded, assetId, schemas);
        var slot = scene.ExternalSlots[0];

        Assert.That(scene.ExternalSlots, Has.Length.EqualTo(1));
        Assert.That(slot.SlotId, Is.EqualTo(slotId));
        Assert.That(slot.Required, Is.False);
        Assert.That(slot.AllowNull, Is.True);
        Assert.That(slot.RequiredComponents, Is.Empty);
    }

    [Test]
    public void RejectsExternalSlotRequiredComponentsNotStrictlySorted()
    {
        var schemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var assetId = new AssetId(3);
        var slotId = new Guid("30000000-0000-4000-8000-000000000023");
        var componentReference = CreateSchemaReference("scene/camera");
        var externalSlot = CreateExternalSlot(
            slotId,
            required: false,
            allowNull: false,
            [componentReference, componentReference]);
        var encoded = SceneAssetReaderTests.Fixtures.CreatePrefab(assetId, externalSlots: [externalSlot]);

        Assert.That(() => ReadPrefabAsset(encoded, assetId, schemas), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RejectsExternalSlotRequiredComponentsWithNonComponentSchema()
    {
        var schemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var assetId = new AssetId(3);
        var slotId = new Guid("30000000-0000-4000-8000-000000000024");
        var externalSlot = CreateExternalSlot(
            slotId,
            required: false,
            allowNull: false,
            [CreateSchemaReference("content/schema-ref")]);
        var encoded = SceneAssetReaderTests.Fixtures.CreatePrefab(assetId, externalSlots: [externalSlot]);

        Assert.That(() => ReadPrefabAsset(encoded, assetId, schemas), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RejectsExternalSlotsNotStrictlySorted()
    {
        var schemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var assetId = new AssetId(3);
        var slotId = new Guid("30000000-0000-4000-8000-000000000025");
        var externalSlot = CreateExternalSlot(slotId, required: false, allowNull: false, requiredComponents: []);
        var encoded = SceneAssetReaderTests.Fixtures.CreatePrefab(
            assetId,
            externalSlots: [externalSlot, externalSlot]);

        Assert.That(() => ReadPrefabAsset(encoded, assetId, schemas), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RejectsRequiredExternalSlotThatAllowsNull()
    {
        var schemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var assetId = new AssetId(3);
        var externalSlot = CreateExternalSlot(
            new Guid("30000000-0000-4000-8000-000000000026"),
            required: true,
            allowNull: true,
            requiredComponents: []);
        var encoded = SceneAssetReaderTests.Fixtures.CreatePrefab(assetId, externalSlots: [externalSlot]);

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
