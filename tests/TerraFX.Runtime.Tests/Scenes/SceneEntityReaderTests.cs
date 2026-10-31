// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

[TestFixture]
internal sealed class SceneEntityReaderTests
{
    [Test]
    public void ReadsRegisteredComponentRecordsAndRequiresLocalTransform()
    {
        var schemas = new CompiledContentSchemaRegistry([
            .. CoreContentSchemaCatalogue.PrimitiveSchemas,
            .. CoreContentSchemaCatalogue.FoundationSchemas,
            .. NativeAssetSchemaCatalogue.Schemas,
            .. SceneSchemaCatalogue.Schemas,
        ]);
        var authoredEntityId = new Guid("10000000-0000-4000-8000-000000000001");
        var localTransformSchema = new ContentSchemaIdentityAssignment("scene/local-transform").SchemaId;
        var localTransformRecord = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            localTransformSchema,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(1, 15, 0, 0, 0),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(2, 17, 0, 0, 0, 1),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(3, 15, 1, 1, 1));
        var entitySchemaId = new ContentSchemaIdentityAssignment("native/entity").SchemaId;
        var encodedEntity = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            entitySchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(1, authoredEntityId),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(2, localTransformRecord));
        var action = new ReadAction(schemas);
        var status = EbinStructuralReader.Read(
            encodedEntity,
            EbinReadLimits.Foundation,
            EbinReadPolicy.Canonical,
            ref action,
            out var bytesConsumed,
            out var diagnostic);

        Assert.That(status, Is.EqualTo(EbinReadStatus.Complete), diagnostic?.Category);
        Assert.That(bytesConsumed, Is.EqualTo(encodedEntity.Length));
        var actual = action.Result
            ?? throw new InvalidOperationException("The authored Entity reader did not produce a value.");
        Assert.That(actual.AuthoredEntityId, Is.EqualTo(authoredEntityId));
        Assert.That(actual.Components, Has.Length.EqualTo(1));
        Assert.That(
            actual.Components[0].Schema,
            Is.EqualTo(new ContentSchemaReference(
                localTransformSchema,
                1,
                0,
                ContentSchemaScope.Content)));
        Assert.That(
            actual.Components[0].EncodedRecord.SequenceEqual(localTransformRecord),
            Is.True);

        var missingTransform = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            entitySchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(1, authoredEntityId),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(2));

        void ReadMissingTransform()
        {
            var invalidAction = new ReadAction(schemas);
            _ = EbinStructuralReader.Read(
                missingTransform,
                EbinReadLimits.Foundation,
                EbinReadPolicy.Canonical,
                ref invalidAction,
                out _,
                out _);
        }

        Assert.That(
            ReadMissingTransform,
            Throws.TypeOf<InvalidDataException>());
    }

    private struct ReadAction : IAction<EbinRecordReadView>
    {
        private readonly CompiledContentSchemaRegistry _schemas;

        internal ReadAction(CompiledContentSchemaRegistry schemas)
        {
            ArgumentNullException.ThrowIfNull(schemas);
            _schemas = schemas;
            Result = null;
        }

        internal SceneEntity? Result { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
            => Result = SceneEntityReader.Read(
                record,
                _schemas,
                EbinReadLimits.Foundation,
                [],
                allowInstanceAbsoluteReferenceSources: false,
                out _);
    }
}
