// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
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
        var schemas = new CompiledContentSchemaRegistry([
            .. CoreContentSchemaCatalogue.PrimitiveSchemas,
            .. CoreContentSchemaCatalogue.FoundationSchemas,
            .. NativeAssetSchemaCatalogue.Schemas,
            .. SceneSchemaCatalogue.Schemas,
        ]);
        var registrationBuilder = new WorldRegistrationBuilder(schemas);
        _ = registrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
        var registration = registrationBuilder.Build();
        using var world = World.Create(new WorldOptions(registration) {
            CpuWorkerCount = 0,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
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
}
