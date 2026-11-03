// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Threading;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldPersistenceEntityCaptureTests
{
    private static void AssertEntityRecord(
        WorldPersistenceCaptureLease lease,
        in World.EntityStateCaptureEnumerator entity,
        WorldOptions options,
        Guid expectedEntityId,
        long expectedAssignmentOrdinal,
        byte[]? expectedComponentRecord,
        Guid? expectedTagSchemaId = null)
    {
        var componentCount = entity.CurrentPersistentComponentCount;
        Span<EbinPayloadSlice> componentSlices = stackalloc EbinPayloadSlice[componentCount];
        Span<EbinPayloadSlice> tagSlices = stackalloc EbinPayloadSlice[entity.CurrentTagCount];
        Span<EbinPayloadSlice> transientSchemaSlices = stackalloc EbinPayloadSlice[entity.CurrentTransientSchemaCount];
        Assert.That(
            lease.MeasureEntityRecord(
                in entity,
                componentSlices,
                tagSlices,
                transientSchemaSlices,
                out var workspace,
                out var diagnostic,
                CancellationToken.None),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        var reservationBytes = checked(
            (long)MemoryMarshal.AsBytes(componentSlices).Length
            + workspace.ComponentStorageBytes
            + MemoryMarshal.AsBytes(tagSlices).Length
            + workspace.TagStorageBytes
            + MemoryMarshal.AsBytes(transientSchemaSlices).Length
            + workspace.TransientSchemaStorageBytes
            + workspace.ChildCandidateBytes
            + workspace.PayloadStorageBytes
            + workspace.RecordBytes
            + workspace.RecordBytes);
        Assert.That(lease.TryReserveAdmissionBytes(reservationBytes, out var reservation), Is.True);
        Assert.That(reservation, Is.Not.Null);
        using var reservationScope = reservation!;
        var componentStorage = new byte[workspace.ComponentStorageBytes];
        var tagStorage = new byte[workspace.TagStorageBytes];
        var transientSchemaStorage = new byte[workspace.TransientSchemaStorageBytes];
        var childCandidateStorage = new byte[workspace.ChildCandidateBytes];
        var payloadStorage = new byte[workspace.PayloadStorageBytes];
        var candidateStorage = new byte[workspace.RecordBytes];
        var destination = new byte[workspace.RecordBytes];

        Assert.That(
            lease.WriteEntityRecord(
                reservationScope,
                in entity,
                componentSlices,
                componentStorage,
                tagSlices,
                tagStorage,
                transientSchemaSlices,
                transientSchemaStorage,
                childCandidateStorage,
                payloadStorage,
                candidateStorage,
                destination,
                out var bytesWritten,
                out diagnostic,
                CancellationToken.None),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(bytesWritten, Is.EqualTo(workspace.RecordBytes));

        var assertions = new EntityRecordAssertions(
            expectedEntityId,
            expectedAssignmentOrdinal,
            expectedComponentRecord,
            expectedTagSchemaId);
        Assert.That(
            EbinStructuralReader.Read(
                destination.AsSpan(0, bytesWritten),
                WorldPersistenceEbinLimits.Create(options),
                EbinReadPolicy.Canonical,
                ref assertions,
                out var bytesConsumed,
                out var readDiagnostic),
            Is.EqualTo(EbinReadStatus.Complete));
        Assert.That(readDiagnostic, Is.Null);
        Assert.That(bytesConsumed, Is.EqualTo(bytesWritten));
    }

    private static byte[] CreateExpectedLocalTransformRecord()
    {
        var assignment = new ContentSchemaIdentityAssignment("scene/local-transform");
        return TerraFX.Runtime.Tests.Scenes.SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            assignment.SchemaId,
            1,
            0,
            TerraFX.Runtime.Tests.Scenes.SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(1, 15, 0, 0, 0),
            TerraFX.Runtime.Tests.Scenes.SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(2, 17, 0, 0, 0, 1),
            TerraFX.Runtime.Tests.Scenes.SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(3, 15, 1, 1, 1));
    }

    private static World CreateSceneWorld()
    {
        var schemas = new CompiledContentSchemaRegistry([
            .. CoreContentSchemaCatalogue.PrimitiveSchemas,
            .. CoreContentSchemaCatalogue.FoundationSchemas,
            .. NativeAssetSchemaCatalogue.Schemas,
            .. SceneSchemaCatalogue.Schemas,
        ]);
        var registrationBuilder = new WorldRegistrationBuilder(schemas);
        _ = registrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
        var registration = registrationBuilder.Build();
        return World.Create(new WorldOptions(registration) {
            CpuWorkerCount = 0,
            MaximumPersistenceAdmissionBytes = 8192,
            InitialReplayLineageId = Guid.Parse("30000000-0000-0000-0000-000000000003"),
            WorldGuid = Guid.Parse("10000000-0000-0000-0000-000000000001"),
        });
    }

    private static World CreateTaggedWorld(Guid tagId)
    {
        var registration = new WorldRegistration(
            new CompiledContentSchemaRegistry([]),
            [],
            [new EntityTagSchema(tagId, 1)]);
        return World.Create(new WorldOptions(registration) {
            CpuWorkerCount = 0,
            MaximumPersistenceAdmissionBytes = 8192,
            InitialReplayLineageId = Guid.Parse("30000000-0000-0000-0000-000000000003"),
            WorldGuid = Guid.Parse("10000000-0000-0000-0000-000000000001"),
        });
    }

    [Test]
    public void WritesCanonicalEntityRowsWithPersistentComponentsAndEmptyArrays()
    {
        using var world = CreateSceneWorld();
        var highId = Guid.Parse("10000000-0000-0000-0000-000000000002");
        var lowId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var transformedEntity = world.CreateEntity(new EntityDescription(persistentId: highId));
        var emptyEntity = world.CreateEntity(new EntityDescription(persistentId: lowId));
        world.Add<SceneComponentDefinitions.LocalTransformComponent>(transformedEntity);
        _ = world.Step();

        using var lease = world.AcquirePersistenceCaptureLease();
        var rows = new World.EntityStateCaptureRow[2];
        var entities = lease.GetCanonicalEntityStateEnumerator(rows);
        Assert.That(entities.MoveNext(), Is.True);
        AssertEntityRecord(lease, in entities, world.Options, lowId, 2, expectedComponentRecord: null);
        Assert.That(entities.MoveNext(), Is.True);
        AssertEntityRecord(lease, in entities, world.Options, highId, 1, CreateExpectedLocalTransformRecord());
        Assert.That(entities.MoveNext(), Is.False);
        Assert.That(world.GetEntityId(emptyEntity), Is.EqualTo(lowId));
    }

    [Test]
    public void WritesRegisteredEntityTagsAsEmptyNestedRecords()
    {
        var tagId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        using var world = CreateTaggedWorld(tagId);
        var entityId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        _ = world.CreateEntity(new EntityDescription(persistentId: entityId, tags: [tagId]));
        _ = world.Step();

        using var lease = world.AcquirePersistenceCaptureLease();
        var rows = new World.EntityStateCaptureRow[1];
        var entities = lease.GetCanonicalEntityStateEnumerator(rows);
        Assert.That(entities.MoveNext(), Is.True);
        AssertEntityRecord(
            lease,
            in entities,
            world.Options,
            entityId,
            expectedAssignmentOrdinal: 1,
            expectedComponentRecord: null,
            expectedTagSchemaId: tagId);
        Assert.That(entities.MoveNext(), Is.False);
    }

    private readonly struct EntityRecordAssertions : IAction<EbinRecordReadView>
    {
        private readonly byte[]? _expectedComponentRecord;
        private readonly Guid _expectedEntityId;
        private readonly Guid? _expectedTagSchemaId;
        private readonly long _expectedAssignmentOrdinal;

        internal EntityRecordAssertions(
            Guid expectedEntityId,
            long expectedAssignmentOrdinal,
            byte[]? expectedComponentRecord,
            Guid? expectedTagSchemaId)
        {
            _expectedComponentRecord = expectedComponentRecord;
            _expectedEntityId = expectedEntityId;
            _expectedTagSchemaId = expectedTagSchemaId;
            _expectedAssignmentOrdinal = expectedAssignmentOrdinal;
        }

        private static void AssertEmptyArray(EbinFieldReadView field, uint tag)
        {
            Assert.That(field.Tag, Is.EqualTo(tag));
            Assert.That(field.Kind, Is.EqualTo(24));
            Assert.That(field.Count, Is.Zero);
            Assert.That(field.Payload.Length, Is.EqualTo(16));
            Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(field.Payload), Is.Zero);
            Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(field.Payload[8..]), Is.EqualTo(16));
        }

        private static Guid ReadGuid(EbinFieldReadView field)
        {
            Assert.That(field.Kind, Is.EqualTo(11));
            Assert.That(field.Payload.Length, Is.EqualTo(16));
            return new Guid(field.Payload, bigEndian: true);
        }

        private static long ReadInt64(EbinFieldReadView field)
        {
            Assert.That(field.Kind, Is.EqualTo(8));
            Assert.That(field.Payload.Length, Is.EqualTo(sizeof(long)));
            return BinaryPrimitives.ReadInt64LittleEndian(field.Payload);
        }

        public readonly void Invoke(scoped EbinRecordReadView record)
        {
            Assert.That(
                record.Header.SchemaId,
                Is.EqualTo(new ContentSchemaIdentityAssignment("state/entity").SchemaId));
            Assert.That(record.Header.Major, Is.EqualTo(1));
            Assert.That(record.Header.Minor, Is.Zero);
            Assert.That(record.FieldCount, Is.EqualTo(5));

            var entityId = record.GetField(0);
            Assert.That(entityId.Tag, Is.EqualTo(1));
            Assert.That(entityId.IsRequired, Is.True);
            Assert.That(ReadGuid(entityId), Is.EqualTo(_expectedEntityId));

            var components = record.GetField(1);
            Assert.That(components.Tag, Is.EqualTo(2));
            Assert.That(components.IsRequired, Is.True);
            Assert.That(components.Kind, Is.EqualTo(24));
            Assert.That(components.Count, Is.EqualTo(_expectedComponentRecord is null ? 0 : 1));

            if (_expectedComponentRecord is null)
            {
                AssertEmptyArray(components, 2);
            }
            else
            {
                Assert.That(
                    record.GetRecordArrayElement(2, 0).EncodedBytes.SequenceEqual(_expectedComponentRecord),
                    Is.True);
            }

            var tags = record.GetField(2);
            Assert.That(tags.Tag, Is.EqualTo(3));
            Assert.That(tags.IsRequired, Is.True);
            Assert.That(tags.Kind, Is.EqualTo(24));
            Assert.That(tags.Count, Is.EqualTo(_expectedTagSchemaId.HasValue ? 1 : 0));

            if (_expectedTagSchemaId is Guid expectedTagSchemaId)
            {
                var tag = record.GetRecordArrayElement(3, 0);
                Assert.That(tag.Header.SchemaId, Is.EqualTo(expectedTagSchemaId));
                Assert.That(tag.Header.Major, Is.EqualTo(1));
                Assert.That(tag.Header.Minor, Is.Zero);
                Assert.That(tag.FieldCount, Is.Zero);
            }
            else
            {
                AssertEmptyArray(tags, 3);
            }

            AssertEmptyArray(record.GetField(3), 4);
            Assert.That(record.GetField(4).Tag, Is.EqualTo(5));
            Assert.That(record.GetField(4).IsRequired, Is.True);
            Assert.That(ReadInt64(record.GetField(4)), Is.EqualTo(_expectedAssignmentOrdinal));
            Assert.That(record.TryGetField(6, out _), Is.False);
        }
    }
}
