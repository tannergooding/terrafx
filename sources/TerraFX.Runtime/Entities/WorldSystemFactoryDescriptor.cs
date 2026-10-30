// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Entities;

/// <summary>Describes one captured World system factory family.</summary>
public sealed class WorldSystemFactoryDescriptor
{
    internal WorldSystemFactoryDescriptor(
        Guid factoryId,
        int factoryVersion,
        Type systemType,
        WorldSystemSourceStateDeclaration sourceState,
        ContentSchemaReference? stateSchema,
        Type? authorType,
        ImmutableArray<WorldSystemFieldDescriptor> fields)
    {
        FactoryId = factoryId;
        FactoryVersion = factoryVersion;
        SystemType = systemType;
        SourceState = sourceState;
        StateSchema = stateSchema;
        AuthorType = authorType;
        Fields = ImmutableArrayCopy.Create(fields);
    }

    /// <summary>Gets the symbolic author type for a stateful factory.</summary>
    public Type? AuthorType { get; }

    /// <summary>Gets the stable factory identity.</summary>
    public Guid FactoryId { get; }

    /// <summary>Gets the positive factory version.</summary>
    public int FactoryVersion { get; }

    /// <summary>Gets the immutable system-state field descriptors.</summary>
    public ImmutableArray<WorldSystemFieldDescriptor> Fields { get; }

    /// <summary>Gets the closed source-state declaration.</summary>
    public WorldSystemSourceStateDeclaration SourceState { get; }

    /// <summary>Gets the exact state schema for a stateful factory.</summary>
    public ContentSchemaReference? StateSchema { get; }

    /// <summary>Gets the concrete closed World system type.</summary>
    public Type SystemType { get; }
}
