// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Scenes;

/// <summary>Provides the compiled native scene, prefab, and built-in scene component schemas.</summary>
public static class SceneSchemaCatalogue
{
    private static readonly ImmutableArray<DataSchema> s_schemas = CreateSchemas();

    /// <summary>Gets the immutable scene and prefab schema declarations.</summary>
    public static ImmutableArray<DataSchema> Schemas => s_schemas;

    private static ContentFieldDescriptor CreateField(
        uint tag,
        ushort wireKind,
        bool isRequired = true,
        ulong minimumCount = 1,
        ulong maximumCount = 1,
        ushort elementKind = 0,
        ImmutableArray<ContentSchemaReference> nestedSchemas = default,
        ulong nestedRoleMask = 0,
        ContentUnit unit = ContentUnit.None,
        ContentReferenceKind reference = ContentReferenceKind.None,
        ContentAuthoring authoringFlags = ContentAuthoring.None,
        ContentFieldDefaultDescriptor? defaultValue = null)
        => new(
            tag,
            wireKind,
            elementKind,
            isRequired,
            minimumCount,
            maximumCount,
            nestedSchemas.IsDefault ? [] : nestedSchemas,
            nestedRoleMask,
            unit,
            reference,
            authoringFlags,
            defaultValue);

    private static DataSchema CreateSchema(
        string name,
        ContentSchemaRole role,
        ImmutableArray<ContentFieldDescriptor> fields,
        int major = 1,
        ContentSchemaStorage storage = ContentSchemaStorage.NotComponent)
    {
        var assignment = new ContentSchemaIdentityAssignment(name);
        var schema = new ContentSchemaReference(assignment.SchemaId, major, 0, ContentSchemaScope.Content);

        return new DataSchema(
            schema,
            assignment,
            role,
            ContentSchemaOwner.Scenes,
            storage,
            ContentTransientRestore.NotApplicable,
            fields);
    }

