// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
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

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<WorldLifetimeControl> CreateQuarantinedLifetimeControl(
        WorldGuidRegistry registry,
        WorldAdmissionDomain domain,
        Guid worldGuid)
    {
        var reservation = WorldSystemInstanceRegistry.ReserveQuarantineCapacity(0);
        WorldGuidGrant grant = default;
        WorldLifetimeControl? lifetimeControl = null;

        try
        {
            if (!registry.TryReserveFresh(worldGuid, out grant, out _))
            {
                throw new InvalidOperationException("The quarantine test could not reserve its WorldGuid.");
            }

            lifetimeControl = new WorldLifetimeControl(
                registry,
                domain,
                grant,
                defaultDomainOwner: null,
                isReplacementCandidate: false);

            if (!lifetimeControl.BeginRetirement() || !lifetimeControl.QuarantineAfterCleanupFailure())
            {
                throw new InvalidOperationException("The quarantine test could not revoke and quarantine its unpublished WorldGuid.");
            }

            WorldSystemInstanceRegistry.QuarantineLifetimeControl(lifetimeControl, reservation);
            return new WeakReference<WorldLifetimeControl>(lifetimeControl);
        }
        finally
        {
            if ((lifetimeControl is not null) && !lifetimeControl.IsQuarantined)
            {
                if (lifetimeControl.BeginRetirement())
                {
                    _ = lifetimeControl.CompleteRetirement();
                }
            }
            else if (lifetimeControl is null && grant.IsValid)
            {
                _ = registry.TryReleaseFresh(grant);
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
        }
        finally
        {
            domain.Dispose();
        }
    }
}
