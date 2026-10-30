// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class ComponentSchemaTests
{
    private static ContentFieldDescriptor CreateField(
        uint tag,
        ushort wireKind,
        ushort elementKind,
        ImmutableArray<ContentSchemaReference> nestedSchemas,
        ulong minimumCount = 1,
        ulong maximumCount = 1)
        => new(
            tag,
            wireKind,
            elementKind,
            true,
            minimumCount,
            maximumCount,
            nestedSchemas,
            0,
            ContentUnit.None,
            ContentReferenceKind.None,
            ContentAuthoring.None,
            null);

    [Test]
    public void DerivedDependenciesRequirePersistentComponentSchemas()
    {
        var assignment = new ContentSchemaIdentityAssignment("application/component/transient-dependency/v1");
        var schemaReference = new ContentSchemaReference(assignment.SchemaId, 1, 0, ContentSchemaScope.Content);
        var metadata = new DataSchema(
            schemaReference,
            assignment,
            ContentSchemaRole.Component,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.Transient,
            ContentTransientRestore.ExactDerivedRehydration,
            [CreateField(1, 3, 0, [])]);
        var registry = new CompiledContentSchemaRegistry([metadata]);
        var layout = new ComponentLayout(new Guid("10112233-4455-4677-8899-aabbccddeeff"), sizeof(int));
        var transientSchema = ComponentSchema<int>.FromRegistry(registry, schemaReference, layout);

        Assert.That(transientSchema.Flags, Is.EqualTo(ComponentStorageFlags.Transient));
        Assert.That(() => ComponentDerivedDependency.FromSchema(transientSchema), Throws.ArgumentException);
    }

    [Test]
    public void FromRegistryResolvesExactRecordAndFixedArrayPaths()
    {
        var nestedAssignment = new ContentSchemaIdentityAssignment("application/descriptor/nested/v1");
        var nestedReference = new ContentSchemaReference(nestedAssignment.SchemaId, 1, 0, ContentSchemaScope.Content);
        var nestedSchema = new DataSchema(
            nestedReference,
            nestedAssignment,
            ContentSchemaRole.Descriptor,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            [CreateField(7, 3, 0, [])]);

        var alternativeAssignment = new ContentSchemaIdentityAssignment("application/descriptor/alternative/v1");
        var alternativeReference = new ContentSchemaReference(alternativeAssignment.SchemaId, 1, 0, ContentSchemaScope.Content);
        var alternativeSchema = new DataSchema(
            alternativeReference,
            alternativeAssignment,
            ContentSchemaRole.Descriptor,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            [CreateField(7, 3, 0, [])]);

        var componentAssignment = new ContentSchemaIdentityAssignment("application/component/test/v1");
        var componentReference = new ContentSchemaReference(componentAssignment.SchemaId, 1, 0, ContentSchemaScope.Content);
        var componentMetadata = new DataSchema(
            componentReference,
            componentAssignment,
            ContentSchemaRole.Component,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.Persistent,
            ContentTransientRestore.NotApplicable,
            [
                CreateField(1, 22, 0, [nestedReference, alternativeReference]),
                CreateField(2, 23, 8, [], 1, 4),
                CreateField(3, 24, 0, [nestedReference], 0, 2),
            ]);
        var registry = new CompiledContentSchemaRegistry([nestedSchema, alternativeSchema, componentMetadata]);
        var layout = new ComponentLayout(new Guid("00112233-4455-4677-8899-aabbccddeeff"), sizeof(int));
        var schema = ComponentSchema<int>.FromRegistry(registry, componentReference, layout);

        Assert.That(schema.Schema, Is.EqualTo(componentReference));
        Assert.That(schema.Layout, Is.EqualTo(layout));
        Assert.That(schema.Flags, Is.EqualTo(ComponentStorageFlags.Persistent));
        Assert.That(schema.Fields, Has.Length.EqualTo(3));
        Assert.That(() => schema.GetField(new ComponentFieldPath([
            ComponentPropertySegment.FieldTag(1),
            ComponentPropertySegment.FieldTag(7),
        ])), Throws.ArgumentException);

        var fixedArrayElement = schema.GetField(new ComponentFieldPath([
            ComponentPropertySegment.FieldTag(2),
            ComponentPropertySegment.FixedArrayIndex(3),
        ]));

        Assert.That(fixedArrayElement.ComponentSchema, Is.EqualTo(componentReference));
        Assert.That(fixedArrayElement.Field.ElementKind, Is.EqualTo(8));
        Assert.That(fixedArrayElement.Path.Segments[1].Value, Is.EqualTo(3));
        Assert.That(() => schema.GetField(new ComponentFieldPath([
            ComponentPropertySegment.FieldTag(2),
            ComponentPropertySegment.FixedArrayIndex(4),
        ])), Throws.ArgumentException);
        Assert.That(() => schema.GetField(new ComponentFieldPath([
            ComponentPropertySegment.FieldTag(3),
            ComponentPropertySegment.FixedArrayIndex(0),
        ])), Throws.ArgumentException);

        var directSchema = new ComponentSchema<int>(componentReference, layout, schema.Fields, ComponentStorageFlags.Persistent);
        Assert.That(directSchema.Fields, Is.EqualTo(schema.Fields));
        Assert.That(() => new ComponentSchema<int>(componentReference, layout, schema.Fields, ComponentStorageFlags.Transient), Throws.ArgumentException);
        Assert.That(() => new ComponentSchema<int>(
            componentReference,
            layout,
            schema.Fields,
            ComponentStorageFlags.Persistent | ComponentStorageFlags.Transient), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => ComponentSchema<int>.FromRegistry(
            registry,
            componentReference,
            new ComponentLayout(new Guid("10112233-4455-4677-8899-aabbccddeeff"), sizeof(long))), Throws.ArgumentException);
    }
}
