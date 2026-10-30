// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace TerraFX.Entities;

/// <summary>Describes one managed component type selected from a compiled Core schema.</summary>
/// <typeparam name="T">The unmanaged component value type.</typeparam>
public sealed class ComponentSchema<T>
    where T : unmanaged
{
    private readonly DataSchema _dataSchema;
    private readonly ImmutableArray<ComponentFieldDescriptor> _fields;
    private readonly CompiledContentSchemaRegistry _schemas;
    private readonly ComponentTransientRestore _transientRestore;

    /// <summary>Initializes a component schema from fields emitted by an exact compiled registration.</summary>
    /// <param name="schema">The exact component schema tuple.</param>
    /// <param name="layout">The component's physical storage layout.</param>
    /// <param name="fields">The complete root field descriptors from the compiled registration.</param>
    /// <param name="flags">The selected component storage class.</param>
    /// <exception cref="ArgumentException">The registration source or selected schema metadata is inconsistent.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="flags"/> does not select exactly one storage class.</exception>
    public ComponentSchema(
        ContentSchemaReference schema,
        ComponentLayout layout,
        ImmutableArray<ComponentFieldDescriptor> fields,
        ComponentStorageFlags flags)
        : this(schema, layout, fields, flags, GetSource(fields))
    {
    }

    private ComponentSchema(
        ContentSchemaReference schema,
        ComponentLayout layout,
        ImmutableArray<ComponentFieldDescriptor> fields,
        ComponentStorageFlags flags,
        (DataSchema DataSchema, CompiledContentSchemaRegistry Schemas) source)
        : this(schema, layout, fields, flags, source.DataSchema, source.Schemas)
    {
    }

    private ComponentSchema(
        ContentSchemaReference schema,
        ComponentLayout layout,
        ImmutableArray<ComponentFieldDescriptor> fields,
        ComponentStorageFlags flags,
        DataSchema dataSchema,
        CompiledContentSchemaRegistry schemas)
    {
        if ((schema.SchemaId == Guid.Empty)
            || (schema.Major <= 0)
            || (schema.Minor < 0))
        {
            throw new ArgumentException("A component schema requires a valid schema tuple.", nameof(schema));
        }

        ArgumentNullException.ThrowIfNull(dataSchema);
        ArgumentNullException.ThrowIfNull(schemas);

        if ((layout.LayoutId == Guid.Empty)
            || (layout.LogicalBytes is < 1 or > 32756)
            || (layout.LogicalBytes != Unsafe.SizeOf<T>()))
        {
            throw new ArgumentException("The component layout must identify the exact managed value size.", nameof(layout));
        }

        if (fields.IsDefault)
        {
            throw new ArgumentException("The component field array must be initialized.", nameof(fields));
        }

        if (flags is not ComponentStorageFlags.Persistent and not ComponentStorageFlags.Transient)
        {
            throw new ArgumentOutOfRangeException(nameof(flags));
        }

        if ((dataSchema.Schema != schema)
            || (dataSchema.Role != ContentSchemaRole.Component)
            || (flags != GetStorageFlags(dataSchema.Storage))
            || !schemas.TryGetSchema(schema, out var registeredSchema)
            || !ReferenceEquals(registeredSchema, dataSchema)
            || (fields.Length != dataSchema.Fields.Length))
        {
            throw new ArgumentException("The component schema must match its exact compiled registration.", nameof(schema));
        }

        uint previousTag = 0;

        for (var index = 0; index < fields.Length; index++)
        {
            var field = fields[index] ?? throw new ArgumentException("The component field array cannot contain null entries.", nameof(fields));

            if ((field.ComponentSchema != schema)
                || !ReferenceEquals(field.DataSchema, dataSchema)
                || !ReferenceEquals(field.Schemas, schemas)
                || (field.Path.Segments.Length != 1)
                || (field.Path.Segments[0].Kind != ComponentPropertySegmentKind.FieldTag)
                || (field.Path.Segments[0].Value != field.Field.Tag)
                || (field.Field.Tag <= previousTag)
                || !ReferenceEquals(field.Field, dataSchema.Fields[index]))
            {
                throw new ArgumentException("Component fields must be the complete ordered root declarations from one registration.", nameof(fields));
            }

            previousTag = field.Field.Tag;
        }

        Schema = schema;
        Layout = layout;
        Flags = flags;
        _dataSchema = dataSchema;
        _schemas = schemas;
        _fields = ImmutableArrayCopy.Create(fields);
        _transientRestore = GetTransientRestore(dataSchema.TransientRestore);
    }

    /// <summary>Gets the immutable root field descriptors.</summary>
    public ImmutableArray<ComponentFieldDescriptor> Fields => _fields;

    /// <summary>Gets the component's selected storage class.</summary>
    public ComponentStorageFlags Flags { get; }

    /// <summary>Gets the physical component layout.</summary>
    public ComponentLayout Layout { get; }

    /// <summary>Gets the exact component schema tuple.</summary>
    public ContentSchemaReference Schema { get; }

    internal DataSchema Metadata => _dataSchema;

    internal CompiledContentSchemaRegistry Schemas => _schemas;

    internal ComponentTransientRestore TransientRestore => _transientRestore;

    /// <summary>Creates a component schema from its exact compiled metadata registration.</summary>
    /// <param name="schemas">The immutable compiled metadata registry.</param>
    /// <param name="schema">The exact component schema tuple.</param>
    /// <param name="layout">The component's physical storage layout.</param>
    /// <returns>The component schema bound to the exact compiled registration.</returns>
    /// <exception cref="ArgumentException">The tuple is unknown or does not identify a component schema.</exception>
    [SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "The closed component type is a required input to the public FromRegistry contract.")]
    public static ComponentSchema<T> FromRegistry(
        CompiledContentSchemaRegistry schemas,
        ContentSchemaReference schema,
        ComponentLayout layout)
    {
        ArgumentNullException.ThrowIfNull(schemas);

        if (!schemas.TryGetSchema(schema, out var dataSchema) || (dataSchema.Role != ContentSchemaRole.Component))
        {
            throw new ArgumentException("The exact schema tuple is not a registered component.", nameof(schema));
        }

        var fields = ImmutableArray.CreateBuilder<ComponentFieldDescriptor>(dataSchema.Fields.Length);

        foreach (var field in dataSchema.Fields)
        {
            var path = new ComponentFieldPath([ComponentPropertySegment.FieldTag(field.Tag)]);
            fields.Add(new ComponentFieldDescriptor(schema, path, field, dataSchema, schemas));
        }

        return new ComponentSchema<T>(schema, layout, fields.MoveToImmutable(), GetStorageFlags(dataSchema.Storage), dataSchema, schemas);
    }

    private static (DataSchema DataSchema, CompiledContentSchemaRegistry Schemas) GetSource(
        ImmutableArray<ComponentFieldDescriptor> fields)
    {
        if (fields.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A directly constructed component schema requires its compiled root fields.", nameof(fields));
        }

        var source = fields[0];
        ArgumentNullException.ThrowIfNull(source);
        return (source.DataSchema, source.Schemas);
    }

    private static ComponentStorageFlags GetStorageFlags(ContentSchemaStorage storage) => storage switch {
        ContentSchemaStorage.Persistent => ComponentStorageFlags.Persistent,
        ContentSchemaStorage.Transient => ComponentStorageFlags.Transient,
        _ => throw new ArgumentOutOfRangeException(nameof(storage)),
    };

    private static ComponentTransientRestore GetTransientRestore(ContentTransientRestore transientRestore) => transientRestore switch {
        ContentTransientRestore.NotApplicable => ComponentTransientRestore.None,
        ContentTransientRestore.ResetDeclaredDefaults => ComponentTransientRestore.Reset,
        ContentTransientRestore.ExactDerivedRehydration => ComponentTransientRestore.Derived,
        _ => throw new ArgumentOutOfRangeException(nameof(transientRestore)),
    };

    /// <summary>Resolves one exact field path through records and FixedArrays.</summary>
    /// <param name="path">The exact schema field path.</param>
    /// <returns>The field descriptor for the selected schema field or FixedArray element.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is unresolved or ambiguous in this schema.</exception>
    public ComponentFieldDescriptor GetField(ComponentFieldPath path)
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
                return new ComponentFieldDescriptor(Schema, path, field, currentSchema, _schemas);
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

                return new ComponentFieldDescriptor(Schema, path, field, currentSchema, _schemas);
            }

            if ((nextSegment.Kind != ComponentPropertySegmentKind.FieldTag)
                || (field.WireKind != 22)
                || !TryResolveNestedSchema(field, out currentSchema))
            {
                throw new ArgumentException("The record path is unresolved or ambiguous in this schema.", nameof(path));
            }

            index++;
        }

        throw new ArgumentException("The component field path is invalid.", nameof(path));
    }

    private bool TryResolveNestedSchema(ContentFieldDescriptor field, out DataSchema dataSchema)
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
