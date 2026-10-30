// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Collections.Immutable;

namespace TerraFX.Animation;

/// <summary>Provides the compiled skeleton, clip, track, and animation event schemas.</summary>
public static class AnimationSchemaCatalogue
{
    private static readonly ImmutableArray<DataSchema> s_schemas = CreateSchemas();

    /// <summary>Gets the immutable animation schema declarations.</summary>
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
            ContentAuthoring.None,
            defaultValue);

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
            ContentSchemaOwner.Animation,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            fields);
    }

    private static ImmutableArray<DataSchema> CreateSchemas()
    {
        var joint = ToReference("native/joint");
        var transformTrack = ToReference("native/transform-track");
        var spriteTrack = ToReference("native/sprite-track");
        var keyVector3 = ToReference("native/key-vector3");
        var keyQuaternion = ToReference("native/key-quaternion");
        var keySprite = ToReference("native/key-sprite");
        var clipEvent = ToReference("native/clip-event");
        var rootMotion = ToReference("native/root-motion");
        var assetReference = ToReference("native/asset-reference");
        var bounds = ToReference("native/bounds3");
        var localTransform = ToReference("scene/local-transform");

        return [
            CreateSchema("native/skeleton", ContentSchemaRole.NativeAsset, [
                CreateField(1, 24, minimumCount: 1, maximumCount: 256, nestedSchemas: [joint]),
                CreateField(2, 11, isRequired: false, reference: ContentReferenceKind.Joint),
            ]),
            CreateSchema("native/joint", ContentSchemaRole.Auxiliary, [
                CreateField(1, 11, reference: ContentReferenceKind.Joint),
                CreateField(2, 7),
                CreateField(3, 22, nestedSchemas: [localTransform]),
                CreateField(4, 19),
            ]),
            CreateSchema("native/clip", ContentSchemaRole.NativeAsset, [
                CreateField(1, 8, unit: ContentUnit.TimeSpanTicks),
                CreateField(2, 24, minimumCount: 0, maximumCount: 4096, nestedSchemas: [transformTrack, spriteTrack]),
                CreateField(3, 24, minimumCount: 0, maximumCount: 1_048_576, nestedSchemas: [clipEvent]),
                CreateField(4, 1, defaultValue: Literal(1, [0])),
                CreateField(5, 22, isRequired: false, nestedSchemas: [assetReference]),
                CreateField(6, 22, isRequired: false, nestedSchemas: [rootMotion]),
                CreateField(7, 22, isRequired: false, nestedSchemas: [bounds]),
                CreateField(8, 1),
                CreateField(9, 10, unit: ContentUnit.EngineDistance, defaultValue: Literal(10, [0, 0, 0, 0])),
                CreateField(10, 22, isRequired: false, nestedSchemas: [assetReference]),
            ]),
            CreateSchema("native/transform-track", ContentSchemaRole.Auxiliary, [
                CreateField(1, 11, reference: ContentReferenceKind.EntityOrJoint),
                CreateField(2, 1),
                CreateField(3, 1),
                CreateField(4, 1),
                CreateField(5, 24, minimumCount: 1, maximumCount: 1_048_576, nestedSchemas: [keyVector3, keyQuaternion]),
            ]),
            CreateSchema("native/sprite-track", ContentSchemaRole.Auxiliary, [
                CreateField(1, 11, reference: ContentReferenceKind.Entity),
                CreateField(2, 24, minimumCount: 1, maximumCount: 1_048_576, nestedSchemas: [keySprite]),
            ]),
            CreateSchema("native/key-vector3", ContentSchemaRole.Auxiliary, [
                CreateField(1, 8, unit: ContentUnit.TimeSpanTicks),
                CreateField(2, 15),
                CreateField(3, 15, isRequired: false),
                CreateField(4, 15, isRequired: false),
            ]),
            CreateSchema("native/key-quaternion", ContentSchemaRole.Auxiliary, [
                CreateField(1, 8, unit: ContentUnit.TimeSpanTicks),
                CreateField(2, 17),
                CreateField(3, 16, isRequired: false),
                CreateField(4, 16, isRequired: false),
            ]),
            CreateSchema("native/key-sprite", ContentSchemaRole.Auxiliary, [
                CreateField(1, 8, unit: ContentUnit.TimeSpanTicks),
                CreateField(2, 22, nestedSchemas: [assetReference]),
                CreateField(3, 11),
            ]),
            CreateSchema("native/clip-event", ContentSchemaRole.Auxiliary, [
                CreateField(1, 11, reference: ContentReferenceKind.Event),
                CreateField(2, 8, unit: ContentUnit.TimeSpanTicks),
                CreateField(3, 22, nestedRoleMask: RoleMask(ContentSchemaRole.EventPayload)),
            ]),
            CreateSchema("native/event-marker", ContentSchemaRole.EventPayload, [
                CreateField(1, 12, minimumCount: 1, maximumCount: 128),
            ]),
            CreateSchema("native/root-motion", ContentSchemaRole.Auxiliary, [
                CreateField(1, 11, reference: ContentReferenceKind.Joint),
                CreateField(2, 1),
            ]),
        ];
    }

    private static ContentFieldDefaultDescriptor Literal(ushort wireKind, byte[] payload)
        => ContentFieldDefaultDescriptor.Literal(wireKind, 1, payload);

    private static ulong RoleMask(ContentSchemaRole role) => 1UL << ((int)role - 1);

    private static ContentSchemaReference ToReference(string name)
        => new(new ContentSchemaIdentityAssignment(name).SchemaId, 1, 0, ContentSchemaScope.Content);
}
