// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;
using TerraFX.Numerics;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldPersistenceControlRecordWriterTests
{
    private static World CreateWorld()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        return World.Create(new WorldOptions(registration) {
            CpuWorkerCount = 0,
            Enable2DFrame = true,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
    }

    private static void PopulateControls(World world)
    {
        world.SetTimeScale(2.0f);

        var frame3 = world.CurrentFrame3;
        var updatedFrame3 = world.CreateNextFrame3(
            new SpatialPosition3(new ChunkIndex3(1, -2, 3), Vector3.Zero),
            frame3.Minimum,
            frame3.Maximum);
        Assert.That(world.SetFrame(updatedFrame3).Status, Is.EqualTo(FrameChangeStatus.Applied));

        var frame2 = world.CurrentFrame2
            ?? throw new AssertionException("The test World did not admit its 2D frame.");
        var updatedFrame2 = world.CreateNextFrame2(
            new SpatialPosition2(new ChunkIndex2(-4, 5), Vector2.Zero),
            frame2.Minimum,
            frame2.Maximum);
        Assert.That(world.SetFrame(updatedFrame2).Status, Is.EqualTo(FrameChangeStatus.Applied));

        world.BeginIdentityBranch(Guid.NewGuid());
        _ = world.Step();
    }

    [Test]
    public void RejectsExpiredCaptureLeaseBeforeMeasuringOrWriting()
    {
        using var world = CreateWorld();
        world.SetTimeScale(2.0f);
        _ = world.Step();
        var lease = world.AcquirePersistenceCaptureLease();
        var staleLease = lease;
        var control = lease.GetControlCapture(0);
        var limits = WorldPersistenceEbinLimits.Create(world.Options);
        lease.Dispose();

        var measureRejected = false;

        try
        {
            _ = WorldPersistenceControlRecordWriter.Measure(
                staleLease,
                control,
                limits,
                out _,
                out _);
        }
        catch (InvalidOperationException)
        {
            measureRejected = true;
        }

        Assert.That(measureRejected, Is.True);

        var writeRejected = false;

        try
        {
            _ = WorldPersistenceControlRecordWriter.Write(
                staleLease,
                control,
                default,
                limits,
                [],
                [],
                [],
                out _,
                out _);
        }
        catch (InvalidOperationException)
        {
            writeRejected = true;
        }

        Assert.That(writeRejected, Is.True);

        var arrayMeasureRejected = false;

        try
        {
            _ = WorldPersistenceControlRecordArrayWriter.Measure(
                staleLease,
                limits,
                new EbinPayloadSlice[1],
                out _,
                out _);
        }
        catch (InvalidOperationException)
        {
            arrayMeasureRejected = true;
        }

        Assert.That(arrayMeasureRejected, Is.True);
    }

    [Test]
    public void WritesCanonicalEmptyControlArray()
    {
        using var world = CreateWorld();
        _ = world.Step();
        using var lease = world.AcquirePersistenceCaptureLease();
        var limits = WorldPersistenceEbinLimits.Create(world.Options);

        Assert.That(
            WorldPersistenceControlRecordArrayWriter.Measure(
                lease,
                limits,
                [],
                out var workspace,
                out var measureDiagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            measureDiagnostic?.ToString());
        Assert.That(measureDiagnostic, Is.Null);
        Assert.That(workspace.ControlRecordCount, Is.Zero);
        Assert.That(workspace.RecordStorageBytes, Is.Zero);

        var arrayPayload = new byte[workspace.ArrayPayloadBytes];
        Assert.That(
            WorldPersistenceControlRecordArrayWriter.Write(
                lease,
                limits,
                workspace,
                [],
                [],
                [],
                [],
                arrayPayload,
                out var payloadBytes,
                out var writeDiagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            writeDiagnostic?.ToString());
        Assert.That(writeDiagnostic, Is.Null);
        Assert.That(payloadBytes, Is.EqualTo(16));
        Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(arrayPayload), Is.Zero);
        Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(arrayPayload.AsSpan(8)), Is.EqualTo(16));
    }

    [Test]
    public void WritesCapturedControlArrayInSequenceOrder()
    {
        using var world = CreateWorld();
        PopulateControls(world);
        using var lease = world.AcquirePersistenceCaptureLease();
        var limits = WorldPersistenceEbinLimits.Create(world.Options);
        var recordSlices = new EbinPayloadSlice[lease.ControlRecordCount];
        var measureStatus = WorldPersistenceControlRecordArrayWriter.Measure(
            lease,
            limits,
            recordSlices,
            out var workspace,
            out var measureDiagnostic);
        Assert.That(measureStatus, Is.EqualTo(EbinWriteStatus.Complete), measureDiagnostic?.ToString());
        Assert.That(measureDiagnostic, Is.Null);
        Assert.That(workspace.ControlRecordCount, Is.EqualTo(4));

        var recordStorage = new byte[workspace.RecordStorageBytes];
        var candidateRecordStorage = new byte[workspace.MaximumCandidateRecordBytes];
        var rootPayloadStorage = new byte[workspace.MaximumRootPayloadBytes];
        var arrayPayload = new byte[workspace.ArrayPayloadBytes];
        var writeStatus = WorldPersistenceControlRecordArrayWriter.Write(
            lease,
            limits,
            workspace,
            recordSlices,
            recordStorage,
            candidateRecordStorage,
            rootPayloadStorage,
            arrayPayload,
            out var payloadBytes,
            out var writeDiagnostic);
        Assert.That(writeStatus, Is.EqualTo(EbinWriteStatus.Complete), writeDiagnostic?.ToString());
        Assert.That(writeDiagnostic, Is.Null);
        Assert.That(payloadBytes, Is.EqualTo(workspace.ArrayPayloadBytes));
        Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(arrayPayload), Is.EqualTo(4));

        var previousOffset = BinaryPrimitives.ReadUInt64LittleEndian(arrayPayload.AsSpan(8));

        for (var index = 0; index < lease.ControlRecordCount; index++)
        {
            var endOffset = BinaryPrimitives.ReadUInt64LittleEndian(arrayPayload.AsSpan(16 + (index * 8)));
            var encodedRecord = arrayPayload.AsSpan(
                checked((int)previousOffset),
                checked((int)(endOffset - previousOffset)));
            var recordSlice = recordSlices[index];
            Assert.That(
                encodedRecord.SequenceEqual(recordStorage.AsSpan(recordSlice.Offset, recordSlice.ByteLength)),
                Is.True);

            var control = lease.GetControlCapture(index);
            Assert.That(control.Sequence, Is.EqualTo(index + 1));
            var assertions = new ControlRecordAssertions(control, lease.WorldGuid, limits);
            Assert.That(
                EbinStructuralReader.Read(
                    encodedRecord,
                    limits,
                    EbinReadPolicy.Canonical,
                    ref assertions,
                    out var bytesConsumed,
                    out var readDiagnostic),
                Is.EqualTo(EbinReadStatus.Complete),
                readDiagnostic?.ToString());
            Assert.That(readDiagnostic, Is.Null);
            Assert.That(bytesConsumed, Is.EqualTo(encodedRecord.Length));
            previousOffset = endOffset;
        }

        Assert.That(previousOffset, Is.EqualTo(checked((ulong)payloadBytes)));
    }

    [Test]
    public void WritesEveryCapturedControlKindAsCanonicalRecord()
    {
        using var world = CreateWorld();
        PopulateControls(world);

        using var lease = world.AcquirePersistenceCaptureLease();
        Assert.That(lease.ControlRecordCount, Is.EqualTo(4));
        var limits = WorldPersistenceEbinLimits.Create(world.Options);
        WorldPersistenceControlKind[] expectedKinds = [
            WorldPersistenceControlKind.TimeScale,
            WorldPersistenceControlKind.Frame3,
            WorldPersistenceControlKind.Frame2,
            WorldPersistenceControlKind.Branch,
        ];

        for (var index = 0; index < lease.ControlRecordCount; index++)
        {
            var control = lease.GetControlCapture(index);
            Assert.That(control.Kind, Is.EqualTo(expectedKinds[index]));

            var measureStatus = WorldPersistenceControlRecordWriter.Measure(
                lease,
                control,
                limits,
                out var workspace,
                out var measureDiagnostic);
            Assert.That(measureStatus, Is.EqualTo(EbinWriteStatus.Complete), measureDiagnostic?.ToString());
            Assert.That(measureDiagnostic, Is.Null);

            var rootPayloadStorage = new byte[workspace.RootPayloadBytes];
            var candidateStorage = new byte[workspace.RecordBytes];
            var destination = new byte[workspace.RecordBytes];
            var writeStatus = WorldPersistenceControlRecordWriter.Write(
                lease,
                control,
                workspace,
                limits,
                rootPayloadStorage,
                candidateStorage,
                destination,
                out var bytesWritten,
                out var writeDiagnostic);
            Assert.That(writeStatus, Is.EqualTo(EbinWriteStatus.Complete), writeDiagnostic?.ToString());
            Assert.That(writeDiagnostic, Is.Null);
            Assert.That(bytesWritten, Is.EqualTo(workspace.RecordBytes));

            var assertions = new ControlRecordAssertions(control, lease.WorldGuid, limits);
            var readStatus = EbinStructuralReader.Read(
                destination,
                limits,
                EbinReadPolicy.Canonical,
                ref assertions,
                out var bytesConsumed,
                out var readDiagnostic);
            Assert.That(readStatus, Is.EqualTo(EbinReadStatus.Complete), readDiagnostic?.ToString());
            Assert.That(readDiagnostic, Is.Null);
            Assert.That(bytesConsumed, Is.EqualTo(bytesWritten));
        }
    }

    private readonly struct ControlRecordAssertions : IAction<EbinRecordReadView>
    {
        private readonly WorldPersistenceControlCaptureRow _control;
        private readonly EbinReadLimits _limits;
        private readonly Guid _worldGuid;

        internal ControlRecordAssertions(
            WorldPersistenceControlCaptureRow control,
            Guid worldGuid,
            EbinReadLimits limits)
        {
            _control = control;
            _limits = limits;
            _worldGuid = worldGuid;
        }

        public readonly void Invoke(scoped EbinRecordReadView record)
        {
            Assert.That(record.Header.SchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment("state/control").SchemaId));
            Assert.That(record.Header.Major, Is.EqualTo(1));
            Assert.That(record.Header.Minor, Is.Zero);
            Assert.That(record.FieldCount, Is.EqualTo(3));
            Assert.That(record.GetField(0).Tag, Is.EqualTo(1U));
            Assert.That(record.GetField(0).Kind, Is.EqualTo((ushort)8));
            Assert.That(BinaryPrimitives.ReadInt64LittleEndian(record.GetField(0).Payload), Is.EqualTo(_control.Sequence));
            Assert.That(record.GetField(1).Tag, Is.EqualTo(2U));
            Assert.That(record.GetField(1).Kind, Is.EqualTo((ushort)8));
            Assert.That(BinaryPrimitives.ReadInt64LittleEndian(record.GetField(1).Payload), Is.EqualTo(_control.AppliedAtNextTick));

            var payloadField = record.GetField(2);
            Assert.That(payloadField.Tag, Is.EqualTo(3U));
            Assert.That(payloadField.Kind, Is.EqualTo((ushort)22));

            var assertions = new PayloadRecordAssertions(_control, _worldGuid, _limits);
            var status = EbinStructuralReader.Read(
                payloadField.Payload,
                _limits,
                EbinReadPolicy.Canonical,
                ref assertions,
                out var bytesConsumed,
                out var diagnostic);
            Assert.That(status, Is.EqualTo(EbinReadStatus.Complete), diagnostic?.ToString());
            Assert.That(diagnostic, Is.Null);
            Assert.That(bytesConsumed, Is.EqualTo(payloadField.Payload.Length));
        }
    }

    private readonly struct FrameRecordAssertions : IAction<EbinRecordReadView>
    {
        private readonly SpatialFrame2? _frame2;
        private readonly SpatialFrame3? _frame3;
        private readonly Guid _worldGuid;

        internal FrameRecordAssertions(Guid worldGuid, SpatialFrame2? frame2, SpatialFrame3? frame3)
        {
            if (worldGuid == Guid.Empty)
            {
                throw new ArgumentException("The containing World identity cannot be empty.", nameof(worldGuid));
            }

            if (frame2.HasValue == frame3.HasValue)
            {
                throw new ArgumentException("Exactly one spatial frame dimension must be selected.");
            }

            _frame2 = frame2;
            _frame3 = frame3;
            _worldGuid = worldGuid;
        }

        private static void AssertFieldPayload(
            EbinFieldReadView field,
            uint tag,
            ushort kind,
            ReadOnlySpan<byte> expectedPayload)
        {
            Assert.That(field.Tag, Is.EqualTo(tag));
            Assert.That(field.Kind, Is.EqualTo(kind));
            Assert.That(field.Payload.SequenceEqual(expectedPayload), Is.True);
        }

        private static void AssertGuidField(EbinFieldReadView field, uint tag, Guid expected)
        {
            Assert.That(field.Tag, Is.EqualTo(tag));
            Assert.That(field.Kind, Is.EqualTo((ushort)11));
            Assert.That(field.Payload.Length, Is.EqualTo(16));
            Assert.That(new Guid(field.Payload, bigEndian: true), Is.EqualTo(expected));
        }

        public readonly void Invoke(scoped EbinRecordReadView record)
        {
            var isTwoDimensional = _frame2.HasValue;
            var frameSchemaId = new ContentSchemaIdentityAssignment(
                isTwoDimensional ? "foundation/frame2" : "foundation/frame3").SchemaId;
            Assert.That(record.Header.SchemaId, Is.EqualTo(frameSchemaId));
            Assert.That(record.Header.Major, Is.EqualTo(1));
            Assert.That(record.Header.Minor, Is.Zero);
            Assert.That(record.FieldCount, Is.EqualTo(6));
            AssertGuidField(record.GetField(0), 1, _worldGuid);

            var profileField = record.GetField(1);
            Assert.That(profileField.Tag, Is.EqualTo(2U));
            Assert.That(profileField.Kind, Is.EqualTo((ushort)22));
            Assert.That(
                EbinStructuralReader.Validate(
                    profileField.Payload,
                    EbinReadLimits.Foundation,
                    EbinReadPolicy.Canonical,
                    out var profileBytesConsumed,
                    out var profileDiagnostic),
                Is.EqualTo(EbinReadStatus.Complete),
                profileDiagnostic?.ToString());
            Assert.That(profileBytesConsumed, Is.EqualTo(profileField.Payload.Length));
            Assert.That(
                EbinStructuralReader.ReadHeader(
                    profileField.Payload,
                    EbinReadLimits.Foundation,
                    out var profileHeader,
                    out _,
                    out profileDiagnostic),
                Is.EqualTo(EbinReadStatus.Complete),
                profileDiagnostic?.ToString());
            Assert.That(profileHeader.SchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment("foundation/profile").SchemaId));

            Span<byte> expectedOrigin = stackalloc byte[24];
            Span<byte> expectedRevision = stackalloc byte[sizeof(ulong)];
            Span<byte> expectedMinimum = stackalloc byte[12];
            Span<byte> expectedMaximum = stackalloc byte[12];
            EbinWriteStatus status;
            int bytesWritten;
            long revision;

            if (_frame2 is SpatialFrame2 frame2)
            {
                status = EbinPayloadWriter.Write(frame2.Origin, expectedOrigin, out bytesWritten, out _);
                revision = frame2.Revision;
                Assert.That(status, Is.EqualTo(EbinWriteStatus.Complete));
                AssertFieldPayload(record.GetField(2), 3, 20, expectedOrigin[..bytesWritten]);
                status = EbinPayloadWriter.Write(frame2.Minimum, expectedMinimum, out bytesWritten, out _);
                Assert.That(status, Is.EqualTo(EbinWriteStatus.Complete));
                AssertFieldPayload(record.GetField(4), 5, 14, expectedMinimum[..bytesWritten]);
                status = EbinPayloadWriter.Write(frame2.Maximum, expectedMaximum, out bytesWritten, out _);
                Assert.That(status, Is.EqualTo(EbinWriteStatus.Complete));
                AssertFieldPayload(record.GetField(5), 6, 14, expectedMaximum[..bytesWritten]);
            }
            else
            {
                var frame3 = _frame3.GetValueOrDefault();
                status = EbinPayloadWriter.Write(frame3.Origin, expectedOrigin, out bytesWritten, out _);
                revision = frame3.Revision;
                Assert.That(status, Is.EqualTo(EbinWriteStatus.Complete));
                AssertFieldPayload(record.GetField(2), 3, 21, expectedOrigin[..bytesWritten]);
                status = EbinPayloadWriter.Write(frame3.Minimum, expectedMinimum, out bytesWritten, out _);
                Assert.That(status, Is.EqualTo(EbinWriteStatus.Complete));
                AssertFieldPayload(record.GetField(4), 5, 15, expectedMinimum[..bytesWritten]);
                status = EbinPayloadWriter.Write(frame3.Maximum, expectedMaximum, out bytesWritten, out _);
                Assert.That(status, Is.EqualTo(EbinWriteStatus.Complete));
                AssertFieldPayload(record.GetField(5), 6, 15, expectedMaximum[..bytesWritten]);
            }

            BinaryPrimitives.WriteUInt64LittleEndian(expectedRevision, checked((ulong)revision));
            AssertFieldPayload(record.GetField(3), 4, 4, expectedRevision);
        }
    }

    private readonly struct PayloadRecordAssertions : IAction<EbinRecordReadView>
    {
        private readonly WorldPersistenceControlCaptureRow _control;
        private readonly EbinReadLimits _limits;
        private readonly Guid _worldGuid;

        internal PayloadRecordAssertions(
            WorldPersistenceControlCaptureRow control,
            Guid worldGuid,
            EbinReadLimits limits)
        {
            _control = control;
            _limits = limits;
            _worldGuid = worldGuid;
        }

        public readonly void Invoke(scoped EbinRecordReadView record)
        {
            var schemaName = _control.Kind switch {
                WorldPersistenceControlKind.Branch => "state/control-branch",
                WorldPersistenceControlKind.Frame2 => "state/control-frame2",
                WorldPersistenceControlKind.Frame3 => "state/control-frame3",
                WorldPersistenceControlKind.TimeScale => "state/control-timescale",
                _ => throw new AssertionException("The control record has an unknown kind."),
            };
            Assert.That(record.Header.SchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment(schemaName).SchemaId));
            Assert.That(record.Header.Major, Is.EqualTo(1));
            Assert.That(record.Header.Minor, Is.Zero);

            switch (_control.Kind)
            {
                case WorldPersistenceControlKind.Branch:
                {
                    Assert.That(record.FieldCount, Is.EqualTo(3));
                    Assert.That(new Guid(record.GetField(0).Payload, bigEndian: true), Is.EqualTo(_control.BranchId));
                    Assert.That(new Guid(record.GetField(1).Payload, bigEndian: true), Is.EqualTo(_control.LineageId));
                    Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(record.GetField(2).Payload), Is.EqualTo(_control.BranchIndex));
                    break;
                }

                case WorldPersistenceControlKind.Frame2:
                case WorldPersistenceControlKind.Frame3:
                {
                    Assert.That(record.FieldCount, Is.EqualTo(1));
                    var frameField = record.GetField(0);
                    Assert.That(frameField.Tag, Is.EqualTo(1U));
                    Assert.That(frameField.Kind, Is.EqualTo((ushort)22));
                    var assertions = new FrameRecordAssertions(_worldGuid, _control.Frame2, _control.Frame3);
                    Assert.That(
                        EbinStructuralReader.Read(
                            frameField.Payload,
                            _limits,
                            EbinReadPolicy.Canonical,
                            ref assertions,
                            out var bytesConsumed,
                            out var diagnostic),
                        Is.EqualTo(EbinReadStatus.Complete),
                        diagnostic?.ToString());
                    Assert.That(diagnostic, Is.Null);
                    Assert.That(bytesConsumed, Is.EqualTo(frameField.Payload.Length));
                    break;
                }

                case WorldPersistenceControlKind.TimeScale:
                {
                    Assert.That(record.FieldCount, Is.EqualTo(2));
                    Assert.That(
                        BinaryPrimitives.ReadInt32LittleEndian(record.GetField(0).Payload),
                        Is.EqualTo(BitConverter.SingleToInt32Bits(_control.TimeScaleValue)));
                    Assert.That(
                        BinaryPrimitives.ReadInt64LittleEndian(record.GetField(1).Payload),
                        Is.EqualTo(_control.SegmentSequence));
                    break;
                }
            }
        }
    }
}
