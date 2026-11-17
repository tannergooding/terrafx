// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace TerraFX.Entities;

[SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "The closed attachment and author types are required by the typed aggregate binding contract.")]
internal sealed class WorldAttachmentRecordBinding<T, TAuthor> : IWorldAttachmentRecordBindingNode
    where T : class
    where TAuthor : notnull
{
    private readonly DataSchema _dataSchema;
    private readonly int _depth;
    private readonly ImmutableArray<WorldAttachmentRecordFieldBinding<T, TAuthor>> _fields;
    private readonly ContentSchemaReference _schema;
    private readonly CompiledContentSchemaRegistry _schemas;

    private WorldAttachmentRecordBinding(
        CompiledContentSchemaRegistry schemas,
        ContentSchemaReference schema,
        DataSchema dataSchema,
        ImmutableArray<WorldAttachmentRecordFieldBinding<T, TAuthor>> fields,
        int depth)
    {
        _schemas = schemas;
        _schema = schema;
        _dataSchema = dataSchema;
        _fields = fields;
        _depth = depth;
    }

    internal DataSchema DataSchema => _dataSchema;

    internal int Depth => _depth;

    internal ImmutableArray<WorldAttachmentRecordFieldBinding<T, TAuthor>> Fields => _fields;

    internal ContentSchemaReference Schema => _schema;

    internal CompiledContentSchemaRegistry Schemas => _schemas;

    int IWorldAttachmentRecordBindingNode.Depth => _depth;

    ContentSchemaRole IWorldAttachmentRecordBindingNode.Role => _dataSchema.Role;

    ContentSchemaReference IWorldAttachmentRecordBindingNode.Schema => _schema;

    CompiledContentSchemaRegistry IWorldAttachmentRecordBindingNode.Schemas => _schemas;

    internal static WorldAttachmentRecordBinding<T, TAuthor> Create(
        CompiledContentSchemaRegistry schemas,
        ContentSchemaReference schema,
        ImmutableArray<WorldAttachmentRecordFieldBinding<T, TAuthor>> fields)
    {
        ArgumentNullException.ThrowIfNull(schemas);

        if ((schema.Scope != ContentSchemaScope.Content)
            || !schemas.TryGetSchema(schema, out var metadata)
            || (metadata.Role is not ContentSchemaRole.AttachmentState and not ContentSchemaRole.Auxiliary
                && !IsLocalTransformComponent(schema, metadata.Role)))
        {
            throw new ArgumentException("The selected tuple is not a compiled Content AttachmentState or Auxiliary schema.", nameof(schema));
        }

        if (fields.IsDefault || (fields.Length != metadata.Fields.Length))
        {
            throw new ArgumentException("Aggregate attachment fields must close every direct schema field.", nameof(fields));
        }

        var capturedFields = ImmutableArray.CreateBuilder<WorldAttachmentRecordFieldBinding<T, TAuthor>>(fields.Length);
        var depth = 1;

        for (var index = 0; index < fields.Length; index++)
        {
            var binding = fields[index]
                ?? throw new ArgumentException("Aggregate attachment fields cannot contain null entries.", nameof(fields));
            var expectedField = metadata.Fields[index];

            if (!ReferenceEquals(binding.Field, expectedField))
            {
                throw new ArgumentException("Aggregate attachment fields must be the exact ordered descriptors from the selected schema.", nameof(fields));
            }

            var child = binding.Child;

            if (child is not null)
            {
                if (!ReferenceEquals(child.Schemas, schemas)
                    || (expectedField.NestedSchemas.Length != 1)
                    || (expectedField.NestedSchemas[0] != child.Schema))
                {
                    throw new ArgumentException("An aggregate attachment child must match its exact nested schema and registry.", nameof(fields));
                }

                depth = Math.Max(depth, checked(child.Depth + 1));
            }
            else if ((expectedField.WireKind is 22 or 24) && !binding.IsAssetReference)
            {
                throw new ArgumentException("An aggregate attachment record field requires its exact typed child binding.", nameof(fields));
            }

            if (binding.IsAssetReference
                && (!binding.TargetSchemaId.HasValue
                    || (binding.TargetSchemaId.Value == Guid.Empty)
                    || binding.TargetMajors.IsDefaultOrEmpty))
            {
                throw new ArgumentException("An aggregate attachment AssetReference must retain its exact target schema policy.", nameof(fields));
            }

            if (binding.IsAssetReference)
            {
                if (!schemas.TryGetSchema(binding.Field.NestedSchemas[0], out var assetReferenceSchema)
                    || !IsAssetReferenceSchema(assetReferenceSchema))
                {
                    throw new ArgumentException("An aggregate AssetReference must use the exact three-field native/asset-reference schema.", nameof(fields));
                }

                var targetSchemaId = binding.TargetSchemaId
                    ?? throw new ArgumentException("An aggregate attachment AssetReference must retain its target schema.", nameof(fields));

                foreach (var targetMajor in binding.TargetMajors)
                {
                    var targetReference = new ContentSchemaReference(
                        targetSchemaId,
                        targetMajor,
                        0,
                        ContentSchemaScope.Content);

                    if (!schemas.TryGetSchema(targetReference, out var targetSchema)
                        || (targetSchema.Role != ContentSchemaRole.NativeAsset))
                    {
                        throw new ArgumentException("An aggregate AssetReference target must be an exact compiled NativeAsset schema major.", nameof(fields));
                    }
                }
            }

            capturedFields.Add(binding);
        }

        if (depth > 64)
        {
            throw new ArgumentException("The aggregate attachment record closure exceeds the supported nesting depth.", nameof(fields));
        }

        return new WorldAttachmentRecordBinding<T, TAuthor>(
            schemas,
            schema,
            metadata,
            capturedFields.MoveToImmutable(),
            depth);
    }

    private static bool IsAssetReferenceField(
        ContentFieldDescriptor field,
        uint tag,
        ushort kind,
        ContentReferenceKind reference)
        => (field.Tag == tag)
            && (field.WireKind == kind)
            && field.IsRequired
            && (field.MinimumCount == 1)
            && (field.MaximumCount == 1)
            && field.NestedSchemas.IsEmpty
            && (field.NestedRoleMask == 0)
            && (field.Unit == ContentUnit.None)
            && (field.Reference == reference)
            && (field.AuthoringFlags == ContentAuthoring.None)
            && (field.DefaultValue is null);

    private static bool IsAssetReferenceSchema(DataSchema schema)
    {
        var fields = schema.Fields;

        return (fields.Length == 3)
            && IsAssetReferenceField(fields[0], tag: 1, kind: 3, ContentReferenceKind.Asset)
            && IsAssetReferenceField(fields[1], tag: 2, kind: 11, ContentReferenceKind.Schema)
            && IsAssetReferenceField(fields[2], tag: 3, kind: 3, ContentReferenceKind.None);
    }

    private static bool IsLocalTransformComponent(ContentSchemaReference schema, ContentSchemaRole role)
    {
        var schemaId = new ContentSchemaIdentityAssignment("scene/local-transform").SchemaId;
        return (role == ContentSchemaRole.Component)
            && (schema.SchemaId == schemaId)
            && (schema.Major == 1)
            && (schema.Minor == 0)
            && (schema.Scope == ContentSchemaScope.Content);
    }

    internal void ValidateAuthor(in TAuthor author)
    {
        foreach (var field in _fields)
        {
            field.ValidateAuthor(in author);
        }
    }

    internal void ValidateNestedRoles()
    {
        foreach (var field in _fields)
        {
            if (field.Child is { } child)
            {
                if ((child.Role != ContentSchemaRole.Auxiliary)
                    && !IsLocalTransformComponent(child.Schema, child.Role))
                {
                    throw new ArgumentException("Nested attachment Records must resolve to Auxiliary schemas.");
                }

                child.ValidateNestedRoles();
            }
        }
    }

    internal void ValidateRuntime(T value, IWorldAttachmentValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(context);

        foreach (var field in _fields)
        {
            field.ValidateRuntime(value, context);
        }
    }

    void IWorldAttachmentRecordBindingNode.ValidateNestedRoles()
        => ValidateNestedRoles();
}
