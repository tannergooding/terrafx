// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Describes one exact semantic field in an attachment state schema.</summary>
public sealed class WorldAttachmentFieldDescriptor
{
    private readonly DataSchema _dataSchema;
    private readonly ContentFieldDescriptor _field;
    private readonly ComponentFieldPath _path;
    private readonly ContentSchemaReference _schema;
    private readonly CompiledContentSchemaRegistry _schemas;

    internal WorldAttachmentFieldDescriptor(
        ContentSchemaReference schema,
        ComponentFieldPath path,
        ContentFieldDescriptor field,
        DataSchema dataSchema,
        CompiledContentSchemaRegistry schemas)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(dataSchema);
        ArgumentNullException.ThrowIfNull(schemas);

        if (schema.Scope != ContentSchemaScope.Content)
        {
            throw new ArgumentException("An attachment state schema must be a Content schema.", nameof(schema));
        }

        if (!schemas.TryGetSchema(schema, out var stateSchema) || (stateSchema.Role != ContentSchemaRole.AttachmentState))
        {
            throw new ArgumentException("The selected tuple is not a registered AttachmentState schema.", nameof(schema));
        }

        if (!schemas.TryGetSchema(dataSchema.Schema, out var fieldSchema) || !ReferenceEquals(fieldSchema, dataSchema))
        {
            throw new ArgumentException("The selected field schema is not an exact member of the compiled registry.", nameof(dataSchema));
        }

        var ownsField = false;

        foreach (var candidate in dataSchema.Fields)
        {
            ownsField |= ReferenceEquals(candidate, field);
        }

        if (!ownsField)
        {
            throw new ArgumentException("The selected field must belong to its data schema.", nameof(field));
        }

        _schema = schema;
        _path = path;
        _field = field;
        _dataSchema = dataSchema;
        _schemas = schemas;
    }

    /// <summary>Gets the exact Core field declaration.</summary>
    public ContentFieldDescriptor Field => _field;

    /// <summary>Gets the exact semantic path to the field or FixedArray element.</summary>
    public ComponentFieldPath Path => _path;

    /// <summary>Gets the exact attachment state schema tuple.</summary>
    public ContentSchemaReference Schema => _schema;

    internal DataSchema DataSchema => _dataSchema;

    internal CompiledContentSchemaRegistry Schemas => _schemas;
}
