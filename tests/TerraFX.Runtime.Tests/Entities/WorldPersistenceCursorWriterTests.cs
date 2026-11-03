// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldPersistenceCursorWriterTests
{
    [Test]
    public void WritesCommittedControlCursorAndEmptyPendingArraysUnderReservation()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        using var world = World.Create(new WorldOptions(registration) {
            CpuWorkerCount = 0,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        _ = world.Step();
        using var lease = world.AcquirePersistenceCaptureLease();
        Assert.That(
            lease.MeasureCursorsRecord(out var recordBytes, out var diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        var reservationBytes = checked((long)recordBytes * 2);
        Assert.That(lease.TryReserveAdmissionBytes(reservationBytes, out var reservation), Is.True);
        using var reservationScope = reservation!;
        var candidateStorage = new byte[recordBytes];
        var destination = new byte[recordBytes];

        Assert.That(
            lease.WriteCursorsRecord(
                reservationScope,
                candidateStorage,
                destination,
                out var bytesWritten,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(bytesWritten, Is.EqualTo(recordBytes));

        var assertions = new CursorAssertions(lease.NextControlSequence);
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

    private readonly struct CursorAssertions : IAction<EbinRecordReadView>
    {
        private readonly long _nextControlSequence;

        internal CursorAssertions(long nextControlSequence)
        {
            _nextControlSequence = nextControlSequence;
        }

        private static void AssertEmptyRecordArray(EbinFieldReadView field, uint tag)
        {
            Assert.That(field.Tag, Is.EqualTo(tag));
            Assert.That(field.Kind, Is.EqualTo(24));
            Assert.That(field.IsRequired, Is.True);
            Assert.That(field.Count, Is.Zero);
            Assert.That(field.Payload.Length, Is.EqualTo(16));
            Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(field.Payload), Is.Zero);
            Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(field.Payload[8..]), Is.EqualTo(16));
        }

        public readonly void Invoke(scoped EbinRecordReadView record)
        {
            Assert.That(record.Header.SchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment("state/cursors").SchemaId));
            Assert.That(record.Header.Major, Is.EqualTo(1));
            Assert.That(record.Header.Minor, Is.Zero);
            Assert.That(record.FieldCount, Is.EqualTo(4));

            var nextControlSequence = record.GetField(0);
            Assert.That(nextControlSequence.Tag, Is.EqualTo(1));
            Assert.That(nextControlSequence.Kind, Is.EqualTo(8));
            Assert.That(nextControlSequence.IsRequired, Is.True);
            Assert.That(nextControlSequence.Count, Is.EqualTo(1));
            Assert.That(BinaryPrimitives.ReadInt64LittleEndian(nextControlSequence.Payload), Is.EqualTo(_nextControlSequence));

            AssertEmptyRecordArray(record.GetField(1), 4);
            AssertEmptyRecordArray(record.GetField(2), 5);
            AssertEmptyRecordArray(record.GetField(3), 6);
        }
    }
}
