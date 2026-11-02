// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

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
}
