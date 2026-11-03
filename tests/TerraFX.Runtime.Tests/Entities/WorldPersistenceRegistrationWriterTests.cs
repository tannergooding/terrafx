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
        var assertions = new RegistrationRootAssertions();
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
    }

    private struct RegistrationRootAssertions : IAction<EbinRecordReadView>
    {
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
                    || (field.Count != 0)
                    || (field.Payload.Length != 16))
                {
                    IsMatch = false;
                    return;
                }
            }
        }
    }
}
