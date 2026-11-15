// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

internal sealed class WorldLifetimeControlQuarantineTests
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CollectGarbage()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "A successful lifetime quarantine transfers the admission reservation to the pre-reserved process anchor; all failed paths release it here.")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<WorldLifetimeControl> CreateQuarantinedLifetimeControl(
        WorldGuidRegistry registry,
        WorldAdmissionDomain domain,
        Guid worldGuid)
    {
        var reservation = WorldSystemInstanceRegistry.ReserveQuarantineCapacity(0);
        WorldGuidGrant grant = default;
        WorldLineageGrant lineageGrant = default;
        WorldDomainReservation? domainReservation = null;
        WorldLifetimeControl? lifetimeControl = null;

        try
        {
            if (!registry.TryReserveFresh(worldGuid, out grant, out _))
            {
                throw new InvalidOperationException("The quarantine test could not reserve its WorldGuid.");
            }

            if (!domain.TryReserveWorld(4096, out domainReservation)
                || (domainReservation is null)
                || !domain.TryReserveFreshLineage(
                    domainReservation,
                    worldGuid,
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    out lineageGrant,
                    out _,
                    issuanceNamespace: worldGuid))
            {
                throw new InvalidOperationException("The quarantine test could not reserve its World history.");
            }

            if (!registry.TryCommitFresh(grant)
                || !domain.TryCommitFreshLineage(lineageGrant))
            {
                throw new InvalidOperationException("The quarantine test could not publish its World identity.");
            }

            lifetimeControl = new WorldLifetimeControl(
                registry,
                domain,
                grant,
                defaultDomainOwner: null,
                domainReservation,
                lineageGrant,
                isReplacementCandidate: false);
            lifetimeControl.PublishFresh();

            if (!lifetimeControl.BeginRetirement() || !lifetimeControl.QuarantineAfterCleanupFailure())
            {
                throw new InvalidOperationException("The quarantine test could not revoke and quarantine its WorldGuid.");
            }

            WorldSystemInstanceRegistry.QuarantineLifetimeControl(lifetimeControl, reservation);
            return new WeakReference<WorldLifetimeControl>(lifetimeControl);
        }
        finally
        {
            if ((lifetimeControl is not null) && !lifetimeControl.IsQuarantined)
            {
                if (lineageGrant.IsValid)
                {
                    _ = domain.TryRollbackFreshLineage(lineageGrant)
                        || domain.TryReleaseFreshLineage(lineageGrant);
                }

                domainReservation?.Dispose();

                if (lifetimeControl.BeginRetirement())
                {
                    _ = lifetimeControl.CompleteRetirement();
                }
            }
            else if (lifetimeControl is null)
            {
                if (lineageGrant.IsValid)
                {
                    _ = domain.TryRollbackFreshLineage(lineageGrant)
                        || domain.TryReleaseFreshLineage(lineageGrant);
                }

                domainReservation?.Dispose();

                if (grant.IsValid)
                {
                    _ = registry.TryReleaseFresh(grant);
                }
            }

            reservation.Release();
            WorldSystemInstanceRegistry.ReleaseLifetimeControlCapacity(reservation);
        }
    }

    [Test]
    public void QuarantinedLifetimeControlRemainsAnchoredUntilProcessShutdown()
    {
        var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        var registry = WorldGuidRegistry.GetSelected();
        var worldGuid = Guid.NewGuid();

        try
        {
            var lifetimeControl = CreateQuarantinedLifetimeControl(registry, domain, worldGuid);
            domain.Dispose();

            CollectGarbage();
            Assert.That(lifetimeControl.TryGetTarget(out var retainedLifetime), Is.True);
            Assert.That(retainedLifetime!.IsQuarantined, Is.True);
            Assert.That(retainedLifetime.CompleteRetirement(), Is.False);
            Assert.That(registry.TryReserveFresh(worldGuid, out _, out _), Is.False);
            Assert.That(domain.Retirement.IsCompleted, Is.False);
        }
        finally
        {
            domain.Dispose();
        }
    }
}