    private static ImmutableArray<DataSchema> CreateSchemas()
    {
        var nativeEntity = ToReference("native/entity");
        var hierarchy = ToReference("native/hierarchy", 2);
        var hierarchyNode = ToReference("native/hierarchy-node", 2);
        var hierarchyEntry = ToReference("native/hierarchy-entry");
        var placement = ToReference("native/prefab-placement");
        var rootPose = ToReference("native/prefab-root-pose");
        var entitySelector = ToReference("native/entity-selector");
        var prefabOverride = ToReference("native/prefab-override");
        var entityReferenceBinding = ToReference("native/entity-reference-binding");
        var referenceTarget = ToReference("native/reference-target");
        var externalSlot = ToReference("native/external-slot");
        var placementSlotBinding = ToReference("native/placement-slot-binding");
        var assetReference = ToReference("native/asset-reference");
        var componentSchemaReference = ToReference("content/schema-ref");
        var propertySegment = ToReference("content/property-segment");
        var fixedLiterals = GetFixedLiteralReferences();

        return [
            CreateSchema("native/scene", ContentSchemaRole.NativeAsset, [
                CreateField(1, 3),
                CreateField(2, 24, minimumCount: 0, maximumCount: 1_048_576, nestedSchemas: [nativeEntity]),
                CreateField(3, 22, nestedSchemas: [hierarchy]),
                CreateField(4, 24, minimumCount: 0, maximumCount: 1_048_576, nestedSchemas: [placement]),
                CreateField(6, 24, minimumCount: 0, maximumCount: 1_048_576, nestedSchemas: [entityReferenceBinding]),
                CreateField(7, 24, minimumCount: 0, maximumCount: 65_536, nestedSchemas: [prefabOverride]),
                CreateField(8, 24, minimumCount: 0, maximumCount: 65_536, nestedSchemas: [externalSlot]),
            ], 2),
            CreateSchema("native/prefab", ContentSchemaRole.NativeAsset, [
                CreateField(1, 3),
                CreateField(3, 24, minimumCount: 0, maximumCount: 65_536, nestedSchemas: [nativeEntity]),
                CreateField(4, 22, nestedSchemas: [hierarchy]),
                CreateField(5, 24, minimumCount: 0, maximumCount: 1_048_576, nestedSchemas: [placement]),
                CreateField(6, 24, minimumCount: 0, maximumCount: 1_048_576, nestedSchemas: [entityReferenceBinding]),
                CreateField(8, 24, minimumCount: 0, maximumCount: 65_536, nestedSchemas: [externalSlot]),
            ], 2),
            CreateSchema("native/entity", ContentSchemaRole.Auxiliary, [
                CreateField(1, 11, reference: ContentReferenceKind.Entity),
                CreateField(2, 24, minimumCount: 0, maximumCount: 256, nestedRoleMask: (1UL << ((int)ContentSchemaRole.Component - 1)) | (1UL << ((int)ContentSchemaRole.EntityTag - 1))),
            ]),
            CreateSchema("native/hierarchy", ContentSchemaRole.Auxiliary, [
                CreateField(1, 24, minimumCount: 0, maximumCount: 1_048_576, nestedSchemas: [hierarchyNode]),
                CreateField(2, 24, minimumCount: 0, maximumCount: 1_048_576, nestedSchemas: [hierarchyEntry]),
            ], 2),
            CreateSchema("native/hierarchy-node", ContentSchemaRole.Auxiliary, [
                CreateField(1, 11, reference: ContentReferenceKind.Entity),
                CreateField(2, 11, isRequired: false, reference: ContentReferenceKind.Entity),
                CreateField(3, 24, minimumCount: 0, maximumCount: 1_048_576, nestedSchemas: [hierarchyEntry]),
            ], 2),
            CreateSchema("native/hierarchy-entry", ContentSchemaRole.Auxiliary, [
                CreateField(1, 1),
                CreateField(2, 11),
            ]),
            CreateSchema("native/prefab-placement", ContentSchemaRole.Auxiliary, [
                CreateField(1, 11),
                CreateField(2, 22, nestedSchemas: [assetReference]),
                CreateField(3, 11, isRequired: false, reference: ContentReferenceKind.Entity),
                CreateField(4, 24, minimumCount: 0, maximumCount: 65_536, nestedSchemas: [rootPose]),
                CreateField(5, 24, minimumCount: 0, maximumCount: 65_536, nestedSchemas: [prefabOverride]),
                CreateField(6, 24, minimumCount: 0, maximumCount: 65_536, nestedSchemas: [placementSlotBinding]),
            ]),
            CreateSchema("native/prefab-root-pose", ContentSchemaRole.Auxiliary, [
                CreateField(1, 22, nestedSchemas: [entitySelector]),
                CreateField(2, 22, nestedSchemas: [ToReference("scene/local-transform")]),
                CreateField(3, 21, isRequired: false, unit: ContentUnit.EngineDistance),
            ]),
            CreateSchema("native/entity-selector", ContentSchemaRole.Auxiliary, [
                CreateField(1, 1),
                CreateField(2, 23, minimumCount: 0, maximumCount: 32, elementKind: 11),
                CreateField(3, 11, reference: ContentReferenceKind.Entity),
            ]),
            CreateSchema("native/prefab-override", ContentSchemaRole.Auxiliary, [
                CreateField(1, 22, nestedSchemas: [entitySelector]),
                CreateField(2, 22, nestedSchemas: [componentSchemaReference]),
                CreateField(3, 24, minimumCount: 1, maximumCount: 64, nestedSchemas: [propertySegment]),
                CreateField(4, 22, isRequired: false, nestedSchemas: fixedLiterals),
                CreateField(5, 1, isRequired: false, defaultValue: ContentFieldDefaultDescriptor.Literal(1, 1, [0])),
                CreateField(6, 22, isRequired: false, nestedSchemas: [referenceTarget]),
            ]),
            CreateSchema("native/entity-reference-binding", ContentSchemaRole.Auxiliary, [
                CreateField(1, 22, nestedSchemas: [entitySelector]),
                CreateField(2, 22, nestedSchemas: [componentSchemaReference]),
                CreateField(3, 24, minimumCount: 1, maximumCount: 64, nestedSchemas: [propertySegment]),
                CreateField(4, 22, nestedSchemas: [referenceTarget]),
            ]),
            CreateSchema("native/reference-target", ContentSchemaRole.Auxiliary, [
                CreateField(1, 1),
                CreateField(2, 22, isRequired: false, nestedSchemas: [entitySelector]),
                CreateField(3, 11, isRequired: false, reference: ContentReferenceKind.ExternalSceneBinding),
            ]),
            CreateSchema("native/external-slot", ContentSchemaRole.Auxiliary, [
                CreateField(1, 11, reference: ContentReferenceKind.ExternalSceneBinding),
                CreateField(2, 9),
                CreateField(3, 9),
                CreateField(4, 24, minimumCount: 0, maximumCount: 256, nestedSchemas: [componentSchemaReference]),
            ]),
            CreateSchema("native/placement-slot-binding", ContentSchemaRole.Auxiliary, [
                CreateField(1, 11, reference: ContentReferenceKind.ExternalSceneBinding),
                CreateField(2, 22, isRequired: false, nestedSchemas: [referenceTarget]),
            ]),
            CreateSchema("scene/local-transform", ContentSchemaRole.Component, [
                CreateField(1, 15, unit: ContentUnit.EngineDistance, authoringFlags: ContentAuthoring.PrefabEditable | ContentAuthoring.Animatable, defaultValue: ContentFieldDefaultDescriptor.Literal(15, 1, new byte[12])),
                CreateField(2, 17, authoringFlags: ContentAuthoring.PrefabEditable | ContentAuthoring.Animatable, defaultValue: ContentFieldDefaultDescriptor.Literal(17, 1, [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 128, 63])),
                CreateField(3, 15, authoringFlags: ContentAuthoring.PrefabEditable | ContentAuthoring.Animatable, defaultValue: ContentFieldDefaultDescriptor.Literal(15, 1, [0, 0, 128, 63, 0, 0, 128, 63, 0, 0, 128, 63])),
            ], storage: ContentSchemaStorage.Persistent),
            CreateSchema("scene/root-position", ContentSchemaRole.Component, [
                CreateField(1, 21, unit: ContentUnit.EngineDistance, authoringFlags: ContentAuthoring.PrefabEditable, defaultValue: ContentFieldDefaultDescriptor.Literal(21, 1, new byte[24])),
            ], storage: ContentSchemaStorage.Persistent),
            CreateSchema("scene/camera", ContentSchemaRole.Component, [
                CreateField(1, 1, authoringFlags: ContentAuthoring.PrefabEditable, defaultValue: ContentFieldDefaultDescriptor.Literal(1, 1, [0])),
                CreateField(2, 10, unit: ContentUnit.Radians, authoringFlags: ContentAuthoring.PrefabEditable, defaultValue: ContentFieldDefaultDescriptor.Literal(10, 1, [146, 10, 134, 63])),
                CreateField(3, 10, unit: ContentUnit.EngineDistance, authoringFlags: ContentAuthoring.PrefabEditable, defaultValue: ContentFieldDefaultDescriptor.Meters([10], 1)),
                CreateField(4, 10, unit: ContentUnit.EngineDistance, authoringFlags: ContentAuthoring.PrefabEditable, defaultValue: ContentFieldDefaultDescriptor.Meters([1], 10)),
                CreateField(5, 1, authoringFlags: ContentAuthoring.PrefabEditable, defaultValue: ContentFieldDefaultDescriptor.Literal(1, 1, [0])),
                CreateField(6, 10, isRequired: false, unit: ContentUnit.EngineDistance, authoringFlags: ContentAuthoring.PrefabEditable, defaultValue: ContentFieldDefaultDescriptor.Meters([1000], 1)),
                CreateField(7, 10, isRequired: false, authoringFlags: ContentAuthoring.PrefabEditable),
                CreateField(8, 16, authoringFlags: ContentAuthoring.PrefabEditable, defaultValue: ContentFieldDefaultDescriptor.Literal(16, 1, [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 128, 63, 0, 0, 128, 63])),
                CreateField(9, 1, authoringFlags: ContentAuthoring.PrefabEditable, defaultValue: ContentFieldDefaultDescriptor.Literal(1, 1, [3])),
                CreateField(10, 16, unit: ContentUnit.LinearColor, authoringFlags: ContentAuthoring.PrefabEditable, defaultValue: ContentFieldDefaultDescriptor.Literal(16, 1, [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 128, 63])),
                CreateField(11, 4, authoringFlags: ContentAuthoring.PrefabEditable, defaultValue: ContentFieldDefaultDescriptor.Literal(4, 1, [255, 255, 255, 255, 255, 255, 255, 255])),
                CreateField(12, 7, authoringFlags: ContentAuthoring.PrefabEditable, defaultValue: ContentFieldDefaultDescriptor.Literal(7, 1, [0, 0, 0, 0])),
            ], storage: ContentSchemaStorage.Persistent),
            CreateSchema("scene/collision-reference", ContentSchemaRole.Component, [
                CreateField(1, 22, reference: ContentReferenceKind.Asset, authoringFlags: ContentAuthoring.PrefabEditable, nestedSchemas: [assetReference]),
            ], storage: ContentSchemaStorage.Persistent),
        ];
    }

    private static ImmutableArray<ContentSchemaReference> GetFixedLiteralReferences()
    {
        var references = ImmutableArray.CreateBuilder<ContentSchemaReference>(19);

        foreach (var schema in CoreContentSchemaCatalogue.PrimitiveSchemas)
        {
            var name = schema.IdentityAssignment.Name;

            if (name.StartsWith("content/literal/", StringComparison.Ordinal)
                && !StringComparer.Ordinal.Equals(name, "content/literal/utf8")
                && !StringComparer.Ordinal.Equals(name, "content/literal/bytes"))
            {
                references.Add(schema.Schema);
            }
        }

        return references.MoveToImmutable();
    }

    private static ContentSchemaReference ToReference(string name, int major = 1)
        => new(new ContentSchemaIdentityAssignment(name).SchemaId, major, 0, ContentSchemaScope.Content);
}
