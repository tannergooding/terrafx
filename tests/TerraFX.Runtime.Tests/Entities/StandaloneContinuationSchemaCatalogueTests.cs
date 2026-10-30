// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Linq;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class StandaloneContinuationSchemaCatalogueTests
{
    [Test]
    public void CatalogueMatchesTheSelectedContinuationAndControlDeclarations()
    {
        var schemas = StandaloneContinuationSchemaCatalogue.Schemas;
        var expected = new (string Name, ContentSchemaOwner Owner, ContentSchemaRole Role)[] {
            ("state/file", ContentSchemaOwner.Hosting, ContentSchemaRole.Checkpoint),
            ("state/file-content", ContentSchemaOwner.Hosting, ContentSchemaRole.Checkpoint),
            ("state/checkpoint", ContentSchemaOwner.Hosting, ContentSchemaRole.Checkpoint),
            ("state/header", ContentSchemaOwner.Hosting, ContentSchemaRole.Checkpoint),
            ("state/canonical", ContentSchemaOwner.Hosting, ContentSchemaRole.Checkpoint),
            ("state/world", ContentSchemaOwner.Hosting, ContentSchemaRole.Checkpoint),
            ("state/configuration", ContentSchemaOwner.Entities, ContentSchemaRole.Auxiliary),
            ("state/scheduling-limits", ContentSchemaOwner.Entities, ContentSchemaRole.Auxiliary),
            ("state/clock", ContentSchemaOwner.Entities, ContentSchemaRole.Auxiliary),
            ("state/time-segment", ContentSchemaOwner.Entities, ContentSchemaRole.Auxiliary),
            ("state/cursors", ContentSchemaOwner.Entities, ContentSchemaRole.Auxiliary),
            ("state/registration", ContentSchemaOwner.Entities, ContentSchemaRole.Auxiliary),
            ("state/component-registration", ContentSchemaOwner.Entities, ContentSchemaRole.Auxiliary),
            ("state/system-registration", ContentSchemaOwner.Entities, ContentSchemaRole.Auxiliary),
            ("state/access", ContentSchemaOwner.Entities, ContentSchemaRole.Auxiliary),
            ("state/partition-range", ContentSchemaOwner.Entities, ContentSchemaRole.Auxiliary),
            ("state/resource-registration", ContentSchemaOwner.Entities, ContentSchemaRole.Auxiliary),
            ("state/ecs", ContentSchemaOwner.Entities, ContentSchemaRole.Auxiliary),
            ("state/entity", ContentSchemaOwner.Entities, ContentSchemaRole.Auxiliary),
            ("state/identity-history", ContentSchemaOwner.Entities, ContentSchemaRole.Auxiliary),
            ("state/identity-branch", ContentSchemaOwner.Entities, ContentSchemaRole.Auxiliary),
            ("state/fork-source", ContentSchemaOwner.Entities, ContentSchemaRole.Auxiliary),
            ("state/identity-page", ContentSchemaOwner.Entities, ContentSchemaRole.Auxiliary),
            ("state/system-state", ContentSchemaOwner.Entities, ContentSchemaRole.Auxiliary),
            ("state/resource-state", ContentSchemaOwner.Entities, ContentSchemaRole.Auxiliary),
            ("state/attachment", ContentSchemaOwner.Entities, ContentSchemaRole.Auxiliary),
            ("state/strict-layout", ContentSchemaOwner.Entities, ContentSchemaRole.Auxiliary),
            ("state/archetype", ContentSchemaOwner.Entities, ContentSchemaRole.Auxiliary),
            ("state/chunk", ContentSchemaOwner.Entities, ContentSchemaRole.Auxiliary),
            ("state/slot", ContentSchemaOwner.Entities, ContentSchemaRole.Auxiliary),
            ("state/component-layout", ContentSchemaOwner.Entities, ContentSchemaRole.Auxiliary),
            ("state/scenes", ContentSchemaOwner.Scenes, ContentSchemaRole.Auxiliary),
            ("state/scene-instance", ContentSchemaOwner.Scenes, ContentSchemaRole.Auxiliary),
            ("state/scene-entity-binding", ContentSchemaOwner.Scenes, ContentSchemaRole.Auxiliary),
            ("state/scene-occurrence", ContentSchemaOwner.Scenes, ContentSchemaRole.Auxiliary),
            ("scene/world-hierarchy", ContentSchemaOwner.Scenes, ContentSchemaRole.Auxiliary),
            ("scene/world-hierarchy-node", ContentSchemaOwner.Scenes, ContentSchemaRole.Auxiliary),
            ("state/application", ContentSchemaOwner.Hosting, ContentSchemaRole.Checkpoint),
            ("state/module-id", ContentSchemaOwner.Hosting, ContentSchemaRole.Checkpoint),
            ("state/module", ContentSchemaOwner.Hosting, ContentSchemaRole.Checkpoint),
            ("state/numerics", ContentSchemaOwner.Hosting, ContentSchemaRole.Checkpoint),
            ("state/runtime-profile", ContentSchemaOwner.Hosting, ContentSchemaRole.Checkpoint),
            ("state/continuation", ContentSchemaOwner.Hosting, ContentSchemaRole.Checkpoint),
            ("state/recorded-tick", ContentSchemaOwner.Hosting, ContentSchemaRole.Auxiliary),
            ("state/command-stream", ContentSchemaOwner.Entities, ContentSchemaRole.Auxiliary),
            ("state/command-key", ContentSchemaOwner.Entities, ContentSchemaRole.Auxiliary),
            ("state/command-target", ContentSchemaOwner.Entities, ContentSchemaRole.Auxiliary),
            ("state/entity-description", ContentSchemaOwner.Entities, ContentSchemaRole.Auxiliary),
            ("state/recorded-command", ContentSchemaOwner.Entities, ContentSchemaRole.Auxiliary),
            ("state/control", ContentSchemaOwner.Entities, ContentSchemaRole.Auxiliary),
            ("state/control-timescale", ContentSchemaOwner.Entities, ContentSchemaRole.ControlPayload),
            ("state/control-step", ContentSchemaOwner.Entities, ContentSchemaRole.ControlPayload),
            ("state/control-frame2", ContentSchemaOwner.Entities, ContentSchemaRole.ControlPayload),
            ("state/control-frame3", ContentSchemaOwner.Entities, ContentSchemaRole.ControlPayload),
            ("state/control-branch", ContentSchemaOwner.Entities, ContentSchemaRole.ControlPayload),
            ("state/control-scene", ContentSchemaOwner.Scenes, ContentSchemaRole.ControlPayload),
            ("state/external-scene-binding", ContentSchemaOwner.Scenes, ContentSchemaRole.Auxiliary),
            ("scene/intent-reparent", ContentSchemaOwner.Scenes, ContentSchemaRole.Auxiliary),
            ("scene/intent-root-position", ContentSchemaOwner.Scenes, ContentSchemaRole.Auxiliary),
            ("state/pcg", ContentSchemaOwner.Entities, ContentSchemaRole.Auxiliary),
        };

        Assert.That(schemas, Has.Length.EqualTo(expected.Length));

        foreach (var (name, owner, role) in expected)
        {
            var schema = schemas.Single(candidate => candidate.IdentityAssignment.Name == name);

            Assert.That(schema.Owner, Is.EqualTo(owner));
            Assert.That(schema.Role, Is.EqualTo(role));
            Assert.That(schema.Schema.Major, Is.EqualTo(1));
            Assert.That(schema.Schema.Minor, Is.Zero);
            Assert.That(schema.Schema.Scope, Is.EqualTo(ContentSchemaScope.Content));
            Assert.That(schema.IdentityAssignment, Is.EqualTo(new ContentSchemaIdentityAssignment(name)));
        }

        var recordedTick = schemas.Single(static schema => schema.IdentityAssignment.Name == "state/recorded-tick");
        Assert.That(recordedTick.Fields[4].WireKind, Is.EqualTo(23));
        Assert.That(recordedTick.Fields[4].ElementKind, Is.EqualTo(3));
        Assert.That(recordedTick.Fields[4].MinimumCount, Is.Zero);
        Assert.That(recordedTick.Fields[4].MaximumCount, Is.EqualTo(1_048_576));
        Assert.That(recordedTick.Fields[1].NestedSchemas.Single().SchemaId, Is.EqualTo(
            schemas.Single(static schema => schema.IdentityAssignment.Name == "state/control").Schema.SchemaId));
        Assert.That(recordedTick.Fields[5].NestedSchemas.Single().SchemaId, Is.EqualTo(
            schemas.Single(static schema => schema.IdentityAssignment.Name == "state/pcg").Schema.SchemaId));

        var runtimeProfile = schemas.Single(static schema => schema.IdentityAssignment.Name == "state/runtime-profile");
        Assert.That(runtimeProfile.Fields[7].NestedSchemas.Single().SchemaId, Is.EqualTo(
            schemas.Single(static schema => schema.IdentityAssignment.Name == "state/numerics").Schema.SchemaId));
        Assert.That(runtimeProfile.Fields[8].NestedSchemas.Single().SchemaId, Is.EqualTo(
            schemas.Single(static schema => schema.IdentityAssignment.Name == "state/module").Schema.SchemaId));

        var header = schemas.Single(static schema => schema.IdentityAssignment.Name == "state/header");
        Assert.That(header.Fields[2].Reference, Is.EqualTo(ContentReferenceKind.World));
        Assert.That(header.Fields[3].NestedSchemas.Single(), Is.EqualTo(
            CoreContentSchemaCatalogue.FoundationSchemas.Single(
                static schema => schema.IdentityAssignment.Name == "foundation/profile").Schema));
        Assert.That(header.Fields[7].NestedSchemas.Single().SchemaId, Is.EqualTo(runtimeProfile.Schema.SchemaId));

        var checkpoint = schemas.Single(static schema => schema.IdentityAssignment.Name == "state/checkpoint");
        Assert.That(checkpoint.Fields[0].NestedSchemas.Single().SchemaId, Is.EqualTo(header.Schema.SchemaId));
        Assert.That(checkpoint.Fields[1].NestedSchemas.Single().SchemaId, Is.EqualTo(
            new ContentSchemaIdentityAssignment("state/world").SchemaId));

        var world = schemas.Single(static schema => schema.IdentityAssignment.Name == "state/world");
        Assert.That(world.Fields[2].IsRequired, Is.False);
        Assert.That(world.Fields[2].NestedSchemas.Single().Scope, Is.EqualTo(ContentSchemaScope.Foundation));
        Assert.That(world.Fields[6].ElementKind, Is.EqualTo(3));
        Assert.That(world.Fields[7].NestedSchemas.Single().SchemaId, Is.EqualTo(
            schemas.Single(static schema => schema.IdentityAssignment.Name == "state/control").Schema.SchemaId));

        var configuration = schemas.Single(static schema => schema.IdentityAssignment.Name == "state/configuration");
        Assert.That(configuration.Fields, Has.Length.EqualTo(16));
        Assert.That(configuration.Fields[8].NestedSchemas.Single().SchemaId, Is.EqualTo(
            schemas.Single(static schema => schema.IdentityAssignment.Name == "state/scheduling-limits").Schema.SchemaId));

        var cursors = schemas.Single(static schema => schema.IdentityAssignment.Name == "state/cursors");
        Assert.That(cursors.Fields[3].NestedRoleMask, Is.EqualTo(1UL << ((int)ContentSchemaRole.ControlPayload - 1)));
        Assert.That(cursors.Fields[5].NestedRoleMask, Is.EqualTo(1UL << ((int)ContentSchemaRole.EventPayload - 1)));

        var registration = schemas.Single(static schema => schema.IdentityAssignment.Name == "state/registration");
        Assert.That(registration.Fields[0].NestedSchemas.Single().SchemaId, Is.EqualTo(
            schemas.Single(static schema => schema.IdentityAssignment.Name == "state/component-registration").Schema.SchemaId));
        Assert.That(registration.Fields[2].NestedSchemas.Single().SchemaId, Is.EqualTo(
            schemas.Single(static schema => schema.IdentityAssignment.Name == "state/system-registration").Schema.SchemaId));

        var entity = schemas.Single(static schema => schema.IdentityAssignment.Name == "state/entity");
        Assert.That(entity.Fields[1].NestedRoleMask, Is.EqualTo(1UL << ((int)ContentSchemaRole.Component - 1)));
        Assert.That(entity.Fields[2].NestedRoleMask, Is.EqualTo(1UL << ((int)ContentSchemaRole.EntityTag - 1)));
        Assert.That(entity.Fields[5].Reference, Is.EqualTo(ContentReferenceKind.SceneInstance));

        var identityPage = schemas.Single(static schema => schema.IdentityAssignment.Name == "state/identity-page");
        Assert.That(identityPage.Fields[1].ElementKind, Is.EqualTo(11));
        Assert.That(identityPage.Fields[2].ElementKind, Is.EqualTo(3));

        var strictLayout = schemas.Single(static schema => schema.IdentityAssignment.Name == "state/strict-layout");
        Assert.That(strictLayout.Fields[0].NestedSchemas.Single().SchemaId, Is.EqualTo(
            schemas.Single(static schema => schema.IdentityAssignment.Name == "state/archetype").Schema.SchemaId));
        Assert.That(strictLayout.Fields[1].MaximumCount, Is.EqualTo(2_097_152));

        var sceneInstance = schemas.Single(static schema => schema.IdentityAssignment.Name == "state/scene-instance");
        Assert.That(sceneInstance.Fields[2].Reference, Is.EqualTo(ContentReferenceKind.Entity));
        Assert.That(sceneInstance.Fields[4].NestedSchemas.Single().SchemaId, Is.EqualTo(
            schemas.Single(static schema => schema.IdentityAssignment.Name == "state/external-scene-binding").Schema.SchemaId));

        var commandTarget = schemas.Single(static schema => schema.IdentityAssignment.Name == "state/command-target");
        Assert.That(commandTarget.Fields[1].Reference, Is.EqualTo(ContentReferenceKind.Entity));
        Assert.That(commandTarget.Fields[2].NestedSchemas.Single().SchemaId, Is.EqualTo(
            schemas.Single(static schema => schema.IdentityAssignment.Name == "state/command-key").Schema.SchemaId));

        var entityDescription = schemas.Single(static schema => schema.IdentityAssignment.Name == "state/entity-description");
        Assert.That(entityDescription.Fields[1].NestedRoleMask, Is.EqualTo(1UL << ((int)ContentSchemaRole.Component - 1)));
        Assert.That(entityDescription.Fields[2].NestedRoleMask, Is.EqualTo(1UL << ((int)ContentSchemaRole.EntityTag - 1)));

        var control = schemas.Single(static schema => schema.IdentityAssignment.Name == "state/control");
        Assert.That(control.Fields[2].NestedRoleMask, Is.EqualTo(1UL << ((int)ContentSchemaRole.ControlPayload - 1)));

        var controlFrame2 = schemas.Single(static schema => schema.IdentityAssignment.Name == "state/control-frame2");
        Assert.That(controlFrame2.Fields[0].NestedSchemas.Single().Scope, Is.EqualTo(ContentSchemaScope.Foundation));
        Assert.That(controlFrame2.Fields[0].NestedSchemas.Single().SchemaId, Is.EqualTo(
            CoreContentSchemaCatalogue.FoundationSchemas.Single(
                static schema => schema.IdentityAssignment.Name == "foundation/frame2").Schema.SchemaId));

        var controlFrame3 = schemas.Single(static schema => schema.IdentityAssignment.Name == "state/control-frame3");
        Assert.That(controlFrame3.Fields[0].NestedSchemas.Single().Scope, Is.EqualTo(ContentSchemaScope.Foundation));
        Assert.That(controlFrame3.Fields[0].NestedSchemas.Single().SchemaId, Is.EqualTo(
            CoreContentSchemaCatalogue.FoundationSchemas.Single(
                static schema => schema.IdentityAssignment.Name == "foundation/frame3").Schema.SchemaId));

        var controlScene = schemas.Single(static schema => schema.IdentityAssignment.Name == "state/control-scene");
        Assert.That(controlScene.Fields[1].Reference, Is.EqualTo(ContentReferenceKind.HistoricalSceneInstance));
        Assert.That(controlScene.Fields[2].NestedSchemas.Single().SchemaId, Is.EqualTo(
            new ContentSchemaIdentityAssignment("native/asset-reference").SchemaId));
        Assert.That(controlScene.Fields[3].NestedSchemas.Select(static schema => schema.SchemaId), Is.EquivalentTo(new[] {
            new ContentSchemaIdentityAssignment("scene/intent-reparent").SchemaId,
            new ContentSchemaIdentityAssignment("scene/intent-root-position").SchemaId,
        }));
        Assert.That(controlScene.Fields[5].NestedSchemas.Single().SchemaId, Is.EqualTo(
            new ContentSchemaIdentityAssignment("state/external-scene-binding").SchemaId));

        var sceneBinding = schemas.Single(static schema => schema.IdentityAssignment.Name == "state/external-scene-binding");
        Assert.That(sceneBinding.Fields[0].Reference, Is.EqualTo(ContentReferenceKind.ExternalSceneBinding));
        Assert.That(sceneBinding.Fields[1].Reference, Is.EqualTo(ContentReferenceKind.HistoricalEntity));
        Assert.That(sceneBinding.Fields[1].IsRequired, Is.False);

        var rootPositionIntent = schemas.Single(static schema => schema.IdentityAssignment.Name == "scene/intent-root-position");
        Assert.That(rootPositionIntent.Fields[0].Reference, Is.EqualTo(ContentReferenceKind.HistoricalEntity));
        Assert.That(rootPositionIntent.Fields[1].WireKind, Is.EqualTo(21));
        Assert.That(rootPositionIntent.Fields[1].Unit, Is.EqualTo(ContentUnit.EngineDistance));
    }
}
