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
        => AnimationSchemaCatalogue.Schemas.Single(schema => StringComparer.Ordinal.Equals(schema.IdentityAssignment.Name, name));

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

        var joint = Find("native/joint");
        Assert.That(joint.Fields[2].NestedSchemas.Single().SchemaId, Is.EqualTo(
            new ContentSchemaIdentityAssignment("scene/local-transform").SchemaId));
        Assert.That(joint.Fields[3].WireKind, Is.EqualTo(19));
    }
}
