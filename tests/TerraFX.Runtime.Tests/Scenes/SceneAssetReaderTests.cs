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
internal sealed class SceneAssetReaderTests
{
    [Test]
    public void ReadsEmptyPrefabAsset()
    {
        var schemas = Fixtures.CreateRegistry();
        var assetId = new AssetId(3);
        var encoded = Fixtures.CreatePrefab(assetId);
        var workspace = new SceneHierarchyValidationWorkspace(entityCount: 0, placementCount: 0);
        var action = new PrefabReadAction(assetId, schemas, workspace);
        var status = EbinStructuralReader.Read(
            encoded,
            EbinReadLimits.Foundation,
            EbinReadPolicy.Canonical,
            ref action,
            out var bytesConsumed,
            out var diagnostic);

        Assert.That(status, Is.EqualTo(EbinReadStatus.Complete), diagnostic?.Category);
        Assert.That(bytesConsumed, Is.EqualTo(encoded.Length));
        var prefab = action.Result
            ?? throw new InvalidOperationException("The Prefab reader did not produce a value.");
        Assert.That(prefab.AssetId, Is.EqualTo(assetId));
        Assert.That(prefab.Entities, Is.Empty);
        Assert.That(prefab.Hierarchy.Nodes, Is.Empty);
        Assert.That(prefab.Hierarchy.Roots, Is.Empty);
        Assert.That(prefab.Placements, Is.Empty);
        Assert.That(prefab.References, Is.Empty);
        Assert.That(prefab.ExternalSlots, Is.Empty);
    }

    [Test]
    public void ReadsPrefabPlacementAndTargetReference()
    {
        var schemas = Fixtures.CreateRegistry();
        var assetId = new AssetId(3);
        var placementId = new Guid("20000000-0000-4000-8000-000000000003");
        var encoded = Fixtures.CreatePrefab(assetId, placementId: placementId);
        var workspace = new SceneHierarchyValidationWorkspace(entityCount: 0, placementCount: 1);
        var action = new PrefabReadAction(assetId, schemas, workspace);
        var status = EbinStructuralReader.Read(
            encoded,
            EbinReadLimits.Foundation,
            EbinReadPolicy.Canonical,
            ref action,
            out _,
            out var diagnostic);

        Assert.That(status, Is.EqualTo(EbinReadStatus.Complete), diagnostic?.Category);
        var prefab = action.Result
            ?? throw new InvalidOperationException("The Prefab reader did not produce a value.");
        Assert.That(prefab.Placements, Has.Length.EqualTo(1));
        Assert.That(prefab.Placements[0].PlacementId, Is.EqualTo(placementId));
        Assert.That(prefab.Placements[0].PrefabAssetId, Is.EqualTo(new AssetId(1)));
        Assert.That(
            prefab.Hierarchy.Roots,
            Is.EqualTo([new SceneHierarchyEntry(SceneHierarchyEntryKind.PrefabPlacement, placementId)]));
    }

    [Test]
    public void ReadsPrefabPlacementExternalSlotBinding()
    {
        var schemas = Fixtures.CreateRegistry();
        var assetId = new AssetId(3);
        var placementId = new Guid("20000000-0000-4000-8000-000000000007");
        var slotId = new Guid("30000000-0000-4000-8000-000000000007");
        var target = Fixtures.CreateReferenceTarget(SceneReferenceTargetKind.ExternalSlot, externalSlotId: slotId);
        var slotBinding = Fixtures.CreatePlacementSlotBinding(slotId, target);
        var encoded = Fixtures.CreatePrefab(assetId, placementId: placementId, slotBindings: [slotBinding]);
        var workspace = new SceneHierarchyValidationWorkspace(entityCount: 0, placementCount: 1);
        var action = new PrefabReadAction(assetId, schemas, workspace);
        var status = EbinStructuralReader.Read(
            encoded,
            EbinReadLimits.Foundation,
            EbinReadPolicy.Canonical,
            ref action,
            out _,
            out var diagnostic);

        Assert.That(status, Is.EqualTo(EbinReadStatus.Complete), diagnostic?.Category);
        var prefab = action.Result
            ?? throw new InvalidOperationException("The Prefab reader did not produce a value.");
        var binding = prefab.Placements[0].SlotBindings[0];
        Assert.That(binding.SlotId, Is.EqualTo(slotId));
        Assert.That(binding.Target?.Kind, Is.EqualTo(SceneReferenceTargetKind.ExternalSlot));
        Assert.That(binding.Target?.ExternalSlotId, Is.EqualTo(slotId));
    }

