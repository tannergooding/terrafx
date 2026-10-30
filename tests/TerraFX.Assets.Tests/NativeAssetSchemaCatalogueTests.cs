// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Linq;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;

namespace TerraFX.Assets.Tests;

[TestFixture]
internal sealed class NativeAssetSchemaCatalogueTests
{
    [Test]
    public void CatalogueMatchesNativeEnvelopeAndMetadataDeclarations()
    {
        var schemas = NativeAssetSchemaCatalogue.Schemas;
        var expectedNames = new[] {
            "native/envelope",
            "native/asset-reference",
            "native/metadata",
            "native/metadata-entry",
        };

        Assert.That(schemas.Select(static schema => schema.IdentityAssignment.Name), Is.EquivalentTo(expectedNames));
        Assert.That(schemas.Select(static schema => schema.Schema).Distinct().Count(), Is.EqualTo(schemas.Length));

        foreach (var schema in schemas)
        {
            Assert.That(schema.Owner, Is.EqualTo(ContentSchemaOwner.Assets));
            Assert.That(schema.Role, Is.EqualTo(ContentSchemaRole.Descriptor));
            Assert.That(schema.Storage, Is.EqualTo(ContentSchemaStorage.NotComponent));
            Assert.That(schema.TransientRestore, Is.EqualTo(ContentTransientRestore.NotApplicable));
            Assert.That(schema.Schema.Scope, Is.EqualTo(ContentSchemaScope.Content));
            Assert.That(schema.Schema.Minor, Is.Zero);
        }

        var envelope = schemas.Single(static schema => schema.IdentityAssignment.Name == "native/envelope");
        Assert.That(envelope.Schema.Major, Is.EqualTo(2));
        Assert.That(envelope.Fields.Select(static field => field.Tag), Is.EqualTo(new uint[] { 1, 2 }));
        Assert.That(envelope.Fields[0].WireKind, Is.EqualTo(3));
        Assert.That(envelope.Fields[0].MinimumCount, Is.EqualTo(1));
        Assert.That(envelope.Fields[0].MaximumCount, Is.EqualTo(1));
        Assert.That(envelope.Fields[1].NestedRoleMask, Is.EqualTo(1UL << ((int)ContentSchemaRole.NativeAsset - 1)));

        var assetReference = schemas.Single(static schema => schema.IdentityAssignment.Name == "native/asset-reference");
        Assert.That(assetReference.Schema.Major, Is.EqualTo(1));
        Assert.That(assetReference.Fields.Select(static field => field.WireKind), Is.EqualTo(new ushort[] { 3, 11, 3 }));
        Assert.That(assetReference.Fields[1].Reference, Is.EqualTo(ContentReferenceKind.Schema));

        var metadata = schemas.Single(static schema => schema.IdentityAssignment.Name == "native/metadata");
        Assert.That(metadata.Fields[0].MinimumCount, Is.Zero);
        Assert.That(metadata.Fields[0].MaximumCount, Is.EqualTo(256));
        Assert.That(metadata.Fields[0].NestedSchemas.Single().SchemaId, Is.EqualTo(
            schemas.Single(static schema => schema.IdentityAssignment.Name == "native/metadata-entry").Schema.SchemaId));

        var metadataEntry = schemas.Single(static schema => schema.IdentityAssignment.Name == "native/metadata-entry");
        Assert.That(metadataEntry.Fields.Select(static field => field.WireKind), Is.EqualTo(new ushort[] { 12, 12 }));
        Assert.That(metadataEntry.Fields[0].MinimumCount, Is.EqualTo(1));
        Assert.That(metadataEntry.Fields[0].MaximumCount, Is.EqualTo(128));
        Assert.That(metadataEntry.Fields[1].MinimumCount, Is.Zero);
        Assert.That(metadataEntry.Fields[1].MaximumCount, Is.EqualTo(4096));
    }
}
