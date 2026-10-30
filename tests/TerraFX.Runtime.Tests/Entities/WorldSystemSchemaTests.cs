// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldSystemSchemaTests
{
    private static ContentFieldDescriptor CreateField(
        uint tag,
        ushort wireKind,
        ushort elementKind,
        ImmutableArray<ContentSchemaReference> nestedSchemas,
        ulong minimumCount = 1,
        ulong maximumCount = 1)
        => new(
            tag,
            wireKind,
            elementKind,
            true,
            minimumCount,
            maximumCount,
            nestedSchemas,
            0,
            ContentUnit.None,
            ContentReferenceKind.None,
            ContentAuthoring.None,
            null);

    [Test]
    public void FromRegistryRejectsNonSystemStateSchema()
    {
        var assignment = new ContentSchemaIdentityAssignment("application/descriptor/not-system-state/v1");
        var schemaReference = new ContentSchemaReference(
            assignment.SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var metadata = new DataSchema(
            schemaReference,
            assignment,
            ContentSchemaRole.Descriptor,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            []);
        var registry = new CompiledContentSchemaRegistry([metadata]);

        Assert.That(
            () => WorldSystemSchema<ProbeSystem>.FromRegistry(registry, schemaReference),
            Throws.ArgumentException);
    }

    [Test]
    public void FromRegistryResolvesNestedAndFixedArrayPaths()
    {
        var nestedAssignment = new ContentSchemaIdentityAssignment("application/system-state/nested/v1");
        var nestedReference = new ContentSchemaReference(
            nestedAssignment.SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var nestedField = CreateField(7, 3, 0, []);
        var nestedSchema = new DataSchema(
            nestedReference,
            nestedAssignment,
            ContentSchemaRole.Descriptor,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            [nestedField]);
        var stateAssignment = new ContentSchemaIdentityAssignment("application/system-state/test/v1");
        var stateReference = new ContentSchemaReference(
            stateAssignment.SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var recordField = CreateField(1, 22, 0, [nestedReference]);
        var arrayField = CreateField(2, 23, 3, [], minimumCount: 1, maximumCount: 4);
        var stateMetadata = new DataSchema(
            stateReference,
            stateAssignment,
            ContentSchemaRole.SystemState,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            [recordField, arrayField]);
        var registry = new CompiledContentSchemaRegistry([nestedSchema, stateMetadata]);
        var schema = WorldSystemSchema<ProbeSystem>.FromRegistry(registry, stateReference);
        var nested = schema.GetField(new ComponentFieldPath([
            ComponentPropertySegment.FieldTag(1),
            ComponentPropertySegment.FieldTag(7),
        ]));
        var arrayElement = schema.GetField(new ComponentFieldPath([
            ComponentPropertySegment.FieldTag(2),
            ComponentPropertySegment.FixedArrayIndex(3),
        ]));

        Assert.That(schema.Fields, Has.Length.EqualTo(2));
        Assert.That(nested.StateSchema, Is.EqualTo(stateReference));
        Assert.That(nested.Field, Is.SameAs(nestedField));
        Assert.That(nested.Path.Segments, Has.Length.EqualTo(2));
        Assert.That(arrayElement.Field, Is.SameAs(arrayField));
        Assert.That(arrayElement.Path.Segments[1].Value, Is.EqualTo(3));
        Assert.That(
            () => schema.GetField(new ComponentFieldPath([ComponentPropertySegment.FieldTag(3)])),
            Throws.ArgumentException);
    }

    private sealed class ProbeSystem : IWorldSystem
    {
        public void Execute(WorldSystemContext context)
        {
        }
    }
}
