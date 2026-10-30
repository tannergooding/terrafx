// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Assets;

/// <summary>Provides the compiled schemas for native asset envelopes and metadata.</summary>
public static class NativeAssetSchemaCatalogue
{
    private static readonly ImmutableArray<DataSchema> s_schemas = CreateSchemas();

    /// <summary>Gets the immutable native asset envelope and metadata schemas.</summary>
    public static ImmutableArray<DataSchema> Schemas => s_schemas;

    private static ContentFieldDescriptor CreateField(
        uint tag,
        ushort wireKind,
        bool isRequired = true,
        ulong minimumCount = 1,
        ulong maximumCount = 1,
        ImmutableArray<ContentSchemaReference> nestedSchemas = default,
        ulong nestedRoleMask = 0,
        ContentReferenceKind reference = ContentReferenceKind.None)
        => new(
            tag,
            wireKind,
            0,
            isRequired,
            minimumCount,
            maximumCount,
            nestedSchemas.IsDefault ? [] : nestedSchemas,
            nestedRoleMask,
            ContentUnit.None,
            reference,
            ContentAuthoring.None,
            null);

    private static DataSchema CreateSchema(string name, int major, ImmutableArray<ContentFieldDescriptor> fields)
    {
        var assignment = new ContentSchemaIdentityAssignment(name);
        var schema = new ContentSchemaReference(assignment.SchemaId, major, 0, ContentSchemaScope.Content);

        return new DataSchema(
            schema,
            assignment,
            ContentSchemaRole.Descriptor,
            ContentSchemaOwner.Assets,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            fields);
    }

    private static ImmutableArray<DataSchema> CreateSchemas()
    {
        var metadataEntry = new ContentSchemaIdentityAssignment("native/metadata-entry");

        return [
            CreateSchema("native/envelope", 2, [
                CreateField(1, 3),
                CreateField(2, 22, nestedRoleMask: RoleMask(ContentSchemaRole.NativeAsset)),
            ]),
            CreateSchema("native/asset-reference", 1, [
                CreateField(1, 3),
                CreateField(2, 11, reference: ContentReferenceKind.Schema),
                CreateField(3, 3),
            ]),
            CreateSchema("native/metadata", 1, [
                CreateField(1, 24, minimumCount: 0, maximumCount: 256, nestedSchemas: [ToReference(metadataEntry)]),
            ]),
            CreateSchema("native/metadata-entry", 1, [
                CreateField(1, 12, minimumCount: 1, maximumCount: 128),
                CreateField(2, 12, minimumCount: 0, maximumCount: 4096),
            ]),
        ];
    }

    private static ulong RoleMask(ContentSchemaRole role) => 1UL << ((int)role - 1);

    private static ContentSchemaReference ToReference(ContentSchemaIdentityAssignment assignment)
        => new(assignment.SchemaId, 1, 0, ContentSchemaScope.Content);
}