    [Test]
    public void ReadsPrefabPlacementRootPose()
    {
        var schemas = Fixtures.CreateRegistry();
        var assetId = new AssetId(3);
        var placementId = new Guid("20000000-0000-4000-8000-000000000004");
        var rootPose = Fixtures.CreateRootPose(Fixtures.RootEntityId);
        var encoded = Fixtures.CreatePrefab(assetId, placementId: placementId, placementRootPoses: [rootPose]);
        var workspace = new SceneHierarchyValidationWorkspace(entityCount: 0, placementCount: 1);
        var action = new PrefabReadAction(assetId, schemas, workspace);
        var status = EbinStructuralReader.Read(
            encoded,
            EbinReadLimits.Foundation,
            EbinReadPolicy.Canonical,
            ref action,
            out _,
            out var diagnostic);

        Assert.That(status, Is.EqualTo(EbinReadStatus.Complete), diagnostic?.Category);
        var prefab = action.Result
            ?? throw new InvalidOperationException("The Prefab reader did not produce a value.");
        Assert.That(prefab.Placements[0].RootPoses, Has.Length.EqualTo(1));
        var parsedRootPose = prefab.Placements[0].RootPoses[0];
        Assert.That(parsedRootPose.Target.Scope, Is.EqualTo(SceneEntitySelectorScope.OccurrenceRelative));
        Assert.That(parsedRootPose.Target.PlacementPath, Is.Empty);
        Assert.That(parsedRootPose.Target.AuthoredEntityId, Is.EqualTo(Fixtures.RootEntityId));
        Assert.That(
            parsedRootPose.LocalTransform.Schema.SchemaId,
            Is.EqualTo(new ContentSchemaIdentityAssignment("scene/local-transform").SchemaId));
        Assert.That(parsedRootPose.RootPositionPayload?.Length, Is.EqualTo(24));
        Assert.That(prefab.Placements[0].Overrides, Is.Empty);
        Assert.That(prefab.Placements[0].SlotBindings, Is.Empty);
    }

