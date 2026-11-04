// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Threading;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldPersistenceCursorWriterTests
{
    [TestCase(SimulationProfile.Standard)]
    [TestCase(SimulationProfile.StrictLocal)]
    public void WritesCommittedControlCursorAndEmptyPendingArraysUnderReservation(SimulationProfile profile)
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        using var world = World.Create(new WorldOptions(registration) {
            CpuWorkerCount = 0,
            InitialReplayLineageId = Guid.NewGuid(),
            Profile = profile,
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

        var assertions = new CursorAssertions(
            lease.Profile,
            lease.NextControlSequence,
            lease.NextOwnerRequestSequence,
            lease.NextBarrierEpoch,
            lease.AcceptedOwnerRequests);
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
    public void WritesStrictLocalOwnerCursorsAfterACommittedAdmission()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        using var world = World.Create(new WorldOptions(registration) {
            CpuWorkerCount = 0,
            InitialReplayLineageId = Guid.NewGuid(),
            Profile = SimulationProfile.StrictLocal,
            WorldGuid = Guid.NewGuid(),
        });
        _ = world.Step();
        var request = WorldOwnerControlRequest.CreateTimeScaleChange(0.5f, CancellationToken.None);
        Assert.That(world.TryAdmitOwnerControl(request, out _), Is.EqualTo(WorldOwnerControlAdmissionStatus.Accepted));
        world.PumpControlBoundary();
        Assert.That(request.TimeScaleCompletion.IsCompletedSuccessfully, Is.True);
        _ = world.Step();

        using var lease = world.AcquirePersistenceCaptureLease();
        Assert.That(lease.AcceptedOwnerRequests, Is.EqualTo(1));
        Assert.That(lease.NextOwnerRequestSequence, Is.EqualTo(2));
        Assert.That(lease.NextBarrierEpoch, Is.EqualTo(lease.NextTickIndex + 1));
        Assert.That(
            WorldPersistenceCursorWriter.Measure(
                lease,
                EbinReadLimits.Foundation,
                out var recordBytes,
                out var measureDiagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            measureDiagnostic?.Category);
        var candidateStorage = new byte[recordBytes];
        var destination = new byte[recordBytes];
        Assert.That(
            WorldPersistenceCursorWriter.Write(
                lease,
                EbinReadLimits.Foundation,
                candidateStorage,
                destination,
                out var bytesWritten,
                out var writeDiagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            writeDiagnostic?.Category);
        var assertions = new CursorAssertions(
            lease.Profile,
            lease.NextControlSequence,
            lease.NextOwnerRequestSequence,
            lease.NextBarrierEpoch,
            lease.AcceptedOwnerRequests);
        Assert.That(
            EbinStructuralReader.Read(
                destination.AsSpan(0, bytesWritten),
                EbinReadLimits.Foundation,
                EbinReadPolicy.Canonical,
                ref assertions,
                out var bytesConsumed,
                out var readDiagnostic),
            Is.EqualTo(EbinReadStatus.Complete),
            readDiagnostic?.Category);
        Assert.That(bytesConsumed, Is.EqualTo(bytesWritten));
    }

    private readonly struct CursorAssertions : IAction<EbinRecordReadView>
    {
        private readonly long _acceptedOwnerRequests;
        private readonly long _nextBarrierEpoch;
        private readonly long _nextControlSequence;
        private readonly long _nextOwnerRequestSequence;
        private readonly SimulationProfile _profile;

        internal CursorAssertions(
            SimulationProfile profile,
            long nextControlSequence,
            long nextOwnerRequestSequence,
            long nextBarrierEpoch,
            long acceptedOwnerRequests)
        {
            _acceptedOwnerRequests = acceptedOwnerRequests;
            _nextBarrierEpoch = nextBarrierEpoch;
            _nextControlSequence = nextControlSequence;
            _nextOwnerRequestSequence = nextOwnerRequestSequence;
            _profile = profile;
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

        private static void AssertOptionalInt64Field(EbinFieldReadView field, uint tag, long expectedValue)
        {
            Assert.That(field.Tag, Is.EqualTo(tag));
            Assert.That(field.Kind, Is.EqualTo(8));
            Assert.That(field.IsRequired, Is.False);
            Assert.That(field.Count, Is.EqualTo(1));
            Assert.That(field.Payload.Length, Is.EqualTo(sizeof(long)));
            Assert.That(BinaryPrimitives.ReadInt64LittleEndian(field.Payload), Is.EqualTo(expectedValue));
        }

        public readonly void Invoke(scoped EbinRecordReadView record)
        {
            Assert.That(record.Header.SchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment("state/cursors").SchemaId));
            Assert.That(record.Header.Major, Is.EqualTo(1));
            Assert.That(record.Header.Minor, Is.Zero);
            var isStrictLocal = _profile is SimulationProfile.StrictLocal;
            Assert.That(record.FieldCount, Is.EqualTo(isStrictLocal ? 7 : 4));

            var fieldIndex = 0;
            var nextControlSequence = record.GetField(fieldIndex++);
            Assert.That(nextControlSequence.Tag, Is.EqualTo(1));
            Assert.That(nextControlSequence.Kind, Is.EqualTo(8));
            Assert.That(nextControlSequence.IsRequired, Is.True);
            Assert.That(nextControlSequence.Count, Is.EqualTo(1));
            Assert.That(BinaryPrimitives.ReadInt64LittleEndian(nextControlSequence.Payload), Is.EqualTo(_nextControlSequence));

            if (isStrictLocal)
            {
                AssertOptionalInt64Field(record.GetField(fieldIndex++), 2, _nextOwnerRequestSequence);
                AssertOptionalInt64Field(record.GetField(fieldIndex++), 3, _nextBarrierEpoch);
            }

            AssertEmptyRecordArray(record.GetField(fieldIndex++), 4);
            AssertEmptyRecordArray(record.GetField(fieldIndex++), 5);
            AssertEmptyRecordArray(record.GetField(fieldIndex++), 6);

            if (isStrictLocal)
            {
                AssertOptionalInt64Field(record.GetField(fieldIndex), 7, _acceptedOwnerRequests);
            }
        }
    }
}
