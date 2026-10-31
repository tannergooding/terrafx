// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

[TestFixture]
internal sealed class ScenePrefabOverrideApplierTests
{
    private static ScenePrefabOverride CreateComponentOverride(
        ContentSchemaReference componentSchema,
        ImmutableArray<ScenePropertyPathSegment> propertyPath,
        ScenePrefabOverrideOperation operation,
        SceneAuthoredRecord? value = null,
        SceneReferenceTarget? referenceValue = null)
        => new(
            CreateSelector(new Guid("10000000-0000-4000-8000-000000000061")),
            componentSchema,
            propertyPath,
            operation,
            value,
            referenceValue);

    private static DataSchema CreateCustomComponentSchema(
        string name,
        ImmutableArray<ContentFieldDescriptor> fields)
    {
        var identityAssignment = new ContentSchemaIdentityAssignment(name);
        var schema = new ContentSchemaReference(identityAssignment.SchemaId, 1, 0, ContentSchemaScope.Content);
        return new DataSchema(
            schema,
            identityAssignment,
            ContentSchemaRole.Component,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.Persistent,
            ContentTransientRestore.NotApplicable,
            fields);
    }

    private static DataSchema CreateEntityReferenceComponentSchema()
        => CreateCustomComponentSchema(
            "test/prefab-entity-reference-component",
            [
                new ContentFieldDescriptor(
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
                    defaultValue: null),
            ]);

    private static DataSchema CreateFixedArrayComponentSchema()
        => CreateCustomComponentSchema(
            "test/prefab-fixed-array-component",
            [
                new ContentFieldDescriptor(
                    tag: 1,
                    wireKind: 23,
                    elementKind: 3,
                    isRequired: true,
                    minimumCount: 2,
                    maximumCount: 2,
                    nestedSchemas: [],
                    nestedRoleMask: 0,
                    unit: ContentUnit.None,
                    reference: ContentReferenceKind.None,
                    authoringFlags: ContentAuthoring.PrefabEditable,
                    defaultValue: null),
            ]);

