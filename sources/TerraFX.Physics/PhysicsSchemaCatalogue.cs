// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;

namespace TerraFX.Physics;

/// <summary>Provides the compiled primitive-shape and statistics schemas.</summary>
public static class PhysicsSchemaCatalogue
{
    private static readonly ImmutableArray<DataSchema> s_schemas = CreateSchemas();

    /// <summary>Gets the immutable primitive and statistics schema declarations.</summary>
    public static ImmutableArray<DataSchema> Schemas => s_schemas;

    private static ContentFieldDescriptor CreateField(
        uint tag,
        ushort wireKind,
        bool isRequired = true,
        ImmutableArray<ContentSchemaReference> nestedSchemas = default,
        ContentUnit unit = ContentUnit.None)
        => new(
            tag,
            wireKind,
            0,
            isRequired,
            1,
            1,
            nestedSchemas.IsDefault ? [] : nestedSchemas,
            0,
            unit,
            ContentReferenceKind.None,
            ContentAuthoring.None,
            null);

    private static DataSchema CreateSchema(
        string name,
        ContentSchemaRole role,
        ImmutableArray<ContentFieldDescriptor> fields)
    {
        var assignment = new ContentSchemaIdentityAssignment(name);
        var schema = new ContentSchemaReference(assignment.SchemaId, 1, 0, ContentSchemaScope.Content);

        return new DataSchema(
            schema,
            assignment,
            role,
            ContentSchemaOwner.Physics,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            fields);
    }

    private static ImmutableArray<DataSchema> CreateSchemas()
    {
        var circle = ToReference("physics/circle");
        var box2 = ToReference("physics/box2");
        var sphere = ToReference("physics/sphere");
        var box3 = ToReference("physics/box3");
        var statistics2 = ToReference("physics/statistics2");
        var statistics3 = ToReference("physics/statistics3");
        var measure = ToReference("physics/shape-measure");
        var settings = ToReference("physics/spatial-settings");
        var foundationProfile = CoreContentSchemaCatalogue.FoundationSchemas
            .Single(static schema => StringComparer.Ordinal.Equals(schema.IdentityAssignment.Name, "foundation/profile"))
            .Schema;

        return [
            CreateSchema("physics/primitive2", ContentSchemaRole.NativeAsset, [
                CreateField(1, 22, nestedSchemas: [circle, box2]),
                CreateField(2, 22, nestedSchemas: [statistics2]),
                CreateField(3, 22, nestedSchemas: [foundationProfile]),
            ]),
            CreateSchema("physics/primitive3", ContentSchemaRole.NativeAsset, [
                CreateField(1, 22, nestedSchemas: [sphere, box3]),
                CreateField(2, 22, nestedSchemas: [statistics3]),
                CreateField(3, 22, nestedSchemas: [foundationProfile]),
            ]),
            CreateSchema("physics/circle", ContentSchemaRole.Auxiliary, [
                CreateField(1, 10, unit: ContentUnit.EngineDistance),
            ]),
            CreateSchema("physics/box2", ContentSchemaRole.Auxiliary, [
                CreateField(1, 14, unit: ContentUnit.EngineDistance),
            ]),
            CreateSchema("physics/sphere", ContentSchemaRole.Auxiliary, [
                CreateField(1, 10, unit: ContentUnit.EngineDistance),
            ]),
            CreateSchema("physics/box3", ContentSchemaRole.Auxiliary, [
                CreateField(1, 15, unit: ContentUnit.EngineDistance),
            ]),
            CreateSchema("physics/shape-measure", ContentSchemaRole.Auxiliary, [
                CreateField(1, 10),
                CreateField(2, 7),
            ]),
            CreateSchema("physics/spatial-settings", ContentSchemaRole.Auxiliary, [
                CreateField(1, 1),
                CreateField(2, 7),
            ]),
            CreateSchema("physics/statistics2", ContentSchemaRole.Auxiliary, [
                CreateField(1, 3, isRequired: false),
                CreateField(2, 3, isRequired: false),
                CreateField(3, 3, isRequired: false),
                CreateField(4, 22, nestedSchemas: [measure], unit: ContentUnit.EngineArea),
                CreateField(5, 14, unit: ContentUnit.EngineDistance),
                CreateField(6, 14, unit: ContentUnit.EngineDistance),
                CreateField(7, 10, unit: ContentUnit.EngineDistance),
                CreateField(8, 3),
                CreateField(9, 3, isRequired: false),
                CreateField(10, 3, isRequired: false),
                CreateField(11, 10, isRequired: false, unit: ContentUnit.EngineDistance),
                CreateField(12, 22, nestedSchemas: [settings]),
            ]),
            CreateSchema("physics/statistics3", ContentSchemaRole.Auxiliary, [
                CreateField(1, 3, isRequired: false),
                CreateField(2, 3, isRequired: false),
                CreateField(3, 3, isRequired: false),
                CreateField(4, 22, nestedSchemas: [measure], unit: ContentUnit.EngineVolume),
                CreateField(5, 15, unit: ContentUnit.EngineDistance),
                CreateField(6, 15, unit: ContentUnit.EngineDistance),
                CreateField(7, 10, unit: ContentUnit.EngineDistance),
                CreateField(8, 3),
                CreateField(9, 3, isRequired: false),
                CreateField(10, 3, isRequired: false),
                CreateField(11, 10, isRequired: false, unit: ContentUnit.EngineDistance),
                CreateField(12, 22, nestedSchemas: [settings]),
            ]),
        ];
    }

    private static ContentSchemaReference ToReference(string name)
        => new(new ContentSchemaIdentityAssignment(name).SchemaId, 1, 0, ContentSchemaScope.Content);
}
