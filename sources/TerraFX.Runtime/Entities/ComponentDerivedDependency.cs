// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Identifies one persistent component dependency of a Derived component restore.</summary>
public sealed class ComponentDerivedDependency
{
    private readonly Type _componentType;
    private readonly DataSchema _metadata;
    private readonly ContentSchemaReference _schema;
    private readonly CompiledContentSchemaRegistry _schemas;

    private ComponentDerivedDependency(
        ContentSchemaReference schema,
        Type componentType,
        DataSchema metadata,
        CompiledContentSchemaRegistry schemas)
    {
        _schema = schema;
        _componentType = componentType;
        _metadata = metadata;
        _schemas = schemas;
    }

    /// <summary>Gets the closed unmanaged dependency component type.</summary>
    public Type ComponentType => _componentType;

    /// <summary>Gets the exact persistent dependency schema tuple.</summary>
    public ContentSchemaReference Schema => _schema;

    internal DataSchema Metadata => _metadata;

    internal CompiledContentSchemaRegistry Schemas => _schemas;

    /// <summary>Creates a Derived dependency from an exact persistent component schema.</summary>
    /// <typeparam name="TDependency">The unmanaged dependency component type.</typeparam>
    /// <param name="schema">The exact compiled dependency schema.</param>
    /// <returns>The immutable Derived dependency description.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="schema"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="schema"/> does not select a Persistent component.</exception>
    public static ComponentDerivedDependency FromSchema<TDependency>(ComponentSchema<TDependency> schema)
        where TDependency : unmanaged
    {
        ArgumentNullException.ThrowIfNull(schema);

        if (schema.Flags != ComponentStorageFlags.Persistent)
        {
            throw new ArgumentException("A Derived dependency must be a Persistent component.", nameof(schema));
        }

        return new ComponentDerivedDependency(schema.Schema, typeof(TDependency), schema.Metadata, schema.Schemas);
    }
}
