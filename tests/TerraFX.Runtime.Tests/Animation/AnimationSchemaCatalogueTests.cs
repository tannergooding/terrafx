// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Linq;
using NUnit.Framework;
using TerraFX;
using TerraFX.Animation;

namespace TerraFX.Runtime.Tests.Animation;

[TestFixture]
internal sealed class AnimationSchemaCatalogueTests
{
    private static DataSchema Find(string name)
        => AnimationSchemaCatalogue.Schemas.Concat(AnimationSchemaCatalogue.PlaybackSchemas)
            .Single(schema => StringComparer.Ordinal.Equals(schema.IdentityAssignment.Name, name));

    [Test]
    public void AnimationPayloadSchemasMatchTheirNestedTrackAndEventShapes()
    {
        var schemas = AnimationSchemaCatalogue.Schemas;
        var expectedNames = new[] {
            "native/skeleton",
            "native/joint",
            "native/clip",
            "native/transform-track",
            "native/sprite-track",
            "native/key-vector3",
            "native/key-quaternion",
            "native/key-sprite",
            "native/clip-event",
            "native/event-marker",
            "native/root-motion",
        };

        Assert.That(schemas.Select(static schema => schema.IdentityAssignment.Name), Is.EquivalentTo(expectedNames));
        Assert.That(schemas.All(static schema =>
            schema.Owner == ContentSchemaOwner.Animation
            && schema.Schema.Major == 1
            && schema.Schema.Minor == 0
            && schema.Schema.Scope == ContentSchemaScope.Content), Is.True);

        Assert.That(Find("native/skeleton").Role, Is.EqualTo(ContentSchemaRole.NativeAsset));
        Assert.That(Find("native/clip").Role, Is.EqualTo(ContentSchemaRole.NativeAsset));

        var clip = Find("native/clip");
        Assert.That(clip.Fields[0].Unit, Is.EqualTo(ContentUnit.TimeSpanTicks));
        Assert.That(clip.Fields[1].NestedSchemas.Select(static schema => schema.SchemaId), Is.EquivalentTo(new[] {
            Find("native/transform-track").Schema.SchemaId,
            Find("native/sprite-track").Schema.SchemaId,
        }));
        Assert.That(clip.Fields[3].DefaultValue?.LiteralPayload, Is.EqualTo(new byte[] { 0 }));
        Assert.That(clip.Fields[7].DefaultValue, Is.Null);
        Assert.That(clip.Fields[4].IsRequired, Is.False);

        var clipEvent = Find("native/clip-event");
        Assert.That(clipEvent.Fields[0].Reference, Is.EqualTo(ContentReferenceKind.Event));
        Assert.That(clipEvent.Fields[2].NestedRoleMask, Is.EqualTo(1UL << ((int)ContentSchemaRole.EventPayload - 1)));
        Assert.That(Find("native/event-marker").Role, Is.EqualTo(ContentSchemaRole.EventPayload));

        var worldState = Find("animation/world-state");
        Assert.That(worldState.Role, Is.EqualTo(ContentSchemaRole.AttachmentState));
        Assert.That(worldState.Fields[0].WireKind, Is.EqualTo(8));
        Assert.That(worldState.Fields[1].NestedSchemas.Single().SchemaId, Is.EqualTo(
            Find("animation/player-state").Schema.SchemaId));
        Assert.That(worldState.Fields[1].MaximumCount, Is.EqualTo(4096));

        var playerState = Find("animation/player-state");
        Assert.That(playerState.Fields[0].Reference, Is.EqualTo(ContentReferenceKind.Entity));
        Assert.That(playerState.Fields[1].Reference, Is.EqualTo(ContentReferenceKind.SceneInstance));
        Assert.That(playerState.Fields[2].Reference, Is.EqualTo(ContentReferenceKind.Asset));
        Assert.That(playerState.Fields[6].MaximumCount, Is.EqualTo(4352));
        Assert.That(playerState.Fields[7].MinimumCount, Is.EqualTo(1));
        Assert.That(playerState.Fields[8].Reference, Is.EqualTo(ContentReferenceKind.Entity));

        var targetBinding = Find("animation/target-binding");
        Assert.That(targetBinding.Fields[3].NestedSchemas.Single().SchemaId, Is.EqualTo(
            new ContentSchemaIdentityAssignment("scene/local-transform").SchemaId));
        Assert.That(targetBinding.Fields[4].WireKind, Is.EqualTo(21));
        Assert.That(targetBinding.Fields[4].Unit, Is.EqualTo(ContentUnit.EngineDistance));

        var activeClip = Find("animation/active-clip");
        Assert.That(activeClip.Fields[4].WireKind, Is.EqualTo(13));
        Assert.That(activeClip.Fields[4].MinimumCount, Is.EqualTo(24));
        Assert.That(activeClip.Fields[4].MaximumCount, Is.EqualTo(24));

        var transitionState = Find("animation/transition-state");
        Assert.That(transitionState.Fields[3].WireKind, Is.EqualTo(23));
        Assert.That(transitionState.Fields[3].ElementKind, Is.EqualTo(10));

        var playerComponent = Find("animation/player");
        Assert.That(playerComponent.Role, Is.EqualTo(ContentSchemaRole.Component));
        Assert.That(playerComponent.Storage, Is.EqualTo(ContentSchemaStorage.Persistent));
        Assert.That(playerComponent.Fields, Has.Length.EqualTo(1));
        Assert.That(playerComponent.Fields[0].Reference, Is.EqualTo(ContentReferenceKind.Asset));
        Assert.That(playerComponent.Fields[0].AuthoringFlags, Is.EqualTo(ContentAuthoring.PrefabEditable));
        Assert.That(playerComponent.Fields[0].NestedSchemas.Single().SchemaId, Is.EqualTo(
            new ContentSchemaIdentityAssignment("native/asset-reference").SchemaId));

        var joint = Find("native/joint");
        Assert.That(joint.Fields[2].NestedSchemas.Single().SchemaId, Is.EqualTo(
            new ContentSchemaIdentityAssignment("scene/local-transform").SchemaId));
        Assert.That(joint.Fields[3].WireKind, Is.EqualTo(19));

        var controller = Find("native/animation-controller");
        Assert.That(controller.Role, Is.EqualTo(ContentSchemaRole.NativeAsset));
        Assert.That(controller.Fields, Has.Length.EqualTo(7));
        Assert.That(controller.Fields[0].IsRequired, Is.False);
        Assert.That(controller.Fields[1].IsRequired, Is.False);
        Assert.That(controller.Fields[2].MinimumCount, Is.EqualTo(1));
        Assert.That(controller.Fields[2].MaximumCount, Is.EqualTo(32));
        Assert.That(controller.Fields[3].MinimumCount, Is.EqualTo(1));
        Assert.That(controller.Fields[3].MaximumCount, Is.EqualTo(8));
        Assert.That(controller.Fields[4].MinimumCount, Is.Zero);
        Assert.That(controller.Fields[4].MaximumCount, Is.EqualTo(64));
        Assert.That(controller.Fields[6].Reference, Is.EqualTo(ContentReferenceKind.Entity));

        var layer = Find("native/animation-layer");
        Assert.That(layer.Fields[3].WireKind, Is.EqualTo(23));
        Assert.That(layer.Fields[3].ElementKind, Is.EqualTo(10));
        Assert.That(layer.Fields[3].MaximumCount, Is.EqualTo(4352));
        Assert.That(layer.Fields[4].NestedSchemas.Single().SchemaId, Is.EqualTo(
            Find("native/animation-reference-pose").Schema.SchemaId));
        Assert.That(layer.Fields[5].NestedSchemas.Single().SchemaId, Is.EqualTo(
            Find("native/animation-clip-weight").Schema.SchemaId));

        var transition = Find("native/animation-transition");
        Assert.That(transition.Fields, Has.Length.EqualTo(10));
        Assert.That(transition.Fields[2].IsRequired, Is.False);
        Assert.That(transition.Fields[6].NestedSchemas.Select(static schema => schema.SchemaId), Is.EquivalentTo(new[] {
            Find("native/animation-condition-manual").Schema.SchemaId,
            Find("native/animation-condition-completed").Schema.SchemaId,
            Find("native/animation-condition-always").Schema.SchemaId,
        }));
        Assert.That(transition.Fields[8].NestedSchemas.Select(static schema => schema.SchemaId), Is.EquivalentTo(new[] {
            Find("native/animation-action-none").Schema.SchemaId,
            Find("native/animation-action-layer-weight").Schema.SchemaId,
        }));
    }

