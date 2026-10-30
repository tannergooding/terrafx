// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Linq;
using NUnit.Framework;
using TerraFX;
using TerraFX.Physics;

namespace TerraFX.Physics.Tests;

[TestFixture]
internal sealed class PhysicsSchemaCatalogueTests
{
    private static DataSchema Find(string name)
        => PhysicsSchemaCatalogue.Schemas.Single(schema => StringComparer.Ordinal.Equals(schema.IdentityAssignment.Name, name));

    [Test]
    public void PrimitiveAndStatisticsSchemasMatchThePhysicsWireInventory()
    {
        var schemas = PhysicsSchemaCatalogue.Schemas;
        var expectedNames = new[] {
            "physics/primitive2",
            "physics/primitive3",
            "physics/circle",
            "physics/box2",
            "physics/sphere",
            "physics/box3",
            "physics/shape-measure",
            "physics/spatial-settings",
            "physics/statistics2",
            "physics/statistics3",
        };

        Assert.That(schemas.Select(static schema => schema.IdentityAssignment.Name), Is.EquivalentTo(expectedNames));
        Assert.That(schemas.All(static schema =>
            schema.Owner == ContentSchemaOwner.Physics
            && schema.Schema.Major == 1
            && schema.Schema.Minor == 0
            && schema.Schema.Scope == ContentSchemaScope.Content), Is.True);

        Assert.That(Find("physics/primitive2").Role, Is.EqualTo(ContentSchemaRole.NativeAsset));
        Assert.That(Find("physics/primitive3").Role, Is.EqualTo(ContentSchemaRole.NativeAsset));
        Assert.That(Find("physics/primitive2").Fields[0].NestedSchemas.Select(static schema => schema.SchemaId), Is.EquivalentTo(new[] {
            Find("physics/circle").Schema.SchemaId,
            Find("physics/box2").Schema.SchemaId,
        }));

        var statistics2 = Find("physics/statistics2");
        var statistics3 = Find("physics/statistics3");
        Assert.That(statistics2.Fields, Has.Length.EqualTo(12));
        Assert.That(statistics3.Fields, Has.Length.EqualTo(12));
        Assert.That(statistics2.Fields[3].Unit, Is.EqualTo(ContentUnit.EngineArea));
        Assert.That(statistics3.Fields[3].Unit, Is.EqualTo(ContentUnit.EngineVolume));
        Assert.That(statistics2.Fields[4].WireKind, Is.EqualTo(14));
        Assert.That(statistics3.Fields[4].WireKind, Is.EqualTo(15));
        Assert.That(statistics2.Fields[0].IsRequired, Is.False);
        Assert.That(statistics3.Fields[10].IsRequired, Is.False);

        var profile = Find("physics/primitive3").Fields[2].NestedSchemas.Single();
        Assert.That(profile, Is.EqualTo(
            CoreContentSchemaCatalogue.FoundationSchemas.Single(
                static schema => StringComparer.Ordinal.Equals(schema.IdentityAssignment.Name, "foundation/profile")).Schema));
    }
}
