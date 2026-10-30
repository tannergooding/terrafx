// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Describes one exact semantic field in a system state schema.</summary>
public sealed class WorldSystemFieldDescriptor
{
    private readonly DataSchema _dataSchema;
    private readonly CompiledContentSchemaRegistry _schemas;

    internal WorldSystemFieldDescriptor(
        ContentSchemaReference stateSchema,
        ComponentFieldPath path,
        ContentFieldDescriptor field,
        DataSchema dataSchema,
        CompiledContentSchemaRegistry schemas)
    {
        if ((stateSchema.SchemaId == Guid.Empty)
            || (stateSchema.Major <= 0)
            || (stateSchema.Minor < 0))
        {
            throw new ArgumentException("A system field requires a valid state schema tuple.", nameof(stateSchema));
        }

        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(dataSchema);
        ArgumentNullException.ThrowIfNull(schemas);

        var ownsField = false;

        foreach (var candidate in dataSchema.Fields)
        {
            ownsField |= ReferenceEquals(candidate, field);
        }

        if (!ownsField)
        {
            throw new ArgumentException("The selected field must belong to its data schema.", nameof(field));
        }

        StateSchema = stateSchema;
        Path = path;
        Field = field;
        _dataSchema = dataSchema;
        _schemas = schemas;
    }

    /// <summary>Gets the exact Core field declaration.</summary>
    public ContentFieldDescriptor Field { get; }

    /// <summary>Gets the exact semantic path to the field or FixedArray element.</summary>
    public ComponentFieldPath Path { get; }

    /// <summary>Gets the exact system state schema tuple.</summary>
    public ContentSchemaReference StateSchema { get; }

    internal DataSchema DataSchema => _dataSchema;

    internal CompiledContentSchemaRegistry Schemas => _schemas;
}
