// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class ComponentFieldBindingTests
{
    private static ComponentSchema<int> CreateComponentSchema()
    {
        var nestedAssignment = new ContentSchemaIdentityAssignment("application/descriptor/binding-nested");
        var nestedReference = new ContentSchemaReference(nestedAssignment.SchemaId, 1, 0, ContentSchemaScope.Content);
        var nestedSchema = new DataSchema(
            nestedReference,
            nestedAssignment,
            ContentSchemaRole.Descriptor,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            [CreateField(1, 3)]);

        var assignment = new ContentSchemaIdentityAssignment("application/component/binding-test");
        var schemaReference = new ContentSchemaReference(assignment.SchemaId, 1, 0, ContentSchemaScope.Content);
        var schema = new DataSchema(
            schemaReference,
            assignment,
            ContentSchemaRole.Component,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.Persistent,
            ContentTransientRestore.NotApplicable,
            [
                CreateField(1, 3),
                CreateField(2, 9, isRequired: false),
                CreateField(3, 11, reference: ContentReferenceKind.Entity),
                CreateField(4, 11, reference: ContentReferenceKind.World),
                CreateField(5, 23, elementKind: 1, minimumCount: 1, maximumCount: 3),
                CreateField(6, 22, nestedSchemas: [nestedReference]),
                CreateField(7, 11),
                CreateField(8, 11, isRequired: false, reference: ContentReferenceKind.Entity),
                CreateField(9, 11, isRequired: false, reference: ContentReferenceKind.World),
                CreateField(10, 22, isRequired: false, nestedSchemas: [nestedReference]),
            ]);
        var registry = new CompiledContentSchemaRegistry([nestedSchema, schema]);
        var layout = new ComponentLayout(new Guid("00112233-4455-4677-8899-aabbccddeeff"), sizeof(int));
        return ComponentSchema<int>.FromRegistry(registry, schemaReference, layout);
    }

    private static ContentFieldDescriptor CreateField(
        uint tag,
        ushort wireKind,
        bool isRequired = true,
        ushort elementKind = 0,
        ulong minimumCount = 1,
        ulong maximumCount = 1,
        ContentReferenceKind reference = ContentReferenceKind.None,
        ImmutableArray<ContentSchemaReference> nestedSchemas = default)
        => new(
            tag,
            wireKind,
            elementKind,
            isRequired,
            minimumCount,
            maximumCount,
            nestedSchemas.IsDefault ? [] : nestedSchemas,
            0,
            ContentUnit.None,
            reference,
            ContentAuthoring.None,
            null);

    private static ComponentStoragePath MemberPath(string name)
        => new([ComponentStorageSegment.Member(name)]);

    [Test]
    public void TypedFieldBindingsRequireExactShapeAndCompleteRecordClosure()
    {
        var schema = CreateComponentSchema();
        var scalarField = schema.GetField(new ComponentFieldPath([ComponentPropertySegment.FieldTag(1)]));
        var optionalField = schema.GetField(new ComponentFieldPath([ComponentPropertySegment.FieldTag(2)]));
        var entityField = schema.GetField(new ComponentFieldPath([ComponentPropertySegment.FieldTag(3)]));
        var stableGuidField = schema.GetField(new ComponentFieldPath([ComponentPropertySegment.FieldTag(4)]));
        var arrayField = schema.GetField(new ComponentFieldPath([ComponentPropertySegment.FieldTag(5)]));
        var aggregateField = schema.GetField(new ComponentFieldPath([ComponentPropertySegment.FieldTag(6)]));
        var opaqueGuidField = schema.GetField(new ComponentFieldPath([ComponentPropertySegment.FieldTag(7)]));
        var optionalEntityField = schema.GetField(new ComponentFieldPath([ComponentPropertySegment.FieldTag(8)]));
        var optionalStableGuidField = schema.GetField(new ComponentFieldPath([ComponentPropertySegment.FieldTag(9)]));
        var optionalAggregateField = schema.GetField(new ComponentFieldPath([ComponentPropertySegment.FieldTag(10)]));

        var scalar = ComponentFieldBinding<int, AuthorValue>.Scalar<uint, UIntAccessor, UIntReader>(
            scalarField,
            [MemberPath("value")],
            default,
            default);
        var optional = ComponentFieldBinding<int, AuthorValue>.Optional<bool, BoolAccessor, BoolReader>(
            optionalField,
            [MemberPath("optional")],
            default,
            default);
        var entity = ComponentFieldBinding<int, AuthorValue>.EntityReference<EntityAccessor, GuidReader>(
            entityField,
            [MemberPath("entity")],
            default,
            default);
        var stableGuid = ComponentFieldBinding<int, AuthorValue>.StableGuidReference<GuidAccessor, GuidReader>(
            stableGuidField,
            [MemberPath("world")],
            default,
            default);
        var opaqueGuid = ComponentFieldBinding<int, AuthorValue>.Scalar<Guid, GuidAccessor, GuidReader>(
            opaqueGuidField,
            [MemberPath("opaqueGuid")],
            default,
            default);
        var fixedArray = ComponentFieldBinding<int, AuthorValue>.FixedArray<byte, ByteArrayAccessor, ByteArrayReader>(
            arrayField,
            [MemberPath("bytes")],
            default,
            default);
        var childField = schema.GetField(new ComponentFieldPath([
            ComponentPropertySegment.FieldTag(6),
            ComponentPropertySegment.FieldTag(1),
        ]));
        var child = ComponentFieldBinding<int, AuthorValue>.Scalar<uint, UIntAccessor, UIntReader>(
            childField,
            [MemberPath("nestedValue")],
            default,
            default);
        var aggregate = ComponentFieldBinding<int, AuthorValue>.Aggregate<uint, uint, UIntAccessor, UIntReader>(
            aggregateField,
            [],
            default,
            default,
            [child]);
        var optionalEntity = ComponentFieldBinding<int, AuthorValue>.OptionalEntityReference<OptionalEntityAccessor, OptionalGuidReader>(
            optionalEntityField,
            [MemberPath("optionalEntity")],
            default,
            default);
        var optionalStableGuid = ComponentFieldBinding<int, AuthorValue>.OptionalStableGuidReference<OptionalGuidAccessor, OptionalGuidReader>(
            optionalStableGuidField,
            [MemberPath("optionalWorld")],
            default,
            default);
        var optionalAggregateChildField = schema.GetField(new ComponentFieldPath([
            ComponentPropertySegment.FieldTag(10),
            ComponentPropertySegment.FieldTag(1),
        ]));
        var optionalAggregateChild = ComponentFieldBinding<int, AuthorValue>.Scalar<uint, UIntAccessor, UIntReader>(
            optionalAggregateChildField,
            [MemberPath("optionalNestedValue")],
            default,
            default);
        var optionalAggregate = ComponentFieldBinding<int, AuthorValue>.OptionalAggregate<uint, uint, OptionalUIntAccessor, OptionalUIntReader>(
            optionalAggregateField,
            [],
            default,
            default,
            [optionalAggregateChild]);

        Assert.That(scalar.Descriptor.Field.WireKind, Is.EqualTo(3));
        Assert.That(optional.Descriptor.Field.IsRequired, Is.False);
        Assert.That(entity.Descriptor.Field.Reference, Is.EqualTo(ContentReferenceKind.Entity));
        Assert.That(stableGuid.Descriptor.Field.Reference, Is.EqualTo(ContentReferenceKind.World));
        Assert.That(opaqueGuid.Descriptor.Field.Reference, Is.EqualTo(ContentReferenceKind.None));
        Assert.That(fixedArray.Descriptor.Field.ElementKind, Is.EqualTo(1));
        Assert.That(aggregate.Children, Has.Length.EqualTo(1));
        Assert.That(optionalEntity.Descriptor.Field.IsRequired, Is.False);
        Assert.That(optionalStableGuid.Descriptor.Field.Reference, Is.EqualTo(ContentReferenceKind.World));
        Assert.That(optionalAggregate.Children, Has.Length.EqualTo(1));

        Assert.That(() => ComponentFieldBinding<int, AuthorValue>.Scalar<Guid, GuidAccessor, GuidReader>(
            entityField,
            [MemberPath("entity")],
            default,
            default), Throws.ArgumentException);
        Assert.That(() => ComponentFieldBinding<int, AuthorValue>.StableGuidReference<GuidAccessor, GuidReader>(
            opaqueGuidField,
            [MemberPath("opaqueGuid")],
            default,
            default), Throws.ArgumentException);
        Assert.That(() => ComponentFieldBinding<int, AuthorValue>.EntityReference<EntityAccessor, GuidReader>(
            stableGuidField,
            [MemberPath("world")],
            default,
            default), Throws.ArgumentException);
        Assert.That(() => ComponentFieldBinding<int, AuthorValue>.Optional<bool, BoolAccessor, BoolReader>(
            scalarField,
            [MemberPath("value")],
            default,
            default), Throws.ArgumentException);
        Assert.That(() => ComponentFieldBinding<int, AuthorValue>.FixedArray<ushort, UShortArrayAccessor, UShortArrayReader>(
            arrayField,
            [MemberPath("bytes")],
            default,
            default), Throws.ArgumentException);
        Assert.That(() => ComponentFieldBinding<int, AuthorValue>.Scalar<uint, UIntAccessor, UIntReader>(
            scalarField,
            [MemberPath("value"), MemberPath("value")],
            default,
            default), Throws.ArgumentException);
        Assert.That(() => ComponentFieldBinding<int, AuthorValue>.Aggregate<uint, uint, UIntAccessor, UIntReader>(
            aggregateField,
            [],
            default,
            default,
            []), Throws.ArgumentException);
    }

    private readonly struct AuthorValue
    {
    }

    private readonly struct BoolAccessor : IComponentOptionalAccessor<int, bool>
    {
        public void Clear(ref int value) => value = 0;

        public bool GetPresent(in int value) => false;

        public bool IsPresent(in int value) => false;

        public void SetPresent(ref int value, in bool fieldValue) => value = fieldValue ? 1 : 0;
    }

    private readonly struct BoolReader : IComponentOptionalReader<AuthorValue, bool>
    {
        public bool GetPresent(in AuthorValue value) => false;

        public bool IsPresent(in AuthorValue value) => false;
    }

    private readonly struct ByteArrayAccessor : IComponentArrayAccessor<int, byte>
    {
        public int GetCount(in int value) => 0;

        public byte GetElement(in int value, int index) => 0;

        public void SetElement(ref int value, int index, in byte element)
        {
        }
    }

    private readonly struct ByteArrayReader : IComponentArrayReader<AuthorValue, byte>
    {
        public int GetCount(in AuthorValue value) => 0;

        public byte GetElement(in AuthorValue value, int index) => 0;
    }

    private readonly struct EntityAccessor : IComponentValueAccessor<int, Entity>
    {
        public Entity Get(in int value) => default;

        public void Set(ref int value, in Entity fieldValue)
        {
        }
    }

    private readonly struct GuidAccessor : IComponentValueAccessor<int, Guid>
    {
        public Guid Get(in int value) => Guid.Empty;

        public void Set(ref int value, in Guid fieldValue)
        {
        }
    }

    private readonly struct GuidReader : IComponentValueReader<AuthorValue, Guid>
    {
        public Guid Get(in AuthorValue value) => Guid.Empty;
    }

    private readonly struct OptionalEntityAccessor : IComponentOptionalAccessor<int, Entity>
    {
        public void Clear(ref int value) => value = 0;

        public Entity GetPresent(in int value) => default;

        public bool IsPresent(in int value) => false;

        public void SetPresent(ref int value, in Entity fieldValue)
        {
        }
    }

    private readonly struct OptionalGuidAccessor : IComponentOptionalAccessor<int, Guid>
    {
        public void Clear(ref int value) => value = 0;

        public Guid GetPresent(in int value) => Guid.Empty;

        public bool IsPresent(in int value) => false;

        public void SetPresent(ref int value, in Guid fieldValue)
        {
        }
    }

    private readonly struct OptionalGuidReader : IComponentOptionalReader<AuthorValue, Guid>
    {
        public Guid GetPresent(in AuthorValue value) => Guid.Empty;

        public bool IsPresent(in AuthorValue value) => false;
    }

    private readonly struct OptionalUIntAccessor : IComponentOptionalAccessor<int, uint>
    {
        public void Clear(ref int value) => value = 0;

        public uint GetPresent(in int value) => 0;

        public bool IsPresent(in int value) => false;

        public void SetPresent(ref int value, in uint fieldValue)
        {
        }
    }

    private readonly struct OptionalUIntReader : IComponentOptionalReader<AuthorValue, uint>
    {
        public uint GetPresent(in AuthorValue value) => 0;

        public bool IsPresent(in AuthorValue value) => false;
    }

    private readonly struct UIntAccessor : IComponentValueAccessor<int, uint>
    {
        public uint Get(in int value) => 0;

        public void Set(ref int value, in uint fieldValue)
        {
        }
    }

    private readonly struct UIntReader : IComponentValueReader<AuthorValue, uint>
    {
        public uint Get(in AuthorValue value) => 0;
    }

    private readonly struct UShortArrayAccessor : IComponentArrayAccessor<int, ushort>
    {
        public int GetCount(in int value) => 0;

        public ushort GetElement(in int value, int index) => 0;

        public void SetElement(ref int value, int index, in ushort element)
        {
        }
    }

    private readonly struct UShortArrayReader : IComponentArrayReader<AuthorValue, ushort>
    {
        public int GetCount(in AuthorValue value) => 0;

        public ushort GetElement(in AuthorValue value, int index) => 0;
    }
}
