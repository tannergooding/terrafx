// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldPersistenceConfigurationCaptureTests
{
    [Test]
    public void WritesConfigurationFromCommittedOptionsUnderAnAdmissionReservation()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        var schedulingLimits = new WorldSchedulingLimits(
            maximumRegistrationBytes: 4096,
            maximumGraphBytes: 8192,
            maximumWorkPlanBytes: 16_384,
            maximumCommandStagingBytes: 32_768,
            maximumOwnedStateBytes: 65_536,
            maximumDiagnosticBytes: 8192,
            maximumConflictComparisons: 1024);
        var options = new WorldOptions(registration) {
            CpuWorkerCount = 0,
            MaximumActiveSceneInstances = 32,
            MaximumArchetypes = 32,
            MaximumComponentTypes = 64,
            MaximumDeferredCommands = 128,
            MaximumEntityReferencePositions = 1024,
            MaximumEntitySlots = 256,
            MaximumIdentityAssignments = 512,
            MaximumIdentityBranches = 16,
            MaximumLiveEntities = 128,
            MaximumPendingOwnerRequests = 32,
            MaximumPersistenceAdmissionBytes = 16_384,
            MaximumRecordedControls = 256,
            MaximumSceneOccurrences = 64,
            MaximumSnapshotBytes = 8192,
            SchedulingLimits = schedulingLimits,
        };
        using var world = World.Create(options);
        _ = world.Step();

        using var lease = world.AcquirePersistenceCaptureLease();
        Assert.That(
            lease.MeasureConfigurationRecord(out var recordBytes, out var diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete));
        Assert.That(diagnostic, Is.Null);
        var reservationBytes = checked((long)recordBytes * 2);
        Assert.That(lease.TryReserveAdmissionBytes(reservationBytes, out var reservation), Is.True);
        Assert.That(reservation, Is.Not.Null);
        using var reservationScope = reservation!;
        var candidateStorage = new byte[recordBytes];
        var destination = new byte[recordBytes];

        Assert.That(
            lease.WriteConfigurationRecord(
                reservationScope,
                candidateStorage,
                destination,
                out var bytesWritten,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete));
        Assert.That(diagnostic, Is.Null);
        Assert.That(bytesWritten, Is.EqualTo(recordBytes));

        var assertions = new WorldPersistenceConfigurationAssertions(options);
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

    private readonly struct WorldPersistenceConfigurationAssertions : IAction<EbinRecordReadView>
    {
        private readonly WorldOptions _options;

        internal WorldPersistenceConfigurationAssertions(WorldOptions options)
        {
            _options = options;
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

        private static uint ReadUInt32(EbinFieldReadView field)
        {
            Assert.That(field.Kind, Is.EqualTo(3));
            Assert.That(field.Payload.Length, Is.EqualTo(sizeof(uint)));
            return BinaryPrimitives.ReadUInt32LittleEndian(field.Payload);
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
                Is.EqualTo(new ContentSchemaIdentityAssignment("state/configuration").SchemaId));
            Assert.That(record.Header.Major, Is.EqualTo(1));
            Assert.That(record.Header.Minor, Is.Zero);
            Assert.That(record.FieldCount, Is.EqualTo(16));

            for (var index = 0; index < record.FieldCount; index++)
            {
                var field = record.GetField(index);
                Assert.That(field.Tag, Is.EqualTo((uint)(index + 1)));
                Assert.That(field.IsRequired, Is.True);
                Assert.That(field.Count, Is.EqualTo(1));
            }

            Assert.That(ReadUInt32(record.GetField(0)), Is.EqualTo((uint)_options.MaximumLiveEntities));
            Assert.That(ReadUInt32(record.GetField(1)), Is.EqualTo((uint)_options.MaximumEntitySlots));
            Assert.That(ReadUInt32(record.GetField(2)), Is.EqualTo((uint)_options.MaximumArchetypes));
            Assert.That(ReadUInt32(record.GetField(3)), Is.EqualTo((uint)_options.MaximumDeferredCommands));
            Assert.That(ReadUInt32(record.GetField(4)), Is.EqualTo((uint)_options.MaximumComponentTypes));
            Assert.That(ReadUInt32(record.GetField(5)), Is.EqualTo((uint)_options.MaximumPendingOwnerRequests));
            Assert.That(ReadUInt32(record.GetField(6)), Is.EqualTo((uint)_options.MaximumRecordedControls));
            Assert.That(ReadUInt32(record.GetField(7)), Is.EqualTo((uint)_options.MaximumIdentityAssignments));
            Assert.That(record.GetField(8).Kind, Is.EqualTo(22));
            Assert.That(ReadUInt8(record.GetField(9)), Is.Zero);
            Assert.That(ReadUInt16(record.GetField(10)), Is.EqualTo((ushort)_options.MaximumIdentityBranches));
            Assert.That(ReadUInt32(record.GetField(11)), Is.EqualTo((uint)_options.MaximumEntityReferencePositions));
            Assert.That(ReadUInt32(record.GetField(12)), Is.EqualTo((uint)_options.MaximumActiveSceneInstances));
            Assert.That(ReadUInt32(record.GetField(13)), Is.EqualTo((uint)_options.MaximumSceneOccurrences));
            Assert.That(ReadInt64(record.GetField(14)), Is.EqualTo(_options.MaximumSnapshotBytes));
            Assert.That(ReadInt64(record.GetField(15)), Is.EqualTo(_options.MaximumPersistenceAdmissionBytes));

            var scheduling = record.GetRecord(9);
            Assert.That(
                scheduling.Header.SchemaId,
                Is.EqualTo(new ContentSchemaIdentityAssignment("state/scheduling-limits").SchemaId));
            Assert.That(scheduling.FieldCount, Is.EqualTo(7));
            Assert.That(ReadInt64(scheduling.GetField(0)), Is.EqualTo(_options.SchedulingLimits.MaximumRegistrationBytes));
            Assert.That(ReadInt64(scheduling.GetField(1)), Is.EqualTo(_options.SchedulingLimits.MaximumGraphBytes));
            Assert.That(ReadInt64(scheduling.GetField(2)), Is.EqualTo(_options.SchedulingLimits.MaximumWorkPlanBytes));
            Assert.That(ReadInt64(scheduling.GetField(3)), Is.EqualTo(_options.SchedulingLimits.MaximumCommandStagingBytes));
            Assert.That(ReadInt64(scheduling.GetField(4)), Is.EqualTo(_options.SchedulingLimits.MaximumOwnedStateBytes));
            Assert.That(ReadInt64(scheduling.GetField(5)), Is.EqualTo(_options.SchedulingLimits.MaximumDiagnosticBytes));
            Assert.That(ReadUInt32(scheduling.GetField(6)), Is.EqualTo((uint)_options.SchedulingLimits.MaximumConflictComparisons));
        }
    }
}
