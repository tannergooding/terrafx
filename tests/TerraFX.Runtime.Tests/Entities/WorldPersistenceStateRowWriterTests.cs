// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldPersistenceStateRowWriterTests
{
    private static void AssertAttachmentOwnerRow(
        byte[] record,
        EbinReadLimits limits,
        WorldPersistenceLiveIdentitySet liveIdentities)
    {
        var assertions = new AttachmentOwnerValidation(liveIdentities);
        var status = EbinStructuralReader.Read(
            record,
            limits,
            EbinReadPolicy.Canonical,
            ref assertions,
            out var bytesConsumed,
            out var diagnostic);
        Assert.That(status, Is.EqualTo(EbinReadStatus.Complete), diagnostic?.ToString());
        Assert.That(diagnostic, Is.Null);
        Assert.That(bytesConsumed, Is.EqualTo(record.Length));
    }

    private static void AssertStateRow(
        Guid rowSchemaId,
        Guid id,
        Guid payloadSchemaId,
        Guid? expectedOwnerEntityId,
        byte[] payloadRecord,
        EbinReadLimits limits,
        StateRowWriter writer)
    {
        Assert.That(
            WorldPersistenceStateRowWriter.MeasureSystem(payloadRecord.Length, limits, out var rowRecordBytes, out var rowPayloadBytes, out var diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        var rowPayloadStorage = new byte[rowPayloadBytes];
        var candidateStorage = new byte[rowRecordBytes];
        var destination = new byte[rowRecordBytes];

        Assert.That(
            writer(
                id,
                new ContentSchemaReference(payloadSchemaId, 1, 0, ContentSchemaScope.Content),
                payloadRecord,
                limits,
                rowPayloadStorage,
                candidateStorage,
                destination,
                out var bytesWritten,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(bytesWritten, Is.EqualTo(rowRecordBytes));

        var assertions = new StateRowAssertions(
            rowSchemaId,
            id,
            payloadSchemaId,
            default,
            expectedOwnerEntityId);
        Assert.That(
            EbinStructuralReader.Read(
                destination,
                limits,
                EbinReadPolicy.Canonical,
                ref assertions,
                out var bytesConsumed,
                out var readDiagnostic),
            Is.EqualTo(EbinReadStatus.Complete));
        Assert.That(readDiagnostic, Is.Null);
        Assert.That(bytesConsumed, Is.EqualTo(bytesWritten));
    }

    private static byte[] CreatePayloadRecord()
    {
        var schemaId = new ContentSchemaIdentityAssignment("application/state-row-test/v1").SchemaId;
        return TerraFX.Runtime.Tests.Scenes.SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            schemaId,
            1,
            0);
    }

    private static World CreateWorld()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        return World.Create(new WorldOptions(registration) {
            CpuWorkerCount = 0,
            InitialReplayLineageId = Guid.Parse("30000000-0000-0000-0000-000000000003"),
            WorldGuid = Guid.Parse("10000000-0000-0000-0000-000000000001"),
        });
    }

    [Test]
    public void RejectsNestedStateRecordWithWrongSchemaVersion()
    {
        using var world = CreateWorld();
        var limits = WorldPersistenceEbinLimits.Create(world.Options);
        var schemaId = new ContentSchemaIdentityAssignment("application/state-row-test/v1").SchemaId;
        var payloadSchema = new ContentSchemaReference(
            schemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var payloadRecord = TerraFX.Runtime.Tests.Scenes.SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            schemaId,
            1,
            1);
        Assert.That(
            WorldPersistenceStateRowWriter.MeasureSystem(
                payloadRecord.Length,
                limits,
                out var rowRecordBytes,
                out var rowPayloadBytes,
                out var diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete));
        Assert.That(diagnostic, Is.Null);
        var rowPayloadStorage = new byte[rowPayloadBytes];
        var candidateStorage = new byte[rowRecordBytes];
        var destination = new byte[rowRecordBytes];

        Assert.That(
            WorldPersistenceStateRowWriter.WriteSystem(
                Guid.Parse("20000000-0000-0000-0000-000000000001"),
                payloadSchema,
                payloadRecord,
                limits,
                rowPayloadStorage,
                candidateStorage,
                destination,
                out var bytesWritten,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.InvalidRecord));
        Assert.That(diagnostic?.Category, Is.EqualTo("nested-state-schema-mismatch"));
        Assert.That(bytesWritten, Is.Zero);
    }

    [Test]
    public void RejectsStatePayloadThatIsNotACompleteNestedRecord()
    {
        using var world = CreateWorld();
        var limits = WorldPersistenceEbinLimits.Create(world.Options);
        var payloadSchema = new ContentSchemaReference(
            new ContentSchemaIdentityAssignment("application/state-row-test/v1").SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var payloadRecord = new byte[sizeof(int)];
        Assert.That(
            WorldPersistenceStateRowWriter.MeasureSystem(
                payloadRecord.Length,
                limits,
                out var rowRecordBytes,
                out var rowPayloadBytes,
                out var diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete));
        Assert.That(diagnostic, Is.Null);
        var rowPayloadStorage = new byte[rowPayloadBytes];
        var candidateStorage = new byte[rowRecordBytes];
        var destination = new byte[rowRecordBytes];
        var systemId = Guid.Parse("20000000-0000-0000-0000-000000000001");

        Assert.That(
            WorldPersistenceStateRowWriter.WriteSystem(
                systemId,
                payloadSchema,
                payloadRecord,
                limits,
                rowPayloadStorage,
                candidateStorage,
                destination,
                out var bytesWritten,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.InvalidRecord));
        Assert.That(diagnostic, Is.Not.Null);
        Assert.That(bytesWritten, Is.Zero);
    }

    [TestCase(null)]
    [TestCase("30000000-0000-0000-0000-000000000007")]
    public void WritesAttachmentRowsWithOptionalEntityOwner(string? ownerEntityIdText)
    {
        using var world = CreateWorld();
        var schemaAssignment = new ContentSchemaIdentityAssignment("application/attachment-state-test/v1");
        var schema = new ContentSchemaReference(
            schemaAssignment.SchemaId,
            2,
            3,
            ContentSchemaScope.Foundation);
        var payloadRecord = TerraFX.Runtime.Tests.Scenes.SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            schema.SchemaId,
            schema.Major,
            schema.Minor);
        Guid? ownerEntityId = (ownerEntityIdText is null) ? null : Guid.Parse(ownerEntityIdText);
        var limits = WorldPersistenceEbinLimits.Create(world.Options);

        Assert.That(
            WorldPersistenceStateRowWriter.MeasureAttachment(
                schema,
                ownerEntityId.HasValue,
                payloadRecord.Length,
                limits,
                out var schemaReferenceRecordBytes,
                out var rowRecordBytes,
                out var rowPayloadBytes,
                out var diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        var rowPayloadStorage = new byte[rowPayloadBytes];
        var candidateStorage = new byte[rowRecordBytes];
        var destination = new byte[rowRecordBytes];

        Assert.That(
            WorldPersistenceStateRowWriter.WriteAttachment(
                schema,
                ownerEntityId,
                payloadRecord,
                limits,
                rowPayloadStorage,
                candidateStorage,
                destination,
                out var bytesWritten,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(bytesWritten, Is.EqualTo(rowRecordBytes));
        Assert.That(schemaReferenceRecordBytes, Is.GreaterThan(0));

        var assertions = new StateRowAssertions(
            new ContentSchemaIdentityAssignment("state/attachment").SchemaId,
            Guid.Empty,
            schema.SchemaId,
            schema,
            ownerEntityId);
        Assert.That(
            EbinStructuralReader.Read(
                destination,
                limits,
                EbinReadPolicy.Canonical,
                ref assertions,
                out var bytesConsumed,
                out var readDiagnostic),
            Is.EqualTo(EbinReadStatus.Complete));
        Assert.That(readDiagnostic, Is.Null);
        Assert.That(bytesConsumed, Is.EqualTo(bytesWritten));

        var liveIdentities = new WorldPersistenceLiveIdentitySet(ownerEntityId.HasValue ? 1 : 0);

        if (ownerEntityId is Guid owner)
        {
            liveIdentities.Add(owner, identityKind: 1, assignmentOrdinal: 1, nameof(ownerEntityId));
            liveIdentities.MatchEntity(owner, nameof(ownerEntityId));
        }

        AssertAttachmentOwnerRow(destination, limits, liveIdentities);

        if (ownerEntityId.HasValue)
        {
            var differentEntityId = Guid.Parse("40000000-0000-0000-0000-000000000008");
            var differentLiveSet = new WorldPersistenceLiveIdentitySet(maximumCount: 1);
            differentLiveSet.Add(differentEntityId, identityKind: 1, assignmentOrdinal: 1, nameof(destination));
            differentLiveSet.MatchEntity(differentEntityId, nameof(destination));
            _ = Assert.Throws<ArgumentException>(() => AssertAttachmentOwnerRow(destination, limits, differentLiveSet));
        }
    }

    [Test]
    public void WritesSystemAndResourceStateRowsAroundTheirPayloadRecords()
    {
        using var world = CreateWorld();
        var payloadRecord = CreatePayloadRecord();
        var systemId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var resourceId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var payloadSchemaId = new ContentSchemaIdentityAssignment("application/state-row-test/v1").SchemaId;
        var limits = WorldPersistenceEbinLimits.Create(world.Options);

        AssertStateRow(
            new ContentSchemaIdentityAssignment("state/system-state").SchemaId,
            systemId,
            payloadSchemaId,
            expectedOwnerEntityId: null,
            payloadRecord,
            limits,
            WorldPersistenceStateRowWriter.WriteSystem);

        AssertStateRow(
            new ContentSchemaIdentityAssignment("state/resource-state").SchemaId,
            resourceId,
            payloadSchemaId,
            expectedOwnerEntityId: null,
            payloadRecord,
            limits,
            WorldPersistenceStateRowWriter.WriteResource);
    }

    private readonly struct AttachmentOwnerValidation : IAction<EbinRecordReadView>
    {
        private readonly WorldPersistenceLiveIdentitySet _liveIdentities;

        internal AttachmentOwnerValidation(WorldPersistenceLiveIdentitySet liveIdentities)
        {
            _liveIdentities = liveIdentities;
        }

        public readonly void Invoke(scoped EbinRecordReadView record)
        {
            if (record.Header.SchemaId == new ContentSchemaIdentityAssignment("state/attachment").SchemaId)
            {
                WorldPersistenceEcsReader.ValidateAttachmentOwner(record, _liveIdentities, nameof(record));
            }
        }
    }

    private readonly struct StateRowAssertions : IAction<EbinRecordReadView>
    {
        private readonly Guid _expectedId;
        private readonly Guid? _expectedOwnerEntityId;
        private readonly Guid _expectedPayloadSchemaId;
        private readonly Guid _expectedRowSchemaId;
        private readonly ContentSchemaReference _expectedSchema;

        internal StateRowAssertions(
            Guid expectedRowSchemaId,
            Guid expectedId,
            Guid expectedPayloadSchemaId,
            ContentSchemaReference expectedSchema,
            Guid? expectedOwnerEntityId)
        {
            _expectedId = expectedId;
            _expectedOwnerEntityId = expectedOwnerEntityId;
            _expectedPayloadSchemaId = expectedPayloadSchemaId;
            _expectedRowSchemaId = expectedRowSchemaId;
            _expectedSchema = expectedSchema;
        }

        private static Guid ReadGuid(EbinFieldReadView field)
        {
            Assert.That(field.Kind, Is.EqualTo(11));
            Assert.That(field.Payload.Length, Is.EqualTo(16));
            return new Guid(field.Payload, bigEndian: true);
        }

        public readonly void Invoke(scoped EbinRecordReadView record)
        {
            Assert.That(record.Header.SchemaId, Is.EqualTo(_expectedRowSchemaId));

            if (_expectedRowSchemaId == new ContentSchemaIdentityAssignment("state/attachment").SchemaId)
            {
                var schema = record.GetRecord(1);
                Assert.That(schema.Header.SchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment("content/schema-ref").SchemaId));
                Assert.That(ReadGuid(schema.GetField(0)), Is.EqualTo(_expectedSchema.SchemaId));
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(schema.GetField(1).Payload), Is.EqualTo(_expectedSchema.Major));
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(schema.GetField(2).Payload), Is.EqualTo(_expectedSchema.Minor));
                Assert.That(schema.GetField(3).Payload[0], Is.EqualTo((byte)_expectedSchema.Scope));
                Assert.That(record.TryGetField(2, out var ownerField), Is.EqualTo(_expectedOwnerEntityId.HasValue));

                if (_expectedOwnerEntityId is Guid expectedOwnerEntityId)
                {
                    Assert.That(ReadGuid(ownerField), Is.EqualTo(expectedOwnerEntityId));
                    Assert.That(ownerField.IsRequired, Is.False);
                }

                Assert.That(record.GetRecord(3).Header.SchemaId, Is.EqualTo(_expectedPayloadSchemaId));
            }
            else
            {
                Assert.That(ReadGuid(record.GetField(0)), Is.EqualTo(_expectedId));
                Assert.That(record.GetRecord(2).Header.SchemaId, Is.EqualTo(_expectedPayloadSchemaId));
            }
        }
    }

    private delegate EbinWriteStatus StateRowWriter(
        Guid id,
        ContentSchemaReference payloadSchema,
        ReadOnlySpan<byte> payloadRecord,
        EbinReadLimits limits,
        Span<byte> rowPayloadStorage,
        Span<byte> candidateStorage,
        Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic);
}
