// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

internal sealed class WorldDefaultDomainOwnershipTests
{
    [Test]
    public void HandoffTransfersCloseAuthorityOnlyToTheExactSuccessor()
    {
        var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        var registry = WorldGuidRegistry.GetSelected();
        WorldGuidGrant currentGrant = default;
        WorldGuidGrant replacementGrant = default;
        WorldDefaultDomainOwner? currentOwner = null;
        WorldDefaultDomainOwner? replacementOwner = null;
        WorldDefaultDomainHandoff? handoff = null;
        WorldDomainOperationCredit? handoffCredit = null;
        WorldDomainReservation? reservation = null;

        try
        {
            Assert.That(registry.TryReserveFresh(Guid.NewGuid(), out currentGrant, out _), Is.True);
            Assert.That(registry.TryCommitFresh(currentGrant), Is.True);
            Assert.That(
                registry.TryReserveReplacement(
                    currentGrant,
                    Guid.NewGuid(),
                    out replacementGrant,
                    out _),
                Is.True);
            Assert.That(
                domain.TryReserveOperationCredit(WorldDefaultDomainHandoff.HandoffBytes, out handoffCredit),
                Is.True);
            currentOwner = new WorldDefaultDomainOwner(domain, currentGrant);
            handoff = new WorldDefaultDomainHandoff(
                currentOwner,
                currentGrant,
                replacementGrant,
                handoffCredit
                    ?? throw new InvalidOperationException("The default-domain handoff credit was not reserved."));
            handoffCredit = null;

            Assert.That(
                handoff.IsBoundTo(currentOwner, domain, currentGrant, replacementGrant),
                Is.True);
            Assert.That(
                handoff.IsBoundTo(currentOwner, domain, replacementGrant, currentGrant),
                Is.False);
            Assert.That(registry.TryCommitReplacement(currentGrant, replacementGrant), Is.True);
            replacementOwner = handoff.Consume(currentOwner, domain, currentGrant, replacementGrant);
            Assert.That(
                handoff.IsBoundTo(currentOwner, domain, currentGrant, replacementGrant),
                Is.False);
            currentOwner.Close();
            Assert.That(domain.TryReserveWorld(4096, out reservation), Is.True);
            reservation?.Dispose();
            reservation = null;

            replacementOwner.Close();
            Assert.That(domain.TryReserveWorld(4096, out var rejectedReservation), Is.False);
            rejectedReservation?.Dispose();
            handoff.Dispose();
            handoff = null;
            Assert.That(domain.Retirement.IsCompleted, Is.True);
        }
        finally
        {
            reservation?.Dispose();
            handoff?.Dispose();
            handoffCredit?.Dispose();
            currentOwner?.Close();
            replacementOwner?.Close();
            domain.Dispose();
            _ = registry.TryReleaseFresh(replacementGrant);
            _ = registry.TryReleaseFresh(currentGrant);
        }
    }
}