    private static byte[] CreateLiteralRecord(string suffix, SceneHierarchyCodecTests.FixtureBuilder.FixtureField field)
        => SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment($"content/literal/{suffix}").SchemaId,
            1,
            0,
            field);

    private static SceneAuthoredComponentRecord CreateLocalTransformRecord()
    {
        var schema = CreateSchemaReference("scene/local-transform");
        var record = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            schema.SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(1, 15, 0, 0, 0),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(2, 17, 0, 0, 0, 1),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(3, 15, 1, 1, 1));
        return new SceneAuthoredComponentRecord(schema, record);
    }

    private static DataSchema CreateNestedComponentSchema(out DataSchema nestedSchema)
    {
        var nestedIdentity = new ContentSchemaIdentityAssignment("test/prefab-nested-value");
        var nestedSchemaReference = new ContentSchemaReference(
            nestedIdentity.SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        nestedSchema = new DataSchema(
            nestedSchemaReference,
            nestedIdentity,
            ContentSchemaRole.Descriptor,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            [
                new ContentFieldDescriptor(
                    tag: 1,
                    wireKind: 1,
                    elementKind: 0,
                    isRequired: true,
                    minimumCount: 1,
                    maximumCount: 1,
                    nestedSchemas: [],
                    nestedRoleMask: 0,
                    unit: ContentUnit.None,
                    reference: ContentReferenceKind.None,
                    authoringFlags: ContentAuthoring.PrefabEditable,
                    defaultValue: null),
            ]);

        return CreateCustomComponentSchema(
            "test/prefab-nested-component",
            [
                new ContentFieldDescriptor(
                    tag: 1,
                    wireKind: 22,
                    elementKind: 0,
                    isRequired: true,
                    minimumCount: 1,
                    maximumCount: 1,
                    nestedSchemas: [nestedSchemaReference],
                    nestedRoleMask: 0,
                    unit: ContentUnit.None,
                    reference: ContentReferenceKind.None,
                    authoringFlags: ContentAuthoring.PrefabEditable,
                    defaultValue: null),
            ]);
    }

    private static DataSchema CreateOptionalComponentSchema()
        => CreateCustomComponentSchema(
            "test/prefab-optional-component",
            [
                new ContentFieldDescriptor(
                    tag: 1,
                    wireKind: 1,
                    elementKind: 0,
                    isRequired: false,
                    minimumCount: 1,
                    maximumCount: 1,
                    nestedSchemas: [],
                    nestedRoleMask: 0,
                    unit: ContentUnit.None,
                    reference: ContentReferenceKind.None,
                    authoringFlags: ContentAuthoring.PrefabEditable,
                    defaultValue: null),
            ]);

    private static CompiledContentSchemaRegistry CreateRegistry(params DataSchema[] additionalSchemas)
    {
        var schemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        return new CompiledContentSchemaRegistry([.. schemas.Schemas, .. additionalSchemas]);
    }

    private static ContentSchemaReference CreateSchemaReference(string schemaName)
        => new(
            new ContentSchemaIdentityAssignment(schemaName).SchemaId,
            1,
            0,
            ContentSchemaScope.Content);

    private static SceneEntitySelector CreateSelector(Guid entityId)
        => new(SceneEntitySelectorScope.OccurrenceRelative, [], entityId);

    private static byte[]? ReadFieldPayload(SceneAuthoredComponentRecord component, uint tag)
    {
        var action = new FieldReadAction(tag);
        var status = EbinStructuralReader.Read(
            component.EncodedRecord,
            EbinReadLimits.Foundation,
            EbinReadPolicy.Canonical,
            ref action,
            out _,
            out var diagnostic);

        if (status is not EbinReadStatus.Complete)
        {
            throw new InvalidOperationException($"The component record failed to parse: {diagnostic?.Category ?? status.ToString()}.");
        }

        return action.Payload;
    }

    private static byte[]? ReadNestedFieldPayload(SceneAuthoredComponentRecord component, uint parentTag, uint childTag)
    {
        var action = new NestedFieldReadAction(parentTag, childTag);
        var status = EbinStructuralReader.Read(
            component.EncodedRecord,
            EbinReadLimits.Foundation,
            EbinReadPolicy.Canonical,
            ref action,
            out _,
            out var diagnostic);

        if (status is not EbinReadStatus.Complete)
        {
            throw new InvalidOperationException($"The component record failed to parse: {diagnostic?.Category ?? status.ToString()}.");
        }

        return action.Payload;
    }

    [Test]
    public void AppliesFixedArrayElementSet()
    {
        var arraySchema = CreateFixedArrayComponentSchema();
        var schemas = CreateRegistry(arraySchema);
        var payload = new byte[40];
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(16), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(20), sizeof(uint));
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(32), 10);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(36), 20);
        var componentBytes = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            arraySchema.Schema.SchemaId,
            1,
            0,
            new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(1, 23, 1, 2, payload));
        var component = new SceneAuthoredComponentRecord(arraySchema.Schema, componentBytes);
        var literalBytes = CreateLiteralRecord("u32", SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(1, 42));
        var literal = new SceneAuthoredRecord(
            CreateSchemaReference("content/literal/u32"),
            literalBytes);
        var prefabOverride = CreateComponentOverride(
            arraySchema.Schema,
            [
                new ScenePropertyPathSegment(ScenePropertyPathSegmentKind.FieldTag, 1),
                new ScenePropertyPathSegment(ScenePropertyPathSegmentKind.FixedArrayIndex, 1),
            ],
            ScenePrefabOverrideOperation.Set,
            literal);

        var result = ScenePrefabOverrideApplier.Apply(component, prefabOverride, schemas, EbinReadLimits.Foundation);
        var updatedPayload = ReadFieldPayload(result, 1)
            ?? throw new InvalidOperationException("The FixedArray field was omitted.");

        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(updatedPayload.AsSpan(32)), Is.EqualTo(10));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(updatedPayload.AsSpan(36)), Is.EqualTo(42));
    }

    [Test]
    public void AppliesOmitToOptionalPrefabEditableField()
    {
        var optionalSchema = CreateOptionalComponentSchema();
        var schemas = CreateRegistry(optionalSchema);
        var componentBytes = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            optionalSchema.Schema.SchemaId,
            1,
            0,
            new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(1, 1, 0, 1, [1]));
        var component = new SceneAuthoredComponentRecord(optionalSchema.Schema, componentBytes);
        var prefabOverride = CreateComponentOverride(
            optionalSchema.Schema,
            [new ScenePropertyPathSegment(ScenePropertyPathSegmentKind.FieldTag, 1)],
            ScenePrefabOverrideOperation.Omit);

        var result = ScenePrefabOverrideApplier.Apply(component, prefabOverride, schemas, EbinReadLimits.Foundation);

        Assert.That(ReadFieldPayload(result, 1), Is.Null);
    }

    [Test]
    public void AppliesSetThroughNestedPrefabEditableFields()
    {
        var componentSchema = CreateNestedComponentSchema(out var nestedSchema);
        var schemas = CreateRegistry(componentSchema, nestedSchema);
        var nestedRecord = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            nestedSchema.Schema.SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(1, 2));
        var componentRecord = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            componentSchema.Schema.SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(1, nestedRecord));
        var component = new SceneAuthoredComponentRecord(componentSchema.Schema, componentRecord);
        var literalRecord = CreateLiteralRecord("u8", SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(1, 9));
        var literal = new SceneAuthoredRecord(CreateSchemaReference("content/literal/u8"), literalRecord);
        var prefabOverride = CreateComponentOverride(
            componentSchema.Schema,
            [
                new ScenePropertyPathSegment(ScenePropertyPathSegmentKind.FieldTag, 1),
                new ScenePropertyPathSegment(ScenePropertyPathSegmentKind.FieldTag, 1),
            ],
            ScenePrefabOverrideOperation.Set,
            literal);

        var result = ScenePrefabOverrideApplier.Apply(component, prefabOverride, schemas, EbinReadLimits.Foundation);
        var updatedPayload = ReadNestedFieldPayload(result, 1, 1)
            ?? throw new InvalidOperationException("The nested PrefabEditable field is absent.");

        Assert.That(updatedPayload, Has.Length.EqualTo(1));
        Assert.That(updatedPayload[0], Is.EqualTo((byte)9));
    }

    [Test]
    public void AppliesSetToEntityReferenceField()
    {
        var entitySchema = CreateEntityReferenceComponentSchema();
        var schemas = CreateRegistry(entitySchema);
        var targetId = new Guid("40000000-0000-4000-8000-000000000061");
        var componentBytes = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            entitySchema.Schema.SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(
                1,
                new Guid("40000000-0000-4000-8000-000000000062")));
        var component = new SceneAuthoredComponentRecord(entitySchema.Schema, componentBytes);
        var literalBytes = CreateLiteralRecord("guid", SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(1, targetId));
        var literal = new SceneAuthoredRecord(CreateSchemaReference("content/literal/guid"), literalBytes);
        var referenceValue = new SceneReferenceTarget(
            SceneReferenceTargetKind.Internal,
            CreateSelector(targetId),
            externalSlotId: null);
        var prefabOverride = CreateComponentOverride(
            entitySchema.Schema,
            [new ScenePropertyPathSegment(ScenePropertyPathSegmentKind.FieldTag, 1)],
            ScenePrefabOverrideOperation.Set,
            literal,
            referenceValue);

        var result = ScenePrefabOverrideApplier.Apply(component, prefabOverride, schemas, EbinReadLimits.Foundation);
        var updatedPayload = ReadFieldPayload(result, 1)
            ?? throw new InvalidOperationException("The Entity field was omitted.");

        Assert.That(new Guid(updatedPayload, bigEndian: true), Is.EqualTo(targetId));
    }

    [Test]
    public void AppliesSetToPrefabEditableField()
    {
        var schemas = CreateRegistry();
        var component = CreateLocalTransformRecord();
        var literalRecord = CreateLiteralRecord(
            "vector3",
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(1, 15, 1, 2, 3));
        var literal = new SceneAuthoredRecord(CreateSchemaReference("content/literal/vector3"), literalRecord);
        var prefabOverride = CreateComponentOverride(
            component.Schema,
            [new ScenePropertyPathSegment(ScenePropertyPathSegmentKind.FieldTag, 1)],
            ScenePrefabOverrideOperation.Set,
            literal);

        var result = ScenePrefabOverrideApplier.Apply(component, prefabOverride, schemas, EbinReadLimits.Foundation);
        var updatedPayload = ReadFieldPayload(result, 1)
            ?? throw new InvalidOperationException("The LocalTransform position field is absent.");

        Assert.That(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(updatedPayload)), Is.EqualTo(1));
        Assert.That(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(updatedPayload.AsSpan(4))), Is.EqualTo(2));
        Assert.That(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(updatedPayload.AsSpan(8))), Is.EqualTo(3));
    }

    [Test]
    public void RejectsSetToNonPrefabEditableField()
    {
        var nonEditableSchema = CreateCustomComponentSchema(
            "test/non-prefab-editable-component",
            [
                new ContentFieldDescriptor(
                    tag: 1,
                    wireKind: 1,
                    elementKind: 0,
                    isRequired: true,
                    minimumCount: 1,
                    maximumCount: 1,
                    nestedSchemas: [],
                    nestedRoleMask: 0,
                    unit: ContentUnit.None,
                    reference: ContentReferenceKind.None,
                    authoringFlags: ContentAuthoring.None,
                    defaultValue: null),
            ]);
        var schemas = CreateRegistry(nonEditableSchema);
        var encoded = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            nonEditableSchema.Schema.SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(1, 1));
        var component = new SceneAuthoredComponentRecord(nonEditableSchema.Schema, encoded);
        var literal = new SceneAuthoredRecord(
            CreateSchemaReference("content/literal/u8"),
            CreateLiteralRecord("u8", SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(1, 2)));
        var prefabOverride = CreateComponentOverride(
            nonEditableSchema.Schema,
            [new ScenePropertyPathSegment(ScenePropertyPathSegmentKind.FieldTag, 1)],
            ScenePrefabOverrideOperation.Set,
            literal);

        Assert.That(
            () => ScenePrefabOverrideApplier.Apply(component, prefabOverride, schemas, EbinReadLimits.Foundation),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RejectsSetWithMismatchedLiteralShape()
    {
        var schemas = CreateRegistry();
        var component = CreateLocalTransformRecord();
        var literal = new SceneAuthoredRecord(
            CreateSchemaReference("content/literal/u8"),
            CreateLiteralRecord("u8", SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(1, 2)));
        var prefabOverride = CreateComponentOverride(
            component.Schema,
            [new ScenePropertyPathSegment(ScenePropertyPathSegmentKind.FieldTag, 1)],
            ScenePrefabOverrideOperation.Set,
            literal);

        Assert.That(
            () => ScenePrefabOverrideApplier.Apply(component, prefabOverride, schemas, EbinReadLimits.Foundation),
            Throws.TypeOf<InvalidDataException>());
    }

    private struct FieldReadAction : IAction<EbinRecordReadView>
    {
        private readonly uint _tag;

        internal FieldReadAction(uint tag)
        {
            _tag = tag;
            Payload = null;
        }

        internal byte[]? Payload { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
            => Payload = record.TryGetField(_tag, out var field) ? field.Payload.ToArray() : null;
    }

    private struct NestedFieldReadAction : IAction<EbinRecordReadView>
    {
        private readonly uint _childTag;
        private readonly uint _parentTag;

        internal NestedFieldReadAction(uint parentTag, uint childTag)
        {
            _childTag = childTag;
            _parentTag = parentTag;
            Payload = null;
        }

        internal byte[]? Payload { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
            => Payload = record.GetRecord(_parentTag).TryGetField(_childTag, out var field)
                ? field.Payload.ToArray()
                : null;
    }
}
