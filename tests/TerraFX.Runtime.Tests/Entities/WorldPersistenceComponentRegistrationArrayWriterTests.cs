// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldPersistenceComponentRegistrationArrayWriterTests
{
    private static WorldRegistration CreateSceneRegistration()
    {
        var schemas = new CompiledContentSchemaRegistry([
            .. CoreContentSchemaCatalogue.PrimitiveSchemas,
            .. CoreContentSchemaCatalogue.FoundationSchemas,
            .. NativeAssetSchemaCatalogue.Schemas,
            .. SceneSchemaCatalogue.Schemas,
        ]);
        var builder = new WorldRegistrationBuilder(schemas);
        _ = builder.RegisterSceneSupport(SceneSupport.CreateRegistration());
        return builder.Build();
    }

    [Test]
    public void WritesCanonicalEmptyComponentArray()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        Assert.That(
            WorldPersistenceComponentRegistrationArrayWriter.Measure(
                registration,
                EbinReadLimits.Foundation,
                out var workspace,
                out var diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(workspace.RecordCount, Is.Zero);
        Assert.That(workspace.RecordStorageBytes, Is.Zero);
        Assert.That(workspace.MaximumCandidateBytes, Is.Zero);
        Assert.That(workspace.ArrayPayloadBytes, Is.EqualTo(16));
        var payload = new byte[workspace.ArrayPayloadBytes];
        Assert.That(
            WorldPersistenceComponentRegistrationArrayWriter.Write(
                registration,
                EbinReadLimits.Foundation,
                workspace,
                [],
                [],
                [],
                payload,
                out var payloadBytes,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(payloadBytes, Is.EqualTo(16));
        Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(payload), Is.Zero);
    }

    [Test]
    public void WritesSceneComponentsInCanonicalSchemaOrder()
    {
        var registration = CreateSceneRegistration();
        Assert.That(registration.Components, Has.Length.EqualTo(3));
        Assert.That(
            WorldPersistenceComponentRegistrationArrayWriter.Measure(
                registration,
                EbinReadLimits.Foundation,
                out var workspace,
                out var diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        var recordSlices = new EbinPayloadSlice[workspace.RecordCount];
        var recordStorage = new byte[workspace.RecordStorageBytes];
        var candidateStorage = new byte[workspace.MaximumCandidateBytes];
        var payload = new byte[workspace.ArrayPayloadBytes];
        Assert.That(
            WorldPersistenceComponentRegistrationArrayWriter.Write(
                registration,
                EbinReadLimits.Foundation,
                workspace,
                recordSlices,
                recordStorage,
                candidateStorage,
                payload,
                out var payloadBytes,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(payloadBytes, Is.EqualTo(workspace.ArrayPayloadBytes));
        Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(payload), Is.EqualTo((ulong)registration.Components.Length));
        var previousOffset = BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(8));

        for (var index = 0; index < registration.Components.Length; index++)
        {
            var endOffset = BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(16 + (index * 8)));
            var assertions = new ComponentRegistrationArrayAssertions(registration.Components[index].SchemaReference);
            Assert.That(
                EbinStructuralReader.Read(
                    payload.AsSpan(checked((int)previousOffset), checked((int)(endOffset - previousOffset))),
                    EbinReadLimits.Foundation,
                    EbinReadPolicy.Canonical,
                    ref assertions,
                    out var rowBytes,
                    out var readDiagnostic),
                Is.EqualTo(EbinReadStatus.Complete));
            Assert.That(readDiagnostic, Is.Null);
            Assert.That(assertions.IsMatch, Is.True);
            Assert.That(rowBytes, Is.EqualTo(checked((int)(endOffset - previousOffset))));
            previousOffset = endOffset;
        }

        Assert.That(previousOffset, Is.EqualTo((ulong)payload.Length));
        var emptyArrayPayload = new byte[16];
        Assert.That(
            EbinPayloadWriter.WriteRecordArray(
                [],
                [],
                EbinReadLimits.Foundation,
                emptyArrayPayload,
                out var emptyArrayBytes,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(emptyArrayBytes, Is.EqualTo(emptyArrayPayload.Length));
        var rootPayloadStorage = new byte[checked(payload.Length + (3 * emptyArrayPayload.Length))];
        Assert.That(
            WorldPersistenceRegistrationWriter.Measure(
                registration,
                payload,
                emptyArrayPayload,
                emptyArrayPayload,
                emptyArrayPayload,
                EbinReadLimits.Foundation,
                rootPayloadStorage,
                out var rootPayloadBytes,
                out var rootRecordBytes,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(rootPayloadBytes, Is.EqualTo(rootPayloadStorage.Length));
        var rootCandidateStorage = new byte[rootRecordBytes];
        var rootDestination = new byte[rootRecordBytes];
        Assert.That(
            WorldPersistenceRegistrationWriter.Write(
                registration,
                payload,
                emptyArrayPayload,
                emptyArrayPayload,
                emptyArrayPayload,
                EbinReadLimits.Foundation,
                rootPayloadStorage,
                rootCandidateStorage,
                rootDestination,
                out var rootBytesWritten,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(rootBytesWritten, Is.EqualTo(rootRecordBytes));
        _ = WorldPersistenceRegistrationReader.Read(
            rootDestination,
            EbinReadLimits.Foundation,
            maximumComponentTypes: 256,
            targetRegistration: registration);
    }

    private struct ComponentRegistrationArrayAssertions : IAction<EbinRecordReadView>
    {
        private readonly ContentSchemaReference _schema;

        internal ComponentRegistrationArrayAssertions(ContentSchemaReference schema)
        {
            _schema = schema;
            IsMatch = false;
        }

        internal bool IsMatch { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
        {
            IsMatch = (record.Header.SchemaId == new ContentSchemaIdentityAssignment("state/component-registration").SchemaId)
                && (record.Header.Major == 1)
                && (record.Header.Minor == 0)
                && (record.FieldCount == 5);

            if (!IsMatch)
            {
                return;
            }

            var schema = record.GetRecord(1);
            var schemaId = schema.GetField(0);
            var major = schema.GetField(1);
            var minor = schema.GetField(2);
            var scope = schema.GetField(3);
            IsMatch = (schema.Header.SchemaId == new ContentSchemaIdentityAssignment("content/schema-ref").SchemaId)
                && (new Guid(schemaId.Payload, bigEndian: true) == _schema.SchemaId)
                && (BinaryPrimitives.ReadUInt32LittleEndian(major.Payload) == (uint)_schema.Major)
                && (BinaryPrimitives.ReadUInt32LittleEndian(minor.Payload) == (uint)_schema.Minor)
                && (scope.Payload[0] == (byte)_schema.Scope);
        }
    }
}
