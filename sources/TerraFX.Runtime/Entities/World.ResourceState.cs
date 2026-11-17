// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Entities;

public sealed partial class World
{
    private long _resourceOperationByteReservation;

    internal void ReleasePreparedRestoreSourceBytes(long bytes)
    {
        lock (_sceneStorageGate)
        {
            if ((bytes <= 0) || (bytes > _resourceOperationByteReservation))
            {
                throw new InvalidOperationException("The prepared source released an invalid World/resource byte interest.");
            }

            _resourceOperationByteReservation -= bytes;
        }
    }

    internal void ReleaseResourceOperationBytes(long bytes)
    {
        lock (_sceneStorageGate)
        {
            if ((bytes <= 0) || (bytes > _resourceOperationByteReservation))
            {
                throw new InvalidOperationException("The resource operation released an invalid owned byte reservation.");
            }

            _resourceOperationByteReservation -= bytes;
            _domainReservation.ReleaseAdditional(bytes);
        }
    }

    internal WorldResourceOperationReservation ReserveResourceOperationBytes(long bytes, long resourceStorageBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
        EnsureOwnerThread();

        lock (_sceneStorageGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var resourceBytes = checked(_resourceOperationByteReservation + bytes);
            var resourceAndSystemBytes = checked(
                _resourceStorageByteReservation + _systemInstanceStorageByteReservation
                + _systemInvocationStorageByteReservation + resourceBytes);

            if ((checked(resourceBytes + resourceStorageBytes) > 128L * 1024 * 1024)
                || (resourceAndSystemBytes > _options.SchedulingLimits.MaximumResourceAndSystemStateBytes)
                || (checked(GetWorldOwnedStateBytes() + bytes) > _maximumOwnedStateBytes)
                || !_domainReservation.TryReserveAdditional(bytes))
            {
                throw new InvalidOperationException("The complete resource operation does not fit its resource, World, or domain byte limits.");
            }

            try
            {
                var reservation = new WorldResourceOperationReservation(this, bytes);
                _resourceOperationByteReservation = resourceBytes;
                return reservation;
            }
            catch
            {
                _domainReservation.ReleaseAdditional(bytes);
                throw;
            }
        }
    }

    internal WorldResourceOperationReservation RetainPreparedRestoreSourceBudget(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
        EnsureOwnerThread();

        lock (_sceneStorageGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var resourceBytes = checked(
                _resourceStorageByteReservation + _systemInstanceStorageByteReservation
                + _systemInvocationStorageByteReservation + _resourceOperationByteReservation + bytes);

            if (!_isRestoreCandidate
                || (resourceBytes > _options.SchedulingLimits.MaximumResourceAndSystemStateBytes)
                || (checked(GetWorldOwnedStateBytes() + bytes) > _maximumOwnedStateBytes))
            {
                throw new InvalidOperationException("The restored source graph does not fit the candidate's existing World/resource byte limits.");
            }

            var reservation = new WorldResourceOperationReservation(this, bytes, ownsDomainCredit: false);
            _resourceOperationByteReservation = checked(_resourceOperationByteReservation + bytes);
            return reservation;
        }
    }

    internal bool TryReservePreparedRestoreSourceBytes(
        long bytes,
        out WorldPersistenceAdmissionReservation? reservation)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);

        lock (_sceneStorageGate)
        {
            var resourceBytes = checked(
                _resourceStorageByteReservation + _systemInstanceStorageByteReservation
                + _systemInvocationStorageByteReservation + _resourceOperationByteReservation + bytes);

            if ((resourceBytes > _options.SchedulingLimits.MaximumResourceAndSystemStateBytes)
                || (checked(GetWorldOwnedStateBytes() + bytes) > _maximumOwnedStateBytes))
            {
                reservation = null;
                return false;
            }

            if (!TryReservePersistenceAdmissionBytes(bytes, out reservation) || (reservation is null))
            {
                return false;
            }

            reservation.BindPreparedResourceSourceStorage();
            _resourceOperationByteReservation = checked(_resourceOperationByteReservation + bytes);
            return true;
        }
    }

    internal bool TryResolveResourceHistoricalEntity(
        Guid entityId, Guid creationLineageId, Guid creationBranchId, out WorldEntityIdentity identity)
    {
        if (_identityLedger.TryFind(entityId, out var ordinal)
            && (_identityLedger.GetKindByOrdinal(ordinal) == IdentityKind.Entity))
        {
            var branch = _identityBranches[_identityLedger.GetBranchIndexForOrdinal(ordinal)];

            if ((branch.BranchId == creationBranchId) && (branch.LineageId == creationLineageId))
            {
                identity = new WorldEntityIdentity(_worldGuid, creationLineageId, creationBranchId, entityId);
                return true;
            }
        }

        identity = default;
        return false;
    }
}
