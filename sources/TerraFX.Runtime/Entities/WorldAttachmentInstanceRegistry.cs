// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Threading;

namespace TerraFX.Entities;

internal static class WorldAttachmentInstanceRegistry
{
    private static readonly Lock s_gate = new();
    private static readonly List<object> s_quarantinedAttachments = [];
    private static readonly List<WorldDomainReservation> s_quarantinedDomainReservations = [];
    private static int s_reservedQuarantineSlots;

    internal static bool HasRetainedDomainReservation(WorldDomainReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        lock (s_gate)
        {
            foreach (var retainedReservation in s_quarantinedDomainReservations)
            {
                if (ReferenceEquals(retainedReservation, reservation))
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal static void Quarantine(
        object attachment,
        WorldAttachmentQuarantineReservation reservation,
        WorldDomainReservation domainReservation)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        ArgumentNullException.ThrowIfNull(reservation);
        ArgumentNullException.ThrowIfNull(domainReservation);

        lock (s_gate)
        {
            reservation.UseSlot();
            s_reservedQuarantineSlots--;
            s_quarantinedAttachments.Add(attachment);
            s_quarantinedDomainReservations.Add(domainReservation);
        }
    }

    internal static void ReleaseQuarantineReservation(WorldAttachmentQuarantineReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        lock (s_gate)
        {
            s_reservedQuarantineSlots -= reservation.ReleaseUnusedCapacity();
        }
    }

    internal static WorldAttachmentQuarantineReservation ReserveQuarantineCapacity(int maximumFailures)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumFailures);

        lock (s_gate)
        {
            var reservedSlots = checked(s_reservedQuarantineSlots + maximumFailures);
            _ = s_quarantinedAttachments.EnsureCapacity(checked(s_quarantinedAttachments.Count + reservedSlots));
            _ = s_quarantinedDomainReservations.EnsureCapacity(checked(s_quarantinedDomainReservations.Count + reservedSlots));
            var reservation = new WorldAttachmentQuarantineReservation(maximumFailures);
            s_reservedQuarantineSlots = reservedSlots;
            return reservation;
        }
    }
}
