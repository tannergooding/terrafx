// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Entities;

/// <summary>Provides the selected standalone checkpoint, continuation, and control schemas.</summary>
public static class StandaloneContinuationSchemaCatalogue
{
    private static readonly ImmutableArray<DataSchema> s_schemas = CreateSchemas();

    /// <summary>Gets the immutable standalone schemas compiled from the selected M03 contracts.</summary>
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
        ContentReferenceKind reference = ContentReferenceKind.None,
        ContentUnit unit = ContentUnit.None)
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
            null);

    private static DataSchema CreateSchema(
        string name,
        ContentSchemaRole role,
        ContentSchemaOwner owner,
        ImmutableArray<ContentFieldDescriptor> fields)
    {
        var assignment = new ContentSchemaIdentityAssignment(name);
        var schema = new ContentSchemaReference(assignment.SchemaId, 1, 0, ContentSchemaScope.Content);

        return new DataSchema(
            schema,
            assignment,
            role,
            owner,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            fields);
    }

    private static ImmutableArray<DataSchema> CreateSchemas()
    {
        var recordedTick = new ContentSchemaIdentityAssignment("state/recorded-tick");
        var commandStream = new ContentSchemaIdentityAssignment("state/command-stream");
        var commandKey = new ContentSchemaIdentityAssignment("state/command-key");
        var commandTarget = new ContentSchemaIdentityAssignment("state/command-target");
        var entityDescription = new ContentSchemaIdentityAssignment("state/entity-description");
        var recordedCommand = new ContentSchemaIdentityAssignment("state/recorded-command");
        var control = new ContentSchemaIdentityAssignment("state/control");
        var pcg = new ContentSchemaIdentityAssignment("state/pcg");
        var runtimeProfile = new ContentSchemaReference(
            new ContentSchemaIdentityAssignment("state/runtime-profile").SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var assetReference = new ContentSchemaReference(
            new ContentSchemaIdentityAssignment("native/asset-reference").SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var contentSchemaRef = new ContentSchemaReference(
            new ContentSchemaIdentityAssignment("content/schema-ref").SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var applicationModuleId = new ContentSchemaIdentityAssignment("state/module-id");
        var applicationModule = new ContentSchemaIdentityAssignment("state/module");
        var runtimeNumerics = new ContentSchemaIdentityAssignment("state/numerics");
        var checkpoint = new ContentSchemaIdentityAssignment("state/checkpoint");
        var header = new ContentSchemaIdentityAssignment("state/header");
        var world = new ContentSchemaIdentityAssignment("state/world");
        var schedulingLimits = new ContentSchemaIdentityAssignment("state/scheduling-limits");
        var timeSegment = new ContentSchemaIdentityAssignment("state/time-segment");
        var cursors = new ContentSchemaIdentityAssignment("state/cursors");
        var componentRegistration = new ContentSchemaIdentityAssignment("state/component-registration");
        var systemRegistration = new ContentSchemaIdentityAssignment("state/system-registration");
        var resourceRegistration = new ContentSchemaIdentityAssignment("state/resource-registration");
        var access = new ContentSchemaIdentityAssignment("state/access");
        var partitionRange = new ContentSchemaIdentityAssignment("state/partition-range");
        var entity = new ContentSchemaIdentityAssignment("state/entity");
        var systemState = new ContentSchemaIdentityAssignment("state/system-state");
        var resourceState = new ContentSchemaIdentityAssignment("state/resource-state");
        var attachment = new ContentSchemaIdentityAssignment("state/attachment");
        var identityHistory = new ContentSchemaIdentityAssignment("state/identity-history");
        var identityBranch = new ContentSchemaIdentityAssignment("state/identity-branch");
        var identityPage = new ContentSchemaIdentityAssignment("state/identity-page");
        var forkSource = new ContentSchemaIdentityAssignment("state/fork-source");
        var archetype = new ContentSchemaIdentityAssignment("state/archetype");
        var chunk = new ContentSchemaIdentityAssignment("state/chunk");
        var slot = new ContentSchemaIdentityAssignment("state/slot");
        var componentLayout = new ContentSchemaIdentityAssignment("state/component-layout");
        var sceneInstance = new ContentSchemaIdentityAssignment("state/scene-instance");
        var sceneEntityBinding = new ContentSchemaIdentityAssignment("state/scene-entity-binding");
        var sceneOccurrence = new ContentSchemaIdentityAssignment("state/scene-occurrence");
        var worldHierarchy = new ContentSchemaIdentityAssignment("scene/world-hierarchy");
        var worldHierarchyNode = new ContentSchemaIdentityAssignment("scene/world-hierarchy-node");
        var propertySegment = new ContentSchemaReference(
            new ContentSchemaIdentityAssignment("content/property-segment").SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var sceneIntentReparent = new ContentSchemaReference(
            new ContentSchemaIdentityAssignment("scene/intent-reparent").SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var sceneIntentRootPosition = new ContentSchemaReference(
            new ContentSchemaIdentityAssignment("scene/intent-root-position").SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var externalSceneBinding = new ContentSchemaReference(
            new ContentSchemaIdentityAssignment("state/external-scene-binding").SchemaId,
            1,
            0,
            ContentSchemaScope.Content);

        return [
            CreateSchema("state/file", ContentSchemaRole.Checkpoint, ContentSchemaOwner.Hosting, [
                CreateField(1, 8),
                CreateField(2, 22, nestedSchemas: [ToReference(checkpoint)]),
                CreateField(3, 13, minimumCount: 32, maximumCount: 32),
                CreateField(4, 13, minimumCount: 32, maximumCount: 32),
            ]),
            CreateSchema("state/file-content", ContentSchemaRole.Checkpoint, ContentSchemaOwner.Hosting, [
                CreateField(1, 8),
                CreateField(2, 22, nestedSchemas: [ToReference(checkpoint)]),
            ]),
            CreateSchema("state/checkpoint", ContentSchemaRole.Checkpoint, ContentSchemaOwner.Hosting, [
                CreateField(1, 22, nestedSchemas: [ToReference(header)]),
                CreateField(2, 22, nestedSchemas: [ToReference(world)]),
                CreateField(3, 13, minimumCount: 32, maximumCount: 32),
            ]),
            CreateSchema("state/header", ContentSchemaRole.Checkpoint, ContentSchemaOwner.Hosting, [
                CreateField(1, 13, isRequired: false, minimumCount: 32, maximumCount: 32),
                CreateField(2, 22, nestedSchemas: [ToReference(new ContentSchemaIdentityAssignment("state/application"))]),
                CreateField(3, 11, reference: ContentReferenceKind.World),
                CreateField(4, 22, nestedSchemas: [ToFoundationReference("foundation/profile")]),
                CreateField(5, 8),
                CreateField(6, 1),
                CreateField(7, 11),
                CreateField(8, 22, nestedSchemas: [ToReference(new ContentSchemaIdentityAssignment("state/runtime-profile"))]),
                CreateField(9, 3),
                CreateField(10, 3),
                CreateField(11, 1),
            ]),
            CreateSchema("state/canonical", ContentSchemaRole.Checkpoint, ContentSchemaOwner.Hosting, [
                CreateField(1, 1),
                CreateField(2, 13, isRequired: false, minimumCount: 32, maximumCount: 32),
                CreateField(3, 22, nestedSchemas: [ToReference(new ContentSchemaIdentityAssignment("state/application"))]),
                CreateField(4, 11, reference: ContentReferenceKind.World),
                CreateField(5, 22, nestedSchemas: [ToFoundationReference("foundation/profile")]),
                CreateField(6, 22, nestedSchemas: [ToReference(world)]),
                CreateField(7, 22, isRequired: false, nestedSchemas: [ToReference(new ContentSchemaIdentityAssignment("state/runtime-profile"))]),
            ]),
            CreateSchema("state/world", ContentSchemaRole.Checkpoint, ContentSchemaOwner.Hosting, [
                CreateField(1, 22, nestedSchemas: [ToReference(new ContentSchemaIdentityAssignment("state/configuration"))]),
                CreateField(2, 22, nestedSchemas: [ToReference(new ContentSchemaIdentityAssignment("state/clock"))]),
                CreateField(3, 22, isRequired: false, nestedSchemas: [ToFoundationReference("foundation/frame2")]),
                CreateField(4, 22, nestedSchemas: [ToFoundationReference("foundation/frame3")]),
                CreateField(5, 22, nestedSchemas: [ToReference(new ContentSchemaIdentityAssignment("state/ecs"))]),
                CreateField(6, 22, nestedSchemas: [ToReference(new ContentSchemaIdentityAssignment("state/scenes"))]),
                CreateField(7, 23, minimumCount: 0, maximumCount: 1_048_576, elementKind: 3),
                CreateField(8, 24, minimumCount: 0, maximumCount: 65_536, nestedSchemas: [ToReference(new ContentSchemaIdentityAssignment("state/control"))]),
                CreateField(9, 22, nestedSchemas: [ToReference(cursors)]),
                CreateField(10, 24, minimumCount: 0, maximumCount: 65_536, nestedSchemas: [ToReference(new ContentSchemaIdentityAssignment("state/pcg"))]),
                CreateField(11, 22, isRequired: false, nestedSchemas: [ToReference(new ContentSchemaIdentityAssignment("state/strict-layout"))]),
                CreateField(12, 22, nestedSchemas: [ToReference(new ContentSchemaIdentityAssignment("state/registration"))]),
            ]),
            CreateSchema("state/configuration", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Entities, [
                CreateField(1, 3),
                CreateField(2, 3),
                CreateField(3, 3),
                CreateField(4, 3),
                CreateField(5, 3),
                CreateField(6, 3),
                CreateField(7, 3),
                CreateField(8, 3),
                CreateField(9, 22, nestedSchemas: [ToReference(schedulingLimits)]),
                CreateField(10, 1),
                CreateField(11, 2),
                CreateField(12, 3),
                CreateField(13, 3),
                CreateField(14, 3),
                CreateField(15, 8),
                CreateField(16, 8),
            ]),
            CreateSchema("state/scheduling-limits", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Entities, [
                CreateField(1, 8),
                CreateField(2, 8),
                CreateField(3, 8),
                CreateField(4, 8),
                CreateField(5, 8),
                CreateField(6, 8),
                CreateField(7, 3),
            ]),
            CreateSchema("state/clock", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Entities, [
                CreateField(1, 8),
                CreateField(2, 3),
                CreateField(3, 10),
                CreateField(4, 8),
                CreateField(5, 24, minimumCount: 1, maximumCount: 65_537, nestedSchemas: [ToReference(timeSegment)]),
                CreateField(6, 9),
                CreateField(7, 8),
                CreateField(8, 22, isRequired: false, nestedSchemas: [ToFoundationReference("foundation/frame2")]),
                CreateField(9, 22, nestedSchemas: [ToFoundationReference("foundation/frame3")]),
                CreateField(10, 8),
            ]),
            CreateSchema("state/time-segment", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Entities, [
                CreateField(1, 8),
                CreateField(2, 8),
                CreateField(3, 10),
            ]),
            CreateSchema("state/cursors", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Entities, [
                CreateField(1, 8),
                CreateField(2, 8, isRequired: false),
                CreateField(3, 8, isRequired: false),
                CreateField(4, 24, minimumCount: 0, maximumCount: 0, nestedRoleMask: RoleMask(ContentSchemaRole.ControlPayload)),
                CreateField(5, 24, minimumCount: 0, maximumCount: 0, nestedRoleMask: RoleMask(ContentSchemaRole.ControlPayload)),
                CreateField(6, 24, minimumCount: 0, maximumCount: 0, nestedRoleMask: RoleMask(ContentSchemaRole.EventPayload)),
                CreateField(7, 8, isRequired: false),
            ]),
            CreateSchema("state/registration", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Entities, [
                CreateField(1, 24, minimumCount: 0, maximumCount: 256, nestedSchemas: [ToReference(componentRegistration)]),
                CreateField(2, 24, minimumCount: 0, maximumCount: 256, nestedSchemas: [ToReference(new ContentSchemaIdentityAssignment("content/schema-ref"))]),
                CreateField(3, 24, minimumCount: 0, maximumCount: 4096, nestedSchemas: [ToReference(systemRegistration)]),
                CreateField(4, 24, minimumCount: 0, maximumCount: 4096, nestedSchemas: [ToReference(resourceRegistration)]),
            ]),
            CreateSchema("state/component-registration", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Entities, [
                CreateField(1, 22, nestedSchemas: [ToReference(new ContentSchemaIdentityAssignment("content/schema-ref"))]),
                CreateField(2, 1),
                CreateField(3, 1),
                CreateField(4, 11, reference: ContentReferenceKind.Factory),
                CreateField(5, 3),
            ]),
            CreateSchema("state/system-registration", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Entities, [
                CreateField(1, 11, reference: ContentReferenceKind.System),
                CreateField(2, 7),
                CreateField(3, 1),
                CreateField(4, 1),
                CreateField(5, 24, minimumCount: 0, maximumCount: 256, nestedSchemas: [ToReference(access)]),
                CreateField(6, 23, minimumCount: 0, maximumCount: 4095, elementKind: 11),
                CreateField(7, 23, minimumCount: 0, maximumCount: 4095, elementKind: 11),
                CreateField(8, 11, reference: ContentReferenceKind.Factory),
                CreateField(9, 3),
                CreateField(10, 22, isRequired: false, nestedSchemas: [ToReference(new ContentSchemaIdentityAssignment("content/schema-ref"))]),
            ]),
            CreateSchema("state/access", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Entities, [
                CreateField(1, 1),
                CreateField(2, 11, reference: ContentReferenceKind.ResourceOrSchema),
                CreateField(3, 1),
                CreateField(4, 22, isRequired: false, nestedSchemas: [ToReference(partitionRange)]),
            ]),
            CreateSchema("state/partition-range", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Entities, [
                CreateField(1, 3),
                CreateField(2, 3),
            ]),
            CreateSchema("state/resource-registration", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Entities, [
                CreateField(1, 11),
                CreateField(2, 3),
                CreateField(3, 9),
                CreateField(4, 22, nestedSchemas: [ToReference(new ContentSchemaIdentityAssignment("content/schema-ref"))]),
                CreateField(5, 11, reference: ContentReferenceKind.Factory),
                CreateField(6, 3),
            ]),
            CreateSchema("state/ecs", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Entities, [
                CreateField(1, 24, minimumCount: 0, maximumCount: 1_048_576, nestedSchemas: [ToReference(entity)]),
                CreateField(2, 24, minimumCount: 0, maximumCount: 4096, nestedSchemas: [ToReference(systemState)]),
                CreateField(3, 24, minimumCount: 0, maximumCount: 4096, nestedSchemas: [ToReference(resourceState)]),
                CreateField(4, 24, minimumCount: 0, maximumCount: 1_048_576, nestedSchemas: [ToReference(attachment)]),
                CreateField(5, 22, nestedSchemas: [ToReference(identityHistory)]),
            ]),
            CreateSchema("state/entity", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Entities, [
                CreateField(1, 11, reference: ContentReferenceKind.Entity),
                CreateField(2, 24, minimumCount: 0, maximumCount: 256, nestedRoleMask: RoleMask(ContentSchemaRole.Component)),
                CreateField(3, 24, minimumCount: 0, maximumCount: 256, nestedRoleMask: RoleMask(ContentSchemaRole.EntityTag)),
                CreateField(4, 24, minimumCount: 0, maximumCount: 256, nestedSchemas: [ToReference(new ContentSchemaIdentityAssignment("content/schema-ref"))]),
                CreateField(5, 8),
                CreateField(6, 11, isRequired: false, reference: ContentReferenceKind.SceneInstance),
            ]),
            CreateSchema("state/identity-history", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Entities, [
                CreateField(1, 11, reference: ContentReferenceKind.World),
                CreateField(2, 11),
                CreateField(3, 11),
                CreateField(4, 11),
                CreateField(5, 8),
                CreateField(6, 24, minimumCount: 1, maximumCount: 1024, nestedSchemas: [ToReference(identityBranch)]),
                CreateField(7, 24, minimumCount: 0, maximumCount: 8192, nestedSchemas: [ToReference(identityPage)]),
            ]),
            CreateSchema("state/identity-branch", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Entities, [
                CreateField(1, 2),
                CreateField(2, 11),
                CreateField(3, 11),
                CreateField(4, 11),
                CreateField(5, 11, isRequired: false),
                CreateField(6, 8),
                CreateField(7, 13, isRequired: false, minimumCount: 32, maximumCount: 32),
                CreateField(8, 22, isRequired: false, nestedSchemas: [ToReference(forkSource)]),
                CreateField(9, 1),
                CreateField(10, 8),
            ]),
            CreateSchema("state/fork-source", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Entities, [
                CreateField(1, 11, reference: ContentReferenceKind.World),
                CreateField(2, 11),
                CreateField(3, 11),
                CreateField(4, 11),
                CreateField(5, 13, minimumCount: 32, maximumCount: 32),
                CreateField(6, 13, minimumCount: 32, maximumCount: 32),
                CreateField(7, 22, isRequired: false, nestedSchemas: [ToReference(forkSource)]),
            ]),
            CreateSchema("state/identity-page", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Entities, [
                CreateField(1, 8),
                CreateField(2, 23, minimumCount: 1, maximumCount: 4096, elementKind: 11),
                CreateField(3, 23, minimumCount: 1, maximumCount: 4096, elementKind: 3),
            ]),
            CreateSchema("state/system-state", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Entities, [
                CreateField(1, 11, reference: ContentReferenceKind.System),
                CreateField(2, 22, nestedRoleMask: RoleMask(ContentSchemaRole.SystemState)),
            ]),
            CreateSchema("state/resource-state", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Entities, [
                CreateField(1, 11),
                CreateField(2, 22, nestedRoleMask: RoleMask(ContentSchemaRole.ResourceState)),
            ]),
            CreateSchema("state/attachment", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Entities, [
                CreateField(1, 22, nestedSchemas: [ToReference(new ContentSchemaIdentityAssignment("content/schema-ref"))]),
                CreateField(2, 11, isRequired: false, reference: ContentReferenceKind.Entity),
                CreateField(3, 22, nestedRoleMask: RoleMask(ContentSchemaRole.AttachmentState)),
            ]),
            CreateSchema("state/strict-layout", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Entities, [
                CreateField(1, 24, minimumCount: 0, maximumCount: 4096, nestedSchemas: [ToReference(archetype)]),
                CreateField(2, 24, minimumCount: 0, maximumCount: 2_097_152, nestedSchemas: [ToReference(slot)]),
                CreateField(3, 23, minimumCount: 0, maximumCount: 2_097_152, elementKind: 3),
                CreateField(4, 8),
                CreateField(5, 24, minimumCount: 0, maximumCount: 256, nestedSchemas: [ToReference(componentLayout)]),
            ]),
            CreateSchema("state/archetype", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Entities, [
                CreateField(1, 23, minimumCount: 0, maximumCount: 256, elementKind: 11),
                CreateField(2, 24, minimumCount: 0, maximumCount: 1_048_576, nestedSchemas: [ToReference(chunk)]),
            ]),
            CreateSchema("state/chunk", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Entities, [
                CreateField(1, 8),
                CreateField(2, 23, minimumCount: 0, maximumCount: 256, elementKind: 11, reference: ContentReferenceKind.Entity),
                CreateField(3, 2),
                CreateField(4, 3),
            ]),
            CreateSchema("state/slot", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Entities, [
                CreateField(1, 3),
                CreateField(2, 3),
                CreateField(3, 1),
                CreateField(4, 11, isRequired: false, reference: ContentReferenceKind.Entity),
                CreateField(5, 13, isRequired: false, minimumCount: 32, maximumCount: 32),
                CreateField(6, 8, isRequired: false),
                CreateField(7, 2, isRequired: false),
            ]),
            CreateSchema("state/component-layout", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Entities, [
                CreateField(1, 22, nestedSchemas: [ToReference(new ContentSchemaIdentityAssignment("content/schema-ref"))]),
                CreateField(2, 3),
                CreateField(3, 11),
            ]),
            CreateSchema("state/scenes", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Scenes, [
                CreateField(1, 24, minimumCount: 0, maximumCount: 65_536, nestedSchemas: [ToReference(sceneInstance)]),
                CreateField(2, 22, nestedSchemas: [ToReference(worldHierarchy)]),
            ]),
            CreateSchema("state/scene-instance", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Scenes, [
                CreateField(1, 11, reference: ContentReferenceKind.SceneInstance),
                CreateField(2, 22, nestedSchemas: [assetReference]),
                CreateField(3, 23, minimumCount: 0, maximumCount: 1_048_576, elementKind: 11, reference: ContentReferenceKind.Entity),
                CreateField(4, 24, minimumCount: 0, maximumCount: 1_048_576, nestedSchemas: [ToReference(sceneEntityBinding)]),
                CreateField(5, 24, minimumCount: 0, maximumCount: 65_536, nestedSchemas: [ToReference(new ContentSchemaIdentityAssignment("state/external-scene-binding"))]),
                CreateField(6, 8),
                CreateField(7, 24, minimumCount: 1, maximumCount: 1_048_576, nestedSchemas: [ToReference(sceneOccurrence)]),
            ]),
            CreateSchema("state/scene-entity-binding", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Scenes, [
                CreateField(1, 23, minimumCount: 0, maximumCount: 32, elementKind: 11),
                CreateField(2, 22, nestedSchemas: [assetReference]),
                CreateField(3, 11),
                CreateField(4, 11),
                CreateField(5, 11),
                CreateField(6, 11),
            ]),
            CreateSchema("state/scene-occurrence", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Scenes, [
                CreateField(1, 23, minimumCount: 0, maximumCount: 32, elementKind: 11),
                CreateField(2, 11),
                CreateField(3, 22, nestedSchemas: [assetReference]),
                CreateField(4, 11, isRequired: false),
                CreateField(5, 11, isRequired: false),
            ]),
            CreateSchema("scene/world-hierarchy", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Scenes, [
                CreateField(1, 24, minimumCount: 0, maximumCount: 1_048_576, nestedSchemas: [ToReference(worldHierarchyNode)]),
                CreateField(2, 23, minimumCount: 0, maximumCount: 1_048_576, elementKind: 11, reference: ContentReferenceKind.Entity),
            ]),
            CreateSchema("scene/world-hierarchy-node", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Scenes, [
                CreateField(1, 11, reference: ContentReferenceKind.Entity),
                CreateField(2, 11, isRequired: false, reference: ContentReferenceKind.Entity),
                CreateField(3, 23, minimumCount: 0, maximumCount: 1_048_576, elementKind: 11, reference: ContentReferenceKind.Entity),
            ]),
            CreateSchema("state/application", ContentSchemaRole.Checkpoint, ContentSchemaOwner.Hosting, [
                CreateField(1, 11),
                CreateField(2, 3),
                CreateField(3, 3),
                CreateField(4, 24, minimumCount: 0, maximumCount: 256, nestedSchemas: [ToReference(applicationModuleId)]),
            ]),
            CreateSchema("state/module-id", ContentSchemaRole.Checkpoint, ContentSchemaOwner.Hosting, [
                CreateField(1, 11, reference: ContentReferenceKind.Module),
                CreateField(2, 3),
            ]),
            CreateSchema("state/module", ContentSchemaRole.Checkpoint, ContentSchemaOwner.Hosting, [
                CreateField(1, 11, reference: ContentReferenceKind.Module),
                CreateField(2, 3),
                CreateField(3, 13, minimumCount: 32, maximumCount: 32),
            ]),
            CreateSchema("state/numerics", ContentSchemaRole.Checkpoint, ContentSchemaOwner.Hosting, [
                CreateField(1, 13, minimumCount: 32, maximumCount: 32),
                CreateField(2, 1),
                CreateField(3, 2),
                CreateField(4, 1),
                CreateField(5, 9),
                CreateField(6, 9),
            ]),
            CreateSchema("state/runtime-profile", ContentSchemaRole.Checkpoint, ContentSchemaOwner.Hosting, [
                CreateField(1, 13, minimumCount: 32, maximumCount: 32),
                CreateField(2, 13, minimumCount: 32, maximumCount: 32),
                CreateField(3, 13, minimumCount: 32, maximumCount: 32),
                CreateField(4, 1),
                CreateField(5, 1),
                CreateField(6, 1),
                CreateField(7, 4),
                CreateField(8, 22, nestedSchemas: [ToReference(runtimeNumerics)]),
                CreateField(9, 24, minimumCount: 0, maximumCount: 256, nestedSchemas: [ToReference(applicationModule)]),
            ]),
            CreateSchema("state/continuation", ContentSchemaRole.Checkpoint, ContentSchemaOwner.Hosting, [
                CreateField(1, 13, minimumCount: 32, maximumCount: 32),
                CreateField(2, 13, minimumCount: 32, maximumCount: 32),
                CreateField(3, 13, isRequired: false, minimumCount: 32, maximumCount: 32),
                CreateField(4, 22, nestedSchemas: [runtimeProfile]),
                CreateField(5, 11),
                CreateField(6, 11),
                CreateField(7, 11),
                CreateField(8, 8),
                CreateField(9, 24, minimumCount: 1, maximumCount: 65_536, nestedSchemas: [ToReference(recordedTick)]),
                CreateField(10, 8),
                CreateField(11, 13, minimumCount: 32, maximumCount: 32),
            ]),
            CreateSchema("state/recorded-tick", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Hosting, [
                CreateField(1, 8),
                CreateField(2, 24, minimumCount: 0, maximumCount: 65_536, nestedSchemas: [ToReference(control)]),
                CreateField(3, 22, nestedSchemas: [ToReference(commandStream)]),
                CreateField(4, 13, minimumCount: 32, maximumCount: 32),
                CreateField(5, 23, minimumCount: 0, maximumCount: 1_048_576, elementKind: 3),
                CreateField(6, 24, minimumCount: 0, maximumCount: 65_536, nestedSchemas: [ToReference(pcg)]),
                CreateField(7, 10),
                CreateField(8, 1),
                CreateField(9, 13, minimumCount: 32, maximumCount: 32),
            ]),
            CreateSchema("state/command-stream", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Entities, [
                CreateField(1, 24, minimumCount: 0, maximumCount: 65_536, nestedSchemas: [ToReference(recordedCommand)]),
            ]),
            CreateSchema("state/command-key", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Entities, [
                CreateField(1, 1),
                CreateField(2, 7),
                CreateField(3, 3),
                CreateField(4, 8),
            ]),
            CreateSchema("state/command-target", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Entities, [
                CreateField(1, 1),
                CreateField(2, 11, isRequired: false, reference: ContentReferenceKind.Entity),
                CreateField(3, 22, isRequired: false, nestedSchemas: [ToReference(commandKey)]),
            ]),
            CreateSchema("state/entity-description", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Entities, [
                CreateField(1, 11, isRequired: false),
                CreateField(2, 24, minimumCount: 0, maximumCount: 256, nestedRoleMask: RoleMask(ContentSchemaRole.Component)),
                CreateField(3, 24, minimumCount: 0, maximumCount: 256, nestedRoleMask: RoleMask(ContentSchemaRole.EntityTag)),
            ]),
            CreateSchema("state/recorded-command", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Entities, [
                CreateField(1, 22, nestedSchemas: [ToReference(commandKey)]),
                CreateField(2, 1),
                CreateField(3, 22, isRequired: false, nestedSchemas: [ToReference(commandTarget)]),
                CreateField(4, 22, isRequired: false, nestedSchemas: [ToReference(entityDescription)]),
                CreateField(5, 22, isRequired: false, nestedRoleMask: RoleMask(ContentSchemaRole.Component)),
                CreateField(6, 22, isRequired: false, nestedSchemas: [contentSchemaRef]),
                CreateField(7, 1, isRequired: false),
                CreateField(8, 24, isRequired: false, minimumCount: 1, maximumCount: 64, nestedSchemas: [propertySegment]),
                CreateField(9, 22, isRequired: false, nestedSchemas: [ToReference(commandTarget)]),
            ]),
            CreateSchema("state/control", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Entities, [
                CreateField(1, 8),
                CreateField(2, 8),
                CreateField(3, 22, nestedRoleMask: RoleMask(ContentSchemaRole.ControlPayload)),
            ]),
            CreateSchema("state/control-timescale", ContentSchemaRole.ControlPayload, ContentSchemaOwner.Entities, [
                CreateField(1, 10),
                CreateField(2, 8),
            ]),
            CreateSchema("state/control-step", ContentSchemaRole.ControlPayload, ContentSchemaOwner.Entities, [
                CreateField(1, 8),
            ]),
            CreateSchema("state/control-frame2", ContentSchemaRole.ControlPayload, ContentSchemaOwner.Entities, [
                CreateField(1, 22, nestedSchemas: [ToFoundationReference("foundation/frame2")]),
            ]),
            CreateSchema("state/control-frame3", ContentSchemaRole.ControlPayload, ContentSchemaOwner.Entities, [
                CreateField(1, 22, nestedSchemas: [ToFoundationReference("foundation/frame3")]),
            ]),
            CreateSchema("state/control-branch", ContentSchemaRole.ControlPayload, ContentSchemaOwner.Entities, [
                CreateField(1, 11),
                CreateField(2, 11),
                CreateField(3, 2),
            ]),
            CreateSchema("state/control-scene", ContentSchemaRole.ControlPayload, ContentSchemaOwner.Scenes, [
                CreateField(1, 1),
                CreateField(2, 11, reference: ContentReferenceKind.HistoricalSceneInstance),
                CreateField(3, 22, isRequired: false, nestedSchemas: [assetReference]),
                CreateField(4, 24, isRequired: false, minimumCount: 1, maximumCount: 65_536, nestedSchemas: [sceneIntentReparent, sceneIntentRootPosition]),
                CreateField(5, 1, isRequired: false),
                CreateField(6, 24, isRequired: false, minimumCount: 0, maximumCount: 65_536, nestedSchemas: [externalSceneBinding]),
                CreateField(7, 11, isRequired: false),
                CreateField(8, 11, isRequired: false),
            ]),
            CreateSchema("state/external-scene-binding", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Scenes, [
                CreateField(1, 11, reference: ContentReferenceKind.ExternalSceneBinding),
                CreateField(2, 11, isRequired: false, reference: ContentReferenceKind.HistoricalEntity),
            ]),
            CreateSchema("scene/intent-reparent", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Scenes, [
                CreateField(1, 11, reference: ContentReferenceKind.HistoricalEntity),
                CreateField(2, 11, isRequired: false, reference: ContentReferenceKind.HistoricalEntity),
                CreateField(3, 1),
            ]),
            CreateSchema("scene/intent-root-position", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Scenes, [
                CreateField(1, 11, reference: ContentReferenceKind.HistoricalEntity),
                CreateField(2, 21, unit: ContentUnit.EngineDistance),
            ]),
            CreateSchema("state/pcg", ContentSchemaRole.Auxiliary, ContentSchemaOwner.Entities, [
                CreateField(1, 11, reference: ContentReferenceKind.Stream),
                CreateField(2, 4),
                CreateField(3, 4),
            ]),
        ];
    }

    private static ulong RoleMask(ContentSchemaRole role) => 1UL << ((int)role - 1);

    private static ContentSchemaReference ToFoundationReference(string name)
    {
        var identityAssignment = new ContentSchemaIdentityAssignment(name);
        var foundationSchema = FoundationCatalogue.Find(identityAssignment.SchemaId)
            ?? throw new InvalidOperationException("The nested Foundation descriptor is missing its frozen name UUID assignment.");

        return new ContentSchemaReference(
            identityAssignment.SchemaId,
            checked((int)foundationSchema.Major),
            0,
            ContentSchemaScope.Foundation);
    }

    private static ContentSchemaReference ToReference(ContentSchemaIdentityAssignment assignment)
        => new(assignment.SchemaId, 1, 0, ContentSchemaScope.Content);

}
