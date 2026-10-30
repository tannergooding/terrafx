// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Describes one exact semantic field in a World resource state schema.</summary>
public sealed class WorldResourceFieldDescriptor
{
    private readonly DataSchema _dataSchema;
    private readonly ContentFieldDescriptor _metadata;
    private readonly ComponentFieldPath _path;
    private readonly ContentSchemaReference _schema;
    private readonly CompiledContentSchemaRegistry _schemas;

    internal WorldResourceFieldDescriptor(
        ContentSchemaReference schema,
        ComponentFieldPath path,
        ContentFieldDescriptor metadata,
        DataSchema dataSchema,
        CompiledContentSchemaRegistry schemas)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(dataSchema);
        ArgumentNullException.ThrowIfNull(schemas);

        var ownsField = false;

        foreach (var candidate in dataSchema.Fields)
        {
            ownsField |= ReferenceEquals(candidate, metadata);
        }

        if (!ownsField)
        {
            throw new ArgumentException("The selected field must belong to its data schema.", nameof(metadata));
        }

        _schema = schema;
        _path = path;
        _metadata = metadata;
        _dataSchema = dataSchema;
        _schemas = schemas;
    }

    /// <summary>Gets the exact Core field declaration.</summary>
    public ContentFieldDescriptor Metadata => _metadata;

    /// <summary>Gets the exact semantic path to the field or FixedArray element.</summary>
    public ComponentFieldPath Path => _path;

    /// <summary>Gets the exact resource state schema tuple.</summary>
    public ContentSchemaReference Schema => _schema;

    internal DataSchema DataSchema => _dataSchema;

    internal CompiledContentSchemaRegistry Schemas => _schemas;
}
