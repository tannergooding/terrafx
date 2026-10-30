// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Linq;
using NUnit.Framework;
using TerraFX;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

[TestFixture]
internal sealed class SceneSchemaCatalogueTests
{
    private static DataSchema Find(string name)
        => SceneSchemaCatalogue.Schemas.Single(schema => StringComparer.Ordinal.Equals(schema.IdentityAssignment.Name, name));

    [Test]
    public void AuthoredSceneAndPrefabSchemasMatchDeclarations()
    {
        var schemas = SceneSchemaCatalogue.Schemas;
        var expectedNames = new[] {
            "native/scene",
            "native/prefab",
            "native/entity",
            "native/hierarchy",
            "native/hierarchy-node",
            "native/hierarchy-entry",
            "native/prefab-placement",
            "native/prefab-root-pose",
            "native/entity-selector",
            "native/prefab-override",
            "native/entity-reference-binding",
            "native/reference-target",
            "native/external-slot",
            "native/placement-slot-binding",
            "scene/local-transform",
            "scene/root-position",
            "scene/camera",
            "scene/collision-reference",
        };

        Assert.That(schemas.Select(static schema => schema.IdentityAssignment.Name), Is.EquivalentTo(expectedNames));
        Assert.That(schemas.Select(static schema => schema.Schema).Distinct().Count(), Is.EqualTo(schemas.Length));

        foreach (var schema in schemas)
        {
            Assert.That(schema.Owner, Is.EqualTo(ContentSchemaOwner.Scenes));
            Assert.That(schema.Schema.Scope, Is.EqualTo(ContentSchemaScope.Content));
            Assert.That(schema.Schema.Minor, Is.Zero);
        }

        var scene = Find("native/scene");
        Assert.That(scene.Schema.Major, Is.EqualTo(2));
        Assert.That(scene.Role, Is.EqualTo(ContentSchemaRole.NativeAsset));
        Assert.That(scene.Fields.Select(static field => field.Tag), Is.EqualTo(new uint[] { 1, 2, 3, 4, 6, 7, 8 }));
        Assert.That(scene.Fields[1].NestedSchemas.Single().SchemaId, Is.EqualTo(Find("native/entity").Schema.SchemaId));
        Assert.That(scene.Fields[2].NestedSchemas.Single(), Is.EqualTo(Find("native/hierarchy").Schema));

        var prefab = Find("native/prefab");
        Assert.That(prefab.Schema.Major, Is.EqualTo(2));
        Assert.That(prefab.Fields.Select(static field => field.Tag), Is.EqualTo(new uint[] { 1, 3, 4, 5, 6, 8 }));

        var entity = Find("native/entity");
        Assert.That(entity.Fields[0].Reference, Is.EqualTo(ContentReferenceKind.Entity));
        Assert.That(entity.Fields[1].NestedRoleMask, Is.EqualTo(0x0000000CUL));

        var overrideSchema = Find("native/prefab-override");
        Assert.That(overrideSchema.Fields[3].IsRequired, Is.False);
        Assert.That(overrideSchema.Fields[3].NestedSchemas, Has.Length.EqualTo(19));
        Assert.That(overrideSchema.Fields[4].DefaultValue?.LiteralPayload, Is.EqualTo(new byte[] { 0 }));
    }

    [Test]
    public void BuiltInSceneComponentsRetainPersistentDefaultsAndSemanticAnnotations()
    {
        var localTransform = Find("scene/local-transform");
        var rootPosition = Find("scene/root-position");
        var camera = Find("scene/camera");
        var collisionReference = Find("scene/collision-reference");

        foreach (var schema in new[] { localTransform, rootPosition, camera, collisionReference })
        {
            Assert.That(schema.Role, Is.EqualTo(ContentSchemaRole.Component));
            Assert.That(schema.Storage, Is.EqualTo(ContentSchemaStorage.Persistent));
            Assert.That(schema.TransientRestore, Is.EqualTo(ContentTransientRestore.NotApplicable));
        }

        Assert.That(localTransform.Fields[0].Unit, Is.EqualTo(ContentUnit.EngineDistance));
        Assert.That(localTransform.Fields[0].AuthoringFlags, Is.EqualTo(ContentAuthoring.PrefabEditable | ContentAuthoring.Animatable));
        Assert.That(localTransform.Fields[1].DefaultValue?.LiteralPayload, Is.EqualTo(new byte[] {
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 128, 63,
        }));
        Assert.That(rootPosition.Fields[0].DefaultValue?.LiteralPayload, Has.Length.EqualTo(24));

        Assert.That(camera.Fields[2].DefaultValue?.Kind, Is.EqualTo(ContentFieldDefaultKind.Meters));
        Assert.That(camera.Fields[2].DefaultValue?.MeterNumerators, Is.EqualTo(new long[] { 10 }));
        Assert.That(camera.Fields[3].DefaultValue?.MeterNumerators, Is.EqualTo(new long[] { 1 }));
        Assert.That(camera.Fields[5].IsRequired, Is.False);
        Assert.That(camera.Fields[6].IsRequired, Is.False);
        Assert.That(camera.Fields[5].DefaultValue?.MeterNumerators, Is.EqualTo(new long[] { 1000 }));

        Assert.That(collisionReference.Fields[0].Reference, Is.EqualTo(ContentReferenceKind.Asset));
        Assert.That(collisionReference.Fields[0].AuthoringFlags, Is.EqualTo(ContentAuthoring.PrefabEditable));
    }
}
