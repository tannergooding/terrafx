// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Describes one exact semantic field in a component schema.</summary>
public sealed class ComponentFieldDescriptor
{
    private readonly ContentSchemaReference _componentSchema;
    private readonly DataSchema _dataSchema;
    private readonly ContentFieldDescriptor _field;
    private readonly ComponentFieldPath _path;
    private readonly CompiledContentSchemaRegistry _schemas;

    internal ComponentFieldDescriptor(
        ContentSchemaReference componentSchema,
        ComponentFieldPath path,
        ContentFieldDescriptor field,
        DataSchema dataSchema,
        CompiledContentSchemaRegistry schemas)
    {
        if ((componentSchema.SchemaId == Guid.Empty)
            || (componentSchema.Major <= 0)
            || (componentSchema.Minor < 0))
        {
            throw new ArgumentException("A component field requires a valid component schema tuple.", nameof(componentSchema));
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

        _componentSchema = componentSchema;
        _dataSchema = dataSchema;
        _field = field;
        _path = path;
        _schemas = schemas;
    }

    /// <summary>Gets the root component schema tuple.</summary>
    public ContentSchemaReference ComponentSchema => _componentSchema;

    /// <summary>Gets the exact Core field declaration.</summary>
    public ContentFieldDescriptor Field => _field;

    /// <summary>Gets the exact semantic path to the field or FixedArray element.</summary>
    public ComponentFieldPath Path => _path;

    internal DataSchema DataSchema => _dataSchema;

    internal CompiledContentSchemaRegistry Schemas => _schemas;
}
