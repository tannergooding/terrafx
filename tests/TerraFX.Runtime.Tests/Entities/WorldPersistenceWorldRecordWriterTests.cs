// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using NUnit.Framework;
using TerraFX;
using TerraFX.Animation;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Graphics;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldPersistenceWorldRecordWriterTests
{
    internal static byte[] ComposeWorldRecord(
        World world,
        in WorldPersistenceCaptureLease lease,
        CompiledContentSchemaRegistry schemas,
        ReadOnlySpan<byte> ecsRecord,
        ReadOnlySpan<byte> scenesRecord,
        ReadOnlySpan<uint> assetIds)
    {
        var status = WorldPersistenceWorldRecordCaptureCoordinator.Capture(
            world,
            lease,
            schemas,
            ecsRecord,
            scenesRecord,
            out var worldRecord,
            out var assetIdCount,
            out var diagnostic,
            CancellationToken.None);
        Assert.That(status, Is.EqualTo(EbinWriteStatus.Complete), diagnostic?.ToString());
        Assert.That(assetIdCount, Is.EqualTo(assetIds.Length));
        return worldRecord
            ?? throw new AssertionException("The World capture coordinator did not produce the captured record.");
    }

    internal static DataSchema CreateApplicationSchema(string name, ContentSchemaRole role, ushort wireKind, ushort elementKind = 0)
    {
        var assignment = new ContentSchemaIdentityAssignment(name);
        var schema = new ContentSchemaReference(assignment.SchemaId, 1, 0, ContentSchemaScope.Content);
        ImmutableArray<ContentFieldDescriptor> fields = (role is ContentSchemaRole.EntityTag)
            ? []
            : [
                new ContentFieldDescriptor(
                1,
                wireKind,
                elementKind,
                true,
                1,
                1,
                [],
                0,
                ContentUnit.None,
                ContentReferenceKind.None,
                ContentAuthoring.None,
                null),
            ];
        return new DataSchema(
            schema,
            assignment,
            role,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            fields);
    }

    [Test]
    public void ComposesCanonicalWorldFromCapturedSubsystems()
    {
        DataSchema[] schemaRows = [
            .. CoreContentSchemaCatalogue.PrimitiveSchemas,
            .. CoreContentSchemaCatalogue.FoundationSchemas,
            .. NativeAssetSchemaCatalogue.Schemas,
            .. SceneSchemaCatalogue.Schemas,
            .. AnimationSchemaCatalogue.Schemas,
            .. RenderingSchemaCatalogue.Schemas,
            .. StandaloneContinuationSchemaCatalogue.Schemas,
            CreateApplicationSchema("application/world-root-test/tag/v1", ContentSchemaRole.EntityTag, 0),
            CreateApplicationSchema("application/world-root-test/system-state/v1", ContentSchemaRole.SystemState, 7),
            CreateApplicationSchema("application/world-root-test/resource-state/v1", ContentSchemaRole.ResourceState, 23, 4),
            CreateApplicationSchema("application/world-root-test/attachment-state/v1", ContentSchemaRole.AttachmentState, 8),
        ];
        var missingReferences = new List<string>();

        foreach (var schema in schemaRows)
        {
            foreach (var field in schema.Fields)
            {
                foreach (var nestedSchema in field.NestedSchemas)
                {
                    var isRegistered = false;

                    foreach (var candidate in schemaRows)
                    {
                        if (candidate.Schema == nestedSchema)
                        {
                            isRegistered = true;
                            break;
                        }
                    }

                    if (!isRegistered)
                    {
                        missingReferences.Add($"{schema.Schema.SchemaId}:{field.Tag}->{nestedSchema.SchemaId}/{nestedSchema.Major}/{nestedSchema.Minor}/{nestedSchema.Scope}");
                    }
                }
            }
        }

        Assert.That(missingReferences, Is.Empty, string.Join(Environment.NewLine, missingReferences));
        var schemas = new CompiledContentSchemaRegistry([.. schemaRows]);
        var registration = new WorldRegistrationBuilder(schemas).Build();
        using var world = World.Create(new WorldOptions(registration) {
            CpuWorkerCount = 0,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        world.SetTimeScale(2.0f);
        _ = world.Step();
        using var lease = world.AcquirePersistenceCaptureLease();
        var limits = WorldPersistenceEbinLimits.Create(world.Options);

        Assert.That(
            WorldPersistenceConfigurationWriter.Measure(world.Options, limits, out var configurationBytes, out var diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.ToString());
        Assert.That(diagnostic, Is.Null);
        var configurationRecord = new byte[configurationBytes];
        Assert.That(
            WorldPersistenceConfigurationWriter.Write(
                world.Options,
                limits,
                new byte[configurationBytes],
                configurationRecord,
                out var configurationWritten,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.ToString());
        Assert.That(configurationWritten, Is.EqualTo(configurationRecord.Length));

        Assert.That(
            lease.MeasureClockRecord(out var clockWorkspace, out diagnostic, CancellationToken.None),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.ToString());
        var clockBuffers = new WorldPersistenceClockBuffers {
            ClockCandidateStorage = new byte[clockWorkspace.RecordBytes],
            ClockPayloadStorage = new byte[clockWorkspace.ClockPayloadStorageBytes],
            Destination = new byte[clockWorkspace.RecordBytes],
            FrameCandidateStorage = new byte[clockWorkspace.FrameCandidateStorageBytes],
            FramePayloadStorage = new byte[clockWorkspace.FramePayloadStorageBytes],
            FrameProfileCandidateStorage = new byte[clockWorkspace.InitialFrame3.ProfileRecordBytes],
            FrameProfileStorage = new byte[clockWorkspace.InitialFrame3.ProfileRecordBytes],
            InitialFrame2Storage = new byte[clockWorkspace.InitialFrame2.RecordBytes],
            InitialFrame3Storage = new byte[clockWorkspace.InitialFrame3.RecordBytes],
            SegmentArrayPayloadStorage = new byte[clockWorkspace.SegmentArrayPayloadBytes],
            SegmentCandidateStorage = new byte[clockWorkspace.SegmentRecordBytes],
            SegmentSlices = new EbinPayloadSlice[clockWorkspace.SegmentCount],
            SegmentStorage = new byte[clockWorkspace.SegmentStorageBytes],
        };
        Assert.That(
            WorldPersistenceClockWriter.Write(
                lease,
                clockWorkspace,
                limits,
                ref clockBuffers,
                out var clockBytesWritten,
                out diagnostic,
                CancellationToken.None),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.ToString());
        var clockRecord = clockBuffers.Destination[..clockBytesWritten].ToArray();

        var frame2Record = Array.Empty<byte>();

        if (lease.Frame2 is SpatialFrame2 frame2)
        {
            Assert.That(
                WorldPersistenceSpatialFrameWriter.MeasureFrame2(
                    lease.WorldGuid,
                    lease.OwnerId,
                    frame2,
                    limits,
                    out var frame2Workspace,
                    out diagnostic),
                Is.EqualTo(EbinWriteStatus.Complete),
                diagnostic?.ToString());
            var frame2Destination = new byte[frame2Workspace.RecordBytes];
            var frame2Buffers = new WorldPersistenceSpatialFrameBuffers {
                Destination = frame2Destination,
                FrameCandidateStorage = new byte[frame2Workspace.RecordBytes],
                FramePayloadStorage = new byte[frame2Workspace.FramePayloadStorageBytes],
                ProfileCandidateStorage = new byte[frame2Workspace.ProfileRecordBytes],
                ProfileStorage = new byte[frame2Workspace.ProfileRecordBytes],
            };
            Assert.That(
                WorldPersistenceSpatialFrameWriter.WriteFrame2(
                    lease.WorldGuid,
                    lease.OwnerId,
                    frame2,
                    frame2Workspace,
                    limits,
                    ref frame2Buffers,
                    out var frame2BytesWritten,
                    out diagnostic),
                Is.EqualTo(EbinWriteStatus.Complete),
                diagnostic?.ToString());
            frame2Record = frame2Destination[..frame2BytesWritten];
        }

        var frame3 = lease.Frame3;
        Assert.That(
            WorldPersistenceSpatialFrameWriter.MeasureFrame3(
                lease.WorldGuid,
                lease.OwnerId,
                frame3,
                limits,
                out var frame3Workspace,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.ToString());
        var frame3Destination = new byte[frame3Workspace.RecordBytes];
        var frame3Buffers = new WorldPersistenceSpatialFrameBuffers {
            Destination = frame3Destination,
            FrameCandidateStorage = new byte[frame3Workspace.RecordBytes],
            FramePayloadStorage = new byte[frame3Workspace.FramePayloadStorageBytes],
            ProfileCandidateStorage = new byte[frame3Workspace.ProfileRecordBytes],
            ProfileStorage = new byte[frame3Workspace.ProfileRecordBytes],
        };
        Assert.That(
            WorldPersistenceSpatialFrameWriter.WriteFrame3(
                lease.WorldGuid,
                lease.OwnerId,
                frame3,
                frame3Workspace,
                limits,
                ref frame3Buffers,
                out var frame3BytesWritten,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.ToString());
        var frame3Record = frame3Destination[..frame3BytesWritten];

        var ecsAssertions = new EcsRecordCopyAction();
        var ecsBytesWritten = WorldPersistenceEcsTestHelper.CaptureEcsRecord(world, in lease, ref ecsAssertions);
        var ecsRecord = ecsAssertions.Record
            ?? throw new AssertionException("The capture helper did not return the actual ECS record.");
        Assert.That(ecsRecord.Length, Is.EqualTo(ecsBytesWritten));

        var emptyArrayPayload = new byte[16];
        Assert.That(
            EbinPayloadWriter.WriteRecordArray([], [], limits, emptyArrayPayload, out var emptyArrayBytes, out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.ToString());
        Assert.That(emptyArrayBytes, Is.EqualTo(emptyArrayPayload.Length));

        var hierarchyRootPayload = new byte[48];
        Assert.That(
            ScenePersistenceWorldHierarchyWriter.Measure(
                nodeCount: 0,
                emptyArrayPayload,
                [],
                limits,
                hierarchyRootPayload,
                out var hierarchyWorkspace,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.ToString());
        var hierarchyDestination = new byte[hierarchyWorkspace.RecordBytes];
        Assert.That(
            ScenePersistenceWorldHierarchyWriter.Write(
                hierarchyWorkspace,
                nodeCount: 0,
                emptyArrayPayload,
                [],
                limits,
                hierarchyRootPayload,
                new byte[hierarchyWorkspace.RecordBytes],
                hierarchyDestination,
                out var hierarchyBytesWritten,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.ToString());
        var hierarchyRecord = hierarchyDestination[..hierarchyBytesWritten];

        var scenesRootPayload = new byte[emptyArrayPayload.Length + hierarchyRecord.Length];
        Assert.That(
            ScenePersistenceScenesWriter.Measure(
                instanceCount: 0,
                emptyArrayPayload,
                hierarchyRecord,
                limits,
                scenesRootPayload,
                out var scenesWorkspace,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.ToString());
        var scenesDestination = new byte[scenesWorkspace.RecordBytes];
        Assert.That(
            ScenePersistenceScenesWriter.Write(
                scenesWorkspace,
                instanceCount: 0,
                emptyArrayPayload,
                hierarchyRecord,
                limits,
                scenesRootPayload,
                new byte[scenesWorkspace.RecordBytes],
                scenesDestination,
                out var scenesBytesWritten,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.ToString());
        var scenesRecord = scenesDestination[..scenesBytesWritten];

        var referenceCount = WorldPersistenceAssetIdSetWriter.MeasureReferenceCount(
            schemas,
            ecsRecord,
            scenesRecord,
            limits);
        Assert.That(referenceCount, Is.Zero);
        var assetIds = new uint[referenceCount];
        var assetIdCount = WorldPersistenceAssetIdSetWriter.CopySortedUniqueAssetIds(
            schemas,
            ecsRecord,
            scenesRecord,
            limits,
            assetIds);
        Assert.That(assetIdCount, Is.Zero);

        var controlSlices = new EbinPayloadSlice[lease.ControlRecordCount];
        Assert.That(
            WorldPersistenceControlRecordArrayWriter.Measure(
                lease,
                limits,
                controlSlices,
                out var controlWorkspace,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.ToString());
        var controlsArrayPayload = new byte[controlWorkspace.ArrayPayloadBytes];
        Assert.That(
            WorldPersistenceControlRecordArrayWriter.Write(
                lease,
                limits,
                controlWorkspace,
                controlSlices,
                new byte[controlWorkspace.RecordStorageBytes],
                new byte[controlWorkspace.MaximumCandidateRecordBytes],
                new byte[controlWorkspace.MaximumRootPayloadBytes],
                controlsArrayPayload,
                out _,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.ToString());

        Assert.That(
            WorldPersistenceCursorWriter.Measure(lease, limits, out var cursorsBytes, out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.ToString());
        var cursorsRecord = new byte[cursorsBytes];
        Assert.That(
            WorldPersistenceCursorWriter.Write(
                lease,
                limits,
                new byte[cursorsBytes],
                cursorsRecord,
                out _,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.ToString());

        var registrationRootPayload = new byte[64];
        Assert.That(
            WorldPersistenceRegistrationWriter.Measure(
                registration,
                emptyArrayPayload,
                emptyArrayPayload,
                emptyArrayPayload,
                emptyArrayPayload,
                limits,
                registrationRootPayload,
                out _,
                out var registrationBytes,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.ToString());
        var registrationRecord = new byte[registrationBytes];
        Assert.That(
            WorldPersistenceRegistrationWriter.Write(
                registration,
                emptyArrayPayload,
                emptyArrayPayload,
                emptyArrayPayload,
                emptyArrayPayload,
                limits,
                registrationRootPayload,
                new byte[registrationBytes],
                registrationRecord,
                out var registrationBytesWritten,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.ToString());
        Assert.That(registrationBytesWritten, Is.EqualTo(registrationRecord.Length));

        var sources = new WorldPersistenceWorldRecordSources(
            lease,
            assetIds.AsSpan(0, assetIdCount),
            clockRecord,
            configurationRecord,
            lease.ControlRecordCount,
            controlsArrayPayload,
            cursorsRecord,
            ecsRecord,
            frame2Record,
            frame3Record,
            emptyArrayPayload,
            randomStreamCount: 0,
            registrationRecord,
            scenesRecord,
            strictLayoutRecord: []);
        Assert.That(
            WorldPersistenceWorldRecordWriter.MeasureRootPayloadStorage(sources, limits, out var rootPayloadBytes, out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.ToString());
        var rootPayloadStorage = new byte[rootPayloadBytes];
        Assert.That(
            WorldPersistenceWorldRecordWriter.Measure(
                sources,
                limits,
                rootPayloadStorage,
                out var worldWorkspace,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.ToString());
        var worldDestination = new byte[worldWorkspace.RecordBytes];
        Assert.That(lease.TryReserveAdmissionBytes(worldWorkspace.WorkspaceBytes, out var rootReservation), Is.True);
        using var rootReservationScope = rootReservation
            ?? throw new AssertionException("The World-root workspace reservation was not created.");
        Assert.That(
            lease.WriteWorldRecord(
                rootReservationScope,
                sources,
                worldWorkspace,
                rootPayloadStorage,
                new byte[worldWorkspace.RecordBytes],
                worldDestination,
                out var worldBytesWritten,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.ToString());
        Assert.That(worldBytesWritten, Is.EqualTo(worldWorkspace.RecordBytes));

        var assertions = new WorldRecordAssertions(
            frame2Record.Length > 0,
            lease.ControlRecordCount,
            assetIds.AsSpan(0, assetIdCount).ToArray());
        Assert.That(
            EbinStructuralReader.Read(
                worldDestination,
                limits,
                EbinReadPolicy.Canonical,
                ref assertions,
                out var bytesConsumed,
                out var readDiagnostic),
            Is.EqualTo(EbinReadStatus.Complete),
            readDiagnostic?.ToString());
        Assert.That(bytesConsumed, Is.EqualTo(worldBytesWritten));

        var inconsistentSources = new WorldPersistenceWorldRecordSources(
                lease,
                sources.AssetIds,
                sources.ClockRecord,
                sources.ConfigurationRecord,
                controlRecordCount: 0,
                emptyArrayPayload,
                sources.CursorsRecord,
                sources.EcsRecord,
                sources.Frame2Record,
                sources.Frame3Record,
                sources.RandomArrayPayload,
                randomStreamCount: 0,
                sources.RegistrationRecord,
                sources.ScenesRecord,
                sources.StrictLayoutRecord);
        Assert.That(
                WorldPersistenceWorldRecordWriter.MeasureRootPayloadStorage(
                    inconsistentSources,
                    limits,
                    out _,
                    out var cursorDiagnostic),
                Is.EqualTo(EbinWriteStatus.InvalidRecord));
        Assert.That(cursorDiagnostic?.Category, Is.EqualTo("next-control-sequence-mismatch"));
    }

    [Test]
    public void RejectsWorldRecordSourcesFromAnEarlierCapture()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        using var world = World.Create(new WorldOptions(registration));
        _ = world.Step();
        WorldPersistenceWorldRecordSources sources;

        using (var originalLease = world.AcquirePersistenceCaptureLease())
        {
            sources = new WorldPersistenceWorldRecordSources(
                originalLease,
                assetIds: [],
                clockRecord: [],
                configurationRecord: [],
                controlRecordCount: 0,
                controlsArrayPayload: [],
                cursorsRecord: [],
                ecsRecord: [],
                frame2Record: [],
                frame3Record: [],
                randomArrayPayload: [],
                randomStreamCount: 0,
                registrationRecord: [],
                scenesRecord: [],
                strictLayoutRecord: []);
        }

        using var currentLease = world.AcquirePersistenceCaptureLease();
        var workspace = new WorldPersistenceWorldRecordWorkspaceRequirements(
            assetIdCount: 0,
            controlRecordCount: 0,
            hasFrame2: false,
            hasStrictLayout: false,
            randomStreamCount: 0,
            rootPayloadStorageBytes: 1,
            recordBytes: 1);
        Assert.That(currentLease.TryReserveAdmissionBytes(workspace.WorkspaceBytes, out var reservation), Is.True);
        using var reservationScope = reservation
            ?? throw new AssertionException("The World-root workspace reservation was not created.");

        var rejected = false;

        try
        {
            _ = currentLease.WriteWorldRecord(
                reservationScope,
                sources,
                workspace,
                new byte[workspace.RootPayloadStorageBytes],
                new byte[workspace.RecordBytes],
                new byte[workspace.RecordBytes],
                out _,
                out _);
        }
        catch (ArgumentException)
        {
            rejected = true;
        }

        Assert.That(rejected, Is.True);
    }

    internal struct EcsRecordCopyAction : IAction<EbinRecordReadView>
    {
        internal byte[]? Record { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
            => Record = record.EncodedBytes.ToArray();
    }

    internal readonly struct WorldRecordAssertions : IAction<EbinRecordReadView>
    {
        private readonly uint[] _assetIds;
        private readonly int _controlRecordCount;
        private readonly bool _hasFrame2;

        internal WorldRecordAssertions(bool hasFrame2, int controlRecordCount, uint[] assetIds)
        {
            _assetIds = assetIds;
            _controlRecordCount = controlRecordCount;
            _hasFrame2 = hasFrame2;
        }

        public readonly void Invoke(scoped EbinRecordReadView record)
        {
            Assert.That(record.Header.SchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment("state/world").SchemaId));
            Assert.That(record.Header.Major, Is.EqualTo(1));
            Assert.That(record.Header.Minor, Is.Zero);
            Assert.That(record.FieldCount, Is.EqualTo(_hasFrame2 ? 11 : 10));
            Assert.That(record.GetRecord(1).Header.SchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment("state/configuration").SchemaId));
            Assert.That(record.GetRecord(2).Header.SchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment("state/clock").SchemaId));

            if (_hasFrame2)
            {
                Assert.That(record.GetRecord(3).Header.SchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment("foundation/frame2").SchemaId));
            }

            Assert.That(record.GetRecord(4).Header.SchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment("foundation/frame3").SchemaId));
            Assert.That(record.GetRecord(5).Header.SchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment("state/ecs").SchemaId));
            Assert.That(record.GetRecord(6).Header.SchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment("state/scenes").SchemaId));
            var assetIdFieldIndex = _hasFrame2 ? 6 : 5;
            var assetIdsField = record.GetField(assetIdFieldIndex);
            Assert.That(assetIdsField.Kind, Is.EqualTo((ushort)23));
            Assert.That(assetIdsField.Count, Is.EqualTo(checked((ulong)_assetIds.Length)));
            Assert.That(assetIdsField.Payload.Length, Is.EqualTo(checked(32 + (4 * _assetIds.Length))));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(assetIdsField.Payload[16..]), Is.EqualTo((ushort)3));
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(assetIdsField.Payload[20..]), Is.EqualTo(4));

            for (var index = 0; index < _assetIds.Length; index++)
            {
                Assert.That(
                    BinaryPrimitives.ReadUInt32LittleEndian(assetIdsField.Payload[(32 + (index * 4))..]),
                    Is.EqualTo(_assetIds[index]));
            }

            var controlsField = record.GetField(assetIdFieldIndex + 1);
            Assert.That(controlsField.Kind, Is.EqualTo((ushort)24));
            Assert.That(controlsField.Count, Is.EqualTo(checked((ulong)_controlRecordCount)));
            Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(controlsField.Payload), Is.EqualTo(checked((ulong)_controlRecordCount)));
            var cursors = record.GetRecord(9);
            Assert.That(cursors.Header.SchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment("state/cursors").SchemaId));
            Assert.That(BinaryPrimitives.ReadInt64LittleEndian(cursors.GetField(0).Payload), Is.EqualTo(checked((long)_controlRecordCount + 1)));
            var randomField = record.GetField(assetIdFieldIndex + 3);
            Assert.That(randomField.Kind, Is.EqualTo((ushort)24));
            Assert.That(randomField.Count, Is.Zero);
            Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(randomField.Payload), Is.Zero);
            Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(randomField.Payload[8..]), Is.EqualTo(16));
            Assert.That(record.GetRecord(12).Header.SchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment("state/registration").SchemaId));
        }
    }
}
