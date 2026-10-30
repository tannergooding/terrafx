// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;

namespace TerraFX.Graphics;

/// <summary>Provides the compiled mesh, material, texture, atlas, and rendering component schemas.</summary>
public static class RenderingSchemaCatalogue
{
    private static readonly ImmutableArray<DataSchema> s_schemas = CreateSchemas();

    /// <summary>Gets the immutable rendering schema declarations.</summary>
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
        ContentSchemaStorage storage = ContentSchemaStorage.NotComponent)
    {
        var assignment = new ContentSchemaIdentityAssignment(name);
        var schema = new ContentSchemaReference(assignment.SchemaId, 1, 0, ContentSchemaScope.Content);

        return new DataSchema(
            schema,
            assignment,
            role,
            ContentSchemaOwner.Rendering,
            storage,
            ContentTransientRestore.NotApplicable,
            fields);
    }

    private static ImmutableArray<DataSchema> CreateSchemas()
    {
        var assetReference = ToReference("native/asset-reference");
        var bounds = ToReference("native/bounds3");
        var meshGroup = ToReference("native/mesh-group");
        var meshPosition = ToReference("native/mesh-position");
        var meshNormal = ToReference("native/mesh-normal");
        var meshTangent = ToReference("native/mesh-tangent");
        var meshUv = ToReference("native/mesh-uv");
        var meshColor = ToReference("native/mesh-color");
        var meshJoints = ToReference("native/mesh-joints");
        var meshWeights = ToReference("native/mesh-weights");
        var meshIndices16 = ToReference("native/mesh-indices16");
        var meshIndices32 = ToReference("native/mesh-indices32");
        var spriteParameters = ToReference("native/material-sprite-parameters");
        var pbrParameters = ToReference("native/material-pbr-parameters");
        var textureBinding = ToReference("native/texture-binding");
        var sampler = ToReference("native/sampler");
        var textureMip = ToReference("native/texture-mip");
        var atlasFrame = ToReference("native/atlas-frame");
        var fixedLiterals = CoreContentSchemaCatalogue.PrimitiveSchemas
            .Where(static schema =>
                schema.IdentityAssignment.Name.StartsWith("content/literal/", StringComparison.Ordinal)
                && !StringComparer.Ordinal.Equals(schema.IdentityAssignment.Name, "content/literal/utf8")
                && !StringComparer.Ordinal.Equals(schema.IdentityAssignment.Name, "content/literal/bytes"))
            .Select(static schema => schema.Schema)
            .ToImmutableArray();
        var allBits = Literal(4, [255, 255, 255, 255, 255, 255, 255, 255]);
        var prefabEditable = ContentAuthoring.PrefabEditable;

        return [
            CreateSchema("native/mesh", ContentSchemaRole.NativeAsset, [
                CreateField(1, 3),
                CreateField(2, 24, minimumCount: 1, maximumCount: 8, nestedSchemas: [meshPosition, meshNormal, meshTangent, meshUv, meshColor, meshJoints, meshWeights]),
                CreateField(3, 22, nestedSchemas: [meshIndices16, meshIndices32]),
                CreateField(4, 24, minimumCount: 1, maximumCount: 65_536, nestedSchemas: [meshGroup]),
                CreateField(5, 24, minimumCount: 1, maximumCount: 256, nestedSchemas: [assetReference]),
                CreateField(6, 22, nestedSchemas: [bounds]),
                CreateField(7, 22, isRequired: false, nestedSchemas: [assetReference]),
            ]),
            CreateSchema("native/bounds3", ContentSchemaRole.Auxiliary, [
                CreateField(1, 15, unit: ContentUnit.EngineDistance),
                CreateField(2, 15, unit: ContentUnit.EngineDistance),
                CreateField(3, 15, unit: ContentUnit.EngineDistance),
                CreateField(4, 10, unit: ContentUnit.EngineDistance),
            ]),
            CreateSchema("native/mesh-position", ContentSchemaRole.Auxiliary, [
                CreateField(1, 3),
                CreateField(2, 23, minimumCount: 1, maximumCount: 262_144, elementKind: 15, unit: ContentUnit.EngineDistance),
            ]),
            CreateSchema("native/mesh-normal", ContentSchemaRole.Auxiliary, [
                CreateField(1, 3),
                CreateField(2, 23, minimumCount: 1, maximumCount: 262_144, elementKind: 15),
            ]),
            CreateSchema("native/mesh-tangent", ContentSchemaRole.Auxiliary, [
                CreateField(1, 3),
                CreateField(2, 23, minimumCount: 1, maximumCount: 262_144, elementKind: 16),
            ]),
            CreateSchema("native/mesh-uv", ContentSchemaRole.Auxiliary, [
                CreateField(1, 1),
                CreateField(2, 3),
                CreateField(3, 23, minimumCount: 1, maximumCount: 262_144, elementKind: 14),
            ]),
            CreateSchema("native/mesh-color", ContentSchemaRole.Auxiliary, [
                CreateField(1, 3),
                CreateField(2, 23, minimumCount: 1, maximumCount: 262_144, elementKind: 16, unit: ContentUnit.LinearColor),
            ]),
            CreateSchema("native/mesh-joints", ContentSchemaRole.Auxiliary, [
                CreateField(1, 3),
                CreateField(2, 23, minimumCount: 4, maximumCount: 1_048_576, elementKind: 2),
            ]),
            CreateSchema("native/mesh-weights", ContentSchemaRole.Auxiliary, [
                CreateField(1, 3),
                CreateField(2, 23, minimumCount: 1, maximumCount: 262_144, elementKind: 16),
            ]),
            CreateSchema("native/mesh-indices16", ContentSchemaRole.Auxiliary, [
                CreateField(1, 23, minimumCount: 3, maximumCount: 1_048_576, elementKind: 2),
            ]),
            CreateSchema("native/mesh-indices32", ContentSchemaRole.Auxiliary, [
                CreateField(1, 23, minimumCount: 3, maximumCount: 1_048_576, elementKind: 3),
            ]),
            CreateSchema("native/mesh-group", ContentSchemaRole.Auxiliary, [
                CreateField(1, 3),
                CreateField(2, 3),
                CreateField(3, 3),
            ]),
            CreateSchema("native/material", ContentSchemaRole.NativeAsset, [
                CreateField(1, 11, reference: ContentReferenceKind.Profile),
                CreateField(2, 11),
                CreateField(3, 11),
                CreateField(4, 11),
                CreateField(5, 22, nestedSchemas: [spriteParameters, pbrParameters]),
                CreateField(6, 1, defaultValue: ContentFieldDefaultDescriptor.ForMaterialProperty(ContentMaterialDefaultProperty.AlphaMode)),
                CreateField(7, 10, defaultValue: Literal(10, [0, 0, 0, 63])),
                CreateField(8, 9, defaultValue: Literal(9, [0])),
                CreateField(9, 9, defaultValue: ContentFieldDefaultDescriptor.ForMaterialProperty(ContentMaterialDefaultProperty.DepthTest)),
                CreateField(10, 9, defaultValue: ContentFieldDefaultDescriptor.ForMaterialProperty(ContentMaterialDefaultProperty.DepthWrite)),
                CreateField(11, 1, defaultValue: ContentFieldDefaultDescriptor.ForMaterialProperty(ContentMaterialDefaultProperty.CullMode)),
                CreateField(12, 1, defaultValue: ContentFieldDefaultDescriptor.ForMaterialProperty(ContentMaterialDefaultProperty.OutputAlpha)),
                CreateField(13, 4, defaultValue: ContentFieldDefaultDescriptor.ForMaterialProperty(ContentMaterialDefaultProperty.Capabilities)),
            ]),
            CreateSchema("native/material-sprite-parameters", ContentSchemaRole.Auxiliary, [
                CreateField(1, 16, unit: ContentUnit.LinearColor, defaultValue: Literal(16, [0, 0, 128, 63, 0, 0, 128, 63, 0, 0, 128, 63, 0, 0, 128, 63])),
                CreateField(2, 22, isRequired: false, nestedSchemas: [textureBinding]),
            ]),
            CreateSchema("native/material-pbr-parameters", ContentSchemaRole.Auxiliary, [
                CreateField(1, 16, unit: ContentUnit.LinearColor, defaultValue: Literal(16, [0, 0, 128, 63, 0, 0, 128, 63, 0, 0, 128, 63, 0, 0, 128, 63])),
                CreateField(2, 10, defaultValue: Literal(10, [0, 0, 0, 0])),
                CreateField(3, 10, defaultValue: Literal(10, [0, 0, 128, 63])),
                CreateField(4, 10, defaultValue: Literal(10, [0, 0, 128, 63])),
                CreateField(5, 15, unit: ContentUnit.LinearColor, defaultValue: Literal(15, new byte[12])),
                CreateField(6, 22, isRequired: false, nestedSchemas: [textureBinding]),
                CreateField(7, 22, isRequired: false, nestedSchemas: [textureBinding]),
                CreateField(8, 22, isRequired: false, nestedSchemas: [textureBinding]),
                CreateField(9, 22, isRequired: false, nestedSchemas: [textureBinding]),
            ]),
            CreateSchema("native/texture-binding", ContentSchemaRole.Auxiliary, [
                CreateField(1, 22, nestedSchemas: [assetReference]),
                CreateField(2, 22, nestedSchemas: [sampler]),
                CreateField(3, 1, defaultValue: Literal(1, [0])),
            ]),
            CreateSchema("native/sampler", ContentSchemaRole.Auxiliary, [
                CreateField(1, 1),
                CreateField(2, 1),
                CreateField(3, 1),
                CreateField(4, 1),
                CreateField(5, 1),
            ]),
            CreateSchema("native/texture", ContentSchemaRole.NativeAsset, [
                CreateField(1, 3),
                CreateField(2, 3),
                CreateField(3, 1),
                CreateField(4, 1),
                CreateField(5, 1),
                CreateField(6, 1),
                CreateField(7, 1),
                CreateField(8, 24, minimumCount: 1, maximumCount: 13, nestedSchemas: [textureMip]),
            ]),
            CreateSchema("native/texture-mip", ContentSchemaRole.Auxiliary, [
                CreateField(1, 3),
                CreateField(2, 3),
                CreateField(3, 3),
                CreateField(4, 13, minimumCount: 4, maximumCount: 67_108_864),
            ]),
            CreateSchema("native/atlas", ContentSchemaRole.NativeAsset, [
                CreateField(1, 24, minimumCount: 1, maximumCount: 16, nestedSchemas: [assetReference]),
                CreateField(2, 24, minimumCount: 1, maximumCount: 1_048_576, nestedSchemas: [atlasFrame]),
            ]),
            CreateSchema("native/atlas-frame", ContentSchemaRole.Auxiliary, [
                CreateField(1, 11),
                CreateField(2, 3),
                CreateField(3, 3),
                CreateField(4, 3),
                CreateField(5, 3),
                CreateField(6, 3),
                CreateField(7, 1),
            ]),
            CreateSchema("render/sprite", ContentSchemaRole.Component, [
                CreateField(1, 22, reference: ContentReferenceKind.Asset, authoringFlags: ContentAuthoring.PrefabEditable | ContentAuthoring.Animatable, nestedSchemas: [assetReference]),
                CreateField(2, 11, authoringFlags: ContentAuthoring.PrefabEditable | ContentAuthoring.Animatable),
                CreateField(3, 14, authoringFlags: prefabEditable, defaultValue: Literal(14, [0, 0, 0, 63, 0, 0, 0, 63])),
                CreateField(4, 14, unit: ContentUnit.EngineDistance, authoringFlags: prefabEditable, defaultValue: ContentFieldDefaultDescriptor.Meters([1, 1], 1)),
                CreateField(5, 16, unit: ContentUnit.LinearColor, authoringFlags: prefabEditable, defaultValue: Literal(16, [0, 0, 128, 63, 0, 0, 128, 63, 0, 0, 128, 63, 0, 0, 128, 63])),
                CreateField(6, 1, authoringFlags: prefabEditable, defaultValue: Literal(1, [0])),
                CreateField(7, 7, authoringFlags: prefabEditable, defaultValue: Literal(7, [0, 0, 0, 0])),
                CreateField(8, 7, authoringFlags: prefabEditable, defaultValue: Literal(7, [0, 0, 0, 0])),
                CreateField(9, 22, isRequired: false, reference: ContentReferenceKind.Asset, authoringFlags: prefabEditable, nestedSchemas: [assetReference]),
                CreateField(10, 9, authoringFlags: prefabEditable, defaultValue: Literal(9, [0])),
            ], ContentSchemaStorage.Persistent),
            CreateSchema("render/mesh", ContentSchemaRole.Component, [
                CreateField(1, 22, reference: ContentReferenceKind.Asset, authoringFlags: prefabEditable, nestedSchemas: [assetReference]),
                CreateField(2, 4, authoringFlags: prefabEditable, defaultValue: allBits),
                CreateField(3, 1, authoringFlags: prefabEditable, defaultValue: Literal(1, [0])),
            ], ContentSchemaStorage.Persistent),
            CreateSchema("render/directional-light", ContentSchemaRole.Component, [
                CreateField(1, 15, unit: ContentUnit.LinearColor, authoringFlags: prefabEditable, defaultValue: Literal(15, [0, 0, 128, 63, 0, 0, 128, 63, 0, 0, 128, 63])),
                CreateField(2, 10, unit: ContentUnit.Lux, authoringFlags: prefabEditable, defaultValue: Literal(10, [0, 64, 28, 70])),
                CreateField(3, 15, authoringFlags: prefabEditable, defaultValue: Literal(15, [0, 0, 0, 0, 0, 0, 128, 191, 0, 0, 0, 0])),
                CreateField(4, 4, authoringFlags: prefabEditable, defaultValue: allBits),
                CreateField(5, 9, authoringFlags: prefabEditable, defaultValue: Literal(9, [0])),
                CreateField(6, 7, authoringFlags: prefabEditable, defaultValue: Literal(7, [0, 0, 0, 0])),
            ], ContentSchemaStorage.Persistent),
            CreateSchema("render/point-light", ContentSchemaRole.Component, [
                CreateField(1, 15, unit: ContentUnit.LinearColor, authoringFlags: prefabEditable, defaultValue: Literal(15, [0, 0, 128, 63, 0, 0, 128, 63, 0, 0, 128, 63])),
                CreateField(2, 10, unit: ContentUnit.Lumens, authoringFlags: prefabEditable, defaultValue: Literal(10, [0, 0, 122, 68])),
                CreateField(3, 10, unit: ContentUnit.EngineDistance, authoringFlags: prefabEditable, defaultValue: ContentFieldDefaultDescriptor.Meters([10], 1)),
                CreateField(4, 4, authoringFlags: prefabEditable, defaultValue: allBits),
            ], ContentSchemaStorage.Persistent),
            CreateSchema("render/spot-light", ContentSchemaRole.Component, [
                CreateField(1, 15, unit: ContentUnit.LinearColor, authoringFlags: prefabEditable, defaultValue: Literal(15, [0, 0, 128, 63, 0, 0, 128, 63, 0, 0, 128, 63])),
                CreateField(2, 10, unit: ContentUnit.Lumens, authoringFlags: prefabEditable, defaultValue: Literal(10, [0, 0, 122, 68])),
                CreateField(3, 10, unit: ContentUnit.EngineDistance, authoringFlags: prefabEditable, defaultValue: ContentFieldDefaultDescriptor.Meters([10], 1)),
                CreateField(4, 15, authoringFlags: prefabEditable, defaultValue: Literal(15, [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 128, 63])),
                CreateField(5, 10, unit: ContentUnit.Radians, authoringFlags: prefabEditable, defaultValue: Literal(10, [146, 10, 6, 63])),
                CreateField(6, 10, unit: ContentUnit.Radians, authoringFlags: prefabEditable, defaultValue: Literal(10, [219, 15, 73, 63])),
                CreateField(7, 4, authoringFlags: prefabEditable, defaultValue: allBits),
                CreateField(8, 9, authoringFlags: prefabEditable, defaultValue: Literal(9, [0])),
                CreateField(9, 7, authoringFlags: prefabEditable, defaultValue: Literal(7, [0, 0, 0, 0])),
            ], ContentSchemaStorage.Persistent),
        ];
    }

    private static ContentFieldDefaultDescriptor Literal(ushort wireKind, byte[] payload)
        => ContentFieldDefaultDescriptor.Literal(wireKind, 1, payload);

    private static ContentSchemaReference ToReference(string name)
        => new(new ContentSchemaIdentityAssignment(name).SchemaId, 1, 0, ContentSchemaScope.Content);
}
