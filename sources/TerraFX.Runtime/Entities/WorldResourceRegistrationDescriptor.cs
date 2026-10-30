// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Describes one frozen resource identity, partition domain, and factory.</summary>
public sealed class WorldResourceRegistrationDescriptor
{
    internal WorldResourceRegistrationDescriptor(
        SystemResourceDescription description,
        WorldResourceFactoryDescriptor factory)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(factory);
        ResourceId = description.ResourceId;
        PartitionCount = description.PartitionCount;
        AllowParallelAccess = description.AllowParallelAccess;
        Factory = factory;
    }

    /// <summary>Gets whether declared parallel systems may access this resource.</summary>
    public bool AllowParallelAccess { get; }

    /// <summary>Gets the captured typed factory descriptor.</summary>
    public WorldResourceFactoryDescriptor Factory { get; }

    /// <summary>Gets the number of registered partitions.</summary>
    public int PartitionCount { get; }

    /// <summary>Gets the exact resource identity.</summary>
    public Guid ResourceId { get; }
}
