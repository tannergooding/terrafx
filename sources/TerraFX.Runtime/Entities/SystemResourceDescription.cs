// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Defines a World-owned resource and its bounded partition domain.</summary>
public sealed class SystemResourceDescription
{
    private const int MaximumPartitionCount = 1_048_576;

    /// <summary>Initializes a World resource description.</summary>
    /// <param name="resourceId">The nonempty resource identity.</param>
    /// <param name="partitionCount">The number of resource partitions.</param>
    /// <param name="allowParallelAccess"><see langword="true"/> when declared parallel systems may access this resource.</param>
    /// <exception cref="ArgumentException"><paramref name="resourceId"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="partitionCount"/> is outside its supported range.</exception>
    public SystemResourceDescription(Guid resourceId, int partitionCount, bool allowParallelAccess)
    {
        if (resourceId == Guid.Empty)
        {
            throw new ArgumentException("A resource identity cannot be empty.", nameof(resourceId));
        }

        if (resourceId == WorldSystemResources.EntityMembershipId)
        {
            throw new ArgumentException("The entity-membership capability is engine-defined, not a World resource.", nameof(resourceId));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(partitionCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(partitionCount, MaximumPartitionCount);

        ResourceId = resourceId;
        PartitionCount = partitionCount;
        AllowParallelAccess = allowParallelAccess;
    }

    /// <summary>Gets whether parallel systems are eligible to access this resource.</summary>
    public bool AllowParallelAccess { get; }

    /// <summary>Gets the number of resource partitions.</summary>
    public int PartitionCount { get; }

    /// <summary>Gets the resource's stable registration identity.</summary>
    public Guid ResourceId { get; }
}
