// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;
using NUnit.Framework;

namespace TerraFX.Core.Tests;

[TestFixture]
internal sealed class ContentSchemaRegistryTests
{
    [Test]
    public void BinarySchemaDiagnosticRetainsItsPhysicalPathAndBounds()
    {
        var schemaId = new Guid("01234567-89ab-4cde-8f01-23456789abcd");
        ImmutableArray<BinarySchemaPathSegment> path = [
            BinarySchemaPathSegment.FieldTag(3),
            BinarySchemaPathSegment.ArrayIndex(1),
            BinarySchemaPathSegment.FieldTag(8),
        ];
        var diagnostic = new BinarySchemaDiagnostic(schemaId, 8, path, "invalid-value", 7, 5);

        Assert.That(diagnostic.SchemaId, Is.EqualTo(schemaId));
        Assert.That(diagnostic.Tag, Is.EqualTo(8));
        Assert.That(diagnostic.Path, Is.EqualTo(path));
        Assert.That(diagnostic.Category, Is.EqualTo("invalid-value"));
        Assert.That(diagnostic.Claim, Is.EqualTo(7));
        Assert.That(diagnostic.Limit, Is.EqualTo(5));
    }

    [Test]
    public void CompiledRegistryRejectsUnresolvedNestedSelections()
    {
        var unresolvedAssignment = new ContentSchemaIdentityAssignment("application/descriptor/unregistered/v1");
        var unresolvedReference = new ContentSchemaReference(
            unresolvedAssignment.SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var rootAssignment = new ContentSchemaIdentityAssignment("application/descriptor/root/v1");
        var rootReference = new ContentSchemaReference(rootAssignment.SchemaId, 1, 0, ContentSchemaScope.Content);
        var exactNestedField = new ContentFieldDescriptor(
            1,
            22,
            0,
            true,
            1,
            1,
            [unresolvedReference],
            0,
            ContentUnit.None,
            ContentReferenceKind.None,
            ContentAuthoring.None,
            null);
        var roleNestedField = new ContentFieldDescriptor(
            1,
            22,
            0,
            true,
            1,
            1,
            [],
            1UL << ((int)ContentSchemaRole.Component - 1),
            ContentUnit.None,
            ContentReferenceKind.None,
            ContentAuthoring.None,
            null);

        DataSchema CreateRoot(ContentFieldDescriptor field)
        {
            return new DataSchema(
                rootReference,
                rootAssignment,
                ContentSchemaRole.Descriptor,
                ContentSchemaOwner.Application,
                ContentSchemaStorage.NotComponent,
                ContentTransientRestore.NotApplicable,
                [field]);
        }

        Assert.That(() => new CompiledContentSchemaRegistry([CreateRoot(exactNestedField)]), Throws.ArgumentException);
        Assert.That(() => new CompiledContentSchemaRegistry([CreateRoot(roleNestedField)]), Throws.ArgumentException);
    }

    [Test]
    public void CompiledRegistryResolvesExactSchemaTuplesAndProfiles()
    {
        var assignment = new ContentSchemaIdentityAssignment("application/component/example/v1");
        var schemaReference = new ContentSchemaReference(assignment.SchemaId, 1, 0, ContentSchemaScope.Content);
        var field = new ContentFieldDescriptor(
            1,
            1,
            0,
            true,
            1,
            1,
            [],
            0,
            ContentUnit.None,
            ContentReferenceKind.None,
            ContentAuthoring.None,
            null);
        var schema = new DataSchema(
            schemaReference,
            assignment,
            ContentSchemaRole.Component,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.Persistent,
            ContentTransientRestore.NotApplicable,
            [field]);
        var profile = new ContentProfileDescriptor(
            new Guid("00112233-4455-4677-8899-aabbccddeeff"),
            [schemaReference]);
        var registry = new CompiledContentSchemaRegistry([schema], [profile]);

        Assert.That(registry.TryGetSchema(schemaReference, out var actualSchema), Is.True);
        Assert.That(ReferenceEquals(actualSchema, schema), Is.True);
        Assert.That(registry.TryGetSchema(new ContentSchemaReference(assignment.SchemaId, 1, 1, ContentSchemaScope.Content), out actualSchema), Is.False);
        Assert.That(actualSchema, Is.Null);
        Assert.That(registry.TryGetProfile(profile.ProfileId, out var actualProfile), Is.True);
        Assert.That(ReferenceEquals(actualProfile, profile), Is.True);
    }

    [Test]
    public void CorePrimitiveCatalogueMatchesTheFunctionalWireDeclarations()
    {
        var schemas = CoreContentSchemaCatalogue.PrimitiveSchemas;
        var registry = new CompiledContentSchemaRegistry(schemas);

        Assert.That(schemas, Has.Length.EqualTo(25));
        Assert.That(schemas.All(static schema =>
            schema.Role == ContentSchemaRole.Descriptor
            && schema.Owner == ContentSchemaOwner.Core
            && schema.Storage == ContentSchemaStorage.NotComponent
            && schema.TransientRestore == ContentTransientRestore.NotApplicable
            && schema.Schema.Major == 1
            && schema.Schema.Minor == 0
            && schema.Schema.Scope == ContentSchemaScope.Content), Is.True);
        Assert.That(schemas.Select(static schema => schema.IdentityAssignment.Name).Distinct().Count(), Is.EqualTo(schemas.Length));

        foreach (var schema in schemas)
        {
            Assert.That(registry.TryGetSchema(schema.Schema, out var registeredSchema), Is.True);
            Assert.That(ReferenceEquals(registeredSchema, schema), Is.True);
        }

        var literalSchemas = schemas
            .Where(static schema => schema.IdentityAssignment.Name.StartsWith("content/literal/", StringComparison.Ordinal))
            .ToDictionary(static schema => schema.IdentityAssignment.Name["content/literal/".Length..], StringComparer.Ordinal);
        Assert.That(literalSchemas, Has.Count.EqualTo(21));

        var expectedLiterals = new (string Suffix, ushort WireKind, ulong MinimumCount, ulong MaximumCount)[] {
            ("u8", 1, 1, 1),
            ("u16", 2, 1, 1),
            ("u32", 3, 1, 1),
            ("u64", 4, 1, 1),
            ("i8", 5, 1, 1),
            ("i16", 6, 1, 1),
            ("i32", 7, 1, 1),
            ("i64", 8, 1, 1),
            ("bool", 9, 1, 1),
            ("f32", 10, 1, 1),
            ("guid", 11, 1, 1),
            ("utf8", 12, 0, 1_048_576),
            ("bytes", 13, 0, 1_048_576),
            ("vector2", 14, 1, 1),
            ("vector3", 15, 1, 1),
            ("vector4", 16, 1, 1),
            ("quaternion", 17, 1, 1),
            ("matrix3x2", 18, 1, 1),
            ("matrix4x4", 19, 1, 1),
            ("position2", 20, 1, 1),
            ("position3", 21, 1, 1),
        };

        foreach (var (suffix, wireKind, minimumCount, maximumCount) in expectedLiterals)
        {
            var schema = literalSchemas[suffix];

            Assert.That(schema.Fields, Has.Length.EqualTo(1));
            Assert.That(schema.Fields[0].Tag, Is.EqualTo(1));
            Assert.That(schema.Fields[0].WireKind, Is.EqualTo(wireKind));
            Assert.That(schema.Fields[0].IsRequired, Is.True);
            Assert.That(schema.Fields[0].MinimumCount, Is.EqualTo(minimumCount));
            Assert.That(schema.Fields[0].MaximumCount, Is.EqualTo(maximumCount));
        }

        var schemaReference = schemas.Single(static schema => schema.IdentityAssignment.Name == "content/schema-ref");
        Assert.That(schemaReference.Fields.Select(static field => field.WireKind), Is.EqualTo(new ushort[] { 11, 3, 3, 1 }));
        Assert.That(schemaReference.Fields[0].Reference, Is.EqualTo(ContentReferenceKind.Schema));
        Assert.That(schemaReference.Fields.All(static field => field.IsRequired), Is.True);

        var defaultMeters = schemas.Single(static schema => schema.IdentityAssignment.Name == "content/default-meters");
        Assert.That(defaultMeters.Fields[0].WireKind, Is.EqualTo(23));
        Assert.That(defaultMeters.Fields[0].ElementKind, Is.EqualTo(8));
        Assert.That(defaultMeters.Fields[0].MinimumCount, Is.EqualTo(1));
        Assert.That(defaultMeters.Fields[0].MaximumCount, Is.EqualTo(3));
    }

    [Test]
    public void FixedArrayDescriptorsRequireAPermanentElementKind()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new ContentFieldDescriptor(
            1,
            23,
            0,
            true,
            1,
            3,
            [],
            0,
            ContentUnit.None,
            ContentReferenceKind.None,
            ContentAuthoring.None,
            null));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new ContentFieldDescriptor(
            1,
            23,
            12,
            true,
            1,
            3,
            [],
            0,
            ContentUnit.None,
            ContentReferenceKind.None,
            ContentAuthoring.None,
            null));
        _ = Assert.Throws<ArgumentException>(() => _ = new ContentFieldDescriptor(
            1,
            3,
            3,
            true,
            1,
            1,
            [],
            0,
            ContentUnit.None,
            ContentReferenceKind.None,
            ContentAuthoring.None,
            null));
    }

    [Test]
    public void FoundationSchemaMetadataMatchesTheFrozenFoundationScope()
    {
        var schemas = CoreContentSchemaCatalogue.FoundationSchemas;
        var registry = new CompiledContentSchemaRegistry([.. CoreContentSchemaCatalogue.PrimitiveSchemas, .. schemas]);

        Assert.That(schemas, Has.Length.EqualTo(8));

        foreach (var schema in schemas)
        {
            Assert.That(schema.Role, Is.EqualTo(ContentSchemaRole.Descriptor));
            Assert.That(schema.Owner, Is.EqualTo(ContentSchemaOwner.Core));
            Assert.That(schema.Storage, Is.EqualTo(ContentSchemaStorage.NotComponent));
            Assert.That(schema.Schema.Scope, Is.EqualTo(ContentSchemaScope.Foundation));
            Assert.That(registry.TryGetSchema(schema.Schema, out var registered), Is.True);
            Assert.That(ReferenceEquals(registered, schema), Is.True);
        }

        var profile = schemas.Single(static schema => schema.IdentityAssignment.Name == "foundation/profile");
        Assert.That(profile.Schema.Major, Is.EqualTo(2));
        Assert.That(profile.Schema.Minor, Is.Zero);
        Assert.That(profile.Fields.Any(static field => field.Tag == 4), Is.False);

        var frame2 = schemas.Single(static schema => schema.IdentityAssignment.Name == "foundation/frame2");
        Assert.That(frame2.Fields[1].NestedSchemas.Single(), Is.EqualTo(profile.Schema));
        Assert.That(frame2.Fields[1].WireKind, Is.EqualTo(22));
    }

    [Test]
    public void IdentityAssignmentUsesTheFrozenNameUuidRecipe()
    {
        var assignment = new ContentSchemaIdentityAssignment("foundation/profile");

        Assert.That(ContentSchemaIdentityAssignment.NamespaceId, Is.EqualTo(new Guid("2ed8b750-e1a0-4b46-9000-000000000001")));
        Assert.That(ContentSchemaIdentityAssignment.Recipe, Is.EqualTo("TerraFXNameUuidSha256V1"));
        Assert.That(assignment.SchemaId, Is.EqualTo(new Guid("3b9760ce-468a-8aab-bb89-f38db9e3bc00")));
    }

    [Test]
    public void LiteralDefaultsCopyCanonicalBytesAndRetainTheDeclaredWireKind()
    {
        byte[] literalPayload = [0x00, 0x00, 0x00, 0x80];
        var defaultValue = ContentFieldDefaultDescriptor.Literal(10, 1, literalPayload);
        literalPayload[3] = 0;
        var field = new ContentFieldDescriptor(
            1,
            10,
            0,
            true,
            1,
            1,
            [],
            0,
            ContentUnit.None,
            ContentReferenceKind.None,
            ContentAuthoring.None,
            defaultValue);

        Assert.That(defaultValue.WireKind, Is.EqualTo(10));
        Assert.That(defaultValue.Count, Is.EqualTo(1));
        Assert.That(defaultValue.LiteralPayload, Is.EqualTo(new byte[] { 0x00, 0x00, 0x00, 0x80 }));
        Assert.That(field.DefaultValue, Is.EqualTo(defaultValue));
    }

    [Test]
    public void RationalMeterDefaultsMatchTheDeclaredGeometryLanes()
    {
        var meters = ContentFieldDefaultDescriptor.Meters([10, 20], 100);
        var field = new ContentFieldDescriptor(
            1,
            14,
            0,
            true,
            1,
            1,
            [],
            0,
            ContentUnit.EngineDistance,
            ContentReferenceKind.None,
            ContentAuthoring.None,
            meters);

        Assert.That(field.DefaultValue?.MeterNumerators, Is.EqualTo(new long[] { 10, 20 }));
        Assert.That(field.DefaultValue?.MeterDenominator, Is.EqualTo(100));
        var wrongLanes = ContentFieldDefaultDescriptor.Meters([10, 20], 100);

        _ = Assert.Throws<ArgumentException>(() => _ = new ContentFieldDescriptor(
            1,
            15,
            0,
            true,
            1,
            1,
            [],
            0,
            ContentUnit.EngineDistance,
            ContentReferenceKind.None,
            ContentAuthoring.None,
            wrongLanes));
    }
}
