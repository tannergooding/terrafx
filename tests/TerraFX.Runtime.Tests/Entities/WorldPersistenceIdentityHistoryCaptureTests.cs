// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldPersistenceIdentityHistoryCaptureTests
{
    [Test]
    public void WritesCanonicalIdentityDirectoryAndPagesAsOneRecord()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        var worldGuid = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var lineageId = Guid.Parse("30000000-0000-0000-0000-000000000003");
        var options = new WorldOptions(registration) {
            CpuWorkerCount = 0,
            InitialReplayLineageId = lineageId,
            MaximumPersistenceAdmissionBytes = 1_000_000,
            WorldGuid = worldGuid,
        };
        using var world = World.Create(options);
        var firstEntity = world.CreateEntity(new EntityDescription());
        var firstEntityId = world.GetEntityId(firstEntity);
        _ = world.Step();

        var rootBranchId = world.IdentityBranchId;
        var branchCreatedAtNextTick = world.NextTickIndex;
        var branchId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        world.BeginIdentityBranch(branchId);
        var secondEntity = world.CreateEntity(new EntityDescription());
        var secondEntityId = world.GetEntityId(secondEntity);
        _ = world.Step();

        using var lease = world.AcquirePersistenceCaptureLease();
        var history = lease.GetIdentityHistoryCapture();
        var branchSlices = new EbinPayloadSlice[history.BranchCount];
        var pageSlices = new EbinPayloadSlice[history.PageCount];
        var pageStableIds = new Guid[StableIdentityLedger.RecordsPerPage];
        var pageMetadata = new uint[StableIdentityLedger.RecordsPerPage];
        var pagePayloadStorage = new byte[
            WorldPersistenceIdentityPageWriter.GetMinimumPayloadWorkspaceBytes(StableIdentityLedger.RecordsPerPage)];

        Assert.That(
            lease.MeasureIdentityHistoryRecord(
                in history,
                branchSlices,
                pageSlices,
                pageStableIds,
                pageMetadata,
                pagePayloadStorage,
                out var workspace,
                out var diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete));
        Assert.That(diagnostic, Is.Null);
        Assert.That(workspace.BranchCount, Is.EqualTo(2));
        Assert.That(workspace.PageCount, Is.EqualTo(1));
        Assert.That(workspace.RecordBytes > workspace.RootPayloadStorageBytes, Is.True);

        var branchStorageLength = workspace.BranchStorageBytes;
        var pageStorageLength = workspace.PageStorageBytes;
        var childCandidateLength = workspace.ChildCandidateBytes;
        var rootPayloadStorageLength = workspace.RootPayloadStorageBytes;
        var candidateStorageLength = workspace.RecordBytes;
        var destinationLength = workspace.RecordBytes;
        var reservationBytes = checked(
            (long)MemoryMarshal.AsBytes(branchSlices.AsSpan()).Length
            + branchStorageLength
            + MemoryMarshal.AsBytes(pageSlices.AsSpan()).Length
            + pageStorageLength
            + MemoryMarshal.AsBytes(pageStableIds.AsSpan()).Length
            + MemoryMarshal.AsBytes(pageMetadata.AsSpan()).Length
            + pagePayloadStorage.Length
            + childCandidateLength
            + rootPayloadStorageLength
            + candidateStorageLength
            + destinationLength);
        Assert.That(lease.TryReserveAdmissionBytes(reservationBytes, out var reservation), Is.True);
        Assert.That(reservation, Is.Not.Null);
        using var reservationScope = reservation!;
        var branchStorage = new byte[branchStorageLength];
        var pageStorage = new byte[pageStorageLength];
        var childCandidateStorage = new byte[childCandidateLength];
        var rootPayloadStorage = new byte[rootPayloadStorageLength];
        var candidateStorage = new byte[candidateStorageLength];
        var destination = new byte[destinationLength];

        Assert.That(
            lease.WriteIdentityHistoryRecord(
                reservationScope,
                in history,
                branchSlices,
                branchStorage,
                pageSlices,
                pageStorage,
                pageStableIds,
                pageMetadata,
                pagePayloadStorage,
                childCandidateStorage,
                rootPayloadStorage,
                candidateStorage,
                destination,
                out var bytesWritten,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete));
        Assert.That(diagnostic, Is.Null);
        Assert.That(bytesWritten, Is.EqualTo(workspace.RecordBytes));

        var assertions = new IdentityHistoryAssertions(
            worldGuid,
            lineageId,
            branchId,
            branchId,
            rootBranchId,
            firstEntityId,
            secondEntityId,
            branchCreatedAtNextTick);
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

    private readonly struct IdentityHistoryAssertions : IAction<EbinRecordReadView>
    {
        private readonly Guid _activeBranchId;
        private readonly long _branchCreatedAtNextTick;
        private readonly Guid _firstEntityId;
        private readonly Guid _lineageId;
        private readonly Guid _namespaceId;
        private readonly Guid _rootBranchId;
        private readonly Guid _secondEntityId;
        private readonly Guid _worldGuid;

        internal IdentityHistoryAssertions(
            Guid worldGuid,
            Guid lineageId,
            Guid activeBranchId,
            Guid namespaceId,
            Guid rootBranchId,
            Guid firstEntityId,
            Guid secondEntityId,
            long branchCreatedAtNextTick)
        {
            _activeBranchId = activeBranchId;
            _firstEntityId = firstEntityId;
            _lineageId = lineageId;
            _namespaceId = namespaceId;
            _rootBranchId = rootBranchId;
            _secondEntityId = secondEntityId;
            _branchCreatedAtNextTick = branchCreatedAtNextTick;
            _worldGuid = worldGuid;
        }

        private static void AssertBranchRecord(
            scoped EbinRecordReadView record,
            int directoryIndex,
            Guid branchId,
            Guid lineageId,
            Guid namespaceId,
            Guid? parentBranchId,
            long createdAtNextTick,
            byte kind,
            long firstAssignmentOrdinal)
        {
            Assert.That(
                record.Header.SchemaId,
                Is.EqualTo(new ContentSchemaIdentityAssignment("state/identity-branch").SchemaId));
            Assert.That(record.Header.Major, Is.EqualTo(1));
            Assert.That(record.Header.Minor, Is.Zero);
            Assert.That(record.FieldCount, Is.EqualTo(parentBranchId.HasValue ? 8 : 7));
            Assert.That(ReadUInt16(record.GetField(0)), Is.EqualTo((ushort)directoryIndex));
            Assert.That(ReadGuid(record.GetField(1)), Is.EqualTo(branchId));
            Assert.That(ReadGuid(record.GetField(2)), Is.EqualTo(lineageId));
            Assert.That(ReadGuid(record.GetField(3)), Is.EqualTo(namespaceId));
            var nextField = 4;

            if (parentBranchId is Guid parentId)
            {
                var parentField = record.GetField(nextField++);
                Assert.That(parentField.Tag, Is.EqualTo(5));
                Assert.That(parentField.IsRequired, Is.False);
                Assert.That(ReadGuid(parentField), Is.EqualTo(parentId));
            }

            Assert.That(record.GetField(nextField).Tag, Is.EqualTo(6));
            Assert.That(ReadInt64(record.GetField(nextField++)), Is.EqualTo(createdAtNextTick));
            Assert.That(record.GetField(nextField).Tag, Is.EqualTo(9));
            Assert.That(ReadUInt8(record.GetField(nextField++)), Is.EqualTo(kind));
            Assert.That(record.GetField(nextField).Tag, Is.EqualTo(10));
            Assert.That(ReadInt64(record.GetField(nextField)), Is.EqualTo(firstAssignmentOrdinal));
        }

        private static void AssertIdentityPage(
            scoped EbinRecordReadView record,
            Guid firstEntityId,
            Guid secondEntityId)
        {
            Assert.That(
                record.Header.SchemaId,
                Is.EqualTo(new ContentSchemaIdentityAssignment("state/identity-page").SchemaId));
            Assert.That(record.Header.Major, Is.EqualTo(1));
            Assert.That(record.Header.Minor, Is.Zero);
            Assert.That(record.FieldCount, Is.EqualTo(3));
            Assert.That(ReadInt64(record.GetField(0)), Is.EqualTo(1));

            var ids = record.GetField(1);
            Assert.That(ids.Kind, Is.EqualTo(23));
            Assert.That(ids.Count, Is.EqualTo(2));
            Assert.That(ReadGuid(ids, 0), Is.EqualTo(firstEntityId));
            Assert.That(ReadGuid(ids, 1), Is.EqualTo(secondEntityId));

            var metadata = record.GetField(2);
            Assert.That(metadata.Kind, Is.EqualTo(23));
            Assert.That(ReadUInt32(metadata, 0), Is.EqualTo(0x5400u));
            Assert.That(ReadUInt32(metadata, 1), Is.EqualTo(0x5401u));
        }

        private static Guid ReadGuid(EbinFieldReadView field)
        {
            Assert.That(field.Kind, Is.EqualTo(11));
            Assert.That(field.Payload.Length, Is.EqualTo(16));
            return new Guid(field.Payload, bigEndian: true);
        }

        private static Guid ReadGuid(EbinFieldReadView field, int index)
        {
            var offset = checked(32 + (index * 16));
            return new Guid(field.Payload.Slice(offset, 16), bigEndian: true);
        }

        private static long ReadInt64(EbinFieldReadView field)
        {
            Assert.That(field.Kind, Is.EqualTo(8));
            Assert.That(field.Payload.Length, Is.EqualTo(sizeof(long)));
            return BinaryPrimitives.ReadInt64LittleEndian(field.Payload);
        }

        private static ushort ReadUInt16(EbinFieldReadView field)
        {
            Assert.That(field.Kind, Is.EqualTo(2));
            Assert.That(field.Payload.Length, Is.EqualTo(sizeof(ushort)));
            return BinaryPrimitives.ReadUInt16LittleEndian(field.Payload);
        }

        private static uint ReadUInt32(EbinFieldReadView field, int index)
        {
            var offset = checked(32 + (index * sizeof(uint)));
            return BinaryPrimitives.ReadUInt32LittleEndian(field.Payload[offset..]);
        }

        private static byte ReadUInt8(EbinFieldReadView field)
        {
            Assert.That(field.Kind, Is.EqualTo(1));
            Assert.That(field.Payload.Length, Is.EqualTo(sizeof(byte)));
            return field.Payload[0];
        }

        public readonly void Invoke(scoped EbinRecordReadView record)
        {
            Assert.That(
                record.Header.SchemaId,
                Is.EqualTo(new ContentSchemaIdentityAssignment("state/identity-history").SchemaId));
            Assert.That(record.Header.Major, Is.EqualTo(1));
            Assert.That(record.Header.Minor, Is.Zero);
            Assert.That(record.FieldCount, Is.EqualTo(7));
            Assert.That(ReadGuid(record.GetField(0)), Is.EqualTo(_worldGuid));
            Assert.That(ReadGuid(record.GetField(1)), Is.EqualTo(_lineageId));
            Assert.That(ReadGuid(record.GetField(2)), Is.EqualTo(_activeBranchId));
            Assert.That(ReadGuid(record.GetField(3)), Is.EqualTo(_namespaceId));
            Assert.That(ReadInt64(record.GetField(4)), Is.EqualTo(3));
            Assert.That(record.GetField(5).Kind, Is.EqualTo(24));
            Assert.That(record.GetField(5).Count, Is.EqualTo(2));
            Assert.That(record.GetField(6).Kind, Is.EqualTo(24));
            Assert.That(record.GetField(6).Count, Is.EqualTo(1));

            AssertBranchRecord(
                record.GetRecordArrayElement(6, 0),
                directoryIndex: 0,
                _rootBranchId,
                _lineageId,
                _worldGuid,
                parentBranchId: null,
                createdAtNextTick: 0,
                kind: 1,
                firstAssignmentOrdinal: 1);
            AssertBranchRecord(
                record.GetRecordArrayElement(6, 1),
                directoryIndex: 1,
                _activeBranchId,
                _lineageId,
                _namespaceId,
                parentBranchId: _rootBranchId,
                createdAtNextTick: _branchCreatedAtNextTick,
                kind: 2,
                firstAssignmentOrdinal: 2);
            AssertIdentityPage(record.GetRecordArrayElement(7, 0), _firstEntityId, _secondEntityId);
        }
    }
}
