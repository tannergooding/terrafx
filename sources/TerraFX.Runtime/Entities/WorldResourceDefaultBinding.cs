// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Identifies one resource partition default-provider invocation.</summary>
public sealed class WorldResourceDefaultBinding
{
    internal WorldResourceDefaultBinding(
        Guid resourceId,
        ContentSchemaReference schema,
        int partitionCount,
        int partitionIndex)
    {
        ResourceId = resourceId;
        Schema = schema;
        PartitionCount = partitionCount;
        PartitionIndex = partitionIndex;
    }

    /// <summary>Gets the complete resource partition count.</summary>
    public int PartitionCount { get; }

    /// <summary>Gets the zero-based partition index being initialized.</summary>
    public int PartitionIndex { get; }

    /// <summary>Gets the resource identity.</summary>
    public Guid ResourceId { get; }

    /// <summary>Gets the exact resource state schema tuple.</summary>
    public ContentSchemaReference Schema { get; }
}
