// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Diagnostics.CodeAnalysis;

namespace TerraFX.Pooling;

/// <summary>Reports a copied observation of an object pool's bounded ownership state.</summary>
[SuppressMessage("Performance", "CA1815", Justification = "Snapshot equality is not part of its accepted reporting contract.")]
public readonly struct PoolSnapshot
{
    internal PoolSnapshot(int maximumCapacity, int createdCount, int availableCount, int rentedCount, int returningCount, int quarantinedCount, int retiredCount, int rentalHighWater, long misses, bool isClosed, bool isDrained)
    {
        MaximumCapacity = maximumCapacity;
        CreatedCount = createdCount;
        AvailableCount = availableCount;
        RentedCount = rentedCount;
        ReturningCount = returningCount;
        QuarantinedCount = quarantinedCount;
        RetiredCount = retiredCount;
        RentalHighWater = rentalHighWater;
        Misses = misses;
        IsClosed = isClosed;
        IsDrained = isDrained;
    }

    /// <summary>Gets the number of reusable payloads.</summary>
    public int AvailableCount { get; }

    /// <summary>Gets the number of currently owned payloads.</summary>
    public int CreatedCount { get; }

    /// <summary>Gets a value indicating whether the pool is permanently closed to new work.</summary>
    public bool IsClosed { get; }

    /// <summary>Gets a value indicating whether a closed pool has released all owned payloads and callbacks.</summary>
    public bool IsDrained { get; }

    /// <summary>Gets the pool's fixed maximum number of slots.</summary>
    public int MaximumCapacity { get; }

    /// <summary>Gets the saturating count of requests that found no available payload.</summary>
    public long Misses { get; }

    /// <summary>Gets the number of payloads retained after cleanup failure.</summary>
    public int QuarantinedCount { get; }

    /// <summary>Gets the maximum number of simultaneous rented and returning payloads observed.</summary>
    public int RentalHighWater { get; }

    /// <summary>Gets the number of payloads currently rented.</summary>
    public int RentedCount { get; }

    /// <summary>Gets the number of permanently retired slots.</summary>
    public int RetiredCount { get; }

    /// <summary>Gets the number of returns waiting for an active callback or cleanup.</summary>
    public int ReturningCount { get; }
}
