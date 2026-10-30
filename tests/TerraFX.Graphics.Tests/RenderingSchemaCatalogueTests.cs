// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Linq;
using NUnit.Framework;
using TerraFX;
using TerraFX.Graphics;

namespace TerraFX.Graphics.Tests;

[TestFixture]
internal sealed class RenderingSchemaCatalogueTests
{
    private static DataSchema Find(string name)
        => RenderingSchemaCatalogue.Schemas.Single(schema => StringComparer.Ordinal.Equals(schema.IdentityAssignment.Name, name));

    [Test]
    public void BuiltInRenderingComponentsArePersistentAndAuthoredAsDeclared()
    {
        var sprite = Find("render/sprite");
        var mesh = Find("render/mesh");
        var directionalLight = Find("render/directional-light");
        var pointLight = Find("render/point-light");
        var spotLight = Find("render/spot-light");
        var components = new[] { sprite, mesh, directionalLight, pointLight, spotLight };

        foreach (var component in components)
        {
            Assert.That(component.Role, Is.EqualTo(ContentSchemaRole.Component));
            Assert.That(component.Storage, Is.EqualTo(ContentSchemaStorage.Persistent));
            Assert.That(component.TransientRestore, Is.EqualTo(ContentTransientRestore.NotApplicable));
            Assert.That(component.Owner, Is.EqualTo(ContentSchemaOwner.Rendering));
        }

        Assert.That(sprite.Fields[0].Reference, Is.EqualTo(ContentReferenceKind.Asset));
        Assert.That(sprite.Fields[0].AuthoringFlags, Is.EqualTo(ContentAuthoring.PrefabEditable | ContentAuthoring.Animatable));
        Assert.That(sprite.Fields[3].DefaultValue?.Kind, Is.EqualTo(ContentFieldDefaultKind.Meters));
        Assert.That(mesh.Fields[1].DefaultValue?.LiteralPayload, Is.EqualTo(new byte[] {
            255, 255, 255, 255, 255, 255, 255, 255,
        }));
        Assert.That(directionalLight.Fields[2].DefaultValue?.LiteralPayload, Is.EqualTo(new byte[] {
            0, 0, 0, 0, 0, 0, 128, 191, 0, 0, 0, 0,
        }));
        Assert.That(pointLight.Fields[2].DefaultValue?.MeterNumerators, Is.EqualTo(new long[] { 10 }));
        Assert.That(spotLight.Fields[4].DefaultValue?.LiteralPayload, Is.EqualTo(new byte[] { 146, 10, 6, 63 }));
    }

    [Test]
    public void NativeRenderingSchemasMatchTheCompiledWireShapes()
    {
        var schemas = RenderingSchemaCatalogue.Schemas;
        var expectedNames = new[] {
            "native/mesh",
            "native/bounds3",
            "native/mesh-position",
            "native/mesh-normal",
            "native/mesh-tangent",
            "native/mesh-uv",
            "native/mesh-color",
            "native/mesh-joints",
            "native/mesh-weights",
            "native/mesh-indices16",
            "native/mesh-indices32",
            "native/mesh-group",
            "native/material",
            "native/material-sprite-parameters",
            "native/material-pbr-parameters",
            "native/texture-binding",
            "native/sampler",
            "native/texture",
            "native/texture-mip",
            "native/atlas",
            "native/atlas-frame",
            "render/sprite",
            "render/mesh",
            "render/directional-light",
            "render/point-light",
            "render/spot-light",
        };

        Assert.That(schemas.Select(static schema => schema.IdentityAssignment.Name), Is.EquivalentTo(expectedNames));
        Assert.That(schemas.All(static schema =>
            schema.Owner == ContentSchemaOwner.Rendering
            && schema.Schema.Major == 1
            && schema.Schema.Minor == 0
            && schema.Schema.Scope == ContentSchemaScope.Content), Is.True);

        var mesh = Find("native/mesh");
        Assert.That(mesh.Role, Is.EqualTo(ContentSchemaRole.NativeAsset));
        Assert.That(mesh.Fields[1].MinimumCount, Is.EqualTo(1));
        Assert.That(mesh.Fields[1].MaximumCount, Is.EqualTo(8));
        Assert.That(mesh.Fields[1].NestedSchemas, Has.Length.EqualTo(7));
        Assert.That(mesh.Fields[2].NestedSchemas, Has.Length.EqualTo(2));

        var material = Find("native/material");
        Assert.That(material.Role, Is.EqualTo(ContentSchemaRole.NativeAsset));
        Assert.That(material.Fields[5].DefaultValue?.MaterialProperty, Is.EqualTo(ContentMaterialDefaultProperty.AlphaMode));
        Assert.That(material.Fields[8].DefaultValue?.MaterialProperty, Is.EqualTo(ContentMaterialDefaultProperty.DepthTest));
        Assert.That(material.Fields[12].DefaultValue?.MaterialProperty, Is.EqualTo(ContentMaterialDefaultProperty.Capabilities));

        Assert.That(Find("native/atlas-frame").Fields[6].DefaultValue, Is.Null);
        var textureMip = Find("native/texture-mip");
        Assert.That(textureMip.Fields[3].WireKind, Is.EqualTo(13));
        Assert.That(textureMip.Fields[3].MinimumCount, Is.EqualTo(4));
        Assert.That(textureMip.Fields[3].MaximumCount, Is.EqualTo(67_108_864));

        var indices16 = Find("native/mesh-indices16");
        Assert.That(indices16.Fields[0].ElementKind, Is.EqualTo(2));
        var indices32 = Find("native/mesh-indices32");
        Assert.That(indices32.Fields[0].ElementKind, Is.EqualTo(3));
    }
}
