// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace TerraFX.Entities;

/// <summary>Describes one managed resource partition type selected from a compiled Core schema.</summary>
/// <typeparam name="T">The unmanaged resource partition value type.</typeparam>
public sealed class WorldResourceSchema<T>
    where T : unmanaged
{
    private readonly DataSchema _dataSchema;
    private readonly ImmutableArray<WorldResourceFieldDescriptor> _fields;
    private readonly CompiledContentSchemaRegistry _schemas;

    private WorldResourceSchema(
        DataSchema dataSchema,
        CompiledContentSchemaRegistry schemas,
        ImmutableArray<WorldResourceFieldDescriptor> fields)
    {
        _dataSchema = dataSchema;
        _schemas = schemas;
        _fields = ImmutableArrayCopy.Create(fields);
    }

    /// <summary>Gets the immutable root field descriptors.</summary>
    public ImmutableArray<WorldResourceFieldDescriptor> Fields => _fields;

    /// <summary>Gets the exact resource state schema tuple.</summary>
    public ContentSchemaReference Schema => _dataSchema.Schema;

    internal DataSchema Metadata => _dataSchema;

    internal CompiledContentSchemaRegistry Schemas => _schemas;

    /// <summary>Creates a resource schema from its exact compiled metadata registration.</summary>
    /// <param name="schemas">The immutable compiled metadata registry.</param>
    /// <param name="schema">The exact resource-state schema tuple.</param>
    /// <returns>The resource schema bound to the exact compiled registration.</returns>
    /// <exception cref="ArgumentException">The tuple is unknown or does not identify a resource-state schema.</exception>
    [SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "The closed resource partition type is a required input to the public FromRegistry contract.")]
    public static WorldResourceSchema<T> FromRegistry(
        CompiledContentSchemaRegistry schemas,
        ContentSchemaReference schema)
    {
        ArgumentNullException.ThrowIfNull(schemas);

        if ((schema.Scope != ContentSchemaScope.Content)
            || !schemas.TryGetSchema(schema, out var dataSchema)
            || (dataSchema.Role != ContentSchemaRole.ResourceState))
        {
            throw new ArgumentException("The exact Content schema tuple is not a registered resource state.", nameof(schema));
        }

        var fields = ImmutableArray.CreateBuilder<WorldResourceFieldDescriptor>(dataSchema.Fields.Length);

        foreach (var field in dataSchema.Fields)
        {
            var path = new ComponentFieldPath([ComponentPropertySegment.FieldTag(field.Tag)]);
            fields.Add(new WorldResourceFieldDescriptor(schema, path, field, dataSchema, schemas));
        }

        return new WorldResourceSchema<T>(dataSchema, schemas, fields.MoveToImmutable());
    }

    /// <summary>Resolves one exact field path through records and FixedArrays.</summary>
    /// <param name="path">The exact schema field path.</param>
    /// <returns>The field descriptor for the selected schema field or FixedArray element.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is unresolved or ambiguous in this schema.</exception>
    public WorldResourceFieldDescriptor GetField(ComponentFieldPath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var segments = path.Segments;
        var currentSchema = _dataSchema;
        var index = 0;

        while (index < segments.Length)
        {
            var segment = segments[index];

            if (segment.Kind != ComponentPropertySegmentKind.FieldTag)
            {
                throw new ArgumentException("A field tag must select the next schema field.", nameof(path));
            }

            ContentFieldDescriptor? field = null;

            foreach (var candidate in currentSchema.Fields)
            {
                if (candidate.Tag == segment.Value)
                {
                    field = candidate;
                    break;
                }
            }

            if (field is null)
            {
                throw new ArgumentException("The field tag is not declared at this schema path.", nameof(path));
            }

            if (index == (segments.Length - 1))
            {
                return new WorldResourceFieldDescriptor(Schema, path, field, currentSchema, _schemas);
            }

            var nextSegment = segments[index + 1];

            if (nextSegment.Kind == ComponentPropertySegmentKind.FixedArrayIndex)
            {
                if ((field.WireKind != 23)
                    || (nextSegment.Value >= field.MaximumCount)
                    || (index + 2 != segments.Length))
                {
                    throw new ArgumentException("The FixedArray index is not valid at this schema path.", nameof(path));
                }

                return new WorldResourceFieldDescriptor(Schema, path, field, currentSchema, _schemas);
            }

            if ((nextSegment.Kind != ComponentPropertySegmentKind.FieldTag)
                || (field.WireKind != 22)
                || !TryResolveNestedSchema(field, out currentSchema))
            {
                throw new ArgumentException("The record path is unresolved or ambiguous in this schema.", nameof(path));
            }

            index++;
        }

        throw new ArgumentException("The resource-state field path is invalid.", nameof(path));
    }

    internal bool TryResolveNestedSchema(ContentFieldDescriptor field, out DataSchema dataSchema)
    {
        if (field.NestedSchemas.Length == 1)
        {
            if (_schemas.TryGetSchema(field.NestedSchemas[0], out var nestedSchema))
            {
                dataSchema = nestedSchema;
                return true;
            }
        }

        if (field.NestedSchemas.IsEmpty && (field.NestedRoleMask != 0))
        {
            DataSchema? match = null;

            foreach (var candidate in _schemas.Schemas)
            {
                if ((candidate.Schema.Scope == ContentSchemaScope.Content)
                    && (field.NestedRoleMask & (1UL << ((int)candidate.Role - 1))) != 0)
                {
                    if (match is not null)
                    {
                        dataSchema = null!;
                        return false;
                    }

                    match = candidate;
                }
            }

            if (match is not null)
            {
                dataSchema = match;
                return true;
            }
        }

        dataSchema = null!;
        return false;
    }
}
