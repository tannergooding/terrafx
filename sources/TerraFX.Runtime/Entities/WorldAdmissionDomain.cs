// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace TerraFX.Entities;

/// <summary>Bounds the World histories and owned memory admitted by one standalone domain.</summary>
public sealed class WorldAdmissionDomain : IDisposable
{
    private const int FreshRootLineageKeyCount = 1;
    private const int MaximumEdges = 1_048_576;
    private const int MaximumInterests = 65_536;
    private const int MaximumLineageIndexCapacity = 524_288;
    private const int MaximumLineageKeys = 262_144;
    private const int MaximumLineages = 4096;
    private const long MaximumRegistryBytes = 134_217_728;
    private const int MaximumReservations = 4096;
    private const long MinimumBytes = 4096;
    private const int MinimumLineageIndexCapacity = 16;
    private const long RegistrarControlBytes = 4096;
    private const int StateCommitted = 2;
    private const int StateFree = 0;
    private const int StateOccupied = 2;
    private const int StateReserved = 1;
    private const int StateRetired = 3;

    private int _activeInterestCount;
    private int _activeReservationCount;
    private ReentrantGate _gate;
    private ReentrantGate _growthGate;
    private InterestEntry[] _interests;
    private bool _isDisposed;
    private int _lineageEdgeCount;
    private LineageIndexBucket[] _lineageIndex;
    private int _lineageKeyCount;
    private Guid[] _lineageKeys;
    private LineageEntry[] _lineages;
    private readonly long _maximumBytes;
    private long _pendingGrowthBytes;
    private long _registryCharge;
    private ReservationEntry[] _reservations;
    private readonly TaskCompletionSource _retirement;
    private long _worldBytes;

    /// <summary>Initializes a bounded domain for fresh World histories.</summary>
    /// <param name="maximumBytes">The maximum combined registry and World-owned bytes, in [4096, 8589934592].</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maximumBytes"/> is outside its supported range.</exception>
    public WorldAdmissionDomain(long maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, MinimumBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumBytes, 8_589_934_592L);

