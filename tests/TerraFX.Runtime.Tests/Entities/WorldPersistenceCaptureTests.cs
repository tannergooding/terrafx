// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldPersistenceCaptureTests
{
    private static World CreateSceneWorld()
    {
        var schemas = new CompiledContentSchemaRegistry([
            .. CoreContentSchemaCatalogue.PrimitiveSchemas,
            .. CoreContentSchemaCatalogue.FoundationSchemas,
            .. NativeAssetSchemaCatalogue.Schemas,
            .. SceneSchemaCatalogue.Schemas,
        ]);
        var registrationBuilder = new WorldRegistrationBuilder(schemas);
        _ = registrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
        var registration = registrationBuilder.Build();
        return World.Create(new WorldOptions(registration) {
            CpuWorkerCount = 0,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
    }

    private static World CreateWorld()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        return World.Create(new WorldOptions(registration) {
            CpuWorkerCount = 0,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
    }

    private static World CreateWorldWithout2DFrame()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        return World.Create(new WorldOptions(registration) {
            CpuWorkerCount = 0,
            Enable2DFrame = false,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
    }

    private static void ValidateFrameRecord(
        scoped ReadOnlySpan<byte> bytes,
        Guid worldGuid,
        SpatialFrame2? frame2,
        SpatialFrame3? frame3,
        EbinReadLimits limits)
    {
        var assertion = new FrameRecordAssertion(worldGuid, frame2, frame3);
        var status = EbinStructuralReader.Read(
            bytes,
            limits,
            EbinReadPolicy.Canonical,
            ref assertion,
            out var bytesConsumed,
            out var diagnostic);

        Assert.That(status, Is.EqualTo(EbinReadStatus.Complete), diagnostic?.ToString());
        Assert.That(bytesConsumed, Is.EqualTo(bytes.Length));
    }

    [Test]
    public void CaptureLeaseRejectsAfterCanonicalMutationIsUncommitted()
    {
        using var world = CreateWorld();
        _ = world.Step();
        _ = world.CreateEntity(new EntityDescription());

        _ = Assert.Throws<InvalidOperationException>(() => world.AcquirePersistenceCaptureLease());
    }

    [Test]
    public void CaptureLeaseRejectsStaleCopiesAndAllowsOnlyOneRelease()
    {
        using var world = CreateWorld();
        _ = world.Step();

        var lease = world.AcquirePersistenceCaptureLease();
        var staleCopy = lease;

        lease.Dispose();
        lease.Dispose();

        var staleCopyRejected = false;

        try
        {
            staleCopy.ValidateCurrent();
        }
        catch (InvalidOperationException)
        {
            staleCopyRejected = true;
        }

        Assert.That(staleCopyRejected, Is.True);

        var staleCopyReleaseRejected = false;

        try
        {
            staleCopy.Dispose();
        }
        catch (InvalidOperationException)
        {
            staleCopyReleaseRejected = true;
        }

        Assert.That(staleCopyReleaseRejected, Is.True);
        _ = world.Step();
    }

    [Test]
    public void CaptureLeaseRequiresCommittedMarkerAndBlocksWorldMutationUntilRelease()
    {
        using var world = CreateWorld();
        var entity = world.CreateEntity(new EntityDescription());

        _ = Assert.Throws<InvalidOperationException>(() => world.AcquirePersistenceCaptureLease());
        _ = world.Step();

        using (var lease = world.AcquirePersistenceCaptureLease())
        {
            lease.ValidateCurrent();
            Assert.That(lease.WorldGuid, Is.EqualTo(world.WorldGuid));
            Assert.That(lease.NextTickIndex, Is.EqualTo(world.NextTickIndex));
            Assert.That(lease.CompletedTick, Is.EqualTo(0));
            Assert.That(lease.CanonicalMutationRevision, Is.EqualTo(lease.CommittedMutationRevision));
            Assert.That(world.GetEntityId(entity), Is.Not.EqualTo(Guid.Empty));
            _ = Assert.Throws<InvalidOperationException>(() => world.Step());
        }

        _ = world.Step();
    }

    [Test]
    public void CaptureLeaseSnapshotsInitialFramesClockAndOrderedControlHistory()
    {
        using var world = CreateWorld();
        var initialFrame = world.CurrentFrame3;
        var branchId = Guid.NewGuid();

        world.SetTimeScale(2.0f);
        var nextFrame = world.CreateNextFrame3(
            initialFrame.Origin,
            initialFrame.Minimum,
            initialFrame.Maximum);
        Assert.That(world.SetFrame(nextFrame).Status, Is.EqualTo(FrameChangeStatus.Applied));
        world.BeginIdentityBranch(branchId);
        _ = world.Step();

        using var lease = world.AcquirePersistenceCaptureLease();
        Assert.That(lease.TickRate, Is.EqualTo(world.TickRate));
        Assert.That(lease.InitialTimeScale, Is.EqualTo(1.0f));
        Assert.That(lease.TimeScale, Is.EqualTo(2.0f));
        Assert.That(lease.TimeScaleSegmentSequence, Is.EqualTo(2));
        Assert.That(lease.InitialFrame3.Revision, Is.EqualTo(1));
        Assert.That(lease.Frame3.Revision, Is.EqualTo(2));
        Assert.That(lease.NextControlSequence, Is.EqualTo(4));
        Assert.That(lease.ControlRecordCount, Is.EqualTo(3));

        var timeScaleControl = lease.GetControlCapture(0);
        Assert.That(timeScaleControl.Kind, Is.EqualTo(WorldPersistenceControlKind.TimeScale));
        Assert.That(timeScaleControl.Sequence, Is.EqualTo(1));
        Assert.That(timeScaleControl.AppliedAtNextTick, Is.EqualTo(0));
        Assert.That(timeScaleControl.TimeScaleValue, Is.EqualTo(2.0f));
        Assert.That(timeScaleControl.SegmentSequence, Is.EqualTo(2));

        var frameControl = lease.GetControlCapture(1);
        Assert.That(frameControl.Kind, Is.EqualTo(WorldPersistenceControlKind.Frame3));
        Assert.That(frameControl.Sequence, Is.EqualTo(2));
        Assert.That(frameControl.Frame3?.Revision, Is.EqualTo(2));

        var branchControl = lease.GetControlCapture(2);
        Assert.That(branchControl.Kind, Is.EqualTo(WorldPersistenceControlKind.Branch));
        Assert.That(branchControl.Sequence, Is.EqualTo(3));
        Assert.That(branchControl.BranchId, Is.EqualTo(branchId));
        Assert.That(branchControl.LineageId, Is.EqualTo(world.ReplayLineageId));
        Assert.That(branchControl.BranchIndex, Is.EqualTo(1));
    }

    [Test]
    public void CapturePersistentComponentRecordFromCommittedWorld()
    {
        using var world = CreateSceneWorld();
        var entity = world.CreateEntity(new EntityDescription());
        world.Add<SceneComponentDefinitions.LocalTransformComponent>(entity);
        _ = world.Step();

        var componentAssignment = new ContentSchemaIdentityAssignment("scene/local-transform");
        var componentSchema = new ContentSchemaReference(
            componentAssignment.SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        using var lease = world.AcquirePersistenceCaptureLease();
        var byteCount = lease.CountComponentStateRecord(entity, componentSchema, CancellationToken.None);
        Assert.That(byteCount > 0, Is.True);

        var encoded = new byte[checked((int)byteCount)];
        var written = lease.WriteComponentStateRecord(entity, componentSchema, encoded, CancellationToken.None);
        var expected = TerraFX.Runtime.Tests.Scenes.SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            componentAssignment.SchemaId,
            1,
            0,
            TerraFX.Runtime.Tests.Scenes.SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(1, 15, 0, 0, 0),
            TerraFX.Runtime.Tests.Scenes.SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(2, 17, 0, 0, 0, 1),
            TerraFX.Runtime.Tests.Scenes.SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(3, 15, 1, 1, 1));

        Assert.That(written, Is.EqualTo(byteCount));
        Assert.That(encoded, Is.EqualTo(expected));
    }

    [Test]
    public void CapturesIdentityHistoryIncludingRetiredAssignmentsAndBranches()
    {
        using var world = CreateWorld();
        var firstEntity = world.CreateEntity(new EntityDescription());
        var firstEntityId = world.GetEntityId(firstEntity);
        _ = world.Step();

        var rootBranchId = world.IdentityBranchId;
        var worldGuid = world.WorldGuid;
        var lineageId = world.ReplayLineageId;
        var branchCreatedAtNextTick = world.NextTickIndex;
        var branchId = Guid.NewGuid();
        world.BeginIdentityBranch(branchId);
        var secondEntity = world.CreateEntity(new EntityDescription());
        var secondEntityId = world.GetEntityId(secondEntity);
        _ = world.Step();
        world.DestroyEntity(secondEntity, HierarchyDestroyMode.DetachChildrenKeepWorld);
        _ = world.Step();

        using var lease = world.AcquirePersistenceCaptureLease();
        var history = lease.GetIdentityHistoryCapture();
        Assert.That(history.WorldGuid, Is.EqualTo(worldGuid));
        Assert.That(history.ReplayLineageId, Is.EqualTo(lineageId));
        Assert.That(history.ActiveBranchId, Is.EqualTo(branchId));
        Assert.That(history.IssuanceNamespaceId, Is.EqualTo(branchId));
        Assert.That(history.AssignmentCount, Is.EqualTo(2));
        Assert.That(history.NextAssignmentOrdinal, Is.EqualTo(3));
        Assert.That(history.PageCount, Is.EqualTo(1));
        Assert.That(history.BranchCount, Is.EqualTo(2));

        var branches = history.GetBranchEnumerator();
        Assert.That(branches.MoveNext(), Is.True);
        Assert.That(branches.CurrentDirectoryIndex, Is.EqualTo(0));
        Assert.That(branches.CurrentBranchId, Is.EqualTo(rootBranchId));
        Assert.That(branches.CurrentNamespaceId, Is.EqualTo(worldGuid));
        Assert.That(branches.CurrentLineageId, Is.EqualTo(lineageId));
        Assert.That(branches.CurrentParentBranchId, Is.Null);
        Assert.That(branches.CurrentCreatedAtNextTick, Is.Zero);
        Assert.That(branches.CurrentFirstAssignmentOrdinal, Is.EqualTo(1));
        Assert.That(branches.CurrentKind, Is.EqualTo((byte)1));
        Assert.That(branches.MoveNext(), Is.True);
        Assert.That(branches.CurrentDirectoryIndex, Is.EqualTo(1));
        Assert.That(branches.CurrentBranchId, Is.EqualTo(branchId));
        Assert.That(branches.CurrentNamespaceId, Is.EqualTo(branchId));
        Assert.That(branches.CurrentLineageId, Is.EqualTo(lineageId));
        Assert.That(branches.CurrentParentBranchId, Is.EqualTo(rootBranchId));
        Assert.That(branches.CurrentCreatedAtNextTick, Is.EqualTo(branchCreatedAtNextTick));
        Assert.That(branches.CurrentFirstAssignmentOrdinal, Is.EqualTo(2));
        Assert.That(branches.CurrentKind, Is.EqualTo((byte)2));
        Assert.That(branches.MoveNext(), Is.False);

        var assignments = history.GetAssignmentEnumerator();
        Assert.That(assignments.MoveNext(), Is.True);
        Assert.That(assignments.CurrentAssignmentOrdinal, Is.EqualTo(1));
        Assert.That(assignments.CurrentStableId, Is.EqualTo(firstEntityId));
        Assert.That(assignments.CurrentDirectoryIndex, Is.EqualTo(0));
        Assert.That(assignments.CurrentKind, Is.EqualTo((byte)1));
        Assert.That(assignments.CurrentOrigin, Is.EqualTo((byte)1));
        Assert.That(assignments.CurrentMetadata, Is.EqualTo(0x5400u));
        Assert.That(assignments.CurrentIsLive, Is.True);
        Assert.That(assignments.CurrentPageIndex, Is.Zero);
        Assert.That(assignments.CurrentPageOffset, Is.Zero);
        Assert.That(assignments.MoveNext(), Is.True);
        Assert.That(assignments.CurrentAssignmentOrdinal, Is.EqualTo(2));
        Assert.That(assignments.CurrentStableId, Is.EqualTo(secondEntityId));
        Assert.That(assignments.CurrentDirectoryIndex, Is.EqualTo(1));
        Assert.That(assignments.CurrentKind, Is.EqualTo((byte)1));
        Assert.That(assignments.CurrentOrigin, Is.EqualTo((byte)1));
        Assert.That(assignments.CurrentMetadata, Is.EqualTo(0x1401u));
        Assert.That(assignments.CurrentIsLive, Is.False);
        Assert.That(assignments.CurrentPageIndex, Is.Zero);
        Assert.That(assignments.CurrentPageOffset, Is.EqualTo(1));
        Assert.That(assignments.MoveNext(), Is.False);

        var stableIds = new Guid[2];
        var metadata = new uint[2];
        Assert.That(history.GetPageRecordCount(0), Is.EqualTo(2));
        Assert.That(history.CopyPage(0, stableIds, metadata), Is.EqualTo(1));
        Assert.That(stableIds, Is.EqualTo([firstEntityId, secondEntityId]));
        Assert.That(metadata, Is.EqualTo([0x5400u, 0x1401u]));
    }

    [Test]
    public void CopiesIdentityHistoryAcrossCanonicalPageBoundary()
    {
        using var world = CreateWorld();
        var expectedIds = new Guid[StableIdentityLedger.RecordsPerPage + 1];

        for (var index = 0; index < expectedIds.Length; index++)
        {
            var entity = world.CreateEntity(new EntityDescription());
            expectedIds[index] = world.GetEntityId(entity);
        }

        _ = world.Step();

        using var lease = world.AcquirePersistenceCaptureLease();
        var history = lease.GetIdentityHistoryCapture();
        Assert.That(history.PageCount, Is.EqualTo(2));
        Assert.That(history.GetPageRecordCount(0), Is.EqualTo(StableIdentityLedger.RecordsPerPage));
        Assert.That(history.GetPageRecordCount(1), Is.EqualTo(1));

        var firstPageIds = new Guid[StableIdentityLedger.RecordsPerPage];
        var firstPageMetadata = new uint[StableIdentityLedger.RecordsPerPage];
        Assert.That(history.CopyPage(0, firstPageIds, firstPageMetadata), Is.EqualTo(1));
        Assert.That(firstPageIds[0], Is.EqualTo(expectedIds[0]));
        Assert.That(firstPageIds[^1], Is.EqualTo(expectedIds[StableIdentityLedger.RecordsPerPage - 1]));
        Assert.That(firstPageMetadata, Has.All.EqualTo(0x5400u));

        var finalPageIds = new Guid[1];
        var finalPageMetadata = new uint[1];
        Assert.That(history.CopyPage(1, finalPageIds, finalPageMetadata), Is.EqualTo(StableIdentityLedger.RecordsPerPage + 1L));
        Assert.That(finalPageIds[0], Is.EqualTo(expectedIds[^1]));
        Assert.That(finalPageMetadata, Is.EqualTo([0x5400u]));
    }

    [Test]
    public void EnumeratesCommittedEntitiesAndArchetypeMembership()
    {
        using var world = CreateSceneWorld();
        var firstEntity = world.CreateEntity(new EntityDescription());
        var secondEntity = world.CreateEntity(new EntityDescription());
        world.Add<SceneComponentDefinitions.LocalTransformComponent>(firstEntity);
        _ = world.Step();

        var firstEntityId = world.GetEntityId(firstEntity);
        var secondEntityId = world.GetEntityId(secondEntity);
        var componentAssignment = new ContentSchemaIdentityAssignment("scene/local-transform");
        var componentSchema = new ContentSchemaReference(
            componentAssignment.SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        using var lease = world.AcquirePersistenceCaptureLease();
        var enumerator = lease.GetEntityStateEnumerator();
        var observations = new Dictionary<
            Guid,
            (uint AssignmentOrdinal, int PersistentCount, ContentSchemaReference? PersistentSchema, Guid? SceneInstanceId, int TransientCount, int TagCount)>(2);

        while (enumerator.MoveNext())
        {
            var persistentCount = enumerator.CurrentPersistentComponentCount;
            ContentSchemaReference? persistentSchema = persistentCount == 0
                ? null
                : enumerator.GetPersistentComponentSchema(0);
            var observation = (
                enumerator.CurrentAssignmentOrdinal,
                persistentCount,
                persistentSchema,
                enumerator.CurrentSceneInstanceId,
                enumerator.CurrentTransientSchemaCount,
                enumerator.CurrentTagCount);
            Assert.That(observations.TryAdd(enumerator.CurrentEntityId, observation), Is.True);
        }

        Assert.That(observations.Count, Is.EqualTo(2));
        Assert.That(
            observations[firstEntityId],
            Is.EqualTo((1U, 1, (ContentSchemaReference?)componentSchema, (Guid?)null, 0, 0)));
        Assert.That(
            observations[secondEntityId],
            Is.EqualTo((2U, 0, (ContentSchemaReference?)null, (Guid?)null, 0, 0)));
    }

    [Test]
    public void EnumeratesEntityRowsInCanonicalEntityIdOrder()
    {
        using var world = CreateWorld();
        var highId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var middleId = Guid.Parse("00000002-0000-0000-0000-000000000002");
        var lowId = Guid.Parse("00000001-0000-0000-0000-000000000003");
        _ = world.CreateEntity(new EntityDescription(persistentId: highId));
        _ = world.CreateEntity(new EntityDescription(persistentId: middleId));
        _ = world.CreateEntity(new EntityDescription(persistentId: lowId));
        _ = world.Step();

        using var lease = world.AcquirePersistenceCaptureLease();
        Assert.That(lease.GetEntityStateCount(), Is.EqualTo(3));
        var captureRows = new World.EntityStateCaptureRow[3];
        var entities = lease.GetCanonicalEntityStateEnumerator(captureRows);
        Assert.That(entities.MoveNext(), Is.True);
        Assert.That(entities.CurrentEntityId, Is.EqualTo(lowId));
        Assert.That(entities.CurrentAssignmentOrdinal, Is.EqualTo(3u));
        Assert.That(entities.CurrentSceneInstanceId, Is.Null);
        Assert.That(entities.MoveNext(), Is.True);
        Assert.That(entities.CurrentEntityId, Is.EqualTo(middleId));
        Assert.That(entities.CurrentAssignmentOrdinal, Is.EqualTo(2u));
        Assert.That(entities.MoveNext(), Is.True);
        Assert.That(entities.CurrentEntityId, Is.EqualTo(highId));
        Assert.That(entities.CurrentAssignmentOrdinal, Is.EqualTo(1u));
        Assert.That(entities.MoveNext(), Is.False);
    }

    [Test]
    public void EnumeratesPersistentComponentsInStableWorkPlanOrder()
    {
        using var world = CreateSceneWorld();
        var firstEntity = world.CreateEntity(new EntityDescription());
        var secondEntity = world.CreateEntity(new EntityDescription());
        world.Add<SceneComponentDefinitions.LocalTransformComponent>(firstEntity);
        world.Add<SceneComponentDefinitions.LocalTransformComponent>(secondEntity);
        _ = world.Step();

        var componentAssignment = new ContentSchemaIdentityAssignment("scene/local-transform");
        var componentSchema = new ContentSchemaReference(
            componentAssignment.SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        using var lease = world.AcquirePersistenceCaptureLease();
        var enumerator = lease.GetComponentStateEnumerator();
        var entityIds = new List<Guid>(2);
        var schemas = new List<ContentSchemaReference>(2);

        while (enumerator.MoveNext())
        {
            entityIds.Add(world.GetEntityId(enumerator.CurrentEntity));
            schemas.Add(enumerator.CurrentSchema);
        }

        Guid[] expectedEntityIds = [world.GetEntityId(firstEntity), world.GetEntityId(secondEntity)];
        ContentSchemaReference[] expectedSchemas = [componentSchema, componentSchema];
        Assert.That(entityIds, Is.EqualTo(expectedEntityIds));
        Assert.That(schemas, Is.EqualTo(expectedSchemas));

        var staleCurrentRejected = false;

        try
        {
            _ = enumerator.CurrentEntity;
        }
        catch (InvalidOperationException)
        {
            staleCurrentRejected = true;
        }

        Assert.That(staleCurrentRejected, Is.True);
    }

    [Test]
    public void WritesCanonicalClockAndTimeScaleSegmentsFromCommittedControls()
    {
        using var world = CreateWorld();
        world.SetTimeScale(-0.0f);
        var currentFrame = world.CurrentFrame3;
        Assert.That(
            world.SetFrame(world.CreateNextFrame3(currentFrame.Origin, currentFrame.Minimum, currentFrame.Maximum)).Status,
            Is.EqualTo(FrameChangeStatus.Applied));
        world.BeginIdentityBranch(Guid.NewGuid());
        world.SetTimeScale(1.5f);
        _ = world.Step();

        using var lease = world.AcquirePersistenceCaptureLease();
        var measureStatus = lease.MeasureClockRecord(out var workspace, out var measureDiagnostic, CancellationToken.None);
        Assert.That(measureStatus, Is.EqualTo(EbinWriteStatus.Complete), measureDiagnostic?.ToString());
        Assert.That(workspace.ControlRecordCount, Is.EqualTo(4));
        Assert.That(workspace.SegmentCount, Is.EqualTo(3));

        var buffers = new WorldPersistenceClockBuffers {
            ClockCandidateStorage = new byte[workspace.RecordBytes],
            ClockPayloadStorage = new byte[workspace.ClockPayloadStorageBytes],
            Destination = new byte[workspace.RecordBytes],
            FrameCandidateStorage = new byte[workspace.FrameCandidateStorageBytes],
            FramePayloadStorage = new byte[workspace.FramePayloadStorageBytes],
            FrameProfileCandidateStorage = new byte[workspace.InitialFrame3.ProfileRecordBytes],
            FrameProfileStorage = new byte[workspace.InitialFrame3.ProfileRecordBytes],
            InitialFrame2Storage = new byte[workspace.InitialFrame2.RecordBytes],
            InitialFrame3Storage = new byte[workspace.InitialFrame3.RecordBytes],
            SegmentArrayPayloadStorage = new byte[workspace.SegmentArrayPayloadBytes],
            SegmentCandidateStorage = new byte[workspace.SegmentRecordBytes],
            SegmentSlices = new EbinPayloadSlice[workspace.SegmentCount],
            SegmentStorage = new byte[workspace.SegmentStorageBytes],
        };
        Assert.That(buffers.GetWorkspaceBytes(), Is.EqualTo(workspace.WorkspaceBytes));

        int bytesWritten;

        if (!world.TryReservePersistenceAdmissionBytes(workspace.WorkspaceBytes, out var reservation)
            || (reservation is null))
        {
            throw new AssertionException("The World refused the measured clock workspace reservation.");
        }

        using (reservation)
        {
            var writeStatus = reservation.WriteClockRecord(
                world,
                lease,
                workspace,
                ref buffers,
                out bytesWritten,
                out var writeDiagnostic,
                CancellationToken.None);
            Assert.That(writeStatus, Is.EqualTo(EbinWriteStatus.Complete), writeDiagnostic?.ToString());
            Assert.That(bytesWritten, Is.EqualTo(workspace.RecordBytes));
        }

        var clockAssertion = new ClockRecordAssertion(
            nextTick: 1,
            tickRate: world.TickRate,
            timeScale: 1.5f,
            nextPublicationSequence: 2,
            hasInitialFrame2: true,
            [
                new TimeSegmentExpectation(1, 0, 1.0f),
                new TimeSegmentExpectation(2, 0, -0.0f),
                new TimeSegmentExpectation(3, 0, 1.5f),
            ]);
        var readStatus = EbinStructuralReader.Read(
            buffers.Destination,
            WorldPersistenceEbinLimits.Create(world.Options),
            EbinReadPolicy.Canonical,
            ref clockAssertion,
            out var bytesConsumed,
            out var readDiagnostic);
        Assert.That(readStatus, Is.EqualTo(EbinReadStatus.Complete), readDiagnostic?.ToString());
        Assert.That(bytesConsumed, Is.EqualTo(bytesWritten));
    }

    [Test]
    public void WritesClockWithoutTheOptional2DFrame()
    {
        using var world = CreateWorldWithout2DFrame();
        _ = world.Step();

        using var lease = world.AcquirePersistenceCaptureLease();
        var measureStatus = lease.MeasureClockRecord(out var workspace, out var measureDiagnostic, CancellationToken.None);
        Assert.That(measureStatus, Is.EqualTo(EbinWriteStatus.Complete), measureDiagnostic?.ToString());
        Assert.That(workspace.HasInitialFrame2, Is.False);
        Assert.That(workspace.SegmentCount, Is.EqualTo(1));

        var buffers = new WorldPersistenceClockBuffers {
            ClockCandidateStorage = new byte[workspace.RecordBytes],
            ClockPayloadStorage = new byte[workspace.ClockPayloadStorageBytes],
            Destination = new byte[workspace.RecordBytes],
            FrameCandidateStorage = new byte[workspace.FrameCandidateStorageBytes],
            FramePayloadStorage = new byte[workspace.FramePayloadStorageBytes],
            FrameProfileCandidateStorage = new byte[workspace.InitialFrame3.ProfileRecordBytes],
            FrameProfileStorage = new byte[workspace.InitialFrame3.ProfileRecordBytes],
            InitialFrame2Storage = [],
            InitialFrame3Storage = new byte[workspace.InitialFrame3.RecordBytes],
            SegmentArrayPayloadStorage = new byte[workspace.SegmentArrayPayloadBytes],
            SegmentCandidateStorage = new byte[workspace.SegmentRecordBytes],
            SegmentSlices = new EbinPayloadSlice[workspace.SegmentCount],
            SegmentStorage = new byte[workspace.SegmentStorageBytes],
        };
        Assert.That(buffers.GetWorkspaceBytes(), Is.EqualTo(workspace.WorkspaceBytes));

        if (!world.TryReservePersistenceAdmissionBytes(workspace.WorkspaceBytes, out var reservation)
            || (reservation is null))
        {
            throw new AssertionException("The World refused the measured clock workspace reservation.");
        }

        using (reservation)
        {
            var writeStatus = reservation.WriteClockRecord(
                world,
                lease,
                workspace,
                ref buffers,
                out var bytesWritten,
                out var writeDiagnostic,
                CancellationToken.None);
            Assert.That(writeStatus, Is.EqualTo(EbinWriteStatus.Complete), writeDiagnostic?.ToString());
            Assert.That(bytesWritten, Is.EqualTo(workspace.RecordBytes));

            var clockAssertion = new ClockRecordAssertion(
                nextTick: 1,
                tickRate: world.TickRate,
                timeScale: 1.0f,
                nextPublicationSequence: 2,
                hasInitialFrame2: false,
                [new TimeSegmentExpectation(1, 0, 1.0f)]);
            var readStatus = EbinStructuralReader.Read(
                buffers.Destination,
                WorldPersistenceEbinLimits.Create(world.Options),
                EbinReadPolicy.Canonical,
                ref clockAssertion,
                out var bytesConsumed,
                out var readDiagnostic);
            Assert.That(readStatus, Is.EqualTo(EbinReadStatus.Complete), readDiagnostic?.ToString());
            Assert.That(bytesConsumed, Is.EqualTo(bytesWritten));
        }
    }

    [Test]
    public void WritesFrameRecordsThroughTheCanonicalFoundationCodec()
    {
        using var world = CreateWorld();
        _ = world.Step();
        using var lease = world.AcquirePersistenceCaptureLease();
        var limits = WorldPersistenceEbinLimits.Create(world.Options);
        var frame2 = lease.Frame2 ?? throw new AssertionException("The default World did not capture its admitted 2D frame.");
        var frame3 = lease.Frame3;

        var frame2MeasureStatus = WorldPersistenceSpatialFrameWriter.MeasureFrame2(
            lease.WorldGuid,
            world.OwnerId,
            frame2,
            limits,
            out var frame2Workspace,
            out var frame2MeasureDiagnostic);
        Assert.That(frame2MeasureStatus, Is.EqualTo(EbinWriteStatus.Complete), frame2MeasureDiagnostic?.ToString());

        var frame2Destination = new byte[frame2Workspace.RecordBytes];
        var frame2Buffers = new WorldPersistenceSpatialFrameBuffers {
            Destination = frame2Destination,
            FrameCandidateStorage = new byte[frame2Workspace.RecordBytes],
            FramePayloadStorage = new byte[frame2Workspace.FramePayloadStorageBytes],
            ProfileCandidateStorage = new byte[frame2Workspace.ProfileRecordBytes],
            ProfileStorage = new byte[frame2Workspace.ProfileRecordBytes],
        };
        Assert.That(frame2Buffers.GetWorkspaceBytes(), Is.EqualTo(frame2Workspace.WorkspaceBytes));
        var frame2WriteStatus = WorldPersistenceSpatialFrameWriter.WriteFrame2(
            lease.WorldGuid,
            world.OwnerId,
            frame2,
            frame2Workspace,
            limits,
            ref frame2Buffers,
            out var frame2BytesWritten,
            out var frame2WriteDiagnostic);
        Assert.That(frame2WriteStatus, Is.EqualTo(EbinWriteStatus.Complete), frame2WriteDiagnostic?.ToString());
        Assert.That(frame2BytesWritten, Is.EqualTo(frame2Workspace.RecordBytes));
        ValidateFrameRecord(frame2Destination, lease.WorldGuid, frame2, null, limits);

        var frame3MeasureStatus = WorldPersistenceSpatialFrameWriter.MeasureFrame3(
            lease.WorldGuid,
            world.OwnerId,
            frame3,
            limits,
            out var frame3Workspace,
            out var frame3MeasureDiagnostic);
        Assert.That(frame3MeasureStatus, Is.EqualTo(EbinWriteStatus.Complete), frame3MeasureDiagnostic?.ToString());

        var frame3Destination = new byte[frame3Workspace.RecordBytes];
        var frame3Buffers = new WorldPersistenceSpatialFrameBuffers {
            Destination = frame3Destination,
            FrameCandidateStorage = new byte[frame3Workspace.RecordBytes],
            FramePayloadStorage = new byte[frame3Workspace.FramePayloadStorageBytes],
            ProfileCandidateStorage = new byte[frame3Workspace.ProfileRecordBytes],
            ProfileStorage = new byte[frame3Workspace.ProfileRecordBytes],
        };
        Assert.That(frame3Buffers.GetWorkspaceBytes(), Is.EqualTo(frame3Workspace.WorkspaceBytes));
        var frame3WriteStatus = WorldPersistenceSpatialFrameWriter.WriteFrame3(
            lease.WorldGuid,
            world.OwnerId,
            frame3,
            frame3Workspace,
            limits,
            ref frame3Buffers,
            out var frame3BytesWritten,
            out var frame3WriteDiagnostic);
        Assert.That(frame3WriteStatus, Is.EqualTo(EbinWriteStatus.Complete), frame3WriteDiagnostic?.ToString());
        Assert.That(frame3BytesWritten, Is.EqualTo(frame3Workspace.RecordBytes));
        ValidateFrameRecord(frame3Destination, lease.WorldGuid, null, frame3, limits);
    }

    private readonly struct ClockRecordAssertion : IAction<EbinRecordReadView>
    {
        private readonly long _nextPublicationSequence;
        private readonly long _nextTick;
        private readonly TimeSegmentExpectation[] _segments;
        private readonly float _timeScale;
        private readonly int _tickRate;
        private readonly bool _hasInitialFrame2;

        internal ClockRecordAssertion(
            long nextTick,
            int tickRate,
            float timeScale,
            long nextPublicationSequence,
            bool hasInitialFrame2,
            TimeSegmentExpectation[] segments)
        {
            _nextPublicationSequence = nextPublicationSequence;
            _nextTick = nextTick;
            _segments = segments;
            _timeScale = timeScale;
            _tickRate = tickRate;
            _hasInitialFrame2 = hasInitialFrame2;
        }

        private static void AssertFrameField(EbinFieldReadView field, uint tag, string schemaName)
        {
            Assert.That(field.Tag, Is.EqualTo(tag));
            Assert.That(field.Kind, Is.EqualTo((ushort)22));
            Assert.That(EbinStructuralReader.ReadHeader(
                field.Payload,
                EbinReadLimits.Foundation,
                out var header,
                out _,
                out var diagnostic), Is.EqualTo(EbinReadStatus.Complete), diagnostic?.ToString());
            Assert.That(header.SchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment(schemaName).SchemaId));
        }

        public void Invoke(scoped EbinRecordReadView record)
        {
            Assert.That(record.Header.SchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment("state/clock").SchemaId));
            Assert.That(record.Header.Major, Is.EqualTo(1));
            Assert.That(record.Header.Minor, Is.Zero);
            Assert.That(record.FieldCount, Is.EqualTo(_hasInitialFrame2 ? 10 : 9));

            var nextTickField = record.GetField(0);
            Assert.That(nextTickField.Tag, Is.EqualTo(1U));
            Assert.That(nextTickField.Kind, Is.EqualTo((ushort)8));
            Assert.That(BinaryPrimitives.ReadInt64LittleEndian(nextTickField.Payload), Is.EqualTo(_nextTick));

            var tickRateField = record.GetField(1);
            Assert.That(tickRateField.Tag, Is.EqualTo(2U));
            Assert.That(tickRateField.Kind, Is.EqualTo((ushort)3));
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(tickRateField.Payload), Is.EqualTo(_tickRate));

            var timeScaleField = record.GetField(2);
            Assert.That(timeScaleField.Tag, Is.EqualTo(3U));
            Assert.That(timeScaleField.Kind, Is.EqualTo((ushort)10));
            Assert.That(
                BinaryPrimitives.ReadInt32LittleEndian(timeScaleField.Payload),
                Is.EqualTo(BitConverter.SingleToInt32Bits(_timeScale)));

            var publicationField = record.GetField(3);
            Assert.That(publicationField.Tag, Is.EqualTo(4U));
            Assert.That(publicationField.Kind, Is.EqualTo((ushort)8));
            Assert.That(BinaryPrimitives.ReadInt64LittleEndian(publicationField.Payload), Is.EqualTo(_nextPublicationSequence));

            var segmentArray = record.GetField(4);
            Assert.That(segmentArray.Tag, Is.EqualTo(5U));
            Assert.That(segmentArray.Kind, Is.EqualTo((ushort)24));
            Assert.That(segmentArray.Count, Is.EqualTo(checked((ulong)_segments.Length)));
            var segmentArrayPayload = segmentArray.Payload;
            Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(segmentArrayPayload), Is.EqualTo(checked((ulong)_segments.Length)));
            var firstChildOffset = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(segmentArrayPayload[8..]));

            for (var index = 0; index < _segments.Length; index++)
            {
                var start = (index == 0)
                    ? firstChildOffset
                    : checked((int)BinaryPrimitives.ReadUInt64LittleEndian(segmentArrayPayload[(16 + ((index - 1) * 8))..]));
                var end = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(segmentArrayPayload[(16 + (index * 8))..]));
                var segment = new TimeSegmentAssertion(_segments[index]);
                var segmentStatus = EbinStructuralReader.Read(
                    segmentArrayPayload[start..end],
                    EbinReadLimits.Foundation,
                    EbinReadPolicy.Canonical,
                    ref segment,
                    out var segmentBytesConsumed,
                    out var segmentDiagnostic);
                Assert.That(segmentStatus, Is.EqualTo(EbinReadStatus.Complete), segmentDiagnostic?.ToString());
                Assert.That(segmentBytesConsumed, Is.EqualTo(end - start));
            }

            var replayCompleteField = record.GetField(5);
            Assert.That(replayCompleteField.Tag, Is.EqualTo(6U));
            Assert.That(replayCompleteField.Kind, Is.EqualTo((ushort)9));
            Assert.That(replayCompleteField.Payload[0], Is.Zero);

            var currentSegmentField = record.GetField(6);
            Assert.That(currentSegmentField.Tag, Is.EqualTo(7U));
            Assert.That(BinaryPrimitives.ReadInt64LittleEndian(currentSegmentField.Payload), Is.EqualTo(_segments.Length));

            if (_hasInitialFrame2)
            {
                AssertFrameField(record.GetField(7), 8, "foundation/frame2");
            }

            var frame3Index = _hasInitialFrame2 ? 8 : 7;
            AssertFrameField(record.GetField(frame3Index), 9, "foundation/frame3");
            var historyBaseField = record.GetField(_hasInitialFrame2 ? 9 : 8);
            Assert.That(historyBaseField.Tag, Is.EqualTo(10U));
            Assert.That(BinaryPrimitives.ReadInt64LittleEndian(historyBaseField.Payload), Is.Zero);
        }
    }

    private readonly struct FrameRecordAssertion : IAction<EbinRecordReadView>
    {
        private readonly SpatialFrame2? _frame2;
        private readonly SpatialFrame3? _frame3;
        private readonly Guid _worldGuid;

        internal FrameRecordAssertion(Guid worldGuid, SpatialFrame2? frame2, SpatialFrame3? frame3)
        {
            if (worldGuid == Guid.Empty)
            {
                throw new ArgumentException("The containing World identity cannot be empty.", nameof(worldGuid));
            }

            if (frame2.HasValue == frame3.HasValue)
            {
                throw new ArgumentException("Exactly one spatial frame dimension must be selected.");
            }

            _worldGuid = worldGuid;
            _frame2 = frame2;
            _frame3 = frame3;
        }

        public void Invoke(scoped EbinRecordReadView record)
        {
            var isTwoDimensional = _frame2 is not null;
            var frameSchemaId = new ContentSchemaIdentityAssignment(isTwoDimensional ? "foundation/frame2" : "foundation/frame3").SchemaId;
            Assert.That(record.Header.SchemaId, Is.EqualTo(frameSchemaId));
            Assert.That(record.Header.Major, Is.EqualTo(1));
            Assert.That(record.Header.Minor, Is.Zero);
            Assert.That(record.FieldCount, Is.EqualTo(6));

            var worldField = record.GetField(0);
            Assert.That(worldField.Tag, Is.EqualTo(1U));
            Assert.That(worldField.Kind, Is.EqualTo((ushort)11));
            Assert.That(new Guid(worldField.Payload, bigEndian: true), Is.EqualTo(_worldGuid));

            var profileField = record.GetField(1);
            Assert.That(profileField.Tag, Is.EqualTo(2U));
            Assert.That(profileField.Kind, Is.EqualTo((ushort)22));
            Assert.That(EbinStructuralReader.Validate(
                profileField.Payload,
                EbinReadLimits.Foundation,
                EbinReadPolicy.Canonical,
                out var profileRecordBytesConsumed,
                out var profileRecordDiagnostic), Is.EqualTo(EbinReadStatus.Complete), profileRecordDiagnostic?.ToString());
            Assert.That(EbinStructuralReader.ReadHeader(
                profileField.Payload,
                EbinReadLimits.Foundation,
                out var profileHeader,
                out _,
                out var profileDiagnostic), Is.EqualTo(EbinReadStatus.Complete), profileDiagnostic?.ToString());
            Assert.That(profileHeader.SchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment("foundation/profile").SchemaId));
            Assert.That(profileHeader.Major, Is.EqualTo(2));
            Assert.That(profileHeader.Minor, Is.Zero);
            Assert.That(profileHeader.FieldCount, Is.EqualTo(9));
            Assert.That(profileRecordBytesConsumed, Is.EqualTo(profileField.Payload.Length));

            Span<byte> expectedOrigin = stackalloc byte[24];
            EbinWriteStatus originStatus;
            int originBytesWritten;

            if (isTwoDimensional)
            {
                originStatus = EbinPayloadWriter.Write(_frame2!.Value.Origin, expectedOrigin, out originBytesWritten, out _);
            }
            else
            {
                originStatus = EbinPayloadWriter.Write(_frame3!.Value.Origin, expectedOrigin, out originBytesWritten, out _);
            }

            Assert.That(originStatus, Is.EqualTo(EbinWriteStatus.Complete));
            Assert.That(record.GetField(2).Tag, Is.EqualTo(3U));
            Assert.That(record.GetField(2).Kind, Is.EqualTo(isTwoDimensional ? (ushort)20 : (ushort)21));
            Assert.That(
                record.GetField(2).Payload.SequenceEqual(expectedOrigin[..originBytesWritten]),
                Is.True);

            var expectedRevision = checked((ulong)(isTwoDimensional ? _frame2!.Value.Revision : _frame3!.Value.Revision));
            Assert.That(record.GetField(3).Tag, Is.EqualTo(4U));
            Assert.That(record.GetField(3).Kind, Is.EqualTo((ushort)4));
            Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(record.GetField(3).Payload), Is.EqualTo(expectedRevision));

            Span<byte> expectedMinimum = stackalloc byte[12];
            EbinWriteStatus minimumStatus;
            int minimumBytesWritten;

            if (isTwoDimensional)
            {
                minimumStatus = EbinPayloadWriter.Write(_frame2!.Value.Minimum, expectedMinimum, out minimumBytesWritten, out _);
            }
            else
            {
                minimumStatus = EbinPayloadWriter.Write(_frame3!.Value.Minimum, expectedMinimum, out minimumBytesWritten, out _);
            }

            Assert.That(minimumStatus, Is.EqualTo(EbinWriteStatus.Complete));
            Assert.That(record.GetField(4).Tag, Is.EqualTo(5U));
            Assert.That(record.GetField(4).Kind, Is.EqualTo(isTwoDimensional ? (ushort)14 : (ushort)15));
            Assert.That(record.GetField(4).Payload.SequenceEqual(expectedMinimum[..minimumBytesWritten]), Is.True);

            Span<byte> expectedMaximum = stackalloc byte[12];
            EbinWriteStatus maximumStatus;
            int maximumBytesWritten;

            if (isTwoDimensional)
            {
                maximumStatus = EbinPayloadWriter.Write(_frame2!.Value.Maximum, expectedMaximum, out maximumBytesWritten, out _);
            }
            else
            {
                maximumStatus = EbinPayloadWriter.Write(_frame3!.Value.Maximum, expectedMaximum, out maximumBytesWritten, out _);
            }

            Assert.That(maximumStatus, Is.EqualTo(EbinWriteStatus.Complete));
            Assert.That(record.GetField(5).Tag, Is.EqualTo(6U));
            Assert.That(record.GetField(5).Kind, Is.EqualTo(isTwoDimensional ? (ushort)14 : (ushort)15));
            Assert.That(record.GetField(5).Payload.SequenceEqual(expectedMaximum[..maximumBytesWritten]), Is.True);
        }
    }

    private readonly struct TimeSegmentAssertion : IAction<EbinRecordReadView>
    {
        private readonly TimeSegmentExpectation _expected;

        internal TimeSegmentAssertion(TimeSegmentExpectation expected)
        {
            _expected = expected;
        }

        public void Invoke(scoped EbinRecordReadView record)
        {
            Assert.That(record.Header.SchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment("state/time-segment").SchemaId));
            Assert.That(record.Header.Major, Is.EqualTo(1));
            Assert.That(record.Header.Minor, Is.Zero);
            Assert.That(record.FieldCount, Is.EqualTo(3));
            Assert.That(record.GetField(2).Kind, Is.EqualTo((ushort)10));
            Assert.That(BinaryPrimitives.ReadInt64LittleEndian(record.GetField(0).Payload), Is.EqualTo(_expected.Sequence));
            Assert.That(BinaryPrimitives.ReadInt64LittleEndian(record.GetField(1).Payload), Is.EqualTo(_expected.StartNextTick));
            Assert.That(
                BinaryPrimitives.ReadInt32LittleEndian(record.GetField(2).Payload),
                Is.EqualTo(BitConverter.SingleToInt32Bits(_expected.TimeScale)));
        }
    }

    private readonly struct TimeSegmentExpectation
    {
        internal TimeSegmentExpectation(long sequence, long startNextTick, float timeScale)
        {
            Sequence = sequence;
            StartNextTick = startNextTick;
            TimeScale = timeScale;
        }

        internal long Sequence { get; }

        internal long StartNextTick { get; }

        internal float TimeScale { get; }
    }
}
