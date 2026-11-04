// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldPersistenceRegistrationWriterTests
{
    private static byte[] CreateEmptyArrayPayload()
    {
        var payload = new byte[16];
        Assert.That(
            EbinPayloadWriter.WriteRecordArray([], [], EbinReadLimits.Foundation, payload, out var bytesWritten, out var diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(bytesWritten, Is.EqualTo(payload.Length));
        return payload;
    }

    private static WorldRegistration CreateTagRegistration()
    {
        var firstAssignment = new ContentSchemaIdentityAssignment("application/registration-root/tag-first/v1");
        var secondAssignment = new ContentSchemaIdentityAssignment("application/registration-root/tag-second/v1");
        var registry = new CompiledContentSchemaRegistry([
            CreateTagSchema(firstAssignment),
            CreateTagSchema(secondAssignment),
        ]);
        var builder = new WorldRegistrationBuilder(registry);
        _ = builder.RegisterTag(new EntityTagSchema(firstAssignment.SchemaId, 1));
        _ = builder.RegisterTag(new EntityTagSchema(secondAssignment.SchemaId, 1));
        return builder.Build();
    }

    private static DataSchema CreateTagSchema(ContentSchemaIdentityAssignment assignment)
        => new(
            new ContentSchemaReference(assignment.SchemaId, 1, 0, ContentSchemaScope.Content),
            assignment,
            ContentSchemaRole.EntityTag,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            []);

    [Test]
    public void RejectsArrayCountThatDoesNotMatchTheFrozenRegistration()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        var nonemptyArrayPayload = new byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(nonemptyArrayPayload, 1);
        BinaryPrimitives.WriteUInt64LittleEndian(nonemptyArrayPayload.AsSpan(8), 16);
        var emptyArrayPayload = CreateEmptyArrayPayload();
        var rootPayloadStorage = new byte[64];
        Assert.That(
            WorldPersistenceRegistrationWriter.Measure(
                registration,
                nonemptyArrayPayload,
                emptyArrayPayload,
                emptyArrayPayload,
                emptyArrayPayload,
                EbinReadLimits.Foundation,
                rootPayloadStorage,
                out var rootPayloadBytes,
                out var recordBytes,
                out var diagnostic),
            Is.EqualTo(EbinWriteStatus.InvalidRecord));
        Assert.That(rootPayloadBytes, Is.Zero);
        Assert.That(recordBytes, Is.Zero);
        Assert.That(diagnostic?.Category, Is.EqualTo("component-registration-array"));
    }

    [Test]
    public void RejectsMalformedArrayPayloads()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        var malformedEmptyArrayPayload = new byte[16];
        var rootPayloadStorage = new byte[64];
        Assert.That(
            WorldPersistenceRegistrationWriter.Measure(
                registration,
                malformedEmptyArrayPayload,
                malformedEmptyArrayPayload,
                malformedEmptyArrayPayload,
                malformedEmptyArrayPayload,
                EbinReadLimits.Foundation,
                rootPayloadStorage,
                out var rootPayloadBytes,
                out var recordBytes,
                out var diagnostic),
            Is.EqualTo(EbinWriteStatus.InvalidRecord));
        Assert.That(rootPayloadBytes, Is.EqualTo(64));
        Assert.That(recordBytes, Is.Zero);
        Assert.That(diagnostic, Is.Not.Null);
    }

    [Test]
    public void WritesFourCanonicalEmptyRegistrationArrays()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        var emptyArrayPayload = CreateEmptyArrayPayload();
        var rootPayloadStorage = new byte[64];
        Assert.That(
            WorldPersistenceRegistrationWriter.Measure(
                registration,
                emptyArrayPayload,
                emptyArrayPayload,
                emptyArrayPayload,
                emptyArrayPayload,
                EbinReadLimits.Foundation,
                rootPayloadStorage,
                out var rootPayloadBytes,
                out var recordBytes,
                out var diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(rootPayloadBytes, Is.EqualTo(rootPayloadStorage.Length));
        var candidateStorage = new byte[recordBytes];
        var destination = new byte[recordBytes];
        Assert.That(
            WorldPersistenceRegistrationWriter.Write(
                registration,
                emptyArrayPayload,
                emptyArrayPayload,
                emptyArrayPayload,
                emptyArrayPayload,
                EbinReadLimits.Foundation,
                rootPayloadStorage,
                candidateStorage,
                destination,
                out var bytesWritten,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(bytesWritten, Is.EqualTo(recordBytes));
        var assertions = new RegistrationRootAssertions(tagCount: 0, tagPayloadBytes: 16);
        Assert.That(
            EbinStructuralReader.Read(
                destination,
                EbinReadLimits.Foundation,
                EbinReadPolicy.Canonical,
                ref assertions,
                out var bytesConsumed,
                out var readDiagnostic),
            Is.EqualTo(EbinReadStatus.Complete));
        Assert.That(readDiagnostic, Is.Null);
        Assert.That(assertions.IsMatch, Is.True);
        Assert.That(bytesConsumed, Is.EqualTo(destination.Length));
        var decoded = WorldPersistenceRegistrationReader.Read(
            destination,
            EbinReadLimits.Foundation,
            maximumComponentTypes: 256);
        Assert.That(decoded.ComponentCount, Is.Zero);
        Assert.That(decoded.TagCount, Is.Zero);
        Assert.That(decoded.SystemCount, Is.Zero);
        Assert.That(decoded.ResourceCount, Is.Zero);
    }

    [Test]
    public void WritesTheCanonicalTagArrayIntoTheRegistrationRoot()
    {
        var registration = CreateTagRegistration();
        Assert.That(
            WorldPersistenceTagRegistrationArrayWriter.Measure(
                registration,
                EbinReadLimits.Foundation,
                out var tagWorkspace,
                out var diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        var tagSlices = new EbinPayloadSlice[tagWorkspace.RecordCount];
        var tagStorage = new byte[tagWorkspace.RecordStorageBytes];
        var tagCandidate = new byte[tagWorkspace.MaximumCandidateBytes];
        var tagPayload = new byte[tagWorkspace.ArrayPayloadBytes];
        Assert.That(
            WorldPersistenceTagRegistrationArrayWriter.Write(
                registration,
                EbinReadLimits.Foundation,
                tagWorkspace,
                tagSlices,
                tagStorage,
                tagCandidate,
                tagPayload,
                out var tagPayloadBytes,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(tagPayloadBytes, Is.EqualTo(tagPayload.Length));
        var emptyArrayPayload = CreateEmptyArrayPayload();
        var rootPayloadStorage = new byte[checked((3 * emptyArrayPayload.Length) + tagPayload.Length)];
        Assert.That(
            WorldPersistenceRegistrationWriter.Measure(
                registration,
                emptyArrayPayload,
                tagPayload,
                emptyArrayPayload,
                emptyArrayPayload,
                EbinReadLimits.Foundation,
                rootPayloadStorage,
                out var rootPayloadBytes,
                out var recordBytes,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(rootPayloadBytes, Is.EqualTo(rootPayloadStorage.Length));
        var candidateStorage = new byte[recordBytes];
        var destination = new byte[recordBytes];
        Assert.That(
            WorldPersistenceRegistrationWriter.Write(
                registration,
                emptyArrayPayload,
                tagPayload,
                emptyArrayPayload,
                emptyArrayPayload,
                EbinReadLimits.Foundation,
                rootPayloadStorage,
                candidateStorage,
                destination,
                out var bytesWritten,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(bytesWritten, Is.EqualTo(recordBytes));
        var assertions = new RegistrationRootAssertions(tagWorkspace.RecordCount, tagWorkspace.ArrayPayloadBytes);
        Assert.That(
            EbinStructuralReader.Read(
                destination,
                EbinReadLimits.Foundation,
                EbinReadPolicy.Canonical,
                ref assertions,
                out var bytesConsumed,
                out var readDiagnostic),
            Is.EqualTo(EbinReadStatus.Complete));
        Assert.That(readDiagnostic, Is.Null);
        Assert.That(assertions.IsMatch, Is.True);
        Assert.That(bytesConsumed, Is.EqualTo(destination.Length));
        var decoded = WorldPersistenceRegistrationReader.Read(
            destination,
            EbinReadLimits.Foundation,
            maximumComponentTypes: 256);
        Assert.That(decoded.ComponentCount, Is.Zero);
        Assert.That(decoded.TagCount, Is.EqualTo(2));
        Assert.That(decoded.SystemCount, Is.Zero);
        Assert.That(decoded.ResourceCount, Is.Zero);
    }

    private struct RegistrationRootAssertions : IAction<EbinRecordReadView>
    {
        private readonly int _tagCount;
        private readonly int _tagPayloadBytes;

        internal RegistrationRootAssertions(int tagCount = 0, int tagPayloadBytes = 16)
        {
            _tagCount = tagCount;
            _tagPayloadBytes = tagPayloadBytes;
            IsMatch = false;
        }

        internal bool IsMatch { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
        {
            IsMatch = (record.Header.SchemaId == new ContentSchemaIdentityAssignment("state/registration").SchemaId)
                && (record.Header.Major == 1)
                && (record.Header.Minor == 0)
                && (record.FieldCount == 4);

            if (!IsMatch)
            {
                return;
            }

            for (var index = 0; index < record.FieldCount; index++)
            {
                var field = record.GetField(index);

                if ((field.Tag != (uint)(index + 1))
                    || (field.Kind != 24)
                    || !field.IsRequired
                    || (field.Count != checked((ulong)((index == 1) ? _tagCount : 0)))
                    || (field.Payload.Length != ((index == 1) ? _tagPayloadBytes : 16)))
                {
                    IsMatch = false;
                    return;
                }
            }
        }
    }
}
