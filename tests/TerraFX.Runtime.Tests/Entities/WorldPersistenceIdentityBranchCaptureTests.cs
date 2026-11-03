// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldPersistenceIdentityBranchCaptureTests
{
    private static void AssertBranchRecord(
        WorldPersistenceCaptureLease lease,
        in World.IdentityBranchCaptureEnumerator branch,
        IdentityBranchAssertions assertions)
    {
        Assert.That(lease.MeasureIdentityBranchRecord(in branch, out var recordBytes, out var diagnostic), Is.EqualTo(EbinWriteStatus.Complete));
        Assert.That(diagnostic, Is.Null);
        var reservationBytes = checked((long)recordBytes * 2);
        Assert.That(lease.TryReserveAdmissionBytes(reservationBytes, out var reservation), Is.True);
        Assert.That(reservation, Is.Not.Null);
        using var reservationScope = reservation!;
        var candidateStorage = new byte[recordBytes];
        var destination = new byte[recordBytes];

        Assert.That(
            lease.WriteIdentityBranchRecord(
                reservationScope,
                in branch,
                candidateStorage,
                destination,
                out var bytesWritten,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete));
        Assert.That(diagnostic, Is.Null);
        Assert.That(bytesWritten, Is.EqualTo(recordBytes));
        Assert.That(
            EbinStructuralReader.Read(
                destination.AsSpan(0, bytesWritten),
                EbinReadLimits.Foundation,
                EbinReadPolicy.Canonical,
                ref assertions,
                out var bytesConsumed,
                out var readDiagnostic),
            Is.EqualTo(EbinReadStatus.Complete));
        Assert.That(readDiagnostic, Is.Null);
        Assert.That(bytesConsumed, Is.EqualTo(bytesWritten));
    }

    [Test]
    public void WritesRootAndOrdinaryIdentityBranchRecordsCanonically()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        var worldGuid = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var lineageId = Guid.Parse("30000000-0000-0000-0000-000000000003");
        var options = new WorldOptions(registration) {
            CpuWorkerCount = 0,
            InitialReplayLineageId = lineageId,
            MaximumPersistenceAdmissionBytes = 4096,
            WorldGuid = worldGuid,
        };
        using var world = World.Create(options);
        _ = world.Step();

        var branchId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var rootBranchId = world.IdentityBranchId;
        world.BeginIdentityBranch(branchId);
        _ = world.Step();

        using var lease = world.AcquirePersistenceCaptureLease();
        var history = lease.GetIdentityHistoryCapture();
        var branches = history.GetBranchEnumerator();
        var expectedBranchCount = 0;

        while (branches.MoveNext())
        {
            var assertions = expectedBranchCount switch {
                0 => new IdentityBranchAssertions(0, rootBranchId, lineageId, worldGuid, null, 0, 1, 1),
                1 => new IdentityBranchAssertions(1, branchId, lineageId, branchId, rootBranchId, 1, 2, 1),
                _ => throw new InvalidOperationException("The identity history contains more than the expected two branches."),
            };
            AssertBranchRecord(lease, in branches, assertions);
            expectedBranchCount++;
        }

        Assert.That(expectedBranchCount, Is.EqualTo(2));
    }

    private readonly struct IdentityBranchAssertions : IAction<EbinRecordReadView>
    {
        private readonly Guid _branchId;
        private readonly long _createdAtNextTick;
        private readonly int _directoryIndex;
        private readonly long _firstAssignmentOrdinal;
        private readonly byte _kind;
        private readonly Guid _lineageId;
        private readonly Guid? _parentBranchId;
        private readonly Guid _namespaceId;

        internal IdentityBranchAssertions(
            int directoryIndex,
            Guid branchId,
            Guid lineageId,
            Guid namespaceId,
            Guid? parentBranchId,
            long createdAtNextTick,
            byte kind,
            uint firstAssignmentOrdinal)
        {
            _directoryIndex = directoryIndex;
            _branchId = branchId;
            _lineageId = lineageId;
            _namespaceId = namespaceId;
            _parentBranchId = parentBranchId;
            _createdAtNextTick = createdAtNextTick;
            _kind = kind;
            _firstAssignmentOrdinal = firstAssignmentOrdinal;
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
                Is.EqualTo(new ContentSchemaIdentityAssignment("state/identity-branch").SchemaId));
            Assert.That(record.Header.Major, Is.EqualTo(1));
            Assert.That(record.Header.Minor, Is.Zero);
            Assert.That(record.FieldCount, Is.EqualTo(_parentBranchId.HasValue ? 8 : 7));
            Assert.That(record.GetField(0).Tag, Is.EqualTo(1));
            Assert.That(record.GetField(0).IsRequired, Is.True);
            Assert.That(ReadUInt16(record.GetField(0)), Is.EqualTo((ushort)_directoryIndex));
            Assert.That(record.GetField(1).Tag, Is.EqualTo(2));
            Assert.That(record.GetField(1).IsRequired, Is.True);
            Assert.That(ReadGuid(record.GetField(1)), Is.EqualTo(_branchId));
            Assert.That(record.GetField(2).Tag, Is.EqualTo(3));
            Assert.That(record.GetField(2).IsRequired, Is.True);
            Assert.That(ReadGuid(record.GetField(2)), Is.EqualTo(_lineageId));
            Assert.That(record.GetField(3).Tag, Is.EqualTo(4));
            Assert.That(record.GetField(3).IsRequired, Is.True);
            Assert.That(ReadGuid(record.GetField(3)), Is.EqualTo(_namespaceId));
            var nextField = 4;

            if (_parentBranchId is Guid parentBranchId)
            {
                var parentField = record.GetField(nextField++);
                Assert.That(parentField.Tag, Is.EqualTo(5));
                Assert.That(parentField.IsRequired, Is.False);
                Assert.That(ReadGuid(parentField), Is.EqualTo(parentBranchId));
            }

            Assert.That(record.GetField(nextField).Tag, Is.EqualTo(6));
            Assert.That(record.GetField(nextField).IsRequired, Is.True);
            Assert.That(ReadInt64(record.GetField(nextField++)), Is.EqualTo(_createdAtNextTick));
            Assert.That(record.GetField(nextField).Tag, Is.EqualTo(9));
            Assert.That(record.GetField(nextField).IsRequired, Is.True);
            Assert.That(ReadUInt8(record.GetField(nextField++)), Is.EqualTo(_kind));
            Assert.That(record.GetField(nextField).Tag, Is.EqualTo(10));
            Assert.That(record.GetField(nextField).IsRequired, Is.True);
            Assert.That(ReadInt64(record.GetField(nextField)), Is.EqualTo(_firstAssignmentOrdinal));
        }
    }
}
