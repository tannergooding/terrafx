// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace TerraFX.Entities;

/// <summary>Captures typed field readers and provider roots for one transferred attachment family.</summary>
/// <typeparam name="T">The immutable attachment CLR type.</typeparam>
/// <typeparam name="TAuthor">The immutable symbolic author type.</typeparam>
[SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "The closed attachment and author types are required by the typed factory contract.")]
public sealed class WorldAttachmentFactoryBinding<T, TAuthor>
    where T : class
    where TAuthor : notnull
{
    private readonly WorldAttachmentFactoryDescriptor _descriptor;
    private readonly ImmutableArray<WorldAttachmentFieldBinding<T, TAuthor>> _fields;
    private readonly IWorldAttachmentStateProvider<T, TAuthor> _provider;
    private readonly CompiledContentSchemaRegistry _schemas;

    private WorldAttachmentFactoryBinding(
        WorldAttachmentFactoryDescriptor descriptor,
        ImmutableArray<WorldAttachmentFieldBinding<T, TAuthor>> fields,
        IWorldAttachmentStateProvider<T, TAuthor> provider,
        CompiledContentSchemaRegistry schemas)
    {
        _descriptor = descriptor;
        _fields = fields;
        _provider = provider;
        _schemas = schemas;
    }

    /// <summary>Gets the immutable attachment factory descriptor.</summary>
    public WorldAttachmentFactoryDescriptor Descriptor => _descriptor;

    internal ImmutableArray<WorldAttachmentFieldBinding<T, TAuthor>> Fields => _fields;

    internal IWorldAttachmentStateProvider<T, TAuthor> Provider => _provider;

    internal CompiledContentSchemaRegistry Schemas => _schemas;

    /// <summary>Captures an attachment provider with complete typed schema-field bindings.</summary>
    /// <param name="factoryId">The nonempty factory identity.</param>
    /// <param name="factoryVersion">The positive factory version.</param>
    /// <param name="schemas">The immutable compiled metadata registry.</param>
    /// <param name="schema">The exact AttachmentState schema tuple.</param>
    /// <param name="fields">The complete ordered binding of attachment state fields.</param>
    /// <param name="provider">The typed attachment state and release provider.</param>
    /// <returns>The immutable attachment factory binding.</returns>
    /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
    /// <exception cref="ArgumentException">The schema or field closure is inconsistent.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="factoryVersion"/> is not positive.</exception>
    public static WorldAttachmentFactoryBinding<T, TAuthor> Create(
        Guid factoryId,
        int factoryVersion,
        CompiledContentSchemaRegistry schemas,
        ContentSchemaReference schema,
        ImmutableArray<WorldAttachmentFieldBinding<T, TAuthor>> fields,
        IWorldAttachmentStateProvider<T, TAuthor> provider)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(factoryId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(factoryVersion);
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(provider);

        if ((schema.Scope != ContentSchemaScope.Content)
            || !schemas.TryGetSchema(schema, out var metadata)
            || (metadata.Role != ContentSchemaRole.AttachmentState))
        {
            throw new ArgumentException("The selected tuple is not an exact active Content AttachmentState schema.", nameof(schema));
        }

        var capturedFields = CopyAndValidateFields(schemas, schema, metadata, fields);
        var descriptors = ImmutableArray.CreateBuilder<WorldAttachmentFieldDescriptor>(capturedFields.Length);

        foreach (var field in capturedFields)
        {
            descriptors.Add(field.Descriptor);
        }

        var descriptor = new WorldAttachmentFactoryDescriptor(
            schema,
            factoryId,
            factoryVersion,
            descriptors.MoveToImmutable());
        return new WorldAttachmentFactoryBinding<T, TAuthor>(descriptor, capturedFields, provider, schemas);
    }

    private static ImmutableArray<WorldAttachmentFieldBinding<T, TAuthor>> CopyAndValidateFields(
        CompiledContentSchemaRegistry schemas,
        ContentSchemaReference schema,
        DataSchema metadata,
        ImmutableArray<WorldAttachmentFieldBinding<T, TAuthor>> fields)
    {
        if (fields.IsDefault)
        {
            throw new ArgumentException("Attachment field bindings must be initialized.", nameof(fields));
        }

        var expectedFields = new List<WorldAttachmentFieldDescriptor>();
        CopyFieldDescriptors(schemas, schema, metadata, [], [], expectedFields, nameof(fields));

        if (fields.Length != expectedFields.Count)
        {
            throw new ArgumentException("Attachment field bindings must close every canonical AttachmentState field.", nameof(fields));
        }

        var capturedFields = ImmutableArray.CreateBuilder<WorldAttachmentFieldBinding<T, TAuthor>>(fields.Length);

        for (var index = 0; index < fields.Length; index++)
        {
            var binding = fields[index]
                ?? throw new ArgumentException("Attachment field bindings cannot contain null entries.", nameof(fields));
            var actual = binding.Descriptor;
            var expected = expectedFields[index];

            if ((actual.Schema != schema)
                || !ReferenceEquals(actual.Schemas, schemas)
                || !ReferenceEquals(actual.DataSchema, expected.DataSchema)
                || !ReferenceEquals(actual.Field, expected.Field)
                || !actual.Path.Segments.AsSpan().SequenceEqual(expected.Path.Segments.AsSpan()))
            {
                throw new ArgumentException("Attachment field bindings must match every canonical field in schema order.", nameof(fields));
            }

            capturedFields.Add(binding);
        }

        return capturedFields.MoveToImmutable();
    }

    private static void CopyFieldDescriptors(
        CompiledContentSchemaRegistry schemas,
        ContentSchemaReference attachmentSchema,
        DataSchema currentSchema,
        List<ComponentPropertySegment> prefix,
        List<DataSchema> schemaStack,
        List<WorldAttachmentFieldDescriptor> fields,
        string parameterName)
    {
        foreach (var activeSchema in schemaStack)
        {
            if (ReferenceEquals(activeSchema, currentSchema))
            {
                throw new ArgumentException("The selected AttachmentState schema contains a recursive record closure.", parameterName);
            }
        }

        schemaStack.Add(currentSchema);

        foreach (var field in currentSchema.Fields)
        {
            if (prefix.Count >= 64)
            {
                throw new ArgumentException("The selected AttachmentState schema exceeds the supported field-path depth.", parameterName);
            }

            prefix.Add(ComponentPropertySegment.FieldTag(field.Tag));

            if ((field.WireKind == 22)
                && field.IsRequired
                && (field.Reference == ContentReferenceKind.None)
                && TryResolveNestedSchema(schemas, field, out var nestedSchema))
            {
                CopyFieldDescriptors(schemas, attachmentSchema, nestedSchema, prefix, schemaStack, fields, parameterName);
            }
            else if (field.WireKind is not 22 and not 23 and not 24)
            {
                var path = new ComponentFieldPath([.. prefix]);
                fields.Add(new WorldAttachmentFieldDescriptor(attachmentSchema, path, field, currentSchema, schemas));
            }
            else
            {
                throw new ArgumentException("Attachment state records must resolve to required scalar or optional Entity-reference fields.", parameterName);
            }

            prefix.RemoveAt(prefix.Count - 1);
        }

        schemaStack.RemoveAt(schemaStack.Count - 1);
    }

    private static bool TryResolveNestedSchema(
        CompiledContentSchemaRegistry schemas,
        ContentFieldDescriptor field,
        out DataSchema dataSchema)
    {
        if (field.NestedSchemas.Length == 1
            && schemas.TryGetSchema(field.NestedSchemas[0], out var exactSchema))
        {
            dataSchema = exactSchema;
            return true;
        }

        if (field.NestedSchemas.IsEmpty && (field.NestedRoleMask != 0))
        {
            DataSchema? match = null;

            foreach (var candidate in schemas.Schemas)
            {
                if ((candidate.Schema.Scope == ContentSchemaScope.Content)
                    && ((field.NestedRoleMask & (1UL << ((int)candidate.Role - 1))) != 0))
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
