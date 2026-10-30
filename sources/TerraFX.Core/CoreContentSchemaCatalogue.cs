// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX;

/// <summary>Provides compiled Core content primitives and Foundation schema metadata.</summary>
public static class CoreContentSchemaCatalogue
{
    private static readonly ImmutableArray<DataSchema> s_foundationSchemas = CreateFoundationSchemas();
    private static readonly ImmutableArray<DataSchema> s_primitiveSchemas = CreatePrimitiveSchemas();

    /// <summary>Gets the unchanged M02 Foundation schema descriptors in Foundation scope.</summary>
    public static ImmutableArray<DataSchema> FoundationSchemas => s_foundationSchemas;

    /// <summary>Gets the functional content descriptors and literal schemas.</summary>
    public static ImmutableArray<DataSchema> PrimitiveSchemas => s_primitiveSchemas;

    private static ContentFieldDescriptor CreateField(
        uint tag,
        ushort wireKind,
        ushort elementKind = 0,
        ulong minimumCount = 1,
        ulong maximumCount = 1,
        ContentReferenceKind reference = ContentReferenceKind.None)
        => new(
            tag,
            wireKind,
            elementKind,
            true,
            minimumCount,
            maximumCount,
            [],
            0,
            ContentUnit.None,
            reference,
            ContentAuthoring.None,
            null);

    private static ImmutableArray<DataSchema> CreateFoundationSchemas()
    {
        var names = new[] {
            "registry/manifest",
            "registry/schema",
            "registry/field",
            "foundation/profile",
            "foundation/frame2",
            "foundation/frame3",
            "kernel/manifest",
            "kernel/recipe",
        };
        var schemas = ImmutableArray.CreateBuilder<DataSchema>(names.Length);

        foreach (var name in names)
        {
            var identityAssignment = new ContentSchemaIdentityAssignment(name);
            var foundationSchema = FoundationCatalogue.Find(identityAssignment.SchemaId)
                ?? throw new InvalidOperationException("The Foundation descriptor is missing its frozen name UUID assignment.");

            var fields = ImmutableArray.CreateBuilder<ContentFieldDescriptor>(foundationSchema.Fields.Length);

            foreach (var field in foundationSchema.Fields)
            {
                fields.Add(new ContentFieldDescriptor(
                    field.Tag,
                    field.Kind,
                    0,
                    field.Required,
                    field.Minimum,
                    field.Maximum,
                    GetNestedSchemas(name, field.Tag),
                    0,
                    ContentUnit.None,
                    ContentReferenceKind.None,
                    ContentAuthoring.None,
                    null));
            }

            var schema = new ContentSchemaReference(
                identityAssignment.SchemaId,
                checked((int)foundationSchema.Major),
                0,
                ContentSchemaScope.Foundation);
            schemas.Add(new DataSchema(
                schema,
                identityAssignment,
                ContentSchemaRole.Descriptor,
                ContentSchemaOwner.Core,
                ContentSchemaStorage.NotComponent,
                ContentTransientRestore.NotApplicable,
                fields.MoveToImmutable()));
        }

        return schemas.MoveToImmutable();
    }

    private static ImmutableArray<DataSchema> CreatePrimitiveSchemas()
    {
        var schemas = ImmutableArray.CreateBuilder<DataSchema>(25);
        schemas.Add(CreateSchema("content/schema-ref", [
            CreateField(1, 11, reference: ContentReferenceKind.Schema),
            CreateField(2, 3),
            CreateField(3, 3),
            CreateField(4, 1),
        ]));
        schemas.Add(CreateSchema("content/property-segment", [
            CreateField(1, 1),
            CreateField(2, 3),
        ]));
        schemas.Add(CreateSchema("content/default-meters", [
            CreateField(1, 23, 8, 1, 3),
            CreateField(2, 4),
        ]));
        schemas.Add(CreateSchema("content/default-material", [
            CreateField(1, 1),
        ]));

        var literals = new (string Suffix, ushort WireKind, ulong MinimumCount, ulong MaximumCount)[] {
            ("u8", 1, 1, 1),
            ("u16", 2, 1, 1),
            ("u32", 3, 1, 1),
            ("u64", 4, 1, 1),
            ("i8", 5, 1, 1),
            ("i16", 6, 1, 1),
            ("i32", 7, 1, 1),
            ("i64", 8, 1, 1),
            ("bool", 9, 1, 1),
            ("f32", 10, 1, 1),
            ("guid", 11, 1, 1),
            ("utf8", 12, 0, 1_048_576),
            ("bytes", 13, 0, 1_048_576),
            ("vector2", 14, 1, 1),
            ("vector3", 15, 1, 1),
            ("vector4", 16, 1, 1),
            ("quaternion", 17, 1, 1),
            ("matrix3x2", 18, 1, 1),
            ("matrix4x4", 19, 1, 1),
            ("position2", 20, 1, 1),
            ("position3", 21, 1, 1),
        };

        foreach (var (suffix, wireKind, minimumCount, maximumCount) in literals)
        {
            schemas.Add(CreateSchema($"content/literal/{suffix}", [
                CreateField(1, wireKind, 0, minimumCount, maximumCount),
            ]));
        }

        return schemas.MoveToImmutable();
    }

    private static DataSchema CreateSchema(string name, ImmutableArray<ContentFieldDescriptor> fields)
    {
        var assignment = new ContentSchemaIdentityAssignment(name);
        var schema = new ContentSchemaReference(assignment.SchemaId, 1, 0, ContentSchemaScope.Content);

        return new DataSchema(
            schema,
            assignment,
            ContentSchemaRole.Descriptor,
            ContentSchemaOwner.Core,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            fields);
    }

    private static ImmutableArray<ContentSchemaReference> GetNestedSchemas(string schemaName, uint tag)
        => (schemaName, tag) switch {
            ("foundation/frame2", 2) or ("foundation/frame3", 2) => [GetSchemaReference("foundation/profile")],
            ("kernel/manifest", 4) => [GetSchemaReference("kernel/recipe")],
            ("registry/manifest", 1) => [GetSchemaReference("registry/schema")],
            ("registry/schema", 4) => [GetSchemaReference("registry/field")],
            _ => [],
        };

    private static ContentSchemaReference GetSchemaReference(string name)
    {
        var identityAssignment = new ContentSchemaIdentityAssignment(name);
        var foundationSchema = FoundationCatalogue.Find(identityAssignment.SchemaId)
            ?? throw new InvalidOperationException("A nested Foundation descriptor is missing its frozen name UUID assignment.");

        return new ContentSchemaReference(
            identityAssignment.SchemaId,
            checked((int)foundationSchema.Major),
            0,
            ContentSchemaScope.Foundation);
    }
}
