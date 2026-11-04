// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace TerraFX.Entities;

public sealed partial class World
{
    internal void ReleasePersistenceAdmissionBytes(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);

        lock (_persistenceAdmissionGate)
        {
            if (bytes > _activePersistenceAdmissionBytes)
            {
                throw new InvalidOperationException("The released persistence admission bytes exceed the live reservation.");
            }

            _activePersistenceAdmissionBytes -= bytes;
        }
    }

    internal bool TryGrowPersistenceAdmissionBytes(WorldDomainReservation domainReservation, long bytes)
    {
        ArgumentNullException.ThrowIfNull(domainReservation);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);

        lock (_persistenceAdmissionGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            if (bytes > _maximumPersistenceAdmissionBytes - _activePersistenceAdmissionBytes)
            {
                return false;
            }

            _activePersistenceAdmissionBytes += bytes;
        }

        if (domainReservation.TryReserveAdditional(bytes))
        {
            return true;
        }

        ReleasePersistenceAdmissionBytes(bytes);
        return false;
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "A successful result transfers the exact domain reservation to the returned persistence reservation; the finally path releases it when admission fails.")]
    internal bool TryReservePersistenceAdmissionBytes(
        long bytes,
        long maximumAdditionalBytes,
        out WorldPersistenceAdmissionReservation? reservation)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumAdditionalBytes);
        reservation = null;

        lock (_persistenceAdmissionGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            if (bytes > _maximumPersistenceAdmissionBytes - _activePersistenceAdmissionBytes)
            {
                return false;
            }

            _activePersistenceAdmissionBytes += bytes;
        }

        WorldDomainReservation? domainReservation = null;
        var reservationCreated = false;

        try
        {
            if (!_admissionDomain.TryReserveWorld(bytes, maximumAdditionalBytes, out domainReservation)
                || (domainReservation is null)
                || (Volatile.Read(ref _disposed) != 0))
            {
                return false;
            }

            reservation = new WorldPersistenceAdmissionReservation(this, domainReservation, bytes);
            domainReservation = null;
            reservationCreated = true;
            return true;
        }
        finally
        {
            if (!reservationCreated)
            {
                domainReservation?.Dispose();
                ReleasePersistenceAdmissionBytes(bytes);
            }
        }
    }

    internal bool TryReservePersistenceAdmissionBytes(long bytes, out WorldPersistenceAdmissionReservation? reservation)
        => TryReservePersistenceAdmissionBytes(bytes, maximumAdditionalBytes: 0, out reservation);
}
