// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Collections.Immutable;

namespace TerraFX.Animation;

/// <summary>Provides the compiled Animation asset, playback-state, control, track, and event schemas.</summary>
public static class AnimationSchemaCatalogue
{
    private const int NativeSchemaCount = 11;
    private static readonly ImmutableArray<DataSchema> s_allSchemas = CreateSchemas();
    private static readonly ImmutableArray<DataSchema> s_playbackSchemas = [
        .. s_allSchemas.AsSpan(NativeSchemaCount, s_allSchemas.Length - NativeSchemaCount),
        .. RootMotionResourceSchemas.Schemas,
        .. CreateControlAndEventSchemas(),
    ];
    private static readonly ImmutableArray<DataSchema> s_schemas = [.. s_allSchemas.AsSpan(0, NativeSchemaCount)];

    /// <summary>Gets the separately selected controller, playback-state, control, event, and root-motion resource declarations.</summary>
    public static ImmutableArray<DataSchema> PlaybackSchemas => s_playbackSchemas;

    /// <summary>Gets the eleven native animation asset, track, and event declarations.</summary>
    public static ImmutableArray<DataSchema> Schemas => s_schemas;

    private static ImmutableArray<DataSchema> CreateControlAndEventSchemas()
    {
        var assetReference = ToReference("native/asset-reference");
        var eventMarker = ToReference("native/event-marker");
        return [
            CreateSchema("animation/control", ContentSchemaRole.ControlPayload, [
                CreateField(1, 1),
                CreateField(2, 11, reference: ContentReferenceKind.HistoricalEntity),
                CreateField(3, 8),
                CreateField(4, 22, isRequired: false, nestedSchemas: [assetReference], reference: ContentReferenceKind.Asset),
                CreateField(5, 10, isRequired: false),
                CreateField(6, 8, isRequired: false, unit: ContentUnit.TimeSpanTicks),
                CreateField(7, 1, isRequired: false),
                CreateField(8, 11, isRequired: false),
                CreateField(9, 1, isRequired: false),
                CreateField(10, 10, isRequired: false),
            ]),
            CreateSchema("animation/event-marker", ContentSchemaRole.EventPayload, [
                CreateField(1, 11, reference: ContentReferenceKind.HistoricalEntity),
                CreateField(2, 11),
                CreateField(3, 11),
                CreateField(4, 22, nestedSchemas: [assetReference], reference: ContentReferenceKind.Asset),
                CreateField(5, 11),
                CreateField(6, 1),
                CreateField(7, 8),
                CreateField(8, 8),
                CreateField(9, 5),
                CreateField(10, 8, unit: ContentUnit.TimeSpanTicks),
                CreateField(11, 11, reference: ContentReferenceKind.Event),
                CreateField(12, 22, nestedSchemas: [eventMarker]),
            ]),
            CreateSchema("animation/event-completed", ContentSchemaRole.EventPayload, [
                CreateField(1, 11, reference: ContentReferenceKind.HistoricalEntity),
                CreateField(2, 11),
                CreateField(3, 11),
                CreateField(4, 22, nestedSchemas: [assetReference], reference: ContentReferenceKind.Asset),
                CreateField(5, 11),
                CreateField(6, 1),
                CreateField(7, 8),
                CreateField(8, 8),
                CreateField(9, 5),
                CreateField(10, 8, unit: ContentUnit.TimeSpanTicks),
            ]),
        ];
    }

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
        ContentFieldDefaultDescriptor? defaultValue = null,
        ContentAuthoring authoring = ContentAuthoring.None)
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
            authoring,
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
            ContentSchemaOwner.Animation,
            storage,
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
        var animationClipSlot = ToReference("native/animation-clip-slot");
        var animationLayer = ToReference("native/animation-layer");
        var animationReferencePose = ToReference("native/animation-reference-pose");
        var animationClipWeight = ToReference("native/animation-clip-weight");
        var animationTransition = ToReference("native/animation-transition");
        var animationConditionManual = ToReference("native/animation-condition-manual");
        var animationConditionCompleted = ToReference("native/animation-condition-completed");
        var animationConditionAlways = ToReference("native/animation-condition-always");
        var animationActionNone = ToReference("native/animation-action-none");
        var animationActionLayerWeight = ToReference("native/animation-action-layer-weight");
        var animationPlayerState = ToReference("animation/player-state");
        var animationTargetBinding = ToReference("animation/target-binding");
        var animationLayerState = ToReference("animation/layer-state");
        var animationActiveClip = ToReference("animation/active-clip");
        var animationTransitionState = ToReference("animation/transition-state");
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
            CreateSchema("native/animation-controller", ContentSchemaRole.NativeAsset, [
                CreateField(1, 22, isRequired: false, nestedSchemas: [assetReference]),
                CreateField(2, 22, isRequired: false, nestedSchemas: [assetReference]),
                CreateField(3, 24, minimumCount: 1, maximumCount: 32, nestedSchemas: [animationClipSlot]),
                CreateField(4, 24, minimumCount: 1, maximumCount: 8, nestedSchemas: [animationLayer]),
                CreateField(5, 24, minimumCount: 0, maximumCount: 64, nestedSchemas: [animationTransition]),
                CreateField(6, 1, defaultValue: Literal(1, [0])),
                CreateField(7, 11, isRequired: false, reference: ContentReferenceKind.Entity),
            ]),
            CreateSchema("native/animation-clip-slot", ContentSchemaRole.Auxiliary, [
                CreateField(1, 11),
                CreateField(2, 22, nestedSchemas: [assetReference]),
            ]),
            CreateSchema("native/animation-layer", ContentSchemaRole.Auxiliary, [
                CreateField(1, 1),
                CreateField(2, 1),
                CreateField(3, 10, defaultValue: Literal(10, [0, 0, 128, 63])),
                CreateField(4, 23, minimumCount: 0, maximumCount: 4352, elementKind: 10),
                CreateField(5, 24, minimumCount: 0, maximumCount: 4352, nestedSchemas: [animationReferencePose]),
                CreateField(6, 24, minimumCount: 0, maximumCount: 4, nestedSchemas: [animationClipWeight]),
                CreateField(7, 1, defaultValue: Literal(1, [0])),
            ]),
            CreateSchema("native/animation-reference-pose", ContentSchemaRole.Auxiliary, [
                CreateField(1, 1),
                CreateField(2, 11, reference: ContentReferenceKind.EntityOrJoint),
                CreateField(3, 22, nestedSchemas: [localTransform]),
            ]),
            CreateSchema("native/animation-clip-weight", ContentSchemaRole.Auxiliary, [
                CreateField(1, 11),
                CreateField(2, 10),
            ]),
            CreateSchema("native/animation-transition", ContentSchemaRole.Auxiliary, [
                CreateField(1, 11),
                CreateField(2, 1),
                CreateField(3, 11, isRequired: false),
                CreateField(4, 11),
                CreateField(5, 8, unit: ContentUnit.TimeSpanTicks, defaultValue: Literal(8, [128, 132, 30, 0, 0, 0, 0, 0])),
                CreateField(6, 7, defaultValue: Literal(7, [0, 0, 0, 0])),
                CreateField(7, 22, nestedSchemas: [
                    animationConditionManual,
                    animationConditionCompleted,
                    animationConditionAlways,
                ]),
                CreateField(8, 1, defaultValue: Literal(1, [0])),
                CreateField(9, 22, nestedSchemas: [animationActionNone, animationActionLayerWeight]),
                CreateField(10, 1, defaultValue: Literal(1, [0])),
            ]),
            CreateSchema("native/animation-condition-manual", ContentSchemaRole.Auxiliary, []),
            CreateSchema("native/animation-condition-completed", ContentSchemaRole.Auxiliary, []),
            CreateSchema("native/animation-condition-always", ContentSchemaRole.Auxiliary, []),
            CreateSchema("native/animation-action-none", ContentSchemaRole.Auxiliary, []),
            CreateSchema("native/animation-action-layer-weight", ContentSchemaRole.Auxiliary, [
                CreateField(1, 1),
                CreateField(2, 10),
            ]),
            CreateSchema("animation/world-state", ContentSchemaRole.AttachmentState, [
                CreateField(1, 8),
                CreateField(2, 24, minimumCount: 0, maximumCount: 4096, nestedSchemas: [animationPlayerState]),
            ]),
            CreateSchema("animation/player-state", ContentSchemaRole.Auxiliary, [
                CreateField(1, 11, reference: ContentReferenceKind.Entity),
                CreateField(2, 11, reference: ContentReferenceKind.SceneInstance),
                CreateField(3, 22, nestedSchemas: [assetReference], reference: ContentReferenceKind.Asset),
                CreateField(4, 10),
                CreateField(5, 8),
                CreateField(6, 8, isRequired: false),
                CreateField(7, 24, minimumCount: 0, maximumCount: 4352, nestedSchemas: [animationTargetBinding]),
                CreateField(8, 24, minimumCount: 1, maximumCount: 8, nestedSchemas: [animationLayerState]),
                CreateField(9, 11, isRequired: false, reference: ContentReferenceKind.Entity),
            ]),
            CreateSchema("animation/target-binding", ContentSchemaRole.Auxiliary, [
                CreateField(1, 1),
                CreateField(2, 11),
                CreateField(3, 11, isRequired: false, reference: ContentReferenceKind.Entity),
                CreateField(4, 22, nestedSchemas: [localTransform]),
                CreateField(5, 21, isRequired: false, unit: ContentUnit.EngineDistance),
            ]),
            CreateSchema("animation/layer-state", ContentSchemaRole.Auxiliary, [
                CreateField(1, 1),
                CreateField(2, 10),
                CreateField(3, 24, minimumCount: 0, maximumCount: 4, nestedSchemas: [animationActiveClip]),
                CreateField(4, 22, isRequired: false, nestedSchemas: [animationTransitionState]),
            ]),
            CreateSchema("animation/active-clip", ContentSchemaRole.Auxiliary, [
                CreateField(1, 11),
                CreateField(2, 8),
                CreateField(3, 8),
                CreateField(4, 8),
                CreateField(5, 13, minimumCount: 24, maximumCount: 24),
                CreateField(6, 8),
                CreateField(7, 8),
                CreateField(8, 10),
                CreateField(9, 3),
                CreateField(10, 3),
                CreateField(11, 9),
                CreateField(12, 9),
                CreateField(13, 5),
            ]),
            CreateSchema("animation/transition-state", ContentSchemaRole.Auxiliary, [
                CreateField(1, 11),
                CreateField(2, 8),
                CreateField(3, 2),
                CreateField(4, 23, minimumCount: 1, maximumCount: 4, elementKind: 10),
                CreateField(5, 11),
            ]),
            CreateSchema("animation/player", ContentSchemaRole.Component, [
                CreateField(
                    1,
                    22,
                    nestedSchemas: [assetReference],
                    reference: ContentReferenceKind.Asset,
                    authoring: ContentAuthoring.PrefabEditable),
            ], storage: ContentSchemaStorage.Persistent),
        ];
    }

    private static ContentFieldDefaultDescriptor Literal(ushort wireKind, byte[] payload)
        => ContentFieldDefaultDescriptor.Literal(wireKind, 1, payload);

    private static ulong RoleMask(ContentSchemaRole role) => 1UL << ((int)role - 1);

    private static ContentSchemaReference ToReference(string name)
        => new(new ContentSchemaIdentityAssignment(name).SchemaId, 1, 0, ContentSchemaScope.Content);
}
