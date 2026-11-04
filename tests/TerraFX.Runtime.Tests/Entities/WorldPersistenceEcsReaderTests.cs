// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldPersistenceEcsReaderTests
{
    private static byte[] CreateComponentRecord(
        ContentSchemaReference schema,
        Guid? targetEntityId,
        EbinReadLimits limits)
    {
        var payloadStorage = Array.Empty<byte>();
        EbinFieldSource[] fields = [];

        if (targetEntityId is { } target)
        {
            payloadStorage = new byte[16];
            Assert.That(
                EbinPayloadWriter.Write(target, payloadStorage, out var payloadBytes, out var payloadDiagnostic),
                Is.EqualTo(EbinWriteStatus.Complete),
                payloadDiagnostic?.ToString());
            fields = [
                new EbinFieldSource(
                    1,
                    11,
                    flags: 1,
                    count: 1,
                    payload: new EbinPayloadSlice(0, payloadBytes)),
            ];
        }

        var source = new EbinRecordWriteView(
            schema.SchemaId,
            schema.Major,
            schema.Minor,
            fields,
            payloadStorage);

        Assert.That(
            EbinStructuralWriter.Measure(in source, limits, out var recordBytes, out var measureDiagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            measureDiagnostic?.ToString());
        var candidate = new byte[recordBytes];
        var destination = new byte[recordBytes];
        Assert.That(
            EbinStructuralWriter.Write(
                in source,
                limits,
                candidate,
                destination,
                out var bytesWritten,
                out var writeDiagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            writeDiagnostic?.ToString());
        Assert.That(bytesWritten, Is.EqualTo(destination.Length));
        return destination;
    }

    [Test]
    public void RejectsComponentEntityReferencesOutsideTheLiveIdentitySet()
    {
        var assignment = new ContentSchemaIdentityAssignment("application/persistence-ecs-reader/entity-reference/v1");
        var schemaReference = new ContentSchemaReference(
            assignment.SchemaId,
            major: 1,
            minor: 0,
            scope: ContentSchemaScope.Content);
        var schema = new DataSchema(
            schemaReference,
            assignment,
            ContentSchemaRole.Component,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.Persistent,
            ContentTransientRestore.NotApplicable,
            [
                new ContentFieldDescriptor(
                    1,
                    wireKind: 11,
                    elementKind: 0,
                    isRequired: true,
                    minimumCount: 1,
                    maximumCount: 1,
                    nestedSchemas: [],
                    nestedRoleMask: 0,
                    unit: ContentUnit.None,
                    reference: ContentReferenceKind.Entity,
                    authoringFlags: ContentAuthoring.None,
                    defaultValue: null),
            ]);
        var schemas = new CompiledContentSchemaRegistry([
            .. CoreContentSchemaCatalogue.PrimitiveSchemas,
            .. CoreContentSchemaCatalogue.FoundationSchemas,
            schema,
        ]);
        var limits = EbinReadLimits.Foundation;
        var liveEntityId = Guid.NewGuid();
        var unknownEntityId = Guid.NewGuid();
        var liveIdentities = new WorldPersistenceLiveIdentitySet(maximumCount: 1);
        liveIdentities.Add(
            liveEntityId,
            identityKind: 1,
            assignmentOrdinal: 1,
            parameterName: nameof(liveEntityId));

        var liveRecord = CreateComponentRecord(schemaReference, liveEntityId, limits);
        Assert.DoesNotThrow(
            () => WorldPersistenceEcsReader.ValidateComponentRecord(
                liveRecord,
                schemas,
                liveIdentities,
                identityScratch: [],
                worldGuid: Guid.NewGuid(),
                limits: limits,
                parameterName: nameof(liveRecord)));

        var unknownRecord = CreateComponentRecord(schemaReference, unknownEntityId, limits);
        _ = Assert.Throws<ArgumentException>(
            () => WorldPersistenceEcsReader.ValidateComponentRecord(
                unknownRecord,
                schemas,
                liveIdentities,
                identityScratch: [],
                worldGuid: Guid.NewGuid(),
                limits: limits,
                parameterName: nameof(unknownRecord)));

        var missingFieldRecord = CreateComponentRecord(schemaReference, targetEntityId: null, limits: limits);
        _ = Assert.Throws<ArgumentException>(
            () => WorldPersistenceEcsReader.ValidateComponentRecord(
                missingFieldRecord,
                schemas,
                liveIdentities,
                identityScratch: [],
                worldGuid: Guid.NewGuid(),
                limits: limits,
                parameterName: nameof(missingFieldRecord)));
    }

    [Test]
    public void ValidatesPersistedComponentSchemaAndStorageAgainstTargetRegistration()
    {
        const string tagSchemaName = "application/persistence-ecs-reader/tag/v1";
        var tagSchemaAssignment = new ContentSchemaIdentityAssignment(tagSchemaName);
        var schemas = new CompiledContentSchemaRegistry([
            .. CoreContentSchemaCatalogue.PrimitiveSchemas,
            .. CoreContentSchemaCatalogue.FoundationSchemas,
            .. NativeAssetSchemaCatalogue.Schemas,
            .. SceneSchemaCatalogue.Schemas,
            WorldPersistenceWorldRecordWriterTests.CreateApplicationSchema(
                tagSchemaName,
                ContentSchemaRole.EntityTag,
                wireKind: 0),
        ]);
        var localTransformSchema = new ContentSchemaReference(
            new ContentSchemaIdentityAssignment("scene/local-transform").SchemaId,
            major: 1,
            minor: 0,
            scope: ContentSchemaScope.Content);
        var tagSchema = new ContentSchemaReference(
            tagSchemaAssignment.SchemaId,
            major: 1,
            minor: 0,
            scope: ContentSchemaScope.Content);
        var registrationWithoutScene = new WorldRegistrationBuilder(schemas).Build();

        _ = Assert.Throws<ArgumentException>(
            () => WorldPersistenceEcsReader.ValidateComponentSchema(
                localTransformSchema,
                ComponentStorageFlags.Persistent,
                registrationWithoutScene,
                schemas,
                nameof(localTransformSchema)));
        _ = Assert.Throws<ArgumentException>(
            () => WorldPersistenceEcsReader.ValidateTagSchema(
                tagSchema,
                registrationWithoutScene,
                schemas,
                nameof(tagSchema)));
        _ = Assert.Throws<ArgumentException>(
            () => WorldPersistenceEcsReader.ValidateComponentSchema(
                localTransformSchema,
                ComponentStorageFlags.Transient,
                targetRegistration: null,
                schemas: schemas,
                parameterName: nameof(localTransformSchema)));
        Assert.DoesNotThrow(
            () => WorldPersistenceEcsReader.ValidateComponentSchema(
                localTransformSchema,
                ComponentStorageFlags.Persistent,
                targetRegistration: null,
                schemas: schemas,
                parameterName: nameof(localTransformSchema)));

        var registrationBuilder = new WorldRegistrationBuilder(schemas);
        _ = registrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
        _ = registrationBuilder.RegisterTag(new EntityTagSchema(tagSchemaAssignment.SchemaId, 1));
        var registration = registrationBuilder.Build();

        WorldPersistenceEcsReader.ValidateComponentSchema(
            localTransformSchema,
            ComponentStorageFlags.Persistent,
            registration,
            schemas,
            nameof(localTransformSchema));
        WorldPersistenceEcsReader.ValidateTagSchema(
            tagSchema,
            registration,
            schemas,
            nameof(tagSchema));
    }
}
