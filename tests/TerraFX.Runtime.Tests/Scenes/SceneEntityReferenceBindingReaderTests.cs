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
internal sealed class SceneEntityReferenceBindingReaderTests
{
    private static byte[] CreateEntityRecord(
        Guid entityId,
        Guid? parentEntityId,
        Guid? entityReferenceSchemaId,
        Guid? entityReferenceTargetId,
        byte[]? referenceComponentRecord = null)
    {
        List<byte[]> components = [CreateLocalTransformRecord()];

        if (parentEntityId is null)
        {
            components.Add(CreateRootPositionRecord());
        }

        if (entityReferenceSchemaId is Guid componentSchemaId
            && entityReferenceTargetId is Guid targetEntityId)
        {
            components.Add(
                SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
                    componentSchemaId,
                    1,
                    0,
                    SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(1, targetEntityId)));
        }

        if (referenceComponentRecord is not null)
        {
            components.Add(referenceComponentRecord);
        }

        components.Sort(static (left, right) => StringComparer.Ordinal.Compare(
            new Guid(left.AsSpan(16, 16), bigEndian: true).ToString("N"),
            new Guid(right.AsSpan(16, 16), bigEndian: true).ToString("N")));
        return SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("native/entity").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(1, entityId),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(2, [.. components]));
    }

    private static byte[] CreateEntityReferenceArrayComponentRecord(Guid componentSchemaId, params Guid[] targets)
        => SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            componentSchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateFixedGuidArrayField(1, targets));

    private static DataSchema CreateEntityReferenceArrayComponentSchema()
    {
        var identityAssignment = new ContentSchemaIdentityAssignment("test/entity-reference-array-component");
        var schema = new ContentSchemaReference(
            identityAssignment.SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var field = new ContentFieldDescriptor(
            tag: 1,
            wireKind: 23,
            elementKind: 11,
            isRequired: true,
            minimumCount: 0,
            maximumCount: 2,
            nestedSchemas: [],
            nestedRoleMask: 0,
            unit: ContentUnit.None,
            reference: ContentReferenceKind.Entity,
            authoringFlags: ContentAuthoring.None,
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

    private static byte[] CreateEntityReferenceBinding(
        Guid componentSchemaId,
        byte sourceScope,
        Guid sourceEntityId,
        byte targetScope,
        Guid targetEntityId,
        params ScenePropertyPathSegment[] propertyPath)
    {
        var path = propertyPath.Length == 0
            ? [new ScenePropertyPathSegment(ScenePropertyPathSegmentKind.FieldTag, 1)]
            : propertyPath;
        var segments = new byte[path.Length][];

        for (var index = 0; index < path.Length; index++)
        {
            segments[index] = CreatePropertySegment((byte)path[index].Kind, path[index].Value);
        }

        return SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("native/entity-reference-binding").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(
                1,
                CreateEntitySelector(sourceScope, sourceEntityId)),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(2, CreateSchemaReference(componentSchemaId)),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(3, segments),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(
                4,
                CreateReferenceTarget(targetScope, targetEntityId)));
    }

    private static byte[] CreateEntityReferenceBindingToExternalSlot(
        Guid componentSchemaId,
        byte sourceScope,
        Guid sourceEntityId,
        Guid externalSlotId)
        => SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
                new ContentSchemaIdentityAssignment("native/entity-reference-binding").SchemaId,
                1,
                0,
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(
                    1,
                    CreateEntitySelector(sourceScope, sourceEntityId)),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(2, CreateSchemaReference(componentSchemaId)),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(
                    3,
                    CreatePropertySegment((byte)ScenePropertyPathSegmentKind.FieldTag, 1)),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(
                    4,
                    CreateExternalSlotReferenceTarget(externalSlotId)));

    private static DataSchema CreateEntityReferenceComponentSchema(bool optional = false)
    {
        var identityAssignment = new ContentSchemaIdentityAssignment("test/entity-reference-component");
        var schema = new ContentSchemaReference(
            identityAssignment.SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var field = new ContentFieldDescriptor(
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
            authoringFlags: ContentAuthoring.None,
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

    private static byte[] CreateEntitySelector(byte scope, Guid entityId)
        => SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("native/entity-selector").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(1, scope),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFixedGuidArrayField(2),
            SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(3, entityId));

    private static byte[] CreateExternalSlot(Guid slotId)
        => SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("native/external-slot").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(1, slotId),
            new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(2, 9, 1, 1, [0]),
            new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(3, 9, 1, 1, [1]),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(4));

    private static byte[] CreateExternalSlotReferenceTarget(Guid slotId)
        => SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("native/reference-target").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(1, (byte)SceneReferenceTargetKind.ExternalSlot),
            SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(3, slotId, isRequired: false));

    private static byte[] CreateHierarchyRecord(Guid rootEntityId, Guid childEntityId)
    {
        var rootNode = SceneHierarchyCodecTests.FixtureBuilder.EncodeNode(
            rootEntityId,
            parentEntityId: null,
            SceneHierarchyCodecTests.FixtureBuilder.EncodeEntry(
                (byte)SceneHierarchyEntryKind.Entity,
                childEntityId));
        var childNode = SceneHierarchyCodecTests.FixtureBuilder.EncodeNode(childEntityId, rootEntityId);
        var root = SceneHierarchyCodecTests.FixtureBuilder.EncodeEntry(
            (byte)SceneHierarchyEntryKind.Entity,
            rootEntityId);
        return SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("native/hierarchy").SchemaId,
            2,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(1, rootNode, childNode),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(2, root));
    }

    private static byte[] CreateLocalTransformRecord()
        => SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("scene/local-transform").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(1, 15, 0, 0, 0),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(2, 17, 0, 0, 0, 1),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(3, 15, 1, 1, 1));

    private static byte[] CreatePrefabRecord(
        AssetId assetId,
        Guid rootEntityId,
        Guid childEntityId,
        byte[] rootEntity,
        byte[] childEntity,
        byte[][]? externalSlots = null,
        params byte[][] references)
        => SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("native/prefab").SchemaId,
            2,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(1, checked((uint)assetId.Value)),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(3, rootEntity, childEntity),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(
                4,
                CreateHierarchyRecord(rootEntityId, childEntityId)),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(5),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(6, references),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(8, externalSlots ?? []));

    private static byte[] CreatePropertySegment(byte kind, uint value)
        => SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("content/property-segment").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(1, kind),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(2, value));

    private static byte[] CreateReferenceTarget(byte selectorScope, Guid entityId)
        => SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("native/reference-target").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(1, (byte)SceneReferenceTargetKind.Internal),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(
                2,
                CreateEntitySelector(selectorScope, entityId),
                isRequired: false));

    private static CompiledContentSchemaRegistry CreateRegistry(DataSchema componentSchema)
    {
        var schemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        return new CompiledContentSchemaRegistry([.. schemas.Schemas, componentSchema]);
    }

    private static byte[] CreateRootPositionRecord()
        => SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("scene/root-position").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreatePosition3Field(1, 0, 0, 0, 0, 0, 0));

    private static byte[] CreateSceneRecord(
        AssetId assetId,
        Guid rootEntityId,
        Guid childEntityId,
        byte[] rootEntity,
        byte[] childEntity,
        params byte[][] references)
        => SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("native/scene").SchemaId,
            2,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(1, checked((uint)assetId.Value)),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(2, rootEntity, childEntity),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(
                3,
                CreateHierarchyRecord(rootEntityId, childEntityId)),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(4),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(6, references),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(7),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(8));

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
        var action = new PrefabReadAction(
            assetId,
            schemas,
            new SceneHierarchyValidationWorkspace(entityCount: 2, placementCount: 0));
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
            new SceneHierarchyValidationWorkspace(entityCount: 2, placementCount: 0));
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
    public void AllowsAbsentOptionalEntityReferenceWithoutBinding()
    {
        var componentSchema = CreateEntityReferenceComponentSchema(optional: true);
        var schemas = CreateRegistry(componentSchema);
        var rootEntityId = SceneAssetReaderTests.Fixtures.RootEntityId;
        var childEntityId = new Guid("20000000-0000-4000-8000-000000000070");
        var optionalComponent = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            componentSchema.Schema.SchemaId,
            1,
            0);
        var rootEntity = CreateEntityRecord(
            rootEntityId,
            parentEntityId: null,
            entityReferenceSchemaId: null,
            entityReferenceTargetId: null,
            optionalComponent);
        var childEntity = CreateEntityRecord(childEntityId, rootEntityId, entityReferenceSchemaId: null, entityReferenceTargetId: null);

        var prefab = ReadPrefabAsset(
            CreatePrefabRecord(new AssetId(1), rootEntityId, childEntityId, rootEntity, childEntity),
            new AssetId(1),
            schemas);

        Assert.That(prefab.References, Is.Empty);
    }

    [Test]
    public void AllowsDeclaredExternalSlotEntityReference()
    {
        var componentSchema = CreateEntityReferenceComponentSchema();
        var schemas = CreateRegistry(componentSchema);
        var rootEntityId = SceneAssetReaderTests.Fixtures.RootEntityId;
        var childEntityId = new Guid("20000000-0000-4000-8000-000000000071");
        var externalSlotId = new Guid("30000000-0000-4000-8000-000000000071");
        var rootEntity = CreateEntityRecord(rootEntityId, null, componentSchema.Schema.SchemaId, externalSlotId);
        var childEntity = CreateEntityRecord(childEntityId, rootEntityId, entityReferenceSchemaId: null, entityReferenceTargetId: null);
        var binding = CreateEntityReferenceBindingToExternalSlot(
            componentSchema.Schema.SchemaId,
            sourceScope: (byte)SceneEntitySelectorScope.OccurrenceRelative,
            rootEntityId,
            externalSlotId);
        var externalSlot = CreateExternalSlot(externalSlotId);

        var prefab = ReadPrefabAsset(
            CreatePrefabRecord(
                new AssetId(1),
                rootEntityId,
                childEntityId,
                rootEntity,
                childEntity,
                externalSlots: [externalSlot],
                references: [binding]),
            new AssetId(1),
            schemas);

        Assert.That(prefab.References[0].Target.Kind, Is.EqualTo(SceneReferenceTargetKind.ExternalSlot));
        Assert.That(prefab.References[0].Target.ExternalSlotId, Is.EqualTo(externalSlotId));
    }

    [Test]
    public void ReadsEveryFixedArrayEntityReferenceLeaf()
    {
        var componentSchema = CreateEntityReferenceArrayComponentSchema();
        var schemas = CreateRegistry(componentSchema);
        var rootEntityId = SceneAssetReaderTests.Fixtures.RootEntityId;
        var childEntityId = new Guid("20000000-0000-4000-8000-000000000066");
        var rootEntity = CreateEntityRecord(
            rootEntityId,
            parentEntityId: null,
            entityReferenceSchemaId: null,
            entityReferenceTargetId: null,
            CreateEntityReferenceArrayComponentRecord(componentSchema.Schema.SchemaId, childEntityId, childEntityId));
        var childEntity = CreateEntityRecord(childEntityId, rootEntityId, entityReferenceSchemaId: null, entityReferenceTargetId: null);
        var firstBinding = CreateEntityReferenceBinding(
            componentSchema.Schema.SchemaId,
            sourceScope: (byte)SceneEntitySelectorScope.OccurrenceRelative,
            rootEntityId,
            targetScope: (byte)SceneEntitySelectorScope.OccurrenceRelative,
            childEntityId,
            new ScenePropertyPathSegment(ScenePropertyPathSegmentKind.FieldTag, 1),
            new ScenePropertyPathSegment(ScenePropertyPathSegmentKind.FixedArrayIndex, 0));
        var secondBinding = CreateEntityReferenceBinding(
            componentSchema.Schema.SchemaId,
            sourceScope: (byte)SceneEntitySelectorScope.OccurrenceRelative,
            rootEntityId,
            targetScope: (byte)SceneEntitySelectorScope.OccurrenceRelative,
            childEntityId,
            new ScenePropertyPathSegment(ScenePropertyPathSegmentKind.FieldTag, 1),
            new ScenePropertyPathSegment(ScenePropertyPathSegmentKind.FixedArrayIndex, 1));

        var prefab = ReadPrefabAsset(
            CreatePrefabRecord(
                new AssetId(1),
                rootEntityId,
                childEntityId,
                rootEntity,
                childEntity,
                references: [firstBinding, secondBinding]),
            new AssetId(1),
            schemas);

        Assert.That(prefab.References, Has.Length.EqualTo(2));
        Assert.That(prefab.References[0].PropertyPath[^1].Value, Is.Zero);
        Assert.That(prefab.References[1].PropertyPath[^1].Value, Is.EqualTo(1));
    }

    [Test]
    public void ReadsPrefabReferenceBindingWithOccurrenceRelativeSelectors()
    {
        var componentSchema = CreateEntityReferenceComponentSchema();
        var schemas = CreateRegistry(componentSchema);
        var rootEntityId = SceneAssetReaderTests.Fixtures.RootEntityId;
        var childEntityId = new Guid("20000000-0000-4000-8000-000000000062");
        var rootEntity = CreateEntityRecord(
            rootEntityId,
            parentEntityId: null,
            entityReferenceSchemaId: componentSchema.Schema.SchemaId,
            entityReferenceTargetId: childEntityId);
        var childEntity = CreateEntityRecord(childEntityId, rootEntityId, entityReferenceSchemaId: null, entityReferenceTargetId: null);
        var reference = CreateEntityReferenceBinding(
            componentSchema.Schema.SchemaId,
            sourceScope: (byte)SceneEntitySelectorScope.OccurrenceRelative,
            rootEntityId,
            targetScope: (byte)SceneEntitySelectorScope.OccurrenceRelative,
            childEntityId);
        var prefab = ReadPrefabAsset(
            CreatePrefabRecord(
                new AssetId(1),
                rootEntityId,
                childEntityId,
                rootEntity,
                childEntity,
                references: [reference]),
            new AssetId(1),
            schemas);
        var binding = prefab.References[0];

        Assert.That(prefab.References, Has.Length.EqualTo(1));
        Assert.That(binding.Source.Scope, Is.EqualTo(SceneEntitySelectorScope.OccurrenceRelative));
        Assert.That(binding.Source.AuthoredEntityId, Is.EqualTo(rootEntityId));
        Assert.That(binding.ComponentSchema, Is.EqualTo(componentSchema.Schema));
        Assert.That(binding.PropertyPath, Is.EqualTo([
            new ScenePropertyPathSegment(ScenePropertyPathSegmentKind.FieldTag, 1),
        ]));
        Assert.That(binding.Target.Kind, Is.EqualTo(SceneReferenceTargetKind.Internal));
        Assert.That(binding.Target.Entity?.AuthoredEntityId, Is.EqualTo(childEntityId));
    }

    [Test]
    public void ReadsSceneReferenceBindingWithInstanceAbsoluteSelectors()
    {
        var componentSchema = CreateEntityReferenceComponentSchema();
        var schemas = CreateRegistry(componentSchema);
        var rootEntityId = SceneAssetReaderTests.Fixtures.RootEntityId;
        var childEntityId = new Guid("20000000-0000-4000-8000-000000000063");
        var rootEntity = CreateEntityRecord(
            rootEntityId,
            parentEntityId: null,
            entityReferenceSchemaId: componentSchema.Schema.SchemaId,
            entityReferenceTargetId: childEntityId);
        var childEntity = CreateEntityRecord(childEntityId, rootEntityId, entityReferenceSchemaId: null, entityReferenceTargetId: null);
        var reference = CreateEntityReferenceBinding(
            componentSchema.Schema.SchemaId,
            sourceScope: (byte)SceneEntitySelectorScope.InstanceAbsolute,
            rootEntityId,
            targetScope: (byte)SceneEntitySelectorScope.InstanceAbsolute,
            childEntityId);
        var scene = ReadSceneAsset(
            CreateSceneRecord(new AssetId(3), rootEntityId, childEntityId, rootEntity, childEntity, reference),
            new AssetId(3),
            schemas);

        Assert.That(scene.References, Has.Length.EqualTo(1));
        Assert.That(scene.References[0].Source.Scope, Is.EqualTo(SceneEntitySelectorScope.InstanceAbsolute));
        Assert.That(scene.References[0].Target.Entity?.Scope, Is.EqualTo(SceneEntitySelectorScope.InstanceAbsolute));
    }

    [Test]
    public void RejectsEntityReferenceBindingToUndeclaredExternalSlot()
    {
        var componentSchema = CreateEntityReferenceComponentSchema();
        var schemas = CreateRegistry(componentSchema);
        var rootEntityId = SceneAssetReaderTests.Fixtures.RootEntityId;
        var childEntityId = new Guid("20000000-0000-4000-8000-000000000072");
        var externalSlotId = new Guid("30000000-0000-4000-8000-000000000072");
        var rootEntity = CreateEntityRecord(rootEntityId, null, componentSchema.Schema.SchemaId, externalSlotId);
        var childEntity = CreateEntityRecord(childEntityId, rootEntityId, entityReferenceSchemaId: null, entityReferenceTargetId: null);
        var binding = CreateEntityReferenceBindingToExternalSlot(
            componentSchema.Schema.SchemaId,
            sourceScope: (byte)SceneEntitySelectorScope.OccurrenceRelative,
            rootEntityId,
            externalSlotId);

        Assert.That(
            () => ReadPrefabAsset(
                CreatePrefabRecord(
                    new AssetId(1),
                    rootEntityId,
                    childEntityId,
                    rootEntity,
                    childEntity,
                    references: [binding]),
                new AssetId(1),
                schemas),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RejectsEntityReferenceBindingWithMismatchedSymbolicTarget()
    {
        var componentSchema = CreateEntityReferenceComponentSchema();
        var schemas = CreateRegistry(componentSchema);
        var rootEntityId = SceneAssetReaderTests.Fixtures.RootEntityId;
        var childEntityId = new Guid("20000000-0000-4000-8000-000000000068");
        var mismatchedTargetId = new Guid("20000000-0000-4000-8000-000000000069");
        var rootEntity = CreateEntityRecord(rootEntityId, null, componentSchema.Schema.SchemaId, childEntityId);
        var childEntity = CreateEntityRecord(childEntityId, rootEntityId, entityReferenceSchemaId: null, entityReferenceTargetId: null);
        var binding = CreateEntityReferenceBinding(
            componentSchema.Schema.SchemaId,
            sourceScope: (byte)SceneEntitySelectorScope.OccurrenceRelative,
            rootEntityId,
            targetScope: (byte)SceneEntitySelectorScope.OccurrenceRelative,
            mismatchedTargetId);

        Assert.That(
            () => ReadPrefabAsset(
                CreatePrefabRecord(
                    new AssetId(1),
                    rootEntityId,
                    childEntityId,
                    rootEntity,
                    childEntity,
                    references: [binding]),
                new AssetId(1),
                schemas),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RejectsEntityReferenceBindingWithUnmatchedPropertyPath()
    {
        var componentSchema = CreateEntityReferenceComponentSchema();
        var schemas = CreateRegistry(componentSchema);
        var rootEntityId = SceneAssetReaderTests.Fixtures.RootEntityId;
        var childEntityId = new Guid("20000000-0000-4000-8000-000000000073");
        var rootEntity = CreateEntityRecord(rootEntityId, null, componentSchema.Schema.SchemaId, childEntityId);
        var childEntity = CreateEntityRecord(childEntityId, rootEntityId, entityReferenceSchemaId: null, entityReferenceTargetId: null);
        var binding = CreateEntityReferenceBinding(
            componentSchema.Schema.SchemaId,
            sourceScope: (byte)SceneEntitySelectorScope.OccurrenceRelative,
            rootEntityId,
            targetScope: (byte)SceneEntitySelectorScope.OccurrenceRelative,
            childEntityId,
            new ScenePropertyPathSegment(ScenePropertyPathSegmentKind.FieldTag, 2));

        Assert.That(
            () => ReadPrefabAsset(
                CreatePrefabRecord(
                    new AssetId(1),
                    rootEntityId,
                    childEntityId,
                    rootEntity,
                    childEntity,
                    references: [binding]),
                new AssetId(1),
                schemas),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RejectsIncompleteFixedArrayEntityReferenceCoverage()
    {
        var componentSchema = CreateEntityReferenceArrayComponentSchema();
        var schemas = CreateRegistry(componentSchema);
        var rootEntityId = SceneAssetReaderTests.Fixtures.RootEntityId;
        var childEntityId = new Guid("20000000-0000-4000-8000-000000000074");
        var rootEntity = CreateEntityRecord(
            rootEntityId,
            parentEntityId: null,
            entityReferenceSchemaId: null,
            entityReferenceTargetId: null,
            CreateEntityReferenceArrayComponentRecord(componentSchema.Schema.SchemaId, childEntityId, childEntityId));
        var childEntity = CreateEntityRecord(childEntityId, rootEntityId, entityReferenceSchemaId: null, entityReferenceTargetId: null);
        var binding = CreateEntityReferenceBinding(
            componentSchema.Schema.SchemaId,
            sourceScope: (byte)SceneEntitySelectorScope.OccurrenceRelative,
            rootEntityId,
            targetScope: (byte)SceneEntitySelectorScope.OccurrenceRelative,
            childEntityId,
            new ScenePropertyPathSegment(ScenePropertyPathSegmentKind.FieldTag, 1),
            new ScenePropertyPathSegment(ScenePropertyPathSegmentKind.FixedArrayIndex, 0));

        Assert.That(
            () => ReadPrefabAsset(
                CreatePrefabRecord(
                    new AssetId(1),
                    rootEntityId,
                    childEntityId,
                    rootEntity,
                    childEntity,
                    references: [binding]),
                new AssetId(1),
                schemas),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RejectsMissingEntityReferenceBinding()
    {
        var componentSchema = CreateEntityReferenceComponentSchema();
        var schemas = CreateRegistry(componentSchema);
        var rootEntityId = SceneAssetReaderTests.Fixtures.RootEntityId;
        var childEntityId = new Guid("20000000-0000-4000-8000-000000000067");
        var rootEntity = CreateEntityRecord(rootEntityId, null, componentSchema.Schema.SchemaId, childEntityId);
        var childEntity = CreateEntityRecord(childEntityId, rootEntityId, entityReferenceSchemaId: null, entityReferenceTargetId: null);

        Assert.That(
            () => ReadPrefabAsset(
                CreatePrefabRecord(new AssetId(1), rootEntityId, childEntityId, rootEntity, childEntity),
                new AssetId(1),
                schemas),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RejectsPrefabReferenceBindingWithInstanceAbsoluteSource()
    {
        var componentSchema = CreateEntityReferenceComponentSchema();
        var schemas = CreateRegistry(componentSchema);
        var rootEntityId = SceneAssetReaderTests.Fixtures.RootEntityId;
        var childEntityId = new Guid("20000000-0000-4000-8000-000000000064");
        var rootEntity = CreateEntityRecord(
            rootEntityId,
            parentEntityId: null,
            entityReferenceSchemaId: componentSchema.Schema.SchemaId,
            entityReferenceTargetId: childEntityId);
        var childEntity = CreateEntityRecord(childEntityId, rootEntityId, entityReferenceSchemaId: null, entityReferenceTargetId: null);
        var reference = CreateEntityReferenceBinding(
            componentSchema.Schema.SchemaId,
            sourceScope: (byte)SceneEntitySelectorScope.InstanceAbsolute,
            rootEntityId,
            targetScope: (byte)SceneEntitySelectorScope.OccurrenceRelative,
            childEntityId);

        Assert.That(
            () => ReadPrefabAsset(
                CreatePrefabRecord(
                    new AssetId(1),
                    rootEntityId,
                    childEntityId,
                    rootEntity,
                    childEntity,
                    references: [reference]),
                new AssetId(1),
                schemas),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RejectsPrefabReferenceBindingWithInstanceAbsoluteTarget()
    {
        var componentSchema = CreateEntityReferenceComponentSchema();
        var schemas = CreateRegistry(componentSchema);
        var rootEntityId = SceneAssetReaderTests.Fixtures.RootEntityId;
        var childEntityId = new Guid("20000000-0000-4000-8000-000000000065");
        var rootEntity = CreateEntityRecord(
            rootEntityId,
            parentEntityId: null,
            entityReferenceSchemaId: componentSchema.Schema.SchemaId,
            entityReferenceTargetId: childEntityId);
        var childEntity = CreateEntityRecord(childEntityId, rootEntityId, entityReferenceSchemaId: null, entityReferenceTargetId: null);
        var reference = CreateEntityReferenceBinding(
            componentSchema.Schema.SchemaId,
            sourceScope: (byte)SceneEntitySelectorScope.OccurrenceRelative,
            rootEntityId,
            targetScope: (byte)SceneEntitySelectorScope.InstanceAbsolute,
            childEntityId);

        Assert.That(
            () => ReadPrefabAsset(
                CreatePrefabRecord(
                    new AssetId(1),
                    rootEntityId,
                    childEntityId,
                    rootEntity,
                    childEntity,
                    references: [reference]),
                new AssetId(1),
                schemas),
            Throws.TypeOf<InvalidDataException>());
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
