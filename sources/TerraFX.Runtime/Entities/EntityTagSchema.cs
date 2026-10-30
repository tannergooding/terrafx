// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Describes one zero-payload entity tag by its stable identity and schema version.</summary>
public sealed class EntityTagSchema
{
    /// <summary>Initializes an entity tag schema declaration.</summary>
    /// <param name="schemaId">The nonempty stable schema identity.</param>
    /// <param name="schemaVersion">The positive schema major version.</param>
    /// <exception cref="ArgumentException"><paramref name="schemaId"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="schemaVersion"/> is not positive.</exception>
    public EntityTagSchema(Guid schemaId, int schemaVersion)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(schemaId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaVersion);

        SchemaId = schemaId;
        SchemaVersion = schemaVersion;
    }

    /// <summary>Gets the stable schema identity.</summary>
    public Guid SchemaId { get; }

    /// <summary>Gets the positive schema major version.</summary>
    public int SchemaVersion { get; }

    internal ContentSchemaReference Schema
        => new(SchemaId, SchemaVersion, 0, ContentSchemaScope.Content);
}
