// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class WorldDomainReservation : IDisposable
{
    private long _bytes;
    private WorldAdmissionDomain? _domain;
    private long _independentUseBytes;
    private long _maximumAdditionalBytes;

    internal WorldDomainReservation(
        WorldAdmissionDomain domain,
        long bytes,
        long maximumAdditionalBytes,
        int interestIndex,
        uint interestGeneration)
    {
        _domain = domain;
        _bytes = bytes;
        _maximumAdditionalBytes = maximumAdditionalBytes;
        InitialBytes = bytes;
        InterestIndex = interestIndex;
        InterestGeneration = interestGeneration;
    }

    internal long Bytes => Interlocked.Read(ref _bytes);

    internal long IndependentUseBytes => Interlocked.Read(ref _independentUseBytes);

    internal long InitialBytes { get; }

    internal uint InterestGeneration { get; }

    internal int InterestIndex { get; }

    internal long MaximumAdditionalBytes => Interlocked.Read(ref _maximumAdditionalBytes);

    public void Dispose()
    {
        var domain = Volatile.Read(ref _domain);
        domain?.ReleaseWorld(this);
    }

    internal void AddBytes(long bytes)
        => _ = Interlocked.Add(ref _bytes, bytes);

    internal void AddIndependentUseBytes(long bytes)
        => _ = Interlocked.Add(ref _independentUseBytes, bytes);

    internal void CommitSceneWorkspaceTransfer(long bytes, long independentUseBytes)
    {
        _ = Interlocked.Exchange(ref _bytes, bytes);
        _ = Interlocked.Exchange(ref _independentUseBytes, independentUseBytes);
    }

    internal bool IsOwnedBy(WorldAdmissionDomain domain)
        => ReferenceEquals(Volatile.Read(ref _domain), domain);

    internal void MarkReleased(WorldAdmissionDomain domain)
    {
        if (!ReferenceEquals(Interlocked.CompareExchange(ref _domain, null, domain), domain))
        {
            throw new InvalidOperationException("The World admission-domain interest was already released.");
        }
    }

    internal void ReleaseAdditional(long bytes)
        => ReleaseAdditional(bytes, out _);

    internal void ReleaseAdditional(long bytes, out bool creditReturned)
    {
        creditReturned = false;
        var domain = Volatile.Read(ref _domain)
            ?? throw new ObjectDisposedException(nameof(WorldDomainReservation));
        domain.ReleaseWorldReservationBytes(this, bytes, out creditReturned);
    }

    internal void ReleaseSceneReservation(WorldSceneStorageReservation reservation)
    {
        var domain = Volatile.Read(ref _domain);
        domain?.ReleaseSceneReservation(this, reservation);
    }

    internal void RemoveBytes(long bytes)
        => _ = Interlocked.Add(ref _bytes, -bytes);

    internal void RemoveIndependentUseBytes(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);

        if (bytes > Interlocked.Read(ref _independentUseBytes))
        {
            throw new InvalidOperationException("The released independent-use bytes exceed this World's live operation credits.");
        }

        _ = Interlocked.Add(ref _independentUseBytes, -bytes);
    }

    internal void SetMaximumAdditionalBytes(long bytes)
        => _ = Interlocked.Exchange(ref _maximumAdditionalBytes, bytes);

    internal void SetSceneReservationOwner(WorldSceneStorageReservation reservation, object owner)
    {
        var domain = Volatile.Read(ref _domain)
            ?? throw new ObjectDisposedException(nameof(WorldDomainReservation));
        domain.SetSceneReservationOwner(this, reservation, owner);
    }

    internal bool TryInstallSceneWorkspace(WorldSceneStorageReservation reservation)
    {
        var domain = Volatile.Read(ref _domain);
        return (domain is not null) && domain.TryInstallSceneWorkspace(this, reservation);
    }

    internal bool TryReserveAdditional(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
        var domain = Volatile.Read(ref _domain);
        return (domain is not null) && domain.TryGrowWorldReservation(this, bytes);
    }

    internal bool TryReserveIndependentUseBytes(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
        var currentAdditionalBytes = Interlocked.Read(ref _bytes) - InitialBytes;
        var currentIndependentUseBytes = Interlocked.Read(ref _independentUseBytes);
        var remainingBytes = MaximumAdditionalBytes - currentAdditionalBytes - currentIndependentUseBytes;

        if (bytes > remainingBytes)
        {
            return false;
        }

        _ = Interlocked.Add(ref _independentUseBytes, bytes);
        return true;
    }
}
