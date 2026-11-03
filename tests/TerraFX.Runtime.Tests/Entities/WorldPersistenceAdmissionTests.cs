// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldPersistenceAdmissionTests
{
    private static World CreateWorld(long maximumPersistenceAdmissionBytes, WorldAdmissionDomain? domain = null)
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        return World.Create(new WorldOptions(registration) {
            AdmissionDomain = domain,
            CpuWorkerCount = 0,
            MaximumPersistenceAdmissionBytes = maximumPersistenceAdmissionBytes,
        });
    }

    [Test]
    public void AcceptedPersistenceAdmissionKeepsTheDomainAliveAfterWorldDisposal()
    {
        var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        var world = CreateWorld(8192, domain);
        Assert.That(world.TryReservePersistenceAdmissionBytes(4096, out var reservation), Is.True);
        Assert.That(reservation, Is.Not.Null);
        domain.Dispose();
        world.Dispose();

        Assert.That(domain.Retirement.IsCompleted, Is.False);
        reservation!.Dispose();
        Assert.That(domain.Retirement.IsCompleted, Is.True);
    }

    [Test]
    public void DisposingReservationDefersReleaseUntilActiveUseExits()
    {
        using var world = CreateWorld(4096);
        Assert.That(world.TryReservePersistenceAdmissionBytes(4096, out var reservation), Is.True);
        Assert.That(reservation, Is.Not.Null);
        var activeUse = reservation!.AcquireUse(world, 4096);

        try
        {
            reservation.Dispose();
            Assert.That(world.TryReservePersistenceAdmissionBytes(1, out var refused), Is.False);
            Assert.That(refused, Is.Null);
            refused?.Dispose();
        }
        finally
        {
            activeUse.Dispose();
        }

        Assert.That(world.TryReservePersistenceAdmissionBytes(1, out var replacement), Is.True);
        Assert.That(replacement, Is.Not.Null);
        replacement!.Dispose();
    }

    [Test]
    public void PersistenceAdmissionRefusesWhenSharedDomainIsClosed()
    {
        using var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        using var world = CreateWorld(8192, domain);
        domain.Dispose();

        Assert.That(world.TryReservePersistenceAdmissionBytes(1, out var reservation), Is.False);
        Assert.That(reservation, Is.Null);
        reservation?.Dispose();
    }

    [Test]
    public void PersistenceAdmissionRejectsNonpositiveByteCounts()
    {
        using var world = CreateWorld(8192);

        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => world.TryReservePersistenceAdmissionBytes(0, out _));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => world.TryReservePersistenceAdmissionBytes(-1, out _));
    }

    [Test]
    public void PersistenceAdmissionsShareTheWorldLimitAndReleaseForReuse()
    {
        using var world = CreateWorld(8192);
        Assert.That(world.TryReservePersistenceAdmissionBytes(6144, out var first), Is.True);
        Assert.That(first, Is.Not.Null);
        Assert.That(world.TryReservePersistenceAdmissionBytes(2049, out var refused), Is.False);
        Assert.That(refused, Is.Null);
        refused?.Dispose();

        first!.Dispose();
        first.Dispose();

        Assert.That(world.TryReservePersistenceAdmissionBytes(8192, out var replacement), Is.True);
        Assert.That(replacement, Is.Not.Null);
        replacement!.Dispose();
    }
}
