// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;

namespace TerraFX.Entities;

[SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "The closed resource record and author types are required by the typed aggregate binding contract.")]
internal sealed class WorldResourceRecordBinding<T, TAuthor> : IWorldResourceRecordBindingNode
    where T : unmanaged
    where TAuthor : notnull
{
    private readonly ImmutableArray<WorldResourceRecordFieldBinding<T, TAuthor>> _fields;
    private readonly IWorldResourceRecordValueValidator<T, TAuthor> _validator;

    private WorldResourceRecordBinding(
        CompiledContentSchemaRegistry schemas,
        ContentSchemaReference schema,
        ComponentStorageDeclaration storage,
        ImmutableArray<WorldResourceRecordFieldBinding<T, TAuthor>> fields,
        IWorldResourceRecordValueValidator<T, TAuthor> validator,
        int depth)
    {
        Schemas = schemas;
        Schema = schema;
        Storage = storage;
        _fields = fields;
        _validator = validator;
        Depth = depth;
    }

    public int Depth { get; }

    public ContentSchemaReference Schema { get; }

    public CompiledContentSchemaRegistry Schemas { get; }

    public ComponentStorageDeclaration Storage { get; }

    internal static WorldResourceRecordBinding<T, TAuthor> Create<TValidator>(
        CompiledContentSchemaRegistry schemas,
        ContentSchemaReference schema,
        ComponentStorageDeclaration storage,
        ImmutableArray<WorldResourceRecordFieldBinding<T, TAuthor>> fields,
        TValidator validator)
        where TValidator : struct, IWorldResourceRecordValueValidator<T, TAuthor>
    {
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(storage);

        if ((schema.Scope != ContentSchemaScope.Content)
            || !schemas.TryGetSchema(schema, out var metadata)
            || (metadata.Role != ContentSchemaRole.Auxiliary))
        {
            throw new ArgumentException("Resource record bindings require an exact compiled Auxiliary schema.", nameof(schema));
        }

        if (storage.LogicalBytes != Unsafe.SizeOf<T>())
        {
            throw new ArgumentException("Resource record storage must match its unmanaged value type.", nameof(storage));
        }

        if (fields.IsDefault || (fields.Length != metadata.Fields.Length))
        {
            throw new ArgumentException("Resource record bindings must close every direct schema field.", nameof(fields));
        }

        var captured = ImmutableArray.CreateBuilder<WorldResourceRecordFieldBinding<T, TAuthor>>(fields.Length);
        var claimed = new bool[storage.Leaves.Length];
        var depth = 1;

        for (var index = 0; index < fields.Length; index++)
        {
            var field = fields[index]
                ?? throw new ArgumentException("Resource record bindings cannot contain null fields.", nameof(fields));

            if (!ReferenceEquals(field.Field, metadata.Fields[index]))
            {
                throw new ArgumentException("Resource record fields must use the exact ordered schema descriptors.", nameof(fields));
            }

            if (field.Child is { } child)
            {
                if (!ReferenceEquals(child.Schemas, schemas)
                    || (field.Field.NestedSchemas.Length != 1)
                    || (field.Field.NestedSchemas[0] != child.Schema)
                    || child.ContainsSchema(schema))
                {
                    throw new ArgumentException("Resource record children must have an exact acyclic schema closure.", nameof(fields));
                }

                depth = Math.Max(depth, checked(child.Depth + 1));
            }

            field.ValidateSchema(schemas);
            field.ValidateStorage(storage, claimed);
            captured.Add(field);
        }

        if (depth > 64)
        {
            throw new ArgumentException("The resource record closure exceeds the supported nesting depth.", nameof(fields));
        }

        for (var index = 0; index < storage.Leaves.Length; index++)
        {
            var leaf = storage.Leaves[index];

            if (leaf.Kind is ComponentStorageKind.EntityHandle
                or ComponentStorageKind.OptionalEntityHandle
                or ComponentStorageKind.TransientNativeValue)
            {
                throw new ArgumentException("Resource records cannot contain Entity handles or native storage.", nameof(storage));
            }

            if ((leaf.Purpose == ComponentStoragePurpose.SchemaValue) && !claimed[index])
            {
                throw new ArgumentException("Every resource record schema-value leaf must be bound exactly once.", nameof(fields));
            }
        }

        return new WorldResourceRecordBinding<T, TAuthor>(
            schemas, schema, storage, captured.MoveToImmutable(), validator, depth);
    }

    public bool ContainsSchema(ContentSchemaReference schema)
    {
        if (schema == Schema)
        {
            return true;
        }

        foreach (var field in _fields)
        {
            if (field.Child is { } child && child.ContainsSchema(schema))
            {
                return true;
            }
        }

        return false;
    }

    internal void ValidateAuthor(in TAuthor value)
    {
        if (value is null || !_validator.IsValidAuthor(in value))
        {
            throw new InvalidDataException("A resource record author violates its captured value predicate.");
        }

        foreach (var field in _fields)
        {
            field.ValidateAuthor(in value);
        }
    }

    internal void ValidateRuntime(in T value)
    {
        if (!_validator.IsValidRuntime(in value))
        {
            throw new InvalidDataException("A resource record violates its captured storage or value predicate.");
        }

        foreach (var field in _fields)
        {
            field.ValidateRuntime(in value);
        }
    }
}
