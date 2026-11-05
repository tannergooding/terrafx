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
    public void GrowingReservationChargesBothLimitsAndRollsBackRefusedGrowth()
    {
        using var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        using var world = CreateWorld(8192, domain);
        Assert.That(world.TryReservePersistenceAdmissionBytes(4096, 1024, out var reservation), Is.True);
        Assert.That(reservation, Is.Not.Null);
        using var reservationScope = reservation
            ?? throw new InvalidOperationException("The persistence admission reservation was not created.");

        Assert.That(reservationScope.TryGrow(1024), Is.True);
        Assert.That(reservationScope.TryGrow(1), Is.False);
        reservationScope.Dispose();

        Assert.That(world.TryReservePersistenceAdmissionBytes(8192, out var replacement), Is.True);
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

    [Test]
    public void ReservationBoundsSimultaneousOperationWorkspaces()
    {
        using var world = CreateWorld(4096);
        Assert.That(world.TryReservePersistenceAdmissionBytes(4096, out var reservation), Is.True);
        using var reservationScope = reservation
            ?? throw new InvalidOperationException("The persistence admission reservation was not created.");
        using var firstUse = reservationScope.AcquireUse(world, 3072);

        var rejected = false;

        try
        {
            _ = reservationScope.AcquireUse(world, 2048);
        }
        catch (ArgumentException)
        {
            rejected = true;
        }

        Assert.That(rejected, Is.True);
        using var secondUse = reservationScope.AcquireUse(world, 1024);
        reservationScope.Dispose();
        Assert.That(world.TryReservePersistenceAdmissionBytes(1, out var refused), Is.False);
        Assert.That(refused, Is.Null);
        refused?.Dispose();

        secondUse.Dispose();
        firstUse.Dispose();
        Assert.That(world.TryReservePersistenceAdmissionBytes(1, out var replacement), Is.True);
        Assert.That(replacement, Is.Not.Null);
        replacement!.Dispose();
    }

    [Test]
    public void WorldLoadOperationCreditBundleAcquiresRetainedUsesTogether()
    {
        using var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        Assert.That(domain.TryReserveLoadOperationCredits(
            1024,
            2048,
            3072,
            4096,
            out var credits,
            out var refusal), Is.True);
        Assert.That(refusal, Is.EqualTo(WorldAdmissionRefusal.None));
        using var creditBundle = credits
            ?? throw new InvalidOperationException("The load operation credit bundle was not created.");
        using var retainedUses = creditBundle.AcquireRetainedUse(1024, 2048, 3072, 4096);

        domain.Dispose();
        creditBundle.Dispose();

        Assert.That(domain.Retirement.IsCompleted, Is.False);
        retainedUses.Dispose();
        Assert.That(domain.Retirement.IsCompleted, Is.True);
    }

    [Test]
    public void WorldLoadOperationCreditBundleRollsBackPartialAcquisition()
    {
        using var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        Assert.That(domain.TryReserveLoadOperationCredits(
            1024,
            2048,
            3072,
            4096,
            out var credits,
            out var refusal), Is.True);
        Assert.That(refusal, Is.EqualTo(WorldAdmissionRefusal.None));
        using var creditBundle = credits
            ?? throw new InvalidOperationException("The load operation credit bundle was not created.");

        _ = Assert.Throws<ArgumentException>(() => {
            using var retainedUses = creditBundle.AcquireRetainedUse(1024, 2048, 3073, 4096);
        });

        creditBundle.Dispose();
        domain.Dispose();
        Assert.That(domain.Retirement.IsCompleted, Is.True);
    }

    [Test]
    public void WorldLoadOperationCreditsRetainAllRolesAfterWorldDisposal()
    {
        using var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        using var world = CreateWorld(8192, domain);
        Assert.That(world.TryReserveLoadOperationCredits(
            1024,
            2048,
            3072,
            4096,
            out var credits,
            out var creditRefusal), Is.True);
        Assert.That(creditRefusal, Is.EqualTo(WorldAdmissionRefusal.None));
        using var creditBundle = credits
            ?? throw new InvalidOperationException("The load operation credit bundle was not created.");
        using var sourceUse = creditBundle.Source.AcquireRetainedUse(1024);
        using var targetUse = creditBundle.Target.AcquireRetainedUse(2048);
        using var dataUse = creditBundle.Data.AcquireRetainedUse(3072);
        using var cleanupUse = creditBundle.Cleanup.AcquireRetainedUse(4096);

        domain.Dispose();
        world.Dispose();
        creditBundle.Dispose();

        Assert.That(domain.Retirement.IsCompleted, Is.False);
        sourceUse.Dispose();
        targetUse.Dispose();
        dataUse.Dispose();
        cleanupUse.Dispose();
        Assert.That(domain.Retirement.IsCompleted, Is.True);
    }

    [Test]
    public void WorldLoadReplacementAdmissionIsAllOrNone()
    {
        using var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        using var world = CreateWorld(8192, domain);

        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => world.TryPrepareLoadReplacement(0, 1, 1, 1, out _, out _, out _));
        _ = Assert.Throws<OverflowException>(
            () => world.TryPrepareLoadReplacement(long.MaxValue, 1, 1, 1, out _, out _, out _));

        Assert.That(
            world.TryPrepareLoadReplacement(
                domain.MaximumBytes,
                1,
                1,
                1,
                out var rejectedPreparation,
                out var rejectedBindingRefusal,
                out var rejectedCreditRefusal),
            Is.False);
        Assert.That(rejectedPreparation, Is.Null);
        Assert.That(rejectedBindingRefusal.IsNone, Is.True);
        Assert.That(rejectedCreditRefusal, Is.EqualTo(WorldAdmissionRefusal.OperationCapacity));
        rejectedPreparation?.Dispose();

        Assert.That(world.TryPrepareGuidReplacement(out var bindingPreparation, out _), Is.True);
        using var bindingScope = bindingPreparation!;
        bindingScope.Dispose();

        Assert.That(
            world.TryPrepareLoadReplacement(
                1024,
                2048,
                3072,
                4096,
                out var preparation,
                out var bindingRefusal,
                out var creditRefusal),
            Is.True);
        Assert.That(bindingRefusal.IsNone, Is.True);
        Assert.That(creditRefusal, Is.EqualTo(WorldAdmissionRefusal.None));
        using var preparationScope = preparation!;
        preparationScope.Dispose();

        world.Dispose();
        domain.Dispose();
        Assert.That(domain.Retirement.IsCompleted, Is.True);
    }

    [Test]
    public void WorldLoadReplacementAdmissionReleasesSuccessorWhenDomainIsClosed()
    {
        using var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        using var world = CreateWorld(8192, domain);
        domain.Dispose();

        Assert.That(
            world.TryPrepareLoadReplacement(
                1024,
                2048,
                3072,
                4096,
                out var preparation,
                out var bindingRefusal,
                out var creditRefusal),
            Is.False);
        Assert.That(preparation, Is.Null);
        Assert.That(bindingRefusal.IsNone, Is.True);
        Assert.That(creditRefusal, Is.EqualTo(WorldAdmissionRefusal.DomainClosed));
        preparation?.Dispose();

        world.Dispose();
        Assert.That(domain.Retirement.IsCompleted, Is.True);
    }

    [Test]
    public void WorldLoadReplacementAdmissionRetainsUseAfterPreparationDisposal()
    {
        var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        var world = CreateWorld(8192, domain);
        Assert.That(
            world.TryPrepareLoadReplacement(
                1024,
                2048,
                3072,
                4096,
                out var preparation,
                out var bindingRefusal,
                out var creditRefusal),
            Is.True);
        using var preparationScope = preparation!;
        Assert.That(bindingRefusal.IsNone, Is.True);
        Assert.That(creditRefusal, Is.EqualTo(WorldAdmissionRefusal.None));
        using var retainedUse = preparationScope.AcquireRetainedUse(1024, 2048, 3072, 4096);

        preparationScope.Dispose();
        _ = Assert.Throws<ObjectDisposedException>(() => {
            using var lateUse = preparationScope.AcquireRetainedUse(0, 0, 0, 0);
        });
        world.Dispose();
        domain.Dispose();

        Assert.That(domain.Retirement.IsCompleted, Is.False);
        retainedUse.Dispose();
        Assert.That(domain.Retirement.IsCompleted, Is.True);
    }

    [Test]
    public void WorldLoadReplacementCommitTransfersBindingAndCreditsExactlyOnce()
    {
        var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        var world = CreateWorld(8192, domain);
        Assert.That(
            world.TryPrepareLoadReplacement(
                1024,
                2048,
                3072,
                4096,
                out var preparation,
                out var bindingRefusal,
                out var creditRefusal),
            Is.True);
        using var preparationScope = preparation!;
        Assert.That(bindingRefusal.IsNone, Is.True);
        Assert.That(creditRefusal, Is.EqualTo(WorldAdmissionRefusal.None));
        Assert.That(
            preparationScope.TryCommitWithCredits(out var replacementGrant, out var credits),
            Is.True);
        Assert.That(replacementGrant.IsValid, Is.True);
        using var creditBundle = credits!;
        var sourceUse = creditBundle.Source.AcquireRetainedUse(1024);
        var targetUse = creditBundle.Target.AcquireRetainedUse(2048);
        var dataUse = creditBundle.Data.AcquireRetainedUse(3072);
        var cleanupUse = creditBundle.Cleanup.AcquireRetainedUse(4096);

        Assert.That(
            preparationScope.TryCommitWithCredits(out var repeatedGrant, out var repeatedCredits),
            Is.False);
        Assert.That(repeatedGrant.IsValid, Is.False);
        Assert.That(repeatedCredits, Is.Null);
        repeatedCredits?.Dispose();
        preparationScope.Dispose();

        domain.Dispose();
        world.Dispose();
        creditBundle.Dispose();
        Assert.That(domain.Retirement.IsCompleted, Is.False);
        Assert.That(WorldGuidRegistry.GetSelected().TryReleaseFresh(replacementGrant), Is.True);

        sourceUse.Dispose();
        targetUse.Dispose();
        dataUse.Dispose();
        cleanupUse.Dispose();
        Assert.That(domain.Retirement.IsCompleted, Is.True);
    }

    [Test]
    public void WorldReplacementPreparationIsBoundToItsLiveBinding()
    {
        using var world = CreateWorld(8192);
        Assert.That(world.TryPrepareGuidReplacement(out var preparation, out var refusal), Is.True);
        Assert.That(refusal.IsNone, Is.True);
        Assert.That(preparation, Is.Not.Null);
        preparation!.Dispose();

        world.Dispose();

        Assert.That(world.TryPrepareGuidReplacement(out var stalePreparation, out refusal), Is.False);
        Assert.That(stalePreparation, Is.Null);
        Assert.That(refusal.Code, Is.EqualTo(WorldGuidRefusal.TargetBindingStale));
        stalePreparation?.Dispose();
    }
}