    [Test]
    public void ReadsPrefabRootEntityAndComponents()
    {
        var schemas = Fixtures.CreateRegistry();
        var assetId = new AssetId(3);
        var encoded = Fixtures.CreatePrefab(assetId, entityId: Fixtures.RootEntityId);
        var workspace = new SceneHierarchyValidationWorkspace(entityCount: 1, placementCount: 0);
        var action = new PrefabReadAction(assetId, schemas, workspace);
        var status = EbinStructuralReader.Read(
            encoded,
            EbinReadLimits.Foundation,
            EbinReadPolicy.Canonical,
            ref action,
            out _,
            out var diagnostic);

        Assert.That(status, Is.EqualTo(EbinReadStatus.Complete), diagnostic?.Category);
        var prefab = action.Result
            ?? throw new InvalidOperationException("The Prefab reader did not produce a value.");
        Assert.That(prefab.Entities, Has.Length.EqualTo(1));
        Assert.That(prefab.Entities[0].AuthoredEntityId, Is.EqualTo(Fixtures.RootEntityId));
        Assert.That(prefab.Entities[0].Components, Has.Length.EqualTo(2));
        Assert.That(prefab.Hierarchy.Nodes, Has.Length.EqualTo(1));
        Assert.That(
            prefab.Hierarchy.Roots,
            Is.EqualTo([new SceneHierarchyEntry(SceneHierarchyEntryKind.Entity, Fixtures.RootEntityId)]));
    }

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
    public void RejectsInvalidRootPoseLocalTransform()
    {
        var schemas = Fixtures.CreateRegistry();
        var assetId = new AssetId(3);
        var placementId = new Guid("20000000-0000-4000-8000-000000000049");
        var rootPose = Fixtures.CreateRootPose(Fixtures.RootEntityId, localPositionX: float.NaN);
        var encoded = Fixtures.CreatePrefab(assetId, placementId: placementId, placementRootPoses: [rootPose]);
        var workspace = new SceneHierarchyValidationWorkspace(entityCount: 0, placementCount: 1);

        void ReadInvalidRootPose()
        {
            var action = new PrefabReadAction(assetId, schemas, workspace);
            _ = EbinStructuralReader.Read(
                encoded,
                EbinReadLimits.Foundation,
                EbinReadPolicy.Canonical,
                ref action,
                out _,
                out _);
        }

        Assert.That(ReadInvalidRootPose, Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RejectsNonFiniteRootPoseRootPosition()
    {
        var schemas = Fixtures.CreateRegistry();
        var assetId = new AssetId(3);
        var placementId = new Guid("20000000-0000-4000-8000-00000000004a");
        var rootPose = Fixtures.CreateRootPose(Fixtures.RootEntityId, rootOffsetX: float.NaN);
        var encoded = Fixtures.CreatePrefab(assetId, placementId: placementId, placementRootPoses: [rootPose]);
        var workspace = new SceneHierarchyValidationWorkspace(entityCount: 0, placementCount: 1);

        void ReadInvalidRootPose()
        {
            var action = new PrefabReadAction(assetId, schemas, workspace);
            _ = EbinStructuralReader.Read(
                encoded,
                EbinReadLimits.Foundation,
                EbinReadPolicy.Canonical,
                ref action,
                out _,
                out _);
        }

        Assert.That(ReadInvalidRootPose, Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RejectsPrefabEnclosingAssetIdMismatch()
    {
        var schemas = Fixtures.CreateRegistry();
        var encoded = Fixtures.CreatePrefab(new AssetId(4));
        var workspace = new SceneHierarchyValidationWorkspace(entityCount: 0, placementCount: 0);

        void ReadMismatchedPrefab()
        {
            var action = new PrefabReadAction(new AssetId(3), schemas, workspace);
            _ = EbinStructuralReader.Read(
                encoded,
                EbinReadLimits.Foundation,
                EbinReadPolicy.Canonical,
                ref action,
                out _,
                out _);
        }

        Assert.That(ReadMismatchedPrefab, Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RejectsPrefabPlacementNestedSchemaMismatch()
    {
        var schemas = Fixtures.CreateRegistry();
        var assetId = new AssetId(3);
        var placementId = new Guid("20000000-0000-4000-8000-000000000005");
        var wrongRootPose = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("native/prefab-override").SchemaId,
            1,
            0);
        var encoded = Fixtures.CreatePrefab(assetId, placementId: placementId, placementRootPoses: [wrongRootPose]);
        var workspace = new SceneHierarchyValidationWorkspace(entityCount: 0, placementCount: 1);

        void ReadMismatchedPlacementRecord()
        {
            var action = new PrefabReadAction(assetId, schemas, workspace);
            _ = EbinStructuralReader.Read(
                encoded,
                EbinReadLimits.Foundation,
                EbinReadPolicy.Canonical,
                ref action,
                out _,
                out _);
        }

        Assert.That(ReadMismatchedPlacementRecord, Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RejectsPrefabPlacementReferenceTargetOperandMismatch()
    {
        var schemas = Fixtures.CreateRegistry();
        var assetId = new AssetId(3);
        var placementId = new Guid("20000000-0000-4000-8000-000000000008");
        var slotId = new Guid("30000000-0000-4000-8000-000000000008");
        var target = Fixtures.CreateReferenceTarget(
            SceneReferenceTargetKind.Internal,
            externalSlotId: new Guid("40000000-0000-4000-8000-000000000008"));
        var slotBinding = Fixtures.CreatePlacementSlotBinding(slotId, target);
        var encoded = Fixtures.CreatePrefab(assetId, placementId: placementId, slotBindings: [slotBinding]);
        var workspace = new SceneHierarchyValidationWorkspace(entityCount: 0, placementCount: 1);

        void ReadInvalidTarget()
        {
            var action = new PrefabReadAction(assetId, schemas, workspace);
            _ = EbinStructuralReader.Read(
                encoded,
                EbinReadLimits.Foundation,
                EbinReadPolicy.Canonical,
                ref action,
                out _,
                out _);
        }

        Assert.That(ReadInvalidTarget, Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RejectsPrefabRootPoseInstanceAbsoluteSelector()
    {
        var schemas = Fixtures.CreateRegistry();
        var assetId = new AssetId(3);
        var placementId = new Guid("20000000-0000-4000-8000-000000000006");
        var rootPose = Fixtures.CreateRootPose(Fixtures.RootEntityId, scope: 2);
        var encoded = Fixtures.CreatePrefab(assetId, placementId: placementId, placementRootPoses: [rootPose]);
        var workspace = new SceneHierarchyValidationWorkspace(entityCount: 0, placementCount: 1);

        void ReadInvalidRootPose()
        {
            var action = new PrefabReadAction(assetId, schemas, workspace);
            _ = EbinStructuralReader.Read(
                encoded,
                EbinReadLimits.Foundation,
                EbinReadPolicy.Canonical,
                ref action,
                out _,
                out _);
        }

        Assert.That(ReadInvalidRootPose, Throws.TypeOf<InvalidDataException>());
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

    internal static class Fixtures
    {
        internal static Guid RootEntityId { get; } = new("10000000-0000-4000-8000-000000000001");

        internal static byte[] CreateAssetReferenceScene(
            AssetId assetId,
            Guid placementId,
            byte[][]? rootPoses = null,
            byte[][]? overrides = null,
            byte[][]? slotBindings = null,
            byte[][]? externalSlots = null,
            byte[][]? references = null,
            byte[][]? sceneOverrides = null)
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
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(4, rootPoses ?? []),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(5, overrides ?? []),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(6, slotBindings ?? []));
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
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(6, references ?? []),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(7, sceneOverrides ?? []),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(8, externalSlots ?? []));
        }

        internal static byte[] CreatePlacementSlotBinding(Guid slotId, byte[]? target)
        {
            SceneHierarchyCodecTests.FixtureBuilder.FixtureField[] fields = target is null
                ? [SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(1, slotId)]
                : [
                    SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(1, slotId),
                    SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(2, target, isRequired: false)
                ];
            return SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
                new ContentSchemaIdentityAssignment("native/placement-slot-binding").SchemaId,
                1,
                0,
                fields);
        }

        internal static byte[] CreatePrefab(
            AssetId assetId,
            Guid? entityId = null,
            Guid? placementId = null,
            byte[][]? placementRootPoses = null,
            byte[][]? overrides = null,
            byte[][]? slotBindings = null,
            byte[][]? externalSlots = null,
            byte[][]? references = null,
            AssetId? placementPrefabAssetId = null,
            Guid? placementParentEntityId = null,
            byte[][]? additionalEntityComponents = null)
        {
            if (placementParentEntityId is not null
                && ((placementId is null) || (entityId != placementParentEntityId)))
            {
                throw new ArgumentException("A child PrefabPlacement must name the authored Entity that owns it.");
            }

            if ((entityId is null) && (additionalEntityComponents is { Length: > 0 }))
            {
                throw new ArgumentException("Additional authored components require a source Entity.");
            }

            var entities = entityId is Guid authoredEntityId
                ? new[] { CreateRootEntity(authoredEntityId, additionalEntityComponents) }
                : [];
            byte[][] entityChildren = (entityId is Guid parentNodeId
                && placementId is Guid childPlacementId
                && placementParentEntityId == parentNodeId)
                    ? [SceneHierarchyCodecTests.FixtureBuilder.EncodeEntry(
                        (byte)SceneHierarchyEntryKind.PrefabPlacement,
                        childPlacementId)]
                    : [];
            var placements = placementId is Guid authoredPlacementId
                ? new[] {
                    CreatePrefabPlacement(
                        authoredPlacementId,
                        placementRootPoses ?? [],
                        overrides ?? [],
                        slotBindings ?? [],
                        placementPrefabAssetId ?? new AssetId(1),
                        placementParentEntityId),
                }
                : [];
            var nodes = entityId is Guid nodeEntityId
                ? new[] { SceneHierarchyCodecTests.FixtureBuilder.EncodeNode(nodeEntityId, parentEntityId: null, entityChildren) }
                : [];
            var entries = new byte[entities.Length + placements.Length][];
            var entryIndex = 0;

            if (entityId is Guid rootEntityId)
            {
                entries[entryIndex++] = SceneHierarchyCodecTests.FixtureBuilder.EncodeEntry(
                    (byte)SceneHierarchyEntryKind.Entity,
                    rootEntityId);
            }

            if ((placementId is Guid rootPlacementId) && (placementParentEntityId is null))
            {
                entries[entryIndex] = SceneHierarchyCodecTests.FixtureBuilder.EncodeEntry(
                    (byte)SceneHierarchyEntryKind.PrefabPlacement,
                    rootPlacementId);
            }

            var hierarchy = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
                new ContentSchemaIdentityAssignment("native/hierarchy").SchemaId,
                2,
                0,
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(1, nodes),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(2, entries));

            return SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
                new ContentSchemaIdentityAssignment("native/prefab").SchemaId,
                2,
                0,
                SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(1, checked((uint)assetId.Value)),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(3, entities),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(4, hierarchy),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(5, placements),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(6, references ?? []),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(8, externalSlots ?? []));
        }

        internal static byte[] CreatePrefabPlacement(
            Guid placementId,
            byte[][] rootPoses,
            byte[][] overrides,
            byte[][] slotBindings,
            AssetId prefabAssetId,
            Guid? parentEntityId = null)
        {
            var prefabReference = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
                new ContentSchemaIdentityAssignment("native/asset-reference").SchemaId,
                1,
                0,
                SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(1, checked((uint)prefabAssetId.Value)),
                SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(
                    2,
                    new ContentSchemaIdentityAssignment("native/prefab").SchemaId),
                SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(3, 2));
            List<SceneHierarchyCodecTests.FixtureBuilder.FixtureField> fields = [
                SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(1, placementId),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(2, prefabReference),
            ];

            if (parentEntityId is { } parentId)
            {
                fields.Add(SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(3, parentId, isRequired: false));
            }

            fields.Add(SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(4, rootPoses));
            fields.Add(SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(5, overrides));
            fields.Add(SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(6, slotBindings));
            return SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
                new ContentSchemaIdentityAssignment("native/prefab-placement").SchemaId,
                1,
                0,
                [.. fields]);
        }

        internal static byte[] CreateReferenceTarget(
            SceneReferenceTargetKind kind,
            Guid? entityId = null,
            Guid? externalSlotId = null)
        {
            List<SceneHierarchyCodecTests.FixtureBuilder.FixtureField> fields = [
                SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(1, (byte)kind),
            ];

            if (entityId is { } targetEntityId)
            {
                fields.Add(
                    SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(
                        2,
                        CreateEntitySelector(targetEntityId),
                        isRequired: false));
            }

            if (externalSlotId is { } targetSlotId)
            {
                fields.Add(
                    SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(
                        3,
                        targetSlotId,
                        isRequired: false));
            }

            return SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
                new ContentSchemaIdentityAssignment("native/reference-target").SchemaId,
                1,
                0,
                [.. fields]);
        }

        internal static CompiledContentSchemaRegistry CreateRegistry()
            => new CompiledContentSchemaRegistry([
                .. CoreContentSchemaCatalogue.PrimitiveSchemas,
                .. CoreContentSchemaCatalogue.FoundationSchemas,
                .. NativeAssetSchemaCatalogue.Schemas,
                .. SceneSchemaCatalogue.Schemas,
            ]);

        internal static byte[] CreateRootEntity(Guid entityId, byte[][]? additionalComponents = null)
        {
            var rootPosition = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
                new ContentSchemaIdentityAssignment("scene/root-position").SchemaId,
                1,
                0,
                SceneHierarchyCodecTests.FixtureBuilder.CreatePosition3Field(1, 0, 0, 0, 0, 0, 0));
            var localTransform = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
                new ContentSchemaIdentityAssignment("scene/local-transform").SchemaId,
                1,
                0,
                SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(1, 15, 0, 0, 0),
                SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(2, 17, 0, 0, 0, 1),
                SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(3, 15, 1, 1, 1));
            var components = new byte[2 + (additionalComponents?.Length ?? 0)][];
            components[0] = rootPosition;
            components[1] = localTransform;
            additionalComponents?.CopyTo(components, 2);
            Array.Sort(
                components,
                static (left, right) => left.AsSpan(16, 16).SequenceCompareTo(right.AsSpan(16, 16)));

            return SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
                new ContentSchemaIdentityAssignment("native/entity").SchemaId,
                1,
                0,
                SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(1, entityId),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(2, components));
        }

        internal static byte[] CreateRootPose(
            Guid entityId,
            byte scope = 1,
            Guid[]? placementPath = null,
            float localPositionX = 0.0f,
            float localScaleX = 1.0f,
            bool includeRootPosition = true,
            int rootChunkX = 0,
            float rootOffsetX = 0.0f)
        {
            var selector = CreateEntitySelector(entityId, scope, placementPath);
            var localTransform = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
                new ContentSchemaIdentityAssignment("scene/local-transform").SchemaId,
                1,
                0,
                SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(1, 15, localPositionX, 0, 0),
                SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(2, 17, 0, 0, 0, 1),
                SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(3, 15, localScaleX, 1, 1));
            List<SceneHierarchyCodecTests.FixtureBuilder.FixtureField> fields = [
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(1, selector),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(2, localTransform),
            ];

            if (includeRootPosition)
            {
                fields.Add(SceneHierarchyCodecTests.FixtureBuilder.CreatePosition3Field(
                    3,
                    rootChunkX,
                    0,
                    0,
                    rootOffsetX,
                    0,
                    0,
                    isRequired: false));
            }

            return SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
                new ContentSchemaIdentityAssignment("native/prefab-root-pose").SchemaId,
                1,
                0,
                [.. fields]);
        }

        internal static byte[] CreateScene(
            AssetId assetId,
            bool includeRootPosition = true,
            float localPositionX = 0.0f,
            byte[][]? externalSlots = null,
            byte[][]? overrides = null,
            byte[][]? references = null)
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
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(6, references ?? []),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(7, overrides ?? []),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(8, externalSlots ?? []));
        }

        internal static byte[] CreateSceneWithChildPlacement(
            AssetId assetId,
            Guid rootEntityId,
            Guid placementId,
            byte[] rootPose)
        {
            var entity = CreateRootEntity(rootEntityId);
            var placement = CreatePrefabPlacement(
                placementId,
                [rootPose],
                [],
                [],
                new AssetId(1),
                rootEntityId);
            var hierarchy = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
                new ContentSchemaIdentityAssignment("native/hierarchy").SchemaId,
                2,
                0,
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(
                    1,
                    SceneHierarchyCodecTests.FixtureBuilder.EncodeNode(
                        rootEntityId,
                        parentEntityId: null,
                        SceneHierarchyCodecTests.FixtureBuilder.EncodeEntry(
                            (byte)SceneHierarchyEntryKind.PrefabPlacement,
                            placementId))),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(
                    2,
                    SceneHierarchyCodecTests.FixtureBuilder.EncodeEntry(
                        (byte)SceneHierarchyEntryKind.Entity,
                        rootEntityId)));

            return SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
                new ContentSchemaIdentityAssignment("native/scene").SchemaId,
                2,
                0,
                SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(1, checked((uint)assetId.Value)),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(2, entity),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(3, hierarchy),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(4, placement),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(6),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(7),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(8));
        }

        private static byte[] CreateEntitySelector(Guid entityId, byte scope = 1, Guid[]? placementPath = null)
            => SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
                new ContentSchemaIdentityAssignment("native/entity-selector").SchemaId,
                1,
                0,
                SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(1, scope),
                SceneHierarchyCodecTests.FixtureBuilder.CreateFixedGuidArrayField(2, placementPath ?? []),
                SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(3, entityId));
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
