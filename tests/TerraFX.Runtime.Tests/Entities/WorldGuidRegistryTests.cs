// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests;

[TestFixture]
internal sealed class WorldGuidRegistryTests
{
    private static Guid FindGuidForIndexBucket(int bucket, Guid excluded = default)
    {
        var indexMask = (uint)((WorldGuidRegistry.MaximumWorldGuidKeys * 2) - 1);

        for (var value = 1; ; value++)
        {
            var candidate = new Guid(value, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

            if ((candidate != excluded) && ((unchecked((uint)candidate.GetHashCode()) & indexMask) == (uint)bucket))
            {
                return candidate;
            }
        }
    }

    [Test]
    public void ConcurrentFreshReservationsGrowSlabsAndRejectDuplicateGuid()
    {
        var registry = WorldGuidRegistry.GetSelected();
        const int ReservationCount = 257;
        var worldGuids = new Guid[ReservationCount];
        var grants = new WorldGuidGrant[ReservationCount];
        var reserved = new bool[ReservationCount];

        for (var index = 0; index < worldGuids.Length; index++)
        {
            worldGuids[index] = Guid.NewGuid();
        }

        _ = Parallel.For(0, ReservationCount, index => reserved[index] = registry.TryReserveFresh(worldGuids[index], out grants[index], out _));

        for (var index = 0; index < ReservationCount; index++)
        {
            Assert.That(reserved[index], Is.True);
            Assert.That(grants[index].IsValid, Is.True);
            Assert.That(registry.TryReleaseFresh(grants[index]), Is.True);
            Assert.That(registry.TryReleaseFresh(grants[index]), Is.False);
        }

        var duplicateGuid = Guid.NewGuid();
        var duplicateGrants = new WorldGuidGrant[64];
        var duplicateRefusals = new WorldGuidRefusal[64];
        var duplicateReserved = new bool[64];

        _ = Parallel.For(0, duplicateReserved.Length, index => duplicateReserved[index] = registry.TryReserveFresh(duplicateGuid, out duplicateGrants[index], out duplicateRefusals[index]));

        var winnerIndex = -1;

        for (var index = 0; index < duplicateReserved.Length; index++)
        {
            if (duplicateReserved[index])
            {
                Assert.That(winnerIndex, Is.EqualTo(-1));
                winnerIndex = index;
                continue;
            }

            Assert.That(duplicateRefusals[index].Code, Is.EqualTo(WorldGuidRefusal.GuidAlreadyRetained));
        }

        Assert.That(winnerIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(registry.TryReleaseFresh(duplicateGrants[winnerIndex]), Is.True);
    }

    [Test]
    public void ConcurrentSameTypeAccessPublishesOneRegistryInstance()
    {
        var registries = new WorldGuidRegistry[256];

        _ = Parallel.For(0, registries.Length, index => registries[index] = WorldGuidRegistry.GetSelected());

        var selectedRegistry = registries[0];
        Assert.That(selectedRegistry, Is.Not.Null);

        foreach (var registry in registries)
        {
            Assert.That(ReferenceEquals(registry, selectedRegistry), Is.True);
        }

        Assert.That(ReferenceEquals(WorldGuidRegistry.GetSelected(), selectedRegistry), Is.True);
    }

    [Test]
    public void FreshAdmissionRefusesWhenRetainedWorldGuidCapacityIsFull()
    {
        var registry = WorldGuidRegistry.GetSelected();
        var worldGuids = new Guid[WorldGuidRegistry.MaximumWorldGuidKeys];
        var grants = new WorldGuidGrant[WorldGuidRegistry.MaximumWorldGuidKeys];

        for (var index = 0; index < worldGuids.Length; index++)
        {
            worldGuids[index] = Guid.NewGuid();
            Assert.That(registry.TryReserveFresh(worldGuids[index], out grants[index], out _), Is.True);
        }

        Assert.That(registry.TryReserveFresh(Guid.NewGuid(), out var rejectedGrant, out var refusal), Is.False);
        Assert.That(rejectedGrant.IsValid, Is.False);
        Assert.That(refusal.Code, Is.EqualTo(WorldGuidRefusal.KeyCapacity));

        for (var index = 0; index < grants.Length; index++)
        {
            Assert.That(
                registry.TryReserveFresh(worldGuids[index], out var duplicateGrant, out refusal),
                Is.False,
                $"The process index lost the WorldGuid at key index {index} before release.");
            Assert.That(duplicateGrant.IsValid, Is.False);
            Assert.That(refusal.Code, Is.EqualTo(WorldGuidRefusal.GuidAlreadyRetained));

            try
            {
                Assert.That(registry.TryReleaseFresh(grants[index]), Is.True);
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException($"Releasing the WorldGuid at key index {index} failed.", exception);
            }
        }
    }

    [Test]
    public void FreshGrantRejectsDuplicateAndReleasesOnlyOnce()
    {
        var registry = WorldGuidRegistry.GetSelected();
        var worldGuid = Guid.NewGuid();

        Assert.That(registry.TryReserveFresh(worldGuid, out var grant, out var refusal), Is.True);
        Assert.That(grant.IsValid, Is.True);
        Assert.That(refusal.IsNone, Is.True);

        Assert.That(registry.TryReserveFresh(worldGuid, out var duplicateGrant, out refusal), Is.False);
        Assert.That(duplicateGrant.IsValid, Is.False);
        Assert.That(refusal.Code, Is.EqualTo(WorldGuidRefusal.GuidAlreadyRetained));

        Assert.That(registry.TryCommitFresh(grant), Is.True);
        Assert.That(registry.TryCommitFresh(grant), Is.False);
        Assert.That(registry.TryReleaseFresh(grant), Is.True);
        Assert.That(registry.TryReleaseFresh(grant), Is.False);

        Assert.That(registry.TryReserveFresh(worldGuid, out var replacementGrant, out _), Is.True);
        Assert.That(replacementGrant.KeyGeneration, Is.GreaterThan(grant.KeyGeneration));
        Assert.That(registry.TryReleaseFresh(replacementGrant), Is.True);
    }

    [Test]
    public void IndexDeletionPreservesEntriesThatProbeAcrossHomeBuckets()
    {
        var registry = WorldGuidRegistry.GetSelected();
        var firstGuid = FindGuidForIndexBucket(0);
        var homeBucketGuid = FindGuidForIndexBucket(1);
        var displacedGuid = FindGuidForIndexBucket(0, firstGuid);

        Assert.That(registry.TryReserveFresh(firstGuid, out var firstGrant, out _), Is.True);
        Assert.That(registry.TryReserveFresh(homeBucketGuid, out var homeBucketGrant, out _), Is.True);
        Assert.That(registry.TryReserveFresh(displacedGuid, out var displacedGrant, out _), Is.True);

        Assert.That(registry.TryReleaseFresh(firstGrant), Is.True);
        Assert.That(registry.TryReserveFresh(displacedGuid, out var duplicateGrant, out var refusal), Is.False);
        Assert.That(duplicateGrant.IsValid, Is.False);
        Assert.That(refusal.Code, Is.EqualTo(WorldGuidRefusal.GuidAlreadyRetained));

        Assert.That(registry.TryReleaseFresh(homeBucketGrant), Is.True);
        Assert.That(registry.TryReleaseFresh(displacedGrant), Is.True);
    }

    [Test]
    public void RepeatedFreshGuidReleaseDoesNotConsumeRetainedKeyCapacity()
    {
        var registry = WorldGuidRegistry.GetSelected();
        var worldGuid = Guid.NewGuid();

        for (var iteration = 0; iteration < WorldGuidRegistry.MaximumWorldGuidKeys; iteration++)
        {
            Assert.That(registry.TryReserveFresh(worldGuid, out var grant, out _), Is.True);
            Assert.That(registry.TryReleaseFresh(grant), Is.True);
        }
    }
}
