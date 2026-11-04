// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Threading;
using NUnit.Framework;
using TerraFX;
using TerraFX.Animation;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Graphics;
using TerraFX.Hosting;
using TerraFX.Integration;
using TerraFX.Runtime.Tests.Hosting;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldPersistenceRegistrationCaptureTests
{
    private static byte[] CreateExpectedFileContentDigest(
        long generation,
        ReadOnlySpan<byte> checkpointRecord,
        EbinReadLimits limits)
    {
        var payloadStorage = new byte[checked(8 + checkpointRecord.Length)];
        var status = EbinPayloadWriter.Write(generation, payloadStorage, out var generationBytes, out _);
        Assert.That(status, Is.EqualTo(EbinWriteStatus.Complete));
        checkpointRecord.CopyTo(payloadStorage.AsSpan(generationBytes));
        Span<EbinFieldSource> fields = [
            new EbinFieldSource(1, 8, 1, 1, new EbinPayloadSlice(0, generationBytes)),
            new EbinFieldSource(2, 22, 1, 1, new EbinPayloadSlice(generationBytes, checkpointRecord.Length)),
        ];
        var source = new EbinRecordWriteView(
            new ContentSchemaIdentityAssignment("state/file-content").SchemaId,
            major: 1,
            minor: 0,
            fields,
            payloadStorage);
        status = EbinStructuralWriter.Measure(in source, limits, out var recordBytes, out _);
        Assert.That(status, Is.EqualTo(EbinWriteStatus.Complete));
        var candidateStorage = new byte[recordBytes];
        var record = new byte[recordBytes];
        status = EbinStructuralWriter.Write(
            in source,
            limits,
            candidateStorage,
            record,
            out var bytesWritten,
            out _);
        Assert.That(status, Is.EqualTo(EbinWriteStatus.Complete));
        Assert.That(bytesWritten, Is.EqualTo(record.Length));
        var prefix = "TerraFX.WorldFile.v1\0"u8;
        var preimage = new byte[checked(prefix.Length + record.Length)];
        prefix.CopyTo(preimage);
        record.CopyTo(preimage.AsSpan(prefix.Length));
        return SHA256.HashData(preimage);
    }

    private static int FindSequence(ReadOnlySpan<byte> source, ReadOnlySpan<byte> sequence)
    {
        for (var index = 0; index <= source.Length - sequence.Length; index++)
        {
            if (source.Slice(index, sequence.Length).SequenceEqual(sequence))
            {
                return index;
            }
        }

        return -1;
    }

    [Test]
    public void CapturesRegistrationRootFromFrozenWorldRegistration()
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
            WorldPersistenceRegistrationCaptureCoordinator.Capture(
                world,
                lease,
                out var record,
                out var reservation,
                out var diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        var capturedRecord = record ?? throw new InvalidOperationException("The registration capture did not produce its record.");
        using var reservationScope = reservation
            ?? throw new InvalidOperationException("The registration capture did not retain its admission reservation.");
        var assertions = new RegistrationCaptureAssertions();
        Assert.That(
            EbinStructuralReader.Read(
                capturedRecord,
                EbinReadLimits.Foundation,
                EbinReadPolicy.Canonical,
                ref assertions,
                out var bytesConsumed,
                out var readDiagnostic),
            Is.EqualTo(EbinReadStatus.Complete));
        Assert.That(readDiagnostic, Is.Null);
        Assert.That(assertions.IsMatch, Is.True);
        Assert.That(bytesConsumed, Is.EqualTo(capturedRecord.Length));
    }

    [Test]
    public void CapturesWorldRootWithoutSceneSupport()
    {
        var schemas = new CompiledContentSchemaRegistry([
            .. CoreContentSchemaCatalogue.PrimitiveSchemas,
            .. CoreContentSchemaCatalogue.FoundationSchemas,
            .. NativeAssetSchemaCatalogue.Schemas,
            .. SceneSchemaCatalogue.Schemas,
            .. AnimationSchemaCatalogue.Schemas,
            .. RenderingSchemaCatalogue.Schemas,
            .. StandaloneContinuationSchemaCatalogue.Schemas,
            WorldPersistenceWorldRecordWriterTests.CreateApplicationSchema(
                "application/world-root-test/tag/v1",
                ContentSchemaRole.EntityTag,
                0),
            WorldPersistenceWorldRecordWriterTests.CreateApplicationSchema(
                "application/world-root-test/system-state/v1",
                ContentSchemaRole.SystemState,
                7),
            WorldPersistenceWorldRecordWriterTests.CreateApplicationSchema(
                "application/world-root-test/resource-state/v1",
                ContentSchemaRole.ResourceState,
                23,
                4),
            WorldPersistenceWorldRecordWriterTests.CreateApplicationSchema(
                "application/world-root-test/attachment-state/v1",
                ContentSchemaRole.AttachmentState,
                8),
        ]);
        var registration = new WorldRegistrationBuilder(schemas).Build();
        using var world = World.Create(new WorldOptions(registration) {
            CpuWorkerCount = 0,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        _ = world.Step();
        using var lease = world.AcquirePersistenceCaptureLease();
        Assert.That(
            WorldPersistenceCaptureCoordinator.Capture(
                world,
                lease,
                schemas,
                out var captureReservation,
                out var record,
                out var assetIdCount,
                out var diagnostic,
                CancellationToken.None),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        using var retainedCaptureReservation = captureReservation
            ?? throw new InvalidOperationException("The successful World capture did not retain its admission reservation.");
        Assert.That(assetIdCount, Is.Zero);
        var capturedRecord = record ?? throw new InvalidOperationException("The World capture coordinator did not produce its record.");
        using var capturedRecordUse = retainedCaptureReservation.AcquireUse(world, capturedRecord.Length);
        var assertions = new WorldPersistenceWorldRecordWriterTests.WorldRecordAssertions(
            lease.Frame2.HasValue,
            lease.ControlRecordCount,
            []);
        Assert.That(
            EbinStructuralReader.Read(
                capturedRecord,
                WorldPersistenceEbinLimits.Create(world.Options),
                EbinReadPolicy.Canonical,
                ref assertions,
                out var bytesConsumed,
                out var readDiagnostic),
            Is.EqualTo(EbinReadStatus.Complete));
        Assert.That(readDiagnostic, Is.Null);
        Assert.That(bytesConsumed, Is.EqualTo(capturedRecord.Length));

        var metadata = WorldPersistenceHeaderWriterTests.CreateMetadata();
        var profileRecord = WorldPersistenceHeaderWriterTests.CreateFoundationProfileRecord();
        var header = WorldPersistenceHeaderWriter.Create(
            metadata,
            WorldPersistenceMode.Standard,
            [],
            lease.WorldGuid,
            world.ReplayLineageId,
            lease.NextTickIndex,
            captureWorkerCount: 0,
            profileRecord);
        var limits = WorldPersistenceEbinLimits.Create(world.Options);
        var (firstCheckpointRecord, firstFileRecord, firstCanonicalHash, firstCheckpointDigest, firstFileContentDigest) =
            WorldPersistenceCheckpointWriter.Create(header, capturedRecord, 1, limits);
        var changedWorkerHeader = WorldPersistenceHeaderWriter.Create(
            metadata,
            WorldPersistenceMode.Standard,
            [],
            lease.WorldGuid,
            world.ReplayLineageId,
            lease.NextTickIndex,
            captureWorkerCount: 1,
            profileRecord);
        var (_, _, changedWorkerCanonicalHash, changedWorkerCheckpointDigest, _) =
            WorldPersistenceCheckpointWriter.Create(changedWorkerHeader, capturedRecord, 1, limits);
        var (_, nextGenerationFileRecord, nextGenerationCanonicalHash, nextGenerationCheckpointDigest, nextGenerationFileContentDigest) =
            WorldPersistenceCheckpointWriter.Create(header, capturedRecord, 2, limits);

        Assert.Multiple(() => {
            Assert.That(firstCanonicalHash, Is.EqualTo(changedWorkerCanonicalHash));
            Assert.That(firstCheckpointDigest, Is.Not.EqualTo(changedWorkerCheckpointDigest));
            Assert.That(firstCanonicalHash, Is.EqualTo(nextGenerationCanonicalHash));
            Assert.That(firstCheckpointDigest, Is.EqualTo(nextGenerationCheckpointDigest));
            Assert.That(firstFileContentDigest, Is.Not.EqualTo(nextGenerationFileContentDigest));
            Assert.That(firstCheckpointDigest, Is.EqualTo(SHA256.HashData(firstCheckpointRecord)));
            Assert.That(firstFileContentDigest, Is.EqualTo(
                CreateExpectedFileContentDigest(1, firstCheckpointRecord, limits)));
            Assert.That(EbinStructuralReader.Validate(
                firstFileRecord,
                limits,
                EbinReadPolicy.Canonical,
                out var fileBytesConsumed,
                out _), Is.EqualTo(EbinReadStatus.Complete));
            Assert.That(fileBytesConsumed, Is.EqualTo(firstFileRecord.Length));
            Assert.That(nextGenerationFileRecord.Length, Is.EqualTo(firstFileRecord.Length));
        });

        Assert.That(WorldPersistenceCheckpointWriter.ReadTransactionGeneration(firstFileRecord, limits), Is.EqualTo(1));
        var tamperedFileRecord = (byte[])firstFileRecord.Clone();
        var fileDigestOffset = FindSequence(tamperedFileRecord, firstFileContentDigest);
        Assert.That(fileDigestOffset, Is.GreaterThanOrEqualTo(0));
        tamperedFileRecord[fileDigestOffset] ^= 0x01;
        Assert.That(
            () => WorldPersistenceCheckpointWriter.ReadTransactionGeneration(tamperedFileRecord, limits),
            Throws.ArgumentException);

        capturedRecordUse.Dispose();
        retainedCaptureReservation.Dispose();
        lease.Dispose();
        world.SetTimeScale(0.5f);
        _ = world.Step();
        using var nextLease = world.AcquirePersistenceCaptureLease();
        var nextCaptureStatus = WorldPersistenceCaptureCoordinator.Capture(
            world,
            nextLease,
            schemas,
            out var nextCaptureReservation,
            out var nextRecord,
            out _,
            out var nextDiagnostic,
            CancellationToken.None);
        Assert.That(nextCaptureStatus, Is.EqualTo(EbinWriteStatus.Complete), nextDiagnostic?.Category);
        using var nextReservation = nextCaptureReservation
            ?? throw new InvalidOperationException("The next World capture did not retain its admission reservation.");
        var nextWorldRecord = nextRecord
            ?? throw new InvalidOperationException("The next World capture did not produce its record.");
        var nextHeader = WorldPersistenceHeaderWriter.Create(
            metadata,
            WorldPersistenceMode.Standard,
            [],
            nextLease.WorldGuid,
            world.ReplayLineageId,
            nextLease.NextTickIndex,
            captureWorkerCount: 0,
            profileRecord);
        var (_, _, nextWorldCanonicalHash, _, _) =
            WorldPersistenceCheckpointWriter.Create(nextHeader, nextWorldRecord, 3, limits);
        Assert.That(nextWorldCanonicalHash, Is.Not.EqualTo(firstCanonicalHash));
    }

    [Test]
    public void WritesRegistrationRootUnderItsExactWorldReservation()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        using var world = World.Create(new WorldOptions(registration) {
            CpuWorkerCount = 0,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        _ = world.Step();
        using var lease = world.AcquirePersistenceCaptureLease();
        var emptyArrayPayload = new byte[16];
        Assert.That(
            EbinPayloadWriter.WriteRecordArray(
                [],
                [],
                EbinReadLimits.Foundation,
                emptyArrayPayload,
                out var emptyArrayBytes,
                out var diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(emptyArrayBytes, Is.EqualTo(emptyArrayPayload.Length));
        var rootPayloadStorage = new byte[64];
        Assert.That(
            lease.MeasureRegistrationRecord(
                emptyArrayPayload,
                emptyArrayPayload,
                emptyArrayPayload,
                emptyArrayPayload,
                rootPayloadStorage,
                out var workspace,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(workspace.WorkspaceBytes, Is.GreaterThan(rootPayloadStorage.Length));
        Assert.That(lease.TryReserveAdmissionBytes(workspace.WorkspaceBytes, out var reservation), Is.True);
        using var admittedReservation = reservation ?? throw new InvalidOperationException("The registration reservation was not created.");
        var candidateStorage = new byte[workspace.RecordBytes];
        var destination = new byte[workspace.RecordBytes];
        Assert.That(
            lease.WriteRegistrationRecord(
                admittedReservation,
                workspace,
                emptyArrayPayload,
                emptyArrayPayload,
                emptyArrayPayload,
                emptyArrayPayload,
                rootPayloadStorage,
                candidateStorage,
                destination,
                out var bytesWritten,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(bytesWritten, Is.EqualTo(workspace.RecordBytes));
        var assertions = new RegistrationCaptureAssertions();
        Assert.That(
            EbinStructuralReader.Read(
                destination,
                EbinReadLimits.Foundation,
                EbinReadPolicy.Canonical,
                ref assertions,
                out var bytesConsumed,
                out var readDiagnostic),
            Is.EqualTo(EbinReadStatus.Complete));
        Assert.That(readDiagnostic, Is.Null);
        Assert.That(assertions.IsMatch, Is.True);
        Assert.That(bytesConsumed, Is.EqualTo(destination.Length));
    }

    private struct RegistrationCaptureAssertions : IAction<EbinRecordReadView>
    {
        internal bool IsMatch { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
        {
            IsMatch = (record.Header.SchemaId == new ContentSchemaIdentityAssignment("state/registration").SchemaId)
                && (record.Header.Major == 1)
                && (record.Header.Minor == 0)
                && (record.FieldCount == 4);

            if (!IsMatch)
            {
                return;
            }

            for (var index = 0; index < record.FieldCount; index++)
            {
                var field = record.GetField(index);

                if ((field.Tag != (uint)(index + 1))
                    || (field.Kind != 24)
                    || !field.IsRequired
                    || (field.Count != 0)
                    || (field.Payload.Length != 16)
                    || (BinaryPrimitives.ReadUInt64LittleEndian(field.Payload) != 0))
                {
                    IsMatch = false;
                    return;
                }
            }
        }
    }
}