    [Test]
    public void PlaybackControlAndEventRowsHaveExactConditionalFields()
    {
        var control = Find("animation/control");
        Assert.That(control.Role, Is.EqualTo(ContentSchemaRole.ControlPayload));
        Assert.That(control.Fields, Has.Length.EqualTo(10));
        Assert.That(control.Fields.Take(3).All(static field => field.IsRequired), Is.True);
        Assert.That(control.Fields.Skip(3).All(static field => !field.IsRequired && field.DefaultValue is null), Is.True);
        Assert.That(control.Fields[1].Reference, Is.EqualTo(ContentReferenceKind.HistoricalEntity));
        Assert.That(control.Fields[3].Reference, Is.EqualTo(ContentReferenceKind.Asset));
        Assert.That(control.Fields[3].NestedSchemas.Single().SchemaId, Is.EqualTo(
            new ContentSchemaIdentityAssignment("native/asset-reference").SchemaId));
        Assert.That(control.Fields[5].Unit, Is.EqualTo(ContentUnit.TimeSpanTicks));

        var marker = Find("animation/event-marker");
        Assert.That(marker.Role, Is.EqualTo(ContentSchemaRole.EventPayload));
        Assert.That(marker.Fields, Has.Length.EqualTo(12));
        Assert.That(marker.Fields[0].Reference, Is.EqualTo(ContentReferenceKind.HistoricalEntity));
        Assert.That(marker.Fields[3].Reference, Is.EqualTo(ContentReferenceKind.Asset));
        Assert.That(marker.Fields[9].Unit, Is.EqualTo(ContentUnit.TimeSpanTicks));
        Assert.That(marker.Fields[10].Reference, Is.EqualTo(ContentReferenceKind.Event));
        Assert.That(marker.Fields[11].NestedSchemas.Single().SchemaId, Is.EqualTo(
            new ContentSchemaIdentityAssignment("native/event-marker").SchemaId));

        var completed = Find("animation/event-completed");
        Assert.That(completed.Role, Is.EqualTo(ContentSchemaRole.EventPayload));
        Assert.That(completed.Fields, Has.Length.EqualTo(10));
        Assert.That(completed.Fields[0].Reference, Is.EqualTo(ContentReferenceKind.HistoricalEntity));
        Assert.That(completed.Fields[3].Reference, Is.EqualTo(ContentReferenceKind.Asset));
        Assert.That(completed.Fields[9].Unit, Is.EqualTo(ContentUnit.TimeSpanTicks));
    }

