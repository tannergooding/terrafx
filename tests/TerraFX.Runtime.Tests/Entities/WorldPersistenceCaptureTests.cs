// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
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
}
