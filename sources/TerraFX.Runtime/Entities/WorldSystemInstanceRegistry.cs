// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace TerraFX.Entities;

internal static class WorldSystemInstanceRegistry
{
    private static readonly ConditionalWeakTable<IWorldSystem, WorldSystemInstanceClaim> s_claims = [];
    private static readonly Lock s_gate = new();
    private static readonly List<WorldDomainReservation> s_quarantinedDomainReservations = [];
    private static readonly List<IWorldSystem> s_quarantinedSystems = [];
    private static int s_reservedQuarantineDomainReservations;
    private static int s_reservedQuarantineSlots;

    internal static bool IsClaimedBy(IWorldSystem system, WorldSystemInstanceClaim claim)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(claim);

        lock (s_gate)
        {
            return s_claims.TryGetValue(system, out var currentClaim)
                && ReferenceEquals(currentClaim, claim);
        }
    }

    internal static void Quarantine(
        IWorldSystem system,
        WorldSystemInstanceClaim claim,
        WorldSystemQuarantineReservation reservation,
        WorldDomainReservation domainReservation)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(reservation);
        ArgumentNullException.ThrowIfNull(domainReservation);

        lock (s_gate)
        {
            if (!s_claims.TryGetValue(system, out var currentClaim) || !ReferenceEquals(currentClaim, claim))
            {
                throw new InvalidOperationException("The failing World system no longer has its original instance claim.");
            }

            reservation.UseSlot();
            s_reservedQuarantineSlots--;
            s_quarantinedSystems.Add(system);

            if (reservation.RetainDomainReservation())
            {
                s_reservedQuarantineDomainReservations--;
                s_quarantinedDomainReservations.Add(domainReservation);
            }
        }
    }

    internal static void ReleaseQuarantineReservation(WorldSystemQuarantineReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        lock (s_gate)
        {
            var (releasedSlots, releasedDomainReservationSlot) = reservation.ReleaseUnusedCapacity();
            s_reservedQuarantineSlots -= releasedSlots;

            if (releasedDomainReservationSlot)
            {
                s_reservedQuarantineDomainReservations--;
            }
        }
    }

    internal static WorldSystemQuarantineReservation ReserveQuarantineCapacity(int maximumFailures)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumFailures);

        lock (s_gate)
        {
            var reservedSlots = checked(s_reservedQuarantineSlots + maximumFailures);
            var reserveDomainReservation = maximumFailures != 0;
            var reservedDomainReservations = checked(
                s_reservedQuarantineDomainReservations + (reserveDomainReservation ? 1 : 0));
            _ = s_quarantinedSystems.EnsureCapacity(checked(s_quarantinedSystems.Count + reservedSlots));
            _ = s_quarantinedDomainReservations.EnsureCapacity(
                checked(s_quarantinedDomainReservations.Count + reservedDomainReservations));
            var reservation = new WorldSystemQuarantineReservation(maximumFailures);
            s_reservedQuarantineSlots = reservedSlots;
            s_reservedQuarantineDomainReservations = reservedDomainReservations;
            return reservation;
        }
    }

    internal static bool TryClaim(IWorldSystem system, WorldSystemInstanceClaim claim)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(claim);

        lock (s_gate)
        {
            if (s_claims.TryGetValue(system, out _))
            {
                return false;
            }

            s_claims.Add(system, claim);
            return true;
        }
    }
}
