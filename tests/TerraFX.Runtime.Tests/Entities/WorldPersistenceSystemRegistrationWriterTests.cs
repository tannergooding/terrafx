// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldPersistenceSystemRegistrationWriterTests
{
    private static SystemDescription CreateDescription(
        Guid systemId,
        ImmutableArray<SystemResourceAccess> access,
        ImmutableArray<Guid> before,
        ImmutableArray<Guid> after)
        => new(
            systemId,
            order: -17,
            SimulationStage.Physics,
            access,
            before,
            after,
            SystemThreadMode.Parallel);

    private static WorldSystemFactoryDescriptor CreateFactory(
        Guid factoryId,
        ContentSchemaReference? stateSchema)
    {
        var sourceState = new WorldSystemSourceStateDeclaration(typeof(SystemRegistrationMarker), []);
        return new WorldSystemFactoryDescriptor(
            factoryId,
            factoryVersion: 4,
            typeof(SystemRegistrationMarker),
            sourceState,
            stateSchema,
            authorType: null,
            []);
    }

    private static byte[] WriteRecord(
        SystemDescription description,
        WorldSystemFactoryDescriptor factory)
    {
        var limits = EbinReadLimits.Foundation;
        Assert.That(
            WorldPersistenceSystemRegistrationWriter.MeasureWorkspace(
                description,
                factory,
                limits,
                out var required,
                out var diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);

        var accessScratch = new SystemResourceAccess[required.AccessCount];
        var accessSlices = new EbinPayloadSlice[required.AccessCount];
        var accessStorage = new byte[required.AccessStorageBytes];
        var accessCandidateStorage = new byte[required.AccessCandidateStorageBytes];
        var accessArrayPayloadStorage = new byte[required.AccessArrayPayloadBytes];
        var afterArrayPayloadStorage = new byte[required.AfterArrayPayloadBytes];
        var beforeArrayPayloadStorage = new byte[required.BeforeArrayPayloadBytes];
        var dependencyScratch = new Guid[required.DependencyScratchCount];
        var rootPayloadStorage = new byte[required.RootPayloadStorageBytes];
        var schemaCandidateStorage = new byte[required.SchemaReferenceRecordBytes];
        var schemaRecordStorage = new byte[required.SchemaReferenceRecordBytes];
        var buffers = new WorldPersistenceSystemRegistrationBuffers {
            AccessScratch = accessScratch,
            AccessSlices = accessSlices,
            AccessStorage = accessStorage,
            AccessCandidateStorage = accessCandidateStorage,
            AccessArrayPayloadStorage = accessArrayPayloadStorage,
            AfterArrayPayloadStorage = afterArrayPayloadStorage,
            BeforeArrayPayloadStorage = beforeArrayPayloadStorage,
            DependencyScratch = dependencyScratch,
            RootPayloadStorage = rootPayloadStorage,
            SchemaCandidateStorage = schemaCandidateStorage,
            SchemaRecordStorage = schemaRecordStorage,
        };

        Assert.That(buffers.GetWorkspaceBytes(), Is.EqualTo(required.WorkspaceBytes));
        Assert.That(
            WorldPersistenceSystemRegistrationWriter.Measure(
                description,
                factory,
                limits,
                ref buffers,
                out var workspace,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(workspace.RecordBytes, Is.GreaterThan(0));
        var candidateStorage = new byte[workspace.RecordBytes];
        var destination = new byte[workspace.RecordBytes];
        buffers.CandidateStorage = candidateStorage;
        buffers.Destination = destination;

        Assert.That(
            WorldPersistenceSystemRegistrationWriter.Write(
                description,
                factory,
                limits,
                workspace,
                ref buffers,
                out var bytesWritten,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(bytesWritten, Is.EqualTo(workspace.RecordBytes));
        return destination.AsSpan(0, bytesWritten).ToArray();
    }

    [Test]
    public void WritesCanonicalSystemRegistrationAndSortsSetsByTheirWireKeys()
    {
        var systemId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var factoryId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var componentHighId = Guid.Parse("40000000-0000-0000-0000-000000000004");
        var componentLowId = Guid.Parse("30000000-0000-0000-0000-000000000003");
        var resourceId = Guid.Parse("50000000-0000-0000-0000-000000000005");
        var beforeLowId = Guid.Parse("60000000-0000-0000-0000-000000000006");
        var beforeHighId = Guid.Parse("70000000-0000-0000-0000-000000000007");
        var afterLowId = Guid.Parse("80000000-0000-0000-0000-000000000008");
        var afterHighId = Guid.Parse("90000000-0000-0000-0000-000000000009");
        var stateSchema = new ContentSchemaReference(
            new ContentSchemaIdentityAssignment("application/system-registration-state-test/v1").SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var access = ImmutableArray.Create(
            new SystemResourceAccess(SystemResourceKind.Resource, resourceId, SystemAccessMode.Read),
            new SystemResourceAccess(SystemResourceKind.Component, componentHighId, SystemAccessMode.Write, new SystemPartitionRange(4, 3)),
            new SystemResourceAccess(SystemResourceKind.Component, componentLowId, SystemAccessMode.Read));
        var description = CreateDescription(
            systemId,
            access,
            [beforeHighId, beforeLowId],
            [afterHighId, afterLowId]);
        var factory = CreateFactory(factoryId, stateSchema);
        var destination = WriteRecord(description, factory);
        var assertions = new SystemRegistrationAssertions(
            description,
            factory,
            [
                new SystemResourceAccess(SystemResourceKind.Component, componentLowId, SystemAccessMode.Read),
                new SystemResourceAccess(SystemResourceKind.Component, componentHighId, SystemAccessMode.Write, new SystemPartitionRange(4, 3)),
                new SystemResourceAccess(SystemResourceKind.Resource, resourceId, SystemAccessMode.Read),
            ],
            [beforeLowId, beforeHighId],
            [afterLowId, afterHighId]);
        Assert.That(
            EbinStructuralReader.Read(
                destination,
                EbinReadLimits.Foundation,
                EbinReadPolicy.Canonical,
                ref assertions,
                out var bytesConsumed,
                out var diagnostic),
            Is.EqualTo(EbinReadStatus.Complete));
        Assert.That(diagnostic, Is.Null);
        Assert.That(bytesConsumed, Is.EqualTo(destination.Length));
    }

    [Test]
    public void WritesEmptyRegistrationArraysAndOmitsStatelessSchema()
    {
        var description = CreateDescription(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            [],
            [],
            []);
        var factory = CreateFactory(
            Guid.Parse("20000000-0000-0000-0000-000000000002"),
            stateSchema: null);
        var destination = WriteRecord(description, factory);
        var assertions = new SystemRegistrationAssertions(
            description,
            factory,
            [],
            [],
            []);
        Assert.That(
            EbinStructuralReader.Read(
                destination,
                EbinReadLimits.Foundation,
                EbinReadPolicy.Canonical,
                ref assertions,
                out var bytesConsumed,
                out var diagnostic),
            Is.EqualTo(EbinReadStatus.Complete));
        Assert.That(diagnostic, Is.Null);
        Assert.That(bytesConsumed, Is.EqualTo(destination.Length));
    }

    private readonly struct AccessAssertions : IAction<EbinRecordReadView>
    {
        private readonly SystemResourceAccess _expected;

        internal AccessAssertions(SystemResourceAccess expected)
        {
            _expected = expected;
        }

        private static uint ReadUInt32(EbinFieldReadView field)
        {
            Assert.That(field.Kind, Is.EqualTo(3));
            Assert.That(field.Payload.Length, Is.EqualTo(sizeof(uint)));
            return BinaryPrimitives.ReadUInt32LittleEndian(field.Payload);
        }

        public readonly void Invoke(scoped EbinRecordReadView record)
        {
            Assert.That(record.Header.SchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment("state/access").SchemaId));
            Assert.That(record.GetField(0).Payload[0], Is.EqualTo((byte)_expected.Kind));
            Assert.That(new Guid(record.GetField(1).Payload, bigEndian: true), Is.EqualTo(_expected.ResourceId));
            Assert.That(record.GetField(2).Payload[0], Is.EqualTo((byte)_expected.Mode));
            Assert.That(record.FieldCount, Is.EqualTo(_expected.Range.HasValue ? 4 : 3));

            if (_expected.Range is SystemPartitionRange expectedRange)
            {
                var range = record.GetRecord(4);
                Assert.That(range.Header.SchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment("state/partition-range").SchemaId));
                Assert.That(ReadUInt32(range.GetField(0)), Is.EqualTo((uint)expectedRange.Start));
                Assert.That(ReadUInt32(range.GetField(1)), Is.EqualTo((uint)expectedRange.Length));
            }
        }
    }

    private readonly struct SystemRegistrationAssertions : IAction<EbinRecordReadView>
    {
        private readonly SystemDescription _description;
        private readonly ImmutableArray<SystemResourceAccess> _expectedAccess;
        private readonly ImmutableArray<Guid> _expectedAfter;
        private readonly ImmutableArray<Guid> _expectedBefore;
        private readonly WorldSystemFactoryDescriptor _factory;

        internal SystemRegistrationAssertions(
            SystemDescription description,
            WorldSystemFactoryDescriptor factory,
            ImmutableArray<SystemResourceAccess> expectedAccess,
            ImmutableArray<Guid> expectedBefore,
            ImmutableArray<Guid> expectedAfter)
        {
            _description = description;
            _factory = factory;
            _expectedAccess = expectedAccess;
            _expectedBefore = expectedBefore;
            _expectedAfter = expectedAfter;
        }

        private static void AssertGuidArray(EbinFieldReadView field, ImmutableArray<Guid> expected)
        {
            Assert.That(field.Kind, Is.EqualTo(23));
            Assert.That(field.Count, Is.EqualTo((ulong)expected.Length));
            Assert.That(field.Payload.Length, Is.EqualTo(checked(32 + (expected.Length * 16))));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(field.Payload[16..]), Is.EqualTo(11));
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(field.Payload[20..]), Is.EqualTo(16));

            for (var index = 0; index < expected.Length; index++)
            {
                Assert.That(
                    new Guid(field.Payload.Slice(32 + (index * 16), 16), bigEndian: true),
                    Is.EqualTo(expected[index]));
            }
        }

        private static void AssertRecordArray(
            scoped ReadOnlySpan<byte> payload,
            ImmutableArray<SystemResourceAccess> expected)
        {
            Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(payload), Is.EqualTo((ulong)expected.Length));
            Assert.That(payload.Length, Is.GreaterThanOrEqualTo(16));
            var startOffset = BinaryPrimitives.ReadUInt64LittleEndian(payload[8..]);
            var tableBytes = checked(8UL + ((checked((ulong)expected.Length) + 1UL) * 8UL));
            Assert.That(startOffset, Is.EqualTo(checked((tableBytes + 15UL) & ~15UL)));
            var previousOffset = startOffset;

            for (var index = 0; index < expected.Length; index++)
            {
                var endOffset = BinaryPrimitives.ReadUInt64LittleEndian(payload.Slice(16 + (index * 8), 8));
                Assert.That(endOffset, Is.GreaterThan(previousOffset));
                var assertions = new AccessAssertions(expected[index]);
                Assert.That(
                    EbinStructuralReader.Read(
                        payload.Slice(checked((int)previousOffset), checked((int)(endOffset - previousOffset))),
                        EbinReadLimits.Foundation,
                        EbinReadPolicy.Canonical,
                        ref assertions,
                        out var bytesConsumed,
                        out var diagnostic),
                    Is.EqualTo(EbinReadStatus.Complete));
                Assert.That(diagnostic, Is.Null);
                Assert.That(bytesConsumed, Is.EqualTo(checked((int)(endOffset - previousOffset))));
                previousOffset = endOffset;
            }

            Assert.That(previousOffset, Is.EqualTo((ulong)payload.Length));
        }

        private static int ReadInt32(EbinFieldReadView field)
        {
            Assert.That(field.Kind, Is.EqualTo(7));
            Assert.That(field.Payload.Length, Is.EqualTo(sizeof(int)));
            return BinaryPrimitives.ReadInt32LittleEndian(field.Payload);
        }

        private static uint ReadUInt32(EbinFieldReadView field)
        {
            Assert.That(field.Kind, Is.EqualTo(3));
            Assert.That(field.Payload.Length, Is.EqualTo(sizeof(uint)));
            return BinaryPrimitives.ReadUInt32LittleEndian(field.Payload);
        }

        public readonly void Invoke(scoped EbinRecordReadView record)
        {
            Assert.That(record.Header.SchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment("state/system-registration").SchemaId));
            Assert.That(record.Header.Major, Is.EqualTo(1));
            Assert.That(record.Header.Minor, Is.Zero);
            Assert.That(record.FieldCount, Is.EqualTo(_factory.StateSchema.HasValue ? 10 : 9));

            var expectedKinds = new ushort[] { 11, 7, 1, 1, 24, 23, 23, 11, 3 };

            for (var index = 0; index < expectedKinds.Length; index++)
            {
                var field = record.GetField(index);
                Assert.That(field.Tag, Is.EqualTo((uint)(index + 1)));
                Assert.That(field.Kind, Is.EqualTo(expectedKinds[index]));
                Assert.That(field.IsRequired, Is.True);
                Assert.That(field.Count, Is.EqualTo(index switch {
                    4 => (ulong)_expectedAccess.Length,
                    5 => (ulong)_expectedBefore.Length,
                    6 => (ulong)_expectedAfter.Length,
                    _ => 1,
                }));
            }

            Assert.That(new Guid(record.GetField(0).Payload, bigEndian: true), Is.EqualTo(_description.SystemId));
            Assert.That(ReadInt32(record.GetField(1)), Is.EqualTo(_description.Order));
            Assert.That(record.GetField(2).Payload[0], Is.EqualTo((byte)_description.Stage));
            Assert.That(record.GetField(3).Payload[0], Is.EqualTo((byte)_description.ThreadMode));
            AssertRecordArray(record.GetField(4).Payload, _expectedAccess);
            AssertGuidArray(record.GetField(5), _expectedBefore);
            AssertGuidArray(record.GetField(6), _expectedAfter);
            Assert.That(new Guid(record.GetField(7).Payload, bigEndian: true), Is.EqualTo(_factory.FactoryId));
            Assert.That(ReadUInt32(record.GetField(8)), Is.EqualTo((uint)_factory.FactoryVersion));

            if (_factory.StateSchema is ContentSchemaReference expectedSchema)
            {
                var schemaField = record.GetField(9);
                Assert.That(schemaField.Tag, Is.EqualTo(10));
                Assert.That(schemaField.Kind, Is.EqualTo(22));
                Assert.That(schemaField.IsRequired, Is.False);
                Assert.That(schemaField.Count, Is.EqualTo(1));
                var schema = record.GetRecord(10);
                Assert.That(schema.Header.SchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment("content/schema-ref").SchemaId));
                Assert.That(new Guid(schema.GetField(0).Payload, bigEndian: true), Is.EqualTo(expectedSchema.SchemaId));
            }
        }
    }

    private sealed class SystemRegistrationMarker
    {
    }
}