    [Test]
    public void PlaybackSchemasSeparateSelectedControllerStateAndRootResourceRows()
    {
        var schemas = AnimationSchemaCatalogue.PlaybackSchemas;
        var expectedNames = new[] {
            "native/animation-controller", "native/animation-clip-slot", "native/animation-layer",
            "native/animation-reference-pose", "native/animation-clip-weight", "native/animation-transition",
            "native/animation-condition-manual", "native/animation-condition-completed", "native/animation-condition-always",
            "native/animation-action-none", "native/animation-action-layer-weight",
            "animation/world-state", "animation/player-state", "animation/target-binding",
            "animation/layer-state", "animation/active-clip", "animation/transition-state", "animation/player",
            "animation/root-motion-resource", "animation/root-motion-slot", "animation/root-motion-source",
            "animation/control", "animation/event-marker", "animation/event-completed",
        };
        Assert.That(AnimationSchemaCatalogue.Schemas, Has.Length.EqualTo(11));
        Assert.That(schemas.Select(static schema => schema.IdentityAssignment.Name), Is.EquivalentTo(expectedNames));
        Assert.That(schemas.Select(static schema => schema.Schema).Intersect(AnimationSchemaCatalogue.Schemas.Select(static schema => schema.Schema)), Is.Empty);
        Assert.That(schemas.All(static schema => schema.Owner == ContentSchemaOwner.Animation
            && schema.Schema.Major == 1 && schema.Schema.Minor == 0 && schema.Schema.Scope == ContentSchemaScope.Content), Is.True);
        var resource = Find("animation/root-motion-resource");
        var slot = Find("animation/root-motion-slot");
        var source = Find("animation/root-motion-source");
        Assert.That(resource.Role, Is.EqualTo(ContentSchemaRole.ResourceState));
        Assert.That(resource.Fields, Has.Length.EqualTo(1));
        Assert.That(resource.Fields[0].WireKind, Is.EqualTo(24));
        Assert.That(resource.Fields[0].MinimumCount, Is.EqualTo(4096));
        Assert.That(resource.Fields[0].MaximumCount, Is.EqualTo(4096));
        Assert.That(resource.Fields[0].NestedSchemas.Single(), Is.EqualTo(slot.Schema));
        Assert.That(slot.Fields.Select(static field => field.Tag), Is.EqualTo(Enumerable.Range(1, 13).Select(static tag => (uint)tag)));
        Assert.That(slot.Fields[0].IsRequired, Is.True);
        Assert.That(slot.Fields.Skip(1).All(static field => !field.IsRequired && field.DefaultValue is null), Is.True);
        Assert.That(slot.Fields[1].Reference, Is.EqualTo(ContentReferenceKind.HistoricalEntity));
        Assert.That(slot.Fields[4].Reference, Is.EqualTo(ContentReferenceKind.HistoricalEntity));
        Assert.That(slot.Fields[10].MaximumCount, Is.EqualTo(4));
        Assert.That(slot.Fields[10].NestedSchemas.Single(), Is.EqualTo(source.Schema));
        Assert.That(slot.Fields[12].Reference, Is.EqualTo(ContentReferenceKind.Asset));
        Assert.That(source.Fields.Select(static field => field.Tag), Is.EqualTo(new uint[] { 1, 2, 3, 4, 5 }));
        Assert.That(source.Fields.All(static field => field.IsRequired), Is.True);
    }
}
