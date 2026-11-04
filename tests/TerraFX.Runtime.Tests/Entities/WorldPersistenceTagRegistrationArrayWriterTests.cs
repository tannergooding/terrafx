// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldPersistenceTagRegistrationArrayWriterTests
{
    private static WorldRegistration CreateRegistration(out Guid highId, out Guid lowId)
    {
        var highAssignment = new ContentSchemaIdentityAssignment("application/tag-array/high/v1");
        var lowAssignment = new ContentSchemaIdentityAssignment("application/tag-array/low/v1");
        highId = highAssignment.SchemaId;
        lowId = lowAssignment.SchemaId;
        var registry = new CompiledContentSchemaRegistry([
            CreateTagSchema(highAssignment, 3),
            CreateTagSchema(lowAssignment, 2),
        ]);
        var builder = new WorldRegistrationBuilder(registry);
        _ = builder.RegisterTag(new EntityTagSchema(highId, 3));
        _ = builder.RegisterTag(new EntityTagSchema(lowId, 2));
        return builder.Build();
    }

    private static DataSchema CreateTagSchema(ContentSchemaIdentityAssignment assignment, int major)
        => new(
            new ContentSchemaReference(assignment.SchemaId, major, 0, ContentSchemaScope.Content),
            assignment,
            ContentSchemaRole.EntityTag,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            []);

    [Test]
    public void CapturesRegisteredTagsInTheWorldRegistrationRoot()
    {
        var registration = CreateRegistration(out _, out _);
        using var world = World.Create(new WorldOptions(registration) {
            CpuWorkerCount = 0,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        _ = world.Step();
        using var lease = world.AcquirePersistenceCaptureLease();
        Assert.That(
            WorldPersistenceRegistrationCaptureCoordinator.Capture(
                world,
                lease,
                out var record,
                out var reservation,
                out var diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        var capturedRecord = record ?? throw new InvalidOperationException("The registration capture did not produce its record.");
        using var reservationScope = reservation
            ?? throw new InvalidOperationException("The registration capture did not retain its admission reservation.");
        var assertions = new RootRegistrationAssertions(expectedTagCount: registration.Tags.Length);
        Assert.That(
            EbinStructuralReader.Read(
                capturedRecord,
                EbinReadLimits.Foundation,
                EbinReadPolicy.Canonical,
                ref assertions,
                out var bytesConsumed,
                out var readDiagnostic),
            Is.EqualTo(EbinReadStatus.Complete));
        Assert.That(readDiagnostic, Is.Null);
        Assert.That(assertions.IsMatch, Is.True);
        Assert.That(bytesConsumed, Is.EqualTo(capturedRecord.Length));
    }

    [Test]
    public void WritesCanonicalEmptyTagArray()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        Assert.That(
            WorldPersistenceTagRegistrationArrayWriter.Measure(
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
            WorldPersistenceTagRegistrationArrayWriter.Write(
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
    public void WritesTagsInCanonicalSchemaOrder()
    {
        var registration = CreateRegistration(out var highId, out var lowId);
        Assert.That(registration.Tags, Has.Length.EqualTo(2));
        Assert.That(
            WorldPersistenceTagRegistrationArrayWriter.Measure(
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
            WorldPersistenceTagRegistrationArrayWriter.Write(
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
        Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(payload), Is.EqualTo(2));
        var expectedSchemaIds = new[] { registration.Tags[0].SchemaId, registration.Tags[1].SchemaId };
        Assert.That(expectedSchemaIds, Does.Contain(highId));
        Assert.That(expectedSchemaIds, Does.Contain(lowId));
        var previousOffset = BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(8));

        for (var index = 0; index < expectedSchemaIds.Length; index++)
        {
            var endOffset = BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(16 + (index * 8)));
            var assertions = new SchemaReferenceAssertions(registration.Tags[index].Schema);
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
    }

    private struct RootRegistrationAssertions : IAction<EbinRecordReadView>
    {
        private readonly int _expectedTagCount;

        internal RootRegistrationAssertions(int expectedTagCount)
        {
            _expectedTagCount = expectedTagCount;
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

            var tags = record.GetField(1);
            IsMatch = (tags.Tag == 2)
                && (tags.Kind == 24)
                && (tags.Count == checked((ulong)_expectedTagCount))
                && (BinaryPrimitives.ReadUInt64LittleEndian(tags.Payload) == checked((ulong)_expectedTagCount));
        }
    }

    private struct SchemaReferenceAssertions : IAction<EbinRecordReadView>
    {
        private readonly ContentSchemaReference _expected;

        internal SchemaReferenceAssertions(ContentSchemaReference expected)
        {
            _expected = expected;
            IsMatch = false;
        }

        internal bool IsMatch { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
        {
            IsMatch = (record.Header.SchemaId == new ContentSchemaIdentityAssignment("content/schema-ref").SchemaId)
                && (record.Header.Major == 1)
                && (record.Header.Minor == 0)
                && (record.FieldCount == 4);

            if (!IsMatch)
            {
                return;
            }

            var schemaId = record.GetField(0);
            var major = record.GetField(1);
            var minor = record.GetField(2);
            var scope = record.GetField(3);
            IsMatch = (new Guid(schemaId.Payload, bigEndian: true) == _expected.SchemaId)
                && (BinaryPrimitives.ReadUInt32LittleEndian(major.Payload) == (uint)_expected.Major)
                && (BinaryPrimitives.ReadUInt32LittleEndian(minor.Payload) == (uint)_expected.Minor)
                && (scope.Payload[0] == (byte)_expected.Scope);
        }
    }
}
