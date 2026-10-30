// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Entities;

/// <summary>Describes one captured World resource factory registration.</summary>
public sealed class WorldResourceFactoryDescriptor
{
    internal WorldResourceFactoryDescriptor(
        ContentSchemaReference schema,
        Guid factoryId,
        int factoryVersion,
        ComponentStorageDeclaration partitionStorage,
        ImmutableArray<WorldResourceFieldDescriptor> fields)
    {
        Schema = schema;
        FactoryId = factoryId;
        FactoryVersion = factoryVersion;
        PartitionStorage = partitionStorage;
        Fields = ImmutableArrayCopy.Create(fields);
    }

    /// <summary>Gets the stable resource factory identity.</summary>
    public Guid FactoryId { get; }

    /// <summary>Gets the positive resource factory version.</summary>
    public int FactoryVersion { get; }

    /// <summary>Gets the immutable resource state fields bound by this factory.</summary>
    public ImmutableArray<WorldResourceFieldDescriptor> Fields { get; }

    /// <summary>Gets the exact per-partition storage declaration.</summary>
    public ComponentStorageDeclaration PartitionStorage { get; }

    /// <summary>Gets the exact resource state schema tuple.</summary>
    public ContentSchemaReference Schema { get; }
}
