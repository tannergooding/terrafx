// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldPersistenceIdentityPageCaptureTests
{
    private static void WritePageRecord(
        WorldPersistenceCaptureLease lease,
        in World.IdentityHistoryCapture history,
        WorldOptions options,
        int pageIndex,
        Guid[] expectedIds)
    {
        var stableIds = new Guid[expectedIds.Length];
        var metadata = new uint[expectedIds.Length];
        var expectedMetadata = new uint[expectedIds.Length];
        Array.Fill(expectedMetadata, 0x5400u);
        var payloadStorage = new byte[WorldPersistenceIdentityPageWriter.GetMinimumPayloadWorkspaceBytes(expectedIds.Length)];

        var measureStatus = lease.MeasureIdentityPageRecord(
            in history,
            pageIndex,
            stableIds,
            metadata,
            payloadStorage,
            out var recordBytes,
            out var diagnostic);
        Assert.That(measureStatus, Is.EqualTo(EbinWriteStatus.Complete), diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        var candidateStorage = new byte[recordBytes];
        var destination = new byte[recordBytes];
        var reservationBytes = checked(
            (long)MemoryMarshal.AsBytes(stableIds.AsSpan()).Length
            + MemoryMarshal.AsBytes(metadata.AsSpan()).Length
            + payloadStorage.Length
            + candidateStorage.Length
            + destination.Length);
        Assert.That(lease.TryReserveAdmissionBytes(reservationBytes, out var reservation), Is.True);
        Assert.That(reservation, Is.Not.Null);
        using var reservationScope = reservation!;

        Assert.That(
            lease.WriteIdentityPageRecord(
                reservationScope,
                in history,
                pageIndex,
                stableIds,
                metadata,
                payloadStorage,
                candidateStorage,
                destination,
                out var bytesWritten,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete));
        Assert.That(diagnostic, Is.Null);
        Assert.That(bytesWritten, Is.EqualTo(recordBytes));
        Assert.That(stableIds, Is.EqualTo(expectedIds));
        Assert.That(metadata, Is.EqualTo(expectedMetadata));

        var assertions = new IdentityPageAssertions(
            expectedIds,
            expectedMetadata,
            checked(((long)pageIndex * StableIdentityLedger.RecordsPerPage) + 1));
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

    [Test]
    public void IdentityPageRecordRespectsTheWorldSnapshotByteLimit()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        var options = new WorldOptions(registration) {
            CpuWorkerCount = 0,
            MaximumSnapshotBytes = 64,
        };
        using var world = World.Create(options);
        _ = world.CreateEntity(new EntityDescription());
        _ = world.Step();

        using var lease = world.AcquirePersistenceCaptureLease();
        var history = lease.GetIdentityHistoryCapture();
        Span<Guid> stableIds = stackalloc Guid[1];
        Span<uint> metadata = stackalloc uint[1];
        var payloadStorage = new byte[WorldPersistenceIdentityPageWriter.GetMinimumPayloadWorkspaceBytes(1)];
        Assert.That(
            lease.MeasureIdentityPageRecord(
                in history,
                pageIndex: 0,
                stableIds,
                metadata,
                payloadStorage,
                out var recordBytes,
                out var diagnostic),
            Is.EqualTo(EbinWriteStatus.InvalidRecord));
        Assert.That(recordBytes, Is.Zero);
        Assert.That(diagnostic?.Category, Is.EqualTo("record-byte-budget"));
    }

    [Test]
    public void WritesCanonicalIdentityIdsAndMetadataArrays()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        var options = new WorldOptions(registration) {
            CpuWorkerCount = 0,
            MaximumPersistenceAdmissionBytes = 4096,
        };
        using var world = World.Create(options);
        var firstEntity = world.CreateEntity(new EntityDescription());
        var secondEntity = world.CreateEntity(new EntityDescription());
        var expectedIds = new[] { world.GetEntityId(firstEntity), world.GetEntityId(secondEntity) };
        _ = world.Step();

        using var lease = world.AcquirePersistenceCaptureLease();
        var history = lease.GetIdentityHistoryCapture();
        Assert.That(history.PageCount, Is.EqualTo(1));
        WritePageRecord(lease, in history, options, 0, expectedIds);
    }

    [Test]
    public void WritesFullAndFinalIdentityPagesAtCanonicalBoundary()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        var options = new WorldOptions(registration) {
            CpuWorkerCount = 0,
            MaximumPersistenceAdmissionBytes = 1_000_000,
        };
        using var world = World.Create(options);
        var expectedIds = new Guid[StableIdentityLedger.RecordsPerPage + 1];

        for (var index = 0; index < expectedIds.Length; index++)
        {
            var entity = world.CreateEntity(new EntityDescription());
            expectedIds[index] = world.GetEntityId(entity);
        }

        _ = world.Step();
        using var lease = world.AcquirePersistenceCaptureLease();
        var history = lease.GetIdentityHistoryCapture();
        Assert.That(history.PageCount, Is.EqualTo(2));
        Assert.That(history.GetPageRecordCount(0), Is.EqualTo(StableIdentityLedger.RecordsPerPage));
        Assert.That(history.GetPageRecordCount(1), Is.EqualTo(1));
        WritePageRecord(
            lease,
            in history,
            options,
            0,
            expectedIds.AsSpan(0, StableIdentityLedger.RecordsPerPage).ToArray());
        WritePageRecord(
            lease,
            in history,
            options,
            1,
            expectedIds.AsSpan(StableIdentityLedger.RecordsPerPage, 1).ToArray());
    }

    private readonly struct IdentityPageAssertions : IAction<EbinRecordReadView>
    {
        private readonly long _expectedFirstOrdinal;
        private readonly Guid[] _expectedIds;
        private readonly uint[] _expectedMetadata;

        internal IdentityPageAssertions(Guid[] expectedIds, uint[] expectedMetadata, long expectedFirstOrdinal)
        {
            _expectedFirstOrdinal = expectedFirstOrdinal;
            _expectedIds = expectedIds;
            _expectedMetadata = expectedMetadata;
        }

        private static Guid ReadGuid(EbinFieldReadView field, int index)
        {
            var elementOffset = checked(32 + (index * 16));
            return new Guid(field.Payload.Slice(elementOffset, 16), bigEndian: true);
        }

        private static long ReadInt64(EbinFieldReadView field)
        {
            Assert.That(field.Kind, Is.EqualTo(8));
            Assert.That(field.Count, Is.EqualTo(1));
            Assert.That(field.Payload.Length, Is.EqualTo(sizeof(long)));
            return BinaryPrimitives.ReadInt64LittleEndian(field.Payload);
        }

        private static uint ReadUInt32(EbinFieldReadView field, int index)
        {
            var elementOffset = checked(32 + (index * sizeof(uint)));
            return BinaryPrimitives.ReadUInt32LittleEndian(field.Payload[elementOffset..]);
        }

        public readonly void Invoke(scoped EbinRecordReadView record)
        {
            Assert.That(
                record.Header.SchemaId,
                Is.EqualTo(new ContentSchemaIdentityAssignment("state/identity-page").SchemaId));
            Assert.That(record.Header.Major, Is.EqualTo(1));
            Assert.That(record.Header.Minor, Is.Zero);
            Assert.That(record.FieldCount, Is.EqualTo(3));

            var firstOrdinal = record.GetField(0);
            Assert.That(firstOrdinal.Tag, Is.EqualTo(1));
            Assert.That(firstOrdinal.IsRequired, Is.True);
            Assert.That(ReadInt64(firstOrdinal), Is.EqualTo(_expectedFirstOrdinal));

            var ids = record.GetField(1);
            Assert.That(ids.Tag, Is.EqualTo(2));
            Assert.That(ids.IsRequired, Is.True);
            Assert.That(ids.Kind, Is.EqualTo(23));
            Assert.That(ids.Count, Is.EqualTo((ulong)_expectedIds.Length));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(ids.Payload[16..]), Is.EqualTo(11));
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(ids.Payload[20..]), Is.EqualTo(16));
            Assert.That(ids.Payload.Length, Is.EqualTo(32 + (_expectedIds.Length * 16)));

            var metadata = record.GetField(2);
            Assert.That(metadata.Tag, Is.EqualTo(3));
            Assert.That(metadata.IsRequired, Is.True);
            Assert.That(metadata.Kind, Is.EqualTo(23));
            Assert.That(metadata.Count, Is.EqualTo((ulong)_expectedMetadata.Length));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(metadata.Payload[16..]), Is.EqualTo(3));
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(metadata.Payload[20..]), Is.EqualTo(sizeof(uint)));
            Assert.That(metadata.Payload.Length, Is.EqualTo(32 + (_expectedMetadata.Length * sizeof(uint))));

            for (var index = 0; index < _expectedIds.Length; index++)
            {
                Assert.That(ReadGuid(ids, index), Is.EqualTo(_expectedIds[index]));
                Assert.That(ReadUInt32(metadata, index), Is.EqualTo(_expectedMetadata[index]));
            }
        }
    }
}
