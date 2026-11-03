// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Threading;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldPersistenceEcsCaptureTests
{
    private static void AllocateWriteBuffers(
        ref WorldPersistenceEcsBuffers buffers,
        WorldPersistenceEcsWorkspaceRequirements workspace)
    {
        buffers.AttachmentStorage = new byte[workspace.AttachmentStorageBytes];
        buffers.EcsCandidateStorage = new byte[workspace.RecordBytes];
        buffers.EntityCandidateStorage = new byte[workspace.EntityCandidateStorageBytes];
        buffers.EntityChildCandidateStorage = new byte[workspace.EntityChildCandidateBytes];
        buffers.EntityComponentStorage = new byte[workspace.EntityComponentStorageBytes];
        buffers.EntityPayloadStorage = new byte[workspace.EntityPayloadStorageBytes];
        buffers.EntityStorage = new byte[workspace.EntityStorageBytes];
        buffers.EntityTagStorage = new byte[workspace.EntityTagStorageBytes];
        buffers.EntityTransientSchemaStorage = new byte[workspace.EntityTransientSchemaStorageBytes];
        buffers.IdentityBranchStorage = new byte[workspace.IdentityHistory.BranchStorageBytes];
        buffers.IdentityCandidateStorage = new byte[workspace.IdentityHistory.RecordBytes];
        buffers.IdentityChildCandidateStorage = new byte[workspace.IdentityHistory.ChildCandidateBytes];
        buffers.IdentityPageStorage = new byte[workspace.IdentityHistory.PageStorageBytes];
        buffers.IdentityRootPayloadStorage = new byte[workspace.IdentityHistory.RootPayloadStorageBytes];
        buffers.ResourceStorage = new byte[workspace.ResourceStorageBytes];
        buffers.RootPayloadStorage = new byte[workspace.RootPayloadStorageBytes];
        buffers.StatePayloadRecordStorage = new byte[workspace.StatePayloadRecordStorageBytes];
        buffers.StateRowCandidateStorage = new byte[workspace.StateRowCandidateStorageBytes];
        buffers.StateRowPayloadStorage = new byte[workspace.StateRowPayloadStorageBytes];
        buffers.SystemStorage = new byte[workspace.SystemStorageBytes];
        buffers.Destination = new byte[workspace.RecordBytes];
    }

    private static void AssertEmptyArray(EbinFieldReadView field, uint tag)
    {
        Assert.That(field.Tag, Is.EqualTo(tag));
        Assert.That(field.IsRequired, Is.True);
        Assert.That(field.Kind, Is.EqualTo(24));
        Assert.That(field.Count, Is.Zero);
        Assert.That(field.Payload.Length, Is.EqualTo(16));
        Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(field.Payload), Is.Zero);
        Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(field.Payload[8..]), Is.EqualTo(16));
    }

    private static WorldPersistenceEcsBuffers CreateMeasurementBuffers(WorldPersistenceCaptureLease lease)
    {
        var history = lease.GetIdentityHistoryCapture();
        var pageWorkspaceBytes = (history.PageCount == 0)
            ? 0
            : WorldPersistenceIdentityPageWriter.GetMinimumPayloadWorkspaceBytes(StableIdentityLedger.RecordsPerPage);
        var pageRecords = (history.PageCount == 0) ? 0 : StableIdentityLedger.RecordsPerPage;
        var buffers = default(WorldPersistenceEcsBuffers);
        buffers.EntityRows = new World.EntityStateCaptureRow[lease.GetEntityStateCount()];
        buffers.EntitySlices = new EbinPayloadSlice[buffers.EntityRows.Length];
        buffers.SystemSlices = new EbinPayloadSlice[lease.GetSystemStateCount()];
        buffers.ResourceSlices = new EbinPayloadSlice[lease.GetResourceStateCount()];
        buffers.AttachmentRows = new World.AttachmentStateCaptureRow[lease.GetAttachmentStateCount()];
        buffers.AttachmentSlices = new EbinPayloadSlice[buffers.AttachmentRows.Length];
        buffers.EntityComponentSlices = new EbinPayloadSlice[WorldPersistenceEcsWorkspaceRequirements.MaximumEntitySchemaRecordCount];
        buffers.EntityTagSlices = new EbinPayloadSlice[WorldPersistenceEcsWorkspaceRequirements.MaximumEntitySchemaRecordCount];
        buffers.EntityTransientSchemaSlices = new EbinPayloadSlice[WorldPersistenceEcsWorkspaceRequirements.MaximumEntitySchemaRecordCount];
        buffers.IdentityBranchSlices = new EbinPayloadSlice[history.BranchCount];
        buffers.IdentityPageSlices = new EbinPayloadSlice[history.PageCount];
        buffers.IdentityPageStableIds = new Guid[pageRecords];
        buffers.IdentityPageMetadata = new uint[pageRecords];
        buffers.IdentityPagePayloadStorage = new byte[pageWorkspaceBytes];
        return buffers;
    }

    private static World CreateWorld()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        return World.Create(new WorldOptions(registration) {
            CpuWorkerCount = 0,
            InitialReplayLineageId = Guid.Parse("30000000-0000-0000-0000-000000000003"),
            MaximumPersistenceAdmissionBytes = 1_048_576,
            WorldGuid = Guid.Parse("10000000-0000-0000-0000-000000000001"),
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AssemblesCanonicalEcsRowsAndIdentityHistory(bool createEntity)
    {
        using var world = CreateWorld();
        Guid? expectedEntityId = null;

        if (createEntity)
        {
            expectedEntityId = Guid.Parse("20000000-0000-0000-0000-000000000001");
            _ = world.CreateEntity(new EntityDescription(persistentId: expectedEntityId.Value));
        }

        _ = world.Step();
        using var lease = world.AcquirePersistenceCaptureLease();
        var buffers = CreateMeasurementBuffers(lease);
        Assert.That(
            lease.MeasureEcsRecord(
                ref buffers,
                out var workspace,
                out var diagnostic,
                CancellationToken.None),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(lease.TryReserveAdmissionBytes(workspace.WorkspaceBytes, out var reservation), Is.True);
        Assert.That(reservation, Is.Not.Null);
        using var reservationScope = reservation!;
        AllocateWriteBuffers(ref buffers, workspace);
        Assert.That(buffers.GetWorkspaceBytes(), Is.EqualTo(workspace.WorkspaceBytes));

        Assert.That(
            lease.WriteEcsRecord(
                reservationScope,
                ref buffers,
                out var bytesWritten,
                out diagnostic,
                CancellationToken.None),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(bytesWritten, Is.EqualTo(workspace.RecordBytes));

        var assertions = new EcsAssertions(world.WorldGuid, expectedEntityId);
        Assert.That(
            EbinStructuralReader.Read(
                buffers.Destination[..bytesWritten],
                WorldPersistenceEbinLimits.Create(world.Options),
                EbinReadPolicy.Canonical,
                ref assertions,
                out var bytesConsumed,
                out var readDiagnostic),
            Is.EqualTo(EbinReadStatus.Complete));
        Assert.That(readDiagnostic, Is.Null);
        Assert.That(bytesConsumed, Is.EqualTo(bytesWritten));
    }

    private readonly struct EcsAssertions : IAction<EbinRecordReadView>
    {
        private readonly Guid? _entityId;
        private readonly Guid _worldGuid;

        internal EcsAssertions(Guid worldGuid, Guid? entityId)
        {
            _entityId = entityId;
            _worldGuid = worldGuid;
        }

        public readonly void Invoke(scoped EbinRecordReadView record)
        {
            Assert.That(record.Header.SchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment("state/ecs").SchemaId));
            Assert.That(record.Header.Major, Is.EqualTo(1));
            Assert.That(record.Header.Minor, Is.Zero);
            Assert.That(record.FieldCount, Is.EqualTo(5));

            var entities = record.GetField(0);
            Assert.That(entities.Tag, Is.EqualTo(1));
            Assert.That(entities.IsRequired, Is.True);
            Assert.That(entities.Kind, Is.EqualTo(24));
            Assert.That(entities.Count, Is.EqualTo(_entityId.HasValue ? 1 : 0));

            if (_entityId is Guid entityId)
            {
                var entity = record.GetRecordArrayElement(1, 0);
                Assert.That(entity.Header.SchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment("state/entity").SchemaId));
                Assert.That(new Guid(entity.GetField(0).Payload, bigEndian: true), Is.EqualTo(entityId));
            }
            else
            {
                AssertEmptyArray(entities, 1);
            }

            AssertEmptyArray(record.GetField(1), 2);
            AssertEmptyArray(record.GetField(2), 3);
            AssertEmptyArray(record.GetField(3), 4);

            var identityHistory = record.GetRecord(5);
            Assert.That(
                identityHistory.Header.SchemaId,
                Is.EqualTo(new ContentSchemaIdentityAssignment("state/identity-history").SchemaId));
            Assert.That(identityHistory.GetField(0).Tag, Is.EqualTo(1));
            Assert.That(
                new Guid(identityHistory.GetField(0).Payload, bigEndian: true),
                Is.EqualTo(_worldGuid));
            Assert.That(identityHistory.GetField(5).Count, Is.EqualTo(1));
            Assert.That(identityHistory.GetField(5).Kind, Is.EqualTo(24));
            Assert.That(identityHistory.GetField(6).Count, Is.EqualTo(_entityId.HasValue ? 1 : 0));
        }
    }
}
