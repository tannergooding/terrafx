// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Entities;
using TerraFX.Hosting;

namespace TerraFX.Runtime.Tests.Hosting;

[TestFixture]
internal sealed class WorldIdentityRemapTests
{
    private static WorldIdentityRemap CreateRemap(
        WorldDomainOperationCredit credit,
        Guid[] sourceEntityIds,
        Guid[] targetEntityIds,
        Guid[] sourceSceneInstanceIds,
        Guid[] targetSceneInstanceIds)
    {
        var remap = new WorldIdentityRemap();
        InitializeRemap(
            remap,
            credit,
            sourceEntityIds,
            targetEntityIds,
            sourceSceneInstanceIds,
            targetSceneInstanceIds);
        return remap;
    }

    private static Guid Identity(int value)
        => new($"00000000-0000-0000-0000-{value:D12}");

    private static void InitializeRemap(
        WorldIdentityRemap remap,
        WorldDomainOperationCredit credit,
        Guid[] sourceEntityIds,
        Guid[] targetEntityIds,
        Guid[] sourceSceneInstanceIds,
        Guid[] targetSceneInstanceIds)
    {
        remap.Initialize(
            Identity(101),
            Identity(102),
            Identity(103),
            Identity(104),
            Identity(105),
            Identity(106),
            new byte[32],
            new byte[32],
            sourceEntityIds,
            targetEntityIds,
            sourceSceneInstanceIds,
            targetSceneInstanceIds,
            credit);
    }

    private static WorldDomainOperationCredit ReserveCredit(WorldAdmissionDomain domain)
    {
        Assert.That(domain.TryReserveOperationCredit(64 * 1024, out var credit), Is.True);
        return credit!;
    }

    [Test]
    public void IdentityMappingRetainsItsBudgetUntilDisposed()
    {
        var domain = new WorldAdmissionDomain(1024 * 1024);
        Assert.That(domain.TryReserveOperationCredit(64 * 1024, out var reservedCredit), Is.True);
        using var credit = reservedCredit!;
        var remap = CreateRemap(
            credit,
            [Identity(1)],
            [Identity(11)],
            [Identity(2)],
            [Identity(12)]);

        credit.Dispose();
        domain.Dispose();

        Assert.That(domain.Retirement.IsCompleted, Is.False);
        Assert.That(remap.SourceWorldGuid, Is.EqualTo(Identity(101)));
        Assert.That(remap.TargetWorldGuid, Is.EqualTo(Identity(104)));
        Assert.That(remap.SourceCheckpointDigest.Length, Is.EqualTo(32));
        Assert.That(remap.SourcePayloadDigest.Length, Is.EqualTo(32));
        Assert.That(remap.TryMapEntity(Identity(1), out var targetEntityId), Is.True);
        Assert.That(targetEntityId, Is.EqualTo(Identity(11)));

        remap.Dispose();

        Assert.That(domain.Retirement.IsCompleted, Is.True);
        Assert.That(() => remap.TryMapEntity(Identity(1), out _), Throws.TypeOf<ObjectDisposedException>());
        remap.Dispose();
    }

    [Test]
    public void IdentityMappingsAreSortedAndKeepKindsSeparate()
    {
        using var domain = new WorldAdmissionDomain(1024 * 1024);
        using var credit = ReserveCredit(domain);
        using var remap = CreateRemap(
            credit,
            [Identity(1), Identity(3)],
            [Identity(11), Identity(13)],
            [Identity(2)],
            [Identity(12)]);

        Assert.That(remap.TryMapEntity(Identity(3), out var targetEntityId), Is.True);
        Assert.That(targetEntityId, Is.EqualTo(Identity(13)));
        Assert.That(remap.TryMapSceneInstance(Identity(2), out var targetSceneInstanceId), Is.True);
        Assert.That(targetSceneInstanceId, Is.EqualTo(Identity(12)));
        Assert.That(remap.TryMapEntity(Identity(2), out var unmappedEntityId), Is.False);
        Assert.That(unmappedEntityId, Is.EqualTo(Guid.Empty));
        Assert.That(remap.TryMapSceneInstance(Identity(3), out var unmappedSceneInstanceId), Is.False);
        Assert.That(unmappedSceneInstanceId, Is.EqualTo(Guid.Empty));
    }

    [Test]
    public void RejectsCrossKindSourceAndTargetCollisions()
    {
        using var domain = new WorldAdmissionDomain(1024 * 1024);
        using var credit = ReserveCredit(domain);
        using var duplicateSourceRemap = new WorldIdentityRemap();
        using var duplicateTargetRemap = new WorldIdentityRemap();

        Assert.That(
            () => InitializeRemap(
                duplicateSourceRemap,
                credit,
                [Identity(1)],
                [Identity(11)],
                [Identity(1)],
                [Identity(12)]),
            Throws.ArgumentException);
        Assert.That(
            () => InitializeRemap(
                duplicateTargetRemap,
                credit,
                [Identity(1)],
                [Identity(11)],
                [Identity(2)],
                [Identity(11)]),
            Throws.ArgumentException);
    }

    [Test]
    public void RejectsMappingsThatExceedTheirRetainedCredit()
    {
        using var domain = new WorldAdmissionDomain(1024 * 1024);
        Assert.That(domain.TryReserveOperationCredit(1, out var reservedCredit), Is.True);
        using var credit = reservedCredit!;
        using var remap = new WorldIdentityRemap();

        Assert.That(
            () => InitializeRemap(
                remap,
                credit,
                [Identity(1)],
                [Identity(11)],
                [],
                []),
            Throws.ArgumentException);
    }

    [Test]
    public void RejectsUnsortedOrDuplicateSourceMappings()
    {
        using var domain = new WorldAdmissionDomain(1024 * 1024);
        using var credit = ReserveCredit(domain);
        using var unsortedRemap = new WorldIdentityRemap();
        using var duplicateRemap = new WorldIdentityRemap();

        Assert.That(
            () => InitializeRemap(
                unsortedRemap,
                credit,
                [Identity(3), Identity(1)],
                [Identity(13), Identity(11)],
                [],
                []),
            Throws.ArgumentException);
        Assert.That(
            () => InitializeRemap(
                duplicateRemap,
                credit,
                [Identity(1), Identity(1)],
                [Identity(11), Identity(12)],
                [],
                []),
            Throws.ArgumentException);
    }
}
