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