        _maximumBytes = maximumBytes;
        _retirement = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (lineageCapacity, indexCapacity) = GetInitialRegistryCapacities(maximumBytes);
        _lineages = (lineageCapacity == 0) ? [] : new LineageEntry[lineageCapacity];
        _lineageKeys = (lineageCapacity == 0) ? [] : new Guid[checked(lineageCapacity * FreshRootLineageKeyCount)];
        _lineageIndex = (indexCapacity == 0) ? [] : new LineageIndexBucket[indexCapacity];
        _interests = (lineageCapacity == 0) ? [] : new InterestEntry[lineageCapacity];
        _reservations = (lineageCapacity == 0) ? [] : new ReservationEntry[lineageCapacity];
        _registryCharge = GetRegistryCharge(lineageCapacity, indexCapacity);
    }

    /// <summary>Gets the maximum combined registry and World-owned bytes.</summary>
    public long MaximumBytes => _maximumBytes;

    /// <summary>Gets the task that completes after the domain closes and all World interests retire.</summary>
    public Task Retirement => _retirement.Task;

    private static long ArrayCharge(int capacity, int stride)
    {
        var payloadBytes = checked((long)capacity * stride);
        var roundedBytes = checked((payloadBytes + 15L) & ~15L);
        return checked(64L + roundedBytes);
    }

    private static (int RootCapacity, int IndexCapacity) GetExpandedRegistryCapacities(
        int currentRootCapacity,
        int currentIndexCapacity,
        long maximumBytes,
        long currentRegistryCharge,
        long pendingGrowthBytes,
        long worldBytes,
        long additionalWorldBytes)
    {
        var registryBudget = Math.Min(MaximumRegistryBytes, maximumBytes);
        var firstCapacity = Math.Min(
            MaximumLineages,
            Math.Max(checked(currentRootCapacity + 1), checked(currentRootCapacity * 2)));

        for (var rootCapacity = firstCapacity; rootCapacity > currentRootCapacity; rootCapacity--)
        {
            var indexCapacity = GetIndexCapacityForRoots(rootCapacity, currentIndexCapacity);
            var registryCharge = GetRegistryCharge(rootCapacity, indexCapacity);
            var newArrayCharge = GetRegistryArrayCharge(rootCapacity, indexCapacity);
            var peakRegistryCharge = checked(currentRegistryCharge + pendingGrowthBytes + newArrayCharge);

            if ((registryCharge <= registryBudget)
                && (peakRegistryCharge <= registryBudget)
                && (checked(peakRegistryCharge + worldBytes + additionalWorldBytes) <= maximumBytes))
            {
                return (rootCapacity, indexCapacity);
            }
        }

        return (0, 0);
    }

    private static int GetIndexCapacityForRoots(int rootCapacity, int currentIndexCapacity)
    {
        var minimumCapacity = Math.Max(MinimumLineageIndexCapacity, checked(rootCapacity * 2));
        var capacity = Math.Min(MaximumLineageIndexCapacity, Math.Max(MinimumLineageIndexCapacity, currentIndexCapacity));

        while ((capacity < minimumCapacity) && (capacity < MaximumLineageIndexCapacity))
        {
            capacity = checked(capacity * 2);
        }

        return Math.Min(capacity, MaximumLineageIndexCapacity);
    }

    private static (int RootCapacity, int IndexCapacity) GetInitialRegistryCapacities(long maximumBytes)
    {
        var registryBudget = Math.Min(MaximumRegistryBytes, maximumBytes);
        var minimumWorldBytes = Math.Min(4096L, maximumBytes - RegistrarControlBytes);

        if ((registryBudget <= RegistrarControlBytes) || (maximumBytes <= RegistrarControlBytes))
        {
            return (0, 0);
        }

        var initialCharge = GetRegistryCharge(1, MinimumLineageIndexCapacity);
        return (initialCharge <= registryBudget && initialCharge + minimumWorldBytes <= maximumBytes)
            ? (1, MinimumLineageIndexCapacity)
            : (0, 0);
    }

    private static long GetRegistryArrayCharge(int rootCapacity, int indexCapacity)
    {
        var charge = 0L;

        if (rootCapacity > 0)
        {
            charge = checked(charge
                + ArrayCharge(rootCapacity, Unsafe.SizeOf<LineageEntry>())
                + ArrayCharge(rootCapacity, Unsafe.SizeOf<InterestEntry>())
                + ArrayCharge(rootCapacity, Unsafe.SizeOf<ReservationEntry>())
                + ArrayCharge(checked(rootCapacity * FreshRootLineageKeyCount), Unsafe.SizeOf<Guid>()));
        }

        if (indexCapacity > 0)
        {
            charge = checked(charge + ArrayCharge(indexCapacity, Unsafe.SizeOf<LineageIndexBucket>()));
        }

        return charge;
    }

    private static long GetRegistryCharge(int rootCapacity, int indexCapacity)
        => checked(RegistrarControlBytes + GetRegistryArrayCharge(rootCapacity, indexCapacity));

    private static bool IsMatchingReservation(in ReservationEntry reservation, in WorldLineageGrant grant)
        => (reservation.Generation == grant.Generation)
        && (reservation.RootIndex == grant.Index)
        && (reservation.LineageId == grant.LineageId)
        && (reservation.State == StateReserved);

    private static void RehashLineageIndex(LineageIndexBucket[] source, LineageIndexBucket[] destination)
    {
        var mask = destination.Length - 1;

        foreach (var sourceBucket in source)
        {
            if (sourceBucket._hashCode == 0)
            {
                continue;
            }

            var bucketIndex = (int)(sourceBucket._hashCode & (uint)mask);
            var distance = 0;

            while (destination[bucketIndex]._hashCode != 0)
            {
                bucketIndex = (bucketIndex + 1) & mask;
                distance++;
            }

            destination[bucketIndex] = sourceBucket;
            destination[bucketIndex]._probeDistance = (uint)distance;
        }
    }

    /// <summary>Closes new domain admissions without synchronously waiting for current Worlds.</summary>
    public void Dispose()
    {
        var completeRetirement = false;

        using var growthScope = ReentrantGate.EnterScope(ref _growthGate);

        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                completeRetirement = _activeInterestCount == 0;

                if (completeRetirement)
                {
                    ReleaseRegistry();
                }
            }
        }

        if (completeRetirement)
        {
            _ = _retirement.TrySetResult();
        }
    }

    internal void ReleaseWorld(WorldDomainReservation reservation)
        => ReleaseWorld(0, 0, 0, reservation);

    internal void ReleaseWorld(long bytes, int interestIndex, uint interestGeneration, WorldDomainReservation? reservation)
    {
        var completeRetirement = false;

        using var growthScope = ReentrantGate.EnterScope(ref _growthGate);

        using (ReentrantGate.EnterScope(ref _gate))
        {
            if ((reservation is not null) && !reservation.IsOwnedBy(this))
            {
                return;
            }

            if (reservation is not null)
            {
                bytes = reservation.Bytes;
                interestIndex = reservation.InterestIndex;
                interestGeneration = reservation.InterestGeneration;
            }

            if ((bytes <= 0)
                || (_worldBytes < bytes)
                || (_activeInterestCount <= 0)
                || ((uint)interestIndex >= (uint)_interests.Length))
            {
                throw new InvalidOperationException("The World admission-domain reservation is not active.");
            }

            ref var interest = ref _interests[interestIndex];

            if ((interest.Generation != interestGeneration)
                || (interest.State != StateOccupied)
                || (interest.Bytes != bytes))
            {
                throw new InvalidOperationException("The World admission-domain interest does not match its issued generation.");
            }

            _worldBytes = checked(_worldBytes - bytes);
            _activeInterestCount--;
            interest.Bytes = 0;
            interest.State = (interest.Generation == uint.MaxValue) ? StateRetired : StateFree;

            if (interest.State == StateFree)
            {
                interest.Generation++;
            }

            reservation?.MarkReleased(this);
            completeRetirement = _isDisposed && (_activeInterestCount == 0);

            if (completeRetirement)
            {
                ReleaseRegistry();
            }
        }

        if (completeRetirement)
        {
            _ = _retirement.TrySetResult();
        }
    }

    internal void ReleaseWorldReservationBytes(WorldDomainReservation reservation, long bytes)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);

        using var growthScope = ReentrantGate.EnterScope(ref _growthGate);

        using (ReentrantGate.EnterScope(ref _gate))
        {
            ObjectDisposedException.ThrowIf(!IsActiveWorldReservation(reservation), reservation);

            var interestIndex = reservation.InterestIndex;
            ref var interest = ref _interests[interestIndex];
            var reservedBytes = reservation.Bytes;

            if ((interest.Bytes != reservedBytes)
                || (bytes >= reservedBytes)
                || (_worldBytes < bytes))
            {
                throw new InvalidOperationException("The released World-domain bytes do not match the live interest.");
            }

            interest.Bytes = checked(interest.Bytes - bytes);
            _worldBytes = checked(_worldBytes - bytes);
            reservation.RemoveBytes(bytes);
        }
    }

    internal bool TryCommitFreshLineage(in WorldLineageGrant grant)
    {
        if (!grant.IsOwnedBy(this) || !(grant.Reservation?.IsOwnedBy(this) ?? false))
        {
            return false;
        }

        using var growthScope = ReentrantGate.EnterScope(ref _growthGate);

        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (_isDisposed
                || !IsActiveWorldReservation(grant.Reservation!)
                || ((uint)grant.Index >= (uint)_lineages.Length))
            {
                return false;
            }

            ref var entry = ref _lineages[grant.Index];
            var keyOffset = grant.Index * FreshRootLineageKeyCount;
            ref var reservation = ref _reservations[grant.Index];

            if ((entry.Generation != grant.Generation)
                || (entry.WorldGuid != grant.WorldGuid)
                || (entry.IdentityBranchId != grant.IdentityBranchId)
                || (_lineageKeys[keyOffset] != grant.LineageId)
                || !IsMatchingReservation(reservation, grant)
                || (entry.State != StateReserved))
            {
                return false;
            }

            entry.State = StateCommitted;
            ReleaseReservation(grant.Index);
            return true;
        }
    }

    internal bool TryGrowWorldReservation(WorldDomainReservation reservation, long bytes)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);

        using var growthScope = ReentrantGate.EnterScope(ref _growthGate);

        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (_isDisposed || !IsActiveWorldReservation(reservation))
            {
                return false;
            }

            if (bytes > _maximumBytes - _registryCharge - _pendingGrowthBytes - _worldBytes)
            {
                return false;
            }

            var interestIndex = reservation.InterestIndex;
            ref var interest = ref _interests[interestIndex];
            var updatedBytes = checked(interest.Bytes + bytes);
            _worldBytes = checked(_worldBytes + bytes);
            interest.Bytes = updatedBytes;
            reservation.AddBytes(bytes);
            return true;
        }
    }

    internal bool TryReleaseFreshLineage(in WorldLineageGrant grant)
    {
        if (!grant.IsOwnedBy(this))
        {
            return false;
        }

        using var growthScope = ReentrantGate.EnterScope(ref _growthGate);

        using (ReentrantGate.EnterScope(ref _gate))
        {
            if ((uint)grant.Index >= (uint)_lineages.Length)
            {
                return false;
            }

            ref var entry = ref _lineages[grant.Index];
            var keyOffset = grant.Index * FreshRootLineageKeyCount;
            ref var reservation = ref _reservations[grant.Index];

            if ((entry.Generation != grant.Generation)
                || (entry.WorldGuid != grant.WorldGuid)
                || (entry.IdentityBranchId != grant.IdentityBranchId)
                || (_lineageKeys[keyOffset] != grant.LineageId)
                || (entry.State != StateReserved))
            {
                return false;
            }

            if (!IsMatchingReservation(reservation, grant))
            {
                return false;
            }

            RemoveLineageIndexEntry(grant.LineageId);
            ClearLineageRoot(ref entry, keyOffset);
            ReleaseReservation(grant.Index);
            return true;
        }
    }

    internal bool TryReserveFreshLineage(
        WorldDomainReservation reservation,
        Guid worldGuid,
        Guid lineageId,
        Guid identityBranchId,
        out WorldLineageGrant grant,
        out WorldAdmissionRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        if (lineageId == Guid.Empty)
        {
            throw new ArgumentException("A fresh replay lineage cannot be empty.", nameof(lineageId));
        }

        if (worldGuid == Guid.Empty)
        {
            throw new ArgumentException("A WorldGuid cannot be empty.", nameof(worldGuid));
        }

        if (identityBranchId == Guid.Empty)
        {
            throw new ArgumentException("An initial identity branch cannot be empty.", nameof(identityBranchId));
        }

        if (!reservation.IsOwnedBy(this))
        {
            throw new ArgumentException("A fresh lineage requires a live reservation from this exact domain.", nameof(reservation));
        }

        using var growthScope = ReentrantGate.EnterScope(ref _growthGate);

        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (_isDisposed)
            {
                grant = default;
                refusal = WorldAdmissionRefusal.DomainClosed;
                return false;
            }
        }

        if (!EnsureLineageCapacity())
        {
            grant = default;
            refusal = WorldAdmissionRefusal.LineageCapacity;
            return false;
        }

        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (_isDisposed)
            {
                grant = default;
                refusal = WorldAdmissionRefusal.DomainClosed;
                return false;
            }

            if (!IsActiveWorldReservation(reservation))
            {
                throw new ArgumentException("A fresh lineage requires an active World admission reservation.", nameof(reservation));
            }

            if (FindLineageIndexBucket(lineageId) >= 0)
            {
                grant = default;
                refusal = WorldAdmissionRefusal.LineageAlreadyRetained;
                return false;
            }

            if ((_activeReservationCount >= MaximumReservations)
                || (_lineageEdgeCount >= MaximumEdges)
                || (_lineageKeyCount >= Math.Min(MaximumLineageKeys, _lineageIndex.Length / 2)))
            {
                grant = default;
                refusal = WorldAdmissionRefusal.LineageCapacity;
                return false;
            }

            var freeIndex = -1;

            for (var index = 0; index < _lineages.Length; index++)
            {
                ref var entry = ref _lineages[index];

                if ((freeIndex < 0) && (entry.State == StateFree))
                {
                    freeIndex = index;
                }
            }

            if (freeIndex < 0)
            {
                grant = default;
                refusal = WorldAdmissionRefusal.LineageCapacity;
                return false;
            }

            ref var reservedEntry = ref _lineages[freeIndex];
            ref var reservationEntry = ref _reservations[freeIndex];

            if (reservationEntry.State != StateFree)
            {
                throw new InvalidOperationException("A free lineage root has a non-free reservation cell.");
            }

            reservedEntry.Generation = (reservedEntry.Generation == 0) ? 1U : reservedEntry.Generation;
            reservedEntry.State = StateReserved;
            reservedEntry.WorldGuid = worldGuid;
            reservedEntry.IdentityBranchId = identityBranchId;
            var reservedKeyOffset = freeIndex * FreshRootLineageKeyCount;

            if (!TryAddLineageIndexEntry(lineageId))
            {
                reservedEntry.State = StateFree;
                reservedEntry.WorldGuid = Guid.Empty;
                reservedEntry.IdentityBranchId = Guid.Empty;
                grant = default;
                refusal = WorldAdmissionRefusal.LineageCapacity;
                return false;
            }

            _lineageKeys[reservedKeyOffset] = lineageId;
            reservationEntry.Generation = reservedEntry.Generation;
            reservationEntry.RootIndex = freeIndex;
            reservationEntry.LineageId = lineageId;
            reservationEntry.State = StateReserved;
            _activeReservationCount++;
            grant = new WorldLineageGrant(
                this,
                reservation,
                freeIndex,
                reservedEntry.Generation,
                worldGuid,
                lineageId,
                identityBranchId);
            refusal = WorldAdmissionRefusal.None;
            return true;
        }
    }

    internal bool TryReserveWorld(long bytes, out WorldDomainReservation? reservation)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);

        using var growthScope = ReentrantGate.EnterScope(ref _growthGate);

        if (!EnsureInterestCapacity(bytes))
        {
            reservation = null;
            return false;
        }

        var interestIndex = -1;
        uint interestGeneration = 0;

        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (_isDisposed
                || (_activeInterestCount >= MaximumInterests)
                || (bytes > _maximumBytes - _registryCharge - _pendingGrowthBytes - _worldBytes))
            {
                reservation = null;
                return false;
            }

            for (var index = 0; index < _interests.Length; index++)
            {
                ref var interest = ref _interests[index];

                if (interest.State != StateFree)
                {
                    continue;
                }

                interest.Generation = (interest.Generation == 0) ? 1U : interest.Generation;
                interest.State = StateOccupied;
                interest.Bytes = bytes;
                interestIndex = index;
                interestGeneration = interest.Generation;
                break;
            }

            if (interestIndex < 0)
            {
                reservation = null;
                return false;
            }

            _worldBytes = checked(_worldBytes + bytes);
            _activeInterestCount++;
        }

        try
        {
            reservation = new WorldDomainReservation(this, bytes, interestIndex, interestGeneration);
            return true;
        }
        catch (OutOfMemoryException)
        {
            ReleaseWorld(bytes, interestIndex, interestGeneration, null);
            throw;
        }
    }

    internal bool TryRollbackFreshLineage(in WorldLineageGrant grant)
    {
        if (!grant.IsOwnedBy(this))
        {
            return false;
        }

        using var growthScope = ReentrantGate.EnterScope(ref _growthGate);

        using (ReentrantGate.EnterScope(ref _gate))
        {
            if ((uint)grant.Index >= (uint)_lineages.Length)
            {
                return false;
            }

            ref var entry = ref _lineages[grant.Index];
            var keyOffset = grant.Index * FreshRootLineageKeyCount;

            if ((entry.Generation != grant.Generation)
                || (entry.WorldGuid != grant.WorldGuid)
                || (entry.IdentityBranchId != grant.IdentityBranchId)
                || (_lineageKeys[keyOffset] != grant.LineageId)
                || (entry.State is not StateReserved and not StateCommitted))
            {
                return false;
            }

            var reservationActive = entry.State == StateReserved;

            if (reservationActive && !IsMatchingReservation(_reservations[grant.Index], grant))
            {
                return false;
            }

            RemoveLineageIndexEntry(grant.LineageId);
            ClearLineageRoot(ref entry, keyOffset);

            if (reservationActive)
            {
                ReleaseReservation(grant.Index);
            }

            return true;
        }
    }

    private void ClearLineageRoot(ref LineageEntry entry, int keyOffset)
    {
        _lineageKeys[keyOffset] = Guid.Empty;
        entry.WorldGuid = Guid.Empty;
        entry.IdentityBranchId = Guid.Empty;
        entry.State = (entry.Generation == uint.MaxValue) ? StateRetired : StateFree;

        if (entry.State == StateFree)
        {
            entry.Generation++;
        }
    }

    private bool EnsureInterestCapacity(long additionalWorldBytes)
    {
        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (_isDisposed)
            {
                return false;
            }

            for (var index = 0; index < _interests.Length; index++)
            {
                if (_interests[index].State == StateFree)
                {
                    return true;
                }
            }
        }

        return GrowRegistryForOneRoot(additionalWorldBytes);
    }

    private bool EnsureLineageCapacity()
    {
        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (_isDisposed)
            {
                return false;
            }

            for (var index = 0; index < _lineages.Length; index++)
            {
                if (_lineages[index].State == StateFree)
                {
                    return true;
                }
            }
        }

        return GrowRegistryForOneRoot(0);
    }

    private int FindLineageIndexBucket(Guid lineageId)
    {
        if (_lineageIndex.Length == 0)
        {
            return -1;
        }

        var hashCode = unchecked((uint)lineageId.GetHashCode());
        hashCode = (hashCode == 0) ? 1U : hashCode;
        var bucketIndex = (int)(hashCode & (uint)(_lineageIndex.Length - 1));

        for (var distance = 0; distance < _lineageIndex.Length; distance++)
        {
            ref var bucket = ref _lineageIndex[bucketIndex];

            if (bucket._hashCode == 0)
            {
                return -1;
            }

            if ((bucket._hashCode == hashCode) && (bucket._lineageId == lineageId))
            {
                return bucketIndex;
            }

            bucketIndex = (bucketIndex + 1) & (_lineageIndex.Length - 1);
        }

        return -1;
    }

    private bool GrowRegistryForOneRoot(long additionalWorldBytes)
    {
        LineageEntry[] oldLineages;
        LineageIndexBucket[] oldLineageIndex;
        Guid[] oldLineageKeys;
        InterestEntry[] oldInterests;
        ReservationEntry[] oldReservations;
        int newRootCapacity;
        int newIndexCapacity;

        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (_isDisposed)
            {
                return false;
            }

            (newRootCapacity, newIndexCapacity) = GetExpandedRegistryCapacities(
                _lineages.Length,
                _lineageIndex.Length,
                _maximumBytes,
                _registryCharge,
                _pendingGrowthBytes,
                _worldBytes,
                additionalWorldBytes);

            if (newRootCapacity == 0)
            {
                return false;
            }

            _pendingGrowthBytes = GetRegistryArrayCharge(newRootCapacity, newIndexCapacity);
            oldLineages = _lineages;
            oldLineageIndex = _lineageIndex;
            oldLineageKeys = _lineageKeys;
            oldInterests = _interests;
            oldReservations = _reservations;
        }

        try
        {
            var newLineages = new LineageEntry[newRootCapacity];
            var newLineageIndex = new LineageIndexBucket[newIndexCapacity];
            var newLineageKeys = new Guid[checked(newRootCapacity * FreshRootLineageKeyCount)];
            var newInterests = new InterestEntry[newRootCapacity];
            var newReservations = new ReservationEntry[newRootCapacity];
            Array.Copy(oldLineages, newLineages, oldLineages.Length);
            Array.Copy(oldLineageKeys, newLineageKeys, oldLineageKeys.Length);
            Array.Copy(oldInterests, newInterests, oldInterests.Length);
            Array.Copy(oldReservations, newReservations, oldReservations.Length);
            RehashLineageIndex(oldLineageIndex, newLineageIndex);

            using (ReentrantGate.EnterScope(ref _gate))
            {
                _lineages = newLineages;
                _lineageIndex = newLineageIndex;
                _lineageKeys = newLineageKeys;
                _interests = newInterests;
                _reservations = newReservations;
                _registryCharge = GetRegistryCharge(newRootCapacity, newIndexCapacity);
                _pendingGrowthBytes = 0;
            }
        }
        catch
        {
            using (ReentrantGate.EnterScope(ref _gate))
            {
                _pendingGrowthBytes = 0;
            }

            throw;
        }

        return true;
    }

    private bool IsActiveWorldReservation(WorldDomainReservation reservation)
    {
        var index = reservation.InterestIndex;

        if (!reservation.IsOwnedBy(this) || ((uint)index >= (uint)_interests.Length))
        {
            return false;
        }

        ref var interest = ref _interests[index];
        return (interest.Generation == reservation.InterestGeneration)
            && (interest.State == StateOccupied)
            && (interest.Bytes > 0);
    }

    private void ReleaseRegistry()
    {
        _lineages = [];
        _lineageIndex = [];
        _lineageKeys = [];
        _interests = [];
        _reservations = [];
        _activeInterestCount = 0;
        _activeReservationCount = 0;
        _lineageEdgeCount = 0;
        _lineageKeyCount = 0;
        _pendingGrowthBytes = 0;
        _registryCharge = 0;
    }

    private void ReleaseReservation(int index)
    {
        ref var reservation = ref _reservations[index];

        if (reservation.State != StateReserved)
        {
            throw new InvalidOperationException("The lineage reservation cell is not active.");
        }

        reservation.LineageId = Guid.Empty;
        reservation.RootIndex = -1;
        reservation.State = (reservation.Generation == uint.MaxValue) ? StateRetired : StateFree;

        if (reservation.State == StateFree)
        {
            reservation.Generation++;
        }

        _activeReservationCount--;
    }

    private void RemoveLineageIndexEntry(Guid lineageId)
    {
        var bucketIndex = FindLineageIndexBucket(lineageId);

        if (bucketIndex < 0)
        {
            throw new InvalidOperationException("The replay lineage is missing from the domain index.");
        }

        ref var target = ref _lineageIndex[bucketIndex];

        if ((target._lineageId != lineageId) || (target._edgeCount <= 0))
        {
            throw new InvalidOperationException("The domain lineage index points to an invalid edge.");
        }

        _lineageEdgeCount--;

        if (target._edgeCount > 1)
        {
            target._edgeCount--;
            return;
        }

        var hole = bucketIndex;
        var next = (hole + 1) & (_lineageIndex.Length - 1);

        while (_lineageIndex[next]._hashCode != 0)
        {
            ref var candidate = ref _lineageIndex[next];
            var homeBucket = (next - (int)candidate._probeDistance) & (_lineageIndex.Length - 1);
            var distanceToHole = (hole - homeBucket) & (_lineageIndex.Length - 1);

            if (distanceToHole < candidate._probeDistance)
            {
                _lineageIndex[hole] = candidate;
                _lineageIndex[hole]._probeDistance = (uint)distanceToHole;
                hole = next;
            }

            next = (next + 1) & (_lineageIndex.Length - 1);
        }

        _lineageIndex[hole] = default;
        _lineageKeyCount--;
    }

    private bool TryAddLineageIndexEntry(Guid lineageId)
    {
        if ((_lineageIndex.Length == 0)
            || (_lineageEdgeCount >= MaximumEdges)
            || (_lineageKeyCount >= Math.Min(MaximumLineageKeys, _lineageIndex.Length / 2)))
        {
            return false;
        }

        var hashCode = unchecked((uint)lineageId.GetHashCode());
        hashCode = (hashCode == 0) ? 1U : hashCode;
        var bucketIndex = (int)(hashCode & (uint)(_lineageIndex.Length - 1));

        for (var distance = 0; distance < _lineageIndex.Length; distance++)
        {
            ref var bucket = ref _lineageIndex[bucketIndex];

            if (bucket._hashCode == 0)
            {
                bucket._lineageId = lineageId;
                bucket._edgeCount = 1;
                bucket._hashCode = hashCode;
                bucket._probeDistance = (uint)distance;
                _lineageEdgeCount++;
                _lineageKeyCount++;
                return true;
            }

            if ((bucket._hashCode == hashCode) && (bucket._lineageId == lineageId))
            {
                return false;
            }

            bucketIndex = (bucketIndex + 1) & (_lineageIndex.Length - 1);
        }

        return false;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8, Size = 128)]
    private struct InterestEntry
    {
        internal uint Generation;

        internal int State;

        internal long Bytes;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8, Size = 256)]
    private struct LineageEntry
    {
        internal uint Generation;

        internal int State;

        internal Guid WorldGuid;

        internal Guid IdentityBranchId;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 64)]
    private struct LineageIndexBucket
    {
        internal int _edgeCount;

        internal uint _hashCode;

        internal Guid _lineageId;

        internal uint _probeDistance;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8, Size = 256)]
    private struct ReservationEntry
    {
        internal uint Generation;

        internal Guid LineageId;

        internal int RootIndex;

        internal int State;
    }
}
