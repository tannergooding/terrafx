// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldPersistenceEmptyIdentityHistoryTests
{
    [Test]
    public void WritesRequiredRootBranchAndCanonicalEmptyPagesArray()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        var worldGuid = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var lineageId = Guid.Parse("30000000-0000-0000-0000-000000000003");
        var options = new WorldOptions(registration) {
            CpuWorkerCount = 0,
            InitialReplayLineageId = lineageId,
            MaximumPersistenceAdmissionBytes = 16_384,
            WorldGuid = worldGuid,
        };
        using var world = World.Create(options);
        _ = world.Step();
        var rootBranchId = world.IdentityBranchId;

        using var lease = world.AcquirePersistenceCaptureLease();
        var history = lease.GetIdentityHistoryCapture();
        Assert.That(history.BranchCount, Is.EqualTo(1));
        Assert.That(history.PageCount, Is.Zero);
        Span<EbinPayloadSlice> branchSlices = stackalloc EbinPayloadSlice[1];
        var pageSlices = Span<EbinPayloadSlice>.Empty;
        var pageStableIds = Span<Guid>.Empty;
        var pageMetadata = Span<uint>.Empty;
        var pagePayloadStorage = Span<byte>.Empty;

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
        var reservationBytes = checked(
            (long)MemoryMarshal.AsBytes(branchSlices).Length
            + workspace.BranchStorageBytes
            + MemoryMarshal.AsBytes(pageSlices).Length
            + workspace.PageStorageBytes
            + MemoryMarshal.AsBytes(pageStableIds).Length
            + MemoryMarshal.AsBytes(pageMetadata).Length
            + pagePayloadStorage.Length
            + workspace.ChildCandidateBytes
            + workspace.RootPayloadStorageBytes
            + ((long)workspace.RecordBytes * 2));
        Assert.That(lease.TryReserveAdmissionBytes(reservationBytes, out var reservation), Is.True);
        Assert.That(reservation, Is.Not.Null);
        using var reservationScope = reservation!;
        var branchStorage = new byte[workspace.BranchStorageBytes];
        var pageStorage = new byte[workspace.PageStorageBytes];
        var childCandidateStorage = new byte[workspace.ChildCandidateBytes];
        var rootPayloadStorage = new byte[workspace.RootPayloadStorageBytes];
        var candidateStorage = new byte[workspace.RecordBytes];
        var destination = new byte[workspace.RecordBytes];

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

        var assertions = new EmptyIdentityHistoryAssertions(worldGuid, lineageId, rootBranchId);
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

    private readonly struct EmptyIdentityHistoryAssertions : IAction<EbinRecordReadView>
    {
        private readonly Guid _lineageId;
        private readonly Guid _rootBranchId;
        private readonly Guid _worldGuid;

        internal EmptyIdentityHistoryAssertions(Guid worldGuid, Guid lineageId, Guid rootBranchId)
        {
            _lineageId = lineageId;
            _rootBranchId = rootBranchId;
            _worldGuid = worldGuid;
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

        private static ushort ReadUInt16(EbinFieldReadView field)
        {
            Assert.That(field.Kind, Is.EqualTo(2));
            Assert.That(field.Payload.Length, Is.EqualTo(sizeof(ushort)));
            return BinaryPrimitives.ReadUInt16LittleEndian(field.Payload);
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
            Assert.That(record.FieldCount, Is.EqualTo(7));
            Assert.That(ReadGuid(record.GetField(0)), Is.EqualTo(_worldGuid));
            Assert.That(ReadGuid(record.GetField(1)), Is.EqualTo(_lineageId));
            Assert.That(ReadGuid(record.GetField(2)), Is.EqualTo(_rootBranchId));
            Assert.That(ReadGuid(record.GetField(3)), Is.EqualTo(_worldGuid));
            Assert.That(ReadInt64(record.GetField(4)), Is.EqualTo(1));

            var branches = record.GetField(5);
            Assert.That(branches.Kind, Is.EqualTo(24));
            Assert.That(branches.Count, Is.EqualTo(1));
            var branch = record.GetRecordArrayElement(6, 0);
            Assert.That(branch.GetField(0).Tag, Is.EqualTo(1));
            Assert.That(ReadUInt16(branch.GetField(0)), Is.Zero);
            Assert.That(ReadGuid(branch.GetField(1)), Is.EqualTo(_rootBranchId));
            Assert.That(ReadGuid(branch.GetField(2)), Is.EqualTo(_lineageId));
            Assert.That(ReadGuid(branch.GetField(3)), Is.EqualTo(_worldGuid));
            Assert.That(branch.GetField(4).Tag, Is.EqualTo(6));
            Assert.That(ReadInt64(branch.GetField(4)), Is.Zero);
            Assert.That(branch.GetField(5).Tag, Is.EqualTo(9));
            Assert.That(ReadUInt8(branch.GetField(5)), Is.EqualTo((byte)1));
            Assert.That(branch.GetField(6).Tag, Is.EqualTo(10));
            Assert.That(ReadInt64(branch.GetField(6)), Is.EqualTo(1));

            var pages = record.GetField(6);
            Assert.That(pages.Kind, Is.EqualTo(24));
            Assert.That(pages.Count, Is.Zero);
            Assert.That(pages.Payload.Length, Is.EqualTo(16));
            Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(pages.Payload), Is.Zero);
            Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(pages.Payload[8..]), Is.EqualTo(16));
        }
    }
}
