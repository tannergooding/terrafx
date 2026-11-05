// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldAdmissionDomainTests
{
    [Test]
    public void DomainGrowthRequiresTheCompleteOldAndNewPeak()
    {
        using var exactDomain = new WorldAdmissionDomain(8192);
        Assert.That(exactDomain.TryReserveWorld(2096, out var exactReservation), Is.True);
        using var exactWorld = exactReservation!;
        exactDomain.Dispose();
        exactWorld.Dispose();
        Assert.That(exactDomain.Retirement.IsCompleted, Is.True);

        using var overBudgetDomain = new WorldAdmissionDomain(8192);
        var overBudgetSucceeded = overBudgetDomain.TryReserveWorld(2097, out var overBudgetReservation);
        Assert.That(overBudgetSucceeded, Is.False);
        Assert.That(overBudgetReservation, Is.Null);
        overBudgetReservation?.Dispose();
        overBudgetDomain.Dispose();
        Assert.That(overBudgetDomain.Retirement.IsCompleted, Is.True);
    }

    [Test]
    public void DomainRetirementWaitsForOutstandingWorldReservationRelease()
    {
        var domain = new WorldAdmissionDomain(8 * 1024 * 1024);

        Assert.That(domain.TryReserveWorld(4096, out var reservation), Is.True);
        Assert.That(reservation, Is.Not.Null);
        Assert.That(domain.Retirement.IsCompleted, Is.False);

        domain.Dispose();

        Assert.That(domain.Retirement.IsCompleted, Is.False);
        Assert.That(domain.TryReserveWorld(1, out var rejectedReservation), Is.False);
        rejectedReservation?.Dispose();
        Assert.That(rejectedReservation, Is.Null);

        reservation!.Dispose();

        Assert.That(domain.Retirement.IsCompleted, Is.True);
        reservation.Dispose();
    }

    [Test]
    public void FreshHistoryMembershipSharesRetainedLineageKeys()
    {
        using var domain = new WorldAdmissionDomain(8 * 1024 * 1024);
        var firstLineageId = new Guid("10000000-0000-4000-8000-000000000001");
        var sharedLineageId = new Guid("20000000-0000-4000-8000-000000000002");
        var secondLineageId = new Guid("30000000-0000-4000-8000-000000000003");
        var thirdLineageId = new Guid("40000000-0000-4000-8000-000000000004");
        Guid[] firstMembership = [firstLineageId, sharedLineageId];
        Guid[] secondMembership = [sharedLineageId, secondLineageId];
        Guid[] thirdMembership = [sharedLineageId, thirdLineageId];

        Assert.That(domain.TryReserveWorld(4096, 4096, out var firstReservation), Is.True);
        using var firstWorld = firstReservation!;
        Assert.That(
            domain.TryReserveFreshLineageHistory(
                firstWorld,
                Guid.NewGuid(),
                firstLineageId,
                Guid.NewGuid(),
                firstMembership,
                out var firstGrant,
                out _),
            Is.True);
        Assert.That(domain.TryCommitFreshLineage(firstGrant), Is.True);

        Assert.That(domain.TryReserveWorld(4096, 4096, out var secondReservation), Is.True);
        using var secondWorld = secondReservation!;
        Assert.That(
            domain.TryReserveFreshLineageHistory(
                secondWorld,
                Guid.NewGuid(),
                secondLineageId,
                Guid.NewGuid(),
                secondMembership,
                out var secondGrant,
                out _),
            Is.True);
        Assert.That(domain.TryCommitFreshLineage(secondGrant), Is.True);

        Assert.That(domain.TryRollbackFreshLineage(firstGrant), Is.True);
        Assert.That(firstWorld.Bytes, Is.EqualTo(4096));
        Assert.That(domain.TryReserveWorld(4096, 4096, out var thirdReservation), Is.True);
        using var thirdWorld = thirdReservation!;
        Assert.That(
            domain.TryReserveFreshLineageHistory(
                thirdWorld,
                Guid.NewGuid(),
                thirdLineageId,
                Guid.NewGuid(),
                thirdMembership,
                out var thirdGrant,
                out _),
            Is.True);
        Assert.That(domain.TryCommitFreshLineage(thirdGrant), Is.True);

        Assert.That(domain.TryRollbackFreshLineage(secondGrant), Is.True);
        Assert.That(domain.TryRollbackFreshLineage(thirdGrant), Is.True);
        Assert.That(secondWorld.Bytes, Is.EqualTo(4096));
        Assert.That(thirdWorld.Bytes, Is.EqualTo(4096));
    }

    [Test]
    public void FreshHistoryMembershipSharesRetainedLineageKeysWithIndexGrowth()
    {
        using var domain = new WorldAdmissionDomain(8 * 1024 * 1024);
        var currentLineageId = Guid.NewGuid();
        var membership = new Guid[20];

        for (var index = 0; index < membership.Length - 1; index++)
        {
            membership[index] = Guid.NewGuid();
        }

        membership[^1] = currentLineageId;
        Array.Sort(membership);

        Assert.That(domain.TryReserveWorld(4096, 4096, out var reservation), Is.True);
        using var world = reservation!;
        Assert.That(
            domain.TryReserveFreshLineageHistory(
                world,
                Guid.NewGuid(),
                currentLineageId,
                Guid.NewGuid(),
                membership,
                out var grant,
                out _),
            Is.True);
        Assert.That(domain.TryCommitFreshLineage(grant), Is.True);

        Assert.That(domain.TryReserveWorld(4096, out var duplicateReservation), Is.True);
        using var duplicateWorld = duplicateReservation!;
        Assert.That(
            domain.TryReserveFreshLineage(
                duplicateWorld,
                Guid.NewGuid(),
                membership[0],
                Guid.NewGuid(),
                out var duplicateGrant,
                out var duplicateRefusal),
            Is.False);
        Assert.That(duplicateGrant.IsValid, Is.False);
        Assert.That(duplicateRefusal, Is.EqualTo(WorldAdmissionRefusal.LineageAlreadyRetained));

        Assert.That(domain.TryRollbackFreshLineage(grant), Is.True);
        Assert.That(
            domain.TryReserveFreshLineage(
                duplicateWorld,
                Guid.NewGuid(),
                membership[0],
                Guid.NewGuid(),
                out var replacementGrant,
                out _),
            Is.True);
        Assert.That(domain.TryCommitFreshLineage(replacementGrant), Is.True);
        Assert.That(domain.TryRollbackFreshLineage(replacementGrant), Is.True);
    }

    [Test]
    public void FreshLineageReservationRejectsDuplicatesAndRetainsCommittedHistory()
    {
        var domain = new WorldAdmissionDomain(8 * 1024 * 1024);
        var worldGuid = Guid.NewGuid();
        var lineageId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        Assert.That(domain.TryReserveWorld(4096, out var reservation), Is.True);
        var worldReservation = reservation!;

        Assert.That(domain.TryReserveFreshLineage(worldReservation, worldGuid, lineageId, branchId, out var grant, out _), Is.True);
        Assert.That(grant.IsValid, Is.True);
        Assert.That(grant.WorldGuid, Is.EqualTo(worldGuid));
        Assert.That(grant.IdentityBranchId, Is.EqualTo(branchId));
        Assert.That(domain.TryReserveFreshLineage(
            worldReservation,
            Guid.NewGuid(),
            lineageId,
            Guid.NewGuid(),
            out var duplicateGrant,
            out var duplicateRefusal), Is.False);
        Assert.That(duplicateGrant.IsValid, Is.False);
        Assert.That(duplicateRefusal, Is.EqualTo(WorldAdmissionRefusal.LineageAlreadyRetained));
        Assert.That(domain.TryCommitFreshLineage(grant), Is.True);
        Assert.That(domain.TryCommitFreshLineage(grant), Is.False);
        Assert.That(domain.TryReleaseFreshLineage(grant), Is.False);
        Assert.That(domain.TryReserveFreshLineage(
            worldReservation,
            Guid.NewGuid(),
            lineageId,
            Guid.NewGuid(),
            out var retainedDuplicate,
            out duplicateRefusal), Is.False);
        Assert.That(retainedDuplicate.IsValid, Is.False);
        Assert.That(duplicateRefusal, Is.EqualTo(WorldAdmissionRefusal.LineageAlreadyRetained));

        domain.Dispose();
        worldReservation.Dispose();
        Assert.That(domain.Retirement.IsCompleted, Is.True);
    }

    [Test]
    public void FreshRootCapacityGrowthPreservesExistingLineageMembership()
    {
        using var domain = new WorldAdmissionDomain(8 * 1024 * 1024);
        var firstLineageId = Guid.Empty;
        var grants = new WorldLineageGrant[9];

        for (var index = 0; index < 9; index++)
        {
            Assert.That(domain.TryReserveWorld(4096, out var reservation), Is.True);
            using var worldReservation = reservation!;
            var lineageId = Guid.NewGuid();

            if (index == 0)
            {
                firstLineageId = lineageId;
            }

            Assert.That(domain.TryReserveFreshLineage(
                worldReservation,
                Guid.NewGuid(),
                lineageId,
                Guid.NewGuid(),
                out grants[index],
                out _), Is.True);
            Assert.That(domain.TryCommitFreshLineage(grants[index]), Is.True);
        }

        Assert.That(domain.TryReserveWorld(4096, out var duplicateReservation), Is.True);
        using var duplicateWorld = duplicateReservation!;
        Assert.That(domain.TryReserveFreshLineage(
            duplicateWorld,
            Guid.NewGuid(),
            firstLineageId,
            Guid.NewGuid(),
            out var duplicateGrant,
            out var refusal), Is.False);
        Assert.That(duplicateGrant.IsValid, Is.False);
        Assert.That(refusal, Is.EqualTo(WorldAdmissionRefusal.LineageAlreadyRetained));

        for (var index = 0; index < grants.Length; index++)
        {
            Assert.That(domain.TryRollbackFreshLineage(grants[index]), Is.True);
        }

        Assert.That(domain.TryReserveFreshLineage(
            duplicateWorld,
            Guid.NewGuid(),
            firstLineageId,
            Guid.NewGuid(),
            out var replacementGrant,
            out _), Is.True);
        Assert.That(domain.TryCommitFreshLineage(replacementGrant), Is.True);
        Assert.That(domain.TryRollbackFreshLineage(replacementGrant), Is.True);

        domain.Dispose();
        duplicateWorld.Dispose();
        Assert.That(domain.Retirement.IsCompleted, Is.True);
    }

    [Test]
    public void RegistrarControlConsumesTheMinimumDomainBudget()
    {
        var domain = new WorldAdmissionDomain(4096);

        Assert.That(domain.TryReserveWorld(1, out var reservation), Is.False);
        Assert.That(reservation, Is.Null);
        reservation?.Dispose();

        domain.Dispose();
        Assert.That(domain.Retirement.IsCompleted, Is.True);
    }

    [Test]
    public void RollbackCommittedFreshLineageReleasesMembership()
    {
        var domain = new WorldAdmissionDomain(8 * 1024 * 1024);
        var worldGuid = Guid.NewGuid();
        var lineageId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        Assert.That(domain.TryReserveWorld(4096, out var reservation), Is.True);
        var worldReservation = reservation!;

        Assert.That(domain.TryReserveFreshLineage(worldReservation, worldGuid, lineageId, branchId, out var grant, out _), Is.True);
        Assert.That(domain.TryCommitFreshLineage(grant), Is.True);
        Assert.That(domain.TryRollbackFreshLineage(grant), Is.True);
        Assert.That(domain.TryRollbackFreshLineage(grant), Is.False);
        Assert.That(domain.TryReserveFreshLineage(worldReservation, worldGuid, lineageId, branchId, out var nextGrant, out _), Is.True);

        domain.Dispose();
        Assert.That(domain.TryReleaseFreshLineage(nextGrant), Is.True);
        worldReservation.Dispose();
        Assert.That(domain.Retirement.IsCompleted, Is.True);
    }

    [Test]
    public void StartNewHistoryCommitRejectsForeignPendingLineageMembership()
    {
        using var domain = new WorldAdmissionDomain(8 * 1024 * 1024);
        var pendingLineageId = Guid.NewGuid();
        var firstHistoryLineageId = Guid.NewGuid();
        var secondLineageId = Guid.NewGuid();
        var firstMembership = new[] { pendingLineageId, firstHistoryLineageId };
        var secondMembership = new[] { pendingLineageId, secondLineageId };
        Array.Sort(firstMembership);
        Array.Sort(secondMembership);

        Assert.That(domain.TryReserveWorld(4096, 4096, out var firstReservation), Is.True);
        using var firstWorld = firstReservation!;
        Assert.That(
            domain.TryReserveFreshLineageHistory(
                firstWorld,
                Guid.NewGuid(),
                pendingLineageId,
                Guid.NewGuid(),
                firstMembership,
                out var firstGrant,
                out _),
            Is.True);

        Assert.That(domain.TryReserveWorld(4096, 4096, out var secondReservation), Is.True);
        using var secondWorld = secondReservation!;
        Assert.That(
            domain.TryReserveFreshLineageHistory(
                secondWorld,
                Guid.NewGuid(),
                secondLineageId,
                Guid.NewGuid(),
                secondMembership,
                out var secondGrant,
                out _),
            Is.True);
        Assert.That(domain.TryCommitFreshLineage(secondGrant), Is.False);
        Assert.That(domain.TryCommitFreshLineage(firstGrant), Is.True);
        Assert.That(domain.TryCommitFreshLineage(secondGrant), Is.True);

        Assert.That(domain.TryRollbackFreshLineage(firstGrant), Is.True);
        Assert.That(domain.TryRollbackFreshLineage(secondGrant), Is.True);
    }

    [Test]
    public void UncommittedLineageReservationCanBeReleasedExactlyOnce()
    {
        var domain = new WorldAdmissionDomain(8 * 1024 * 1024);
        var worldGuid = Guid.NewGuid();
        var lineageId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        Assert.That(domain.TryReserveWorld(4096, out var reservation), Is.True);
        var worldReservation = reservation!;

        Assert.That(domain.TryReserveFreshLineage(worldReservation, worldGuid, lineageId, branchId, out var grant, out _), Is.True);
        Assert.That(domain.TryReleaseFreshLineage(grant), Is.True);
        Assert.That(domain.TryReleaseFreshLineage(grant), Is.False);
        Assert.That(domain.TryReserveFreshLineage(worldReservation, worldGuid, lineageId, branchId, out var nextGrant, out _), Is.True);
        Assert.That(nextGrant.Generation, Is.GreaterThan(grant.Generation));

        domain.Dispose();
        Assert.That(domain.TryReleaseFreshLineage(nextGrant), Is.True);
        worldReservation.Dispose();
        Assert.That(domain.Retirement.IsCompleted, Is.True);
    }

    [Test]
    public void WorldDomainRejectsBudgetsOutsideTheFrozenBounds()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new WorldAdmissionDomain(4095));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new WorldAdmissionDomain(8_589_934_593L));
    }

    [Test]
    public void WorldReservationAdjustmentsRemainBoundToItsInterest()
    {
        var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        Assert.That(domain.TryReserveWorld(4096, out var reservation), Is.True);
        using var worldReservation = reservation!;
        Assert.That(worldReservation.Bytes, Is.EqualTo(4096));

        Assert.That(worldReservation.TryReserveAdditional(8192), Is.True);
        Assert.That(worldReservation.Bytes, Is.EqualTo(12_288));
        worldReservation.ReleaseAdditional(4096);
        Assert.That(worldReservation.Bytes, Is.EqualTo(8192));
        Assert.That(worldReservation.TryReserveAdditional(1), Is.True);
        Assert.That(worldReservation.Bytes, Is.EqualTo(8193));

        domain.Dispose();
        worldReservation.Dispose();
        Assert.That(domain.Retirement.IsCompleted, Is.True);
    }

    [Test]
    public void WorldReservationCanForbidAdditionalGrowth()
    {
        using var domain = new WorldAdmissionDomain(8 * 1024 * 1024);
        Assert.That(domain.TryReserveWorld(4096, 0, out var reservation), Is.True);

        using var exactReservation = reservation!;
        Assert.That(exactReservation.TryReserveAdditional(1), Is.False);
    }

    [Test]
    public void WorldReservationGrowthCannotExceedItsOwnedStateCeiling()
    {
        using var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        Assert.That(domain.TryReserveWorld(4096, 8192, out var reservation), Is.True);
        using var worldReservation = reservation!;

        Assert.That(worldReservation.TryReserveAdditional(8192), Is.True);
        Assert.That(worldReservation.Bytes, Is.EqualTo(12_288));
        Assert.That(worldReservation.TryReserveAdditional(1), Is.False);
        Assert.That(worldReservation.Bytes, Is.EqualTo(12_288));

        worldReservation.ReleaseAdditional(8192);
        Assert.That(worldReservation.Bytes, Is.EqualTo(4096));
        domain.Dispose();
        worldReservation.Dispose();
        Assert.That(domain.Retirement.IsCompleted, Is.True);
    }
}
