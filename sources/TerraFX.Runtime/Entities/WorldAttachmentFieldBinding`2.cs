// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using TerraFX.Numerics;

namespace TerraFX.Entities;

/// <summary>Binds one attachment schema field to concrete immutable runtime and author readers.</summary>
/// <typeparam name="T">The immutable attachment value type.</typeparam>
/// <typeparam name="TAuthor">The immutable symbolic author type.</typeparam>
[SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "The closed attachment and author types are required by the typed accessor factory contract.")]
public sealed class WorldAttachmentFieldBinding<T, TAuthor>
    where T : class
    where TAuthor : notnull
{
    private readonly IWorldAttachmentFieldAccessor<T, TAuthor> _accessor;
    private readonly WorldAttachmentFieldDescriptor _descriptor;
    private readonly WorldAttachmentFieldBindingKind _kind;

    private WorldAttachmentFieldBinding(
        WorldAttachmentFieldDescriptor descriptor,
        WorldAttachmentFieldBindingKind kind,
        IWorldAttachmentFieldAccessor<T, TAuthor> accessor)
    {
        _descriptor = descriptor;
        _kind = kind;
        _accessor = accessor;
    }

    /// <summary>Gets the selected Core attachment field descriptor.</summary>
    public WorldAttachmentFieldDescriptor Descriptor => _descriptor;

    internal WorldAttachmentFieldBindingKind Kind => _kind;

    /// <summary>Creates an optional Entity-reference field binding.</summary>
    /// <typeparam name="TRuntime">The concrete optional runtime reader struct.</typeparam>
    /// <typeparam name="TAuthorReader">The concrete optional author reader struct.</typeparam>
    /// <param name="schemas">The immutable compiled metadata registry.</param>
    /// <param name="schema">The exact AttachmentState schema tuple.</param>
    /// <param name="path">The exact schema field path.</param>
    /// <param name="runtime">The concrete runtime reader, copied by value.</param>
    /// <param name="author">The concrete author reader, copied by value.</param>
    /// <returns>The immutable optional Entity-reference field binding.</returns>
    /// <exception cref="ArgumentException">The schema field or reader shape is incompatible.</exception>
    public static WorldAttachmentFieldBinding<T, TAuthor> OptionalEntityReference<TRuntime, TAuthorReader>(
        CompiledContentSchemaRegistry schemas,
        ContentSchemaReference schema,
        ComponentFieldPath path,
        TRuntime runtime,
        TAuthorReader author)
        where TRuntime : struct, IComponentOptionalReader<T, Entity>
        where TAuthorReader : struct, IComponentOptionalReader<TAuthor, Guid>
    {
        var field = ResolveField(schemas, schema, path);

        if ((field.Field.WireKind != 11)
            || (field.Field.Reference != ContentReferenceKind.Entity)
            || field.Field.IsRequired)
        {
            throw new ArgumentException("The selected attachment field is not an optional Entity reference.", nameof(path));
        }

        return new WorldAttachmentFieldBinding<T, TAuthor>(
            field,
            WorldAttachmentFieldBindingKind.OptionalEntityReference,
            new OptionalEntityReferenceAccessor<TRuntime, TAuthorReader>(runtime, author));
    }

    /// <summary>Creates a required value field binding.</summary>
    /// <typeparam name="TValue">The unmanaged field value.</typeparam>
    /// <typeparam name="TRuntime">The concrete runtime reader struct.</typeparam>
    /// <typeparam name="TAuthorReader">The concrete author reader struct.</typeparam>
    /// <param name="schemas">The immutable compiled metadata registry.</param>
    /// <param name="schema">The exact AttachmentState schema tuple.</param>
    /// <param name="path">The exact schema field path.</param>
    /// <param name="runtime">The concrete runtime reader, copied by value.</param>
    /// <param name="author">The concrete author reader, copied by value.</param>
    /// <returns>The immutable value field binding.</returns>
    /// <exception cref="ArgumentException">The schema field or reader shape is incompatible.</exception>
    public static WorldAttachmentFieldBinding<T, TAuthor> Value<TValue, TRuntime, TAuthorReader>(
        CompiledContentSchemaRegistry schemas,
        ContentSchemaReference schema,
        ComponentFieldPath path,
        TRuntime runtime,
        TAuthorReader author)
        where TValue : unmanaged
        where TRuntime : struct, IComponentValueReader<T, TValue>
        where TAuthorReader : struct, IComponentValueReader<TAuthor, TValue>
    {
        var field = ResolveField(schemas, schema, path);

        if ((field.Field.WireKind != ToWireKind<TValue>())
            || (field.Field.Reference != ContentReferenceKind.None)
            || !field.Field.IsRequired)
        {
            throw new ArgumentException("The selected attachment field is not a matching required opaque value.", nameof(path));
        }

        return new WorldAttachmentFieldBinding<T, TAuthor>(
            field,
            WorldAttachmentFieldBindingKind.Value,
            new ValueAccessor<TValue, TRuntime, TAuthorReader>(runtime, author));
    }

    private static WorldAttachmentFieldDescriptor ResolveField(
        CompiledContentSchemaRegistry schemas,
        ContentSchemaReference schema,
        ComponentFieldPath path)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(path);

        if ((schema.Scope != ContentSchemaScope.Content)
            || !schemas.TryGetSchema(schema, out var rootSchema)
            || (rootSchema.Role != ContentSchemaRole.AttachmentState))
        {
            throw new ArgumentException("The exact Content schema tuple is not a registered AttachmentState schema.", nameof(schema));
        }

        var segments = path.Segments;
        var currentSchema = rootSchema;
        var index = 0;

        while (index < segments.Length)
        {
            var segment = segments[index];

            if (segment.Kind != ComponentPropertySegmentKind.FieldTag)
            {
                throw new ArgumentException("A field tag must select the next attachment schema field.", nameof(path));
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
                throw new ArgumentException("The field tag is not declared at this attachment schema path.", nameof(path));
            }

            if (index == (segments.Length - 1))
            {
                return new WorldAttachmentFieldDescriptor(schema, path, field, currentSchema, schemas);
            }

            var nextSegment = segments[index + 1];

            if (nextSegment.Kind == ComponentPropertySegmentKind.FixedArrayIndex)
            {
                if ((field.WireKind != 23)
                    || (nextSegment.Value >= field.MaximumCount)
                    || (index + 2 != segments.Length))
                {
                    throw new ArgumentException("The FixedArray index is not valid at this attachment schema path.", nameof(path));
                }

                return new WorldAttachmentFieldDescriptor(schema, path, field, currentSchema, schemas);
            }

            if ((nextSegment.Kind != ComponentPropertySegmentKind.FieldTag)
                || (field.WireKind != 22)
                || !TryResolveNestedSchema(schemas, field, out currentSchema))
            {
                throw new ArgumentException("The record path is unresolved or ambiguous in this attachment schema.", nameof(path));
            }

            index++;
        }

        throw new ArgumentException("The attachment field path is invalid.", nameof(path));
    }

    private static ushort ToWireKind<TValue>()
        where TValue : unmanaged
    {
        var type = typeof(TValue);

        if (type == typeof(byte))
        {
            return 1;
        }

        if (type == typeof(ushort))
        {
            return 2;
        }

        if (type == typeof(uint))
        {
            return 3;
        }

        if (type == typeof(ulong))
        {
            return 4;
        }

        if (type == typeof(sbyte))
        {
            return 5;
        }

        if (type == typeof(short))
        {
            return 6;
        }

        if (type == typeof(int))
        {
            return 7;
        }

        if (type == typeof(long))
        {
            return 8;
        }

        if (type == typeof(bool))
        {
            return 9;
        }

        if (type == typeof(float))
        {
            return 10;
        }

        if (type == typeof(Guid))
        {
            return 11;
        }

        if (type == typeof(Vector2))
        {
            return 14;
        }

        if (type == typeof(Vector3))
        {
            return 15;
        }

        if (type == typeof(Vector4))
        {
            return 16;
        }

        if (type == typeof(Quaternion))
        {
            return 17;
        }

        if (type == typeof(Matrix3x2))
        {
            return 18;
        }

        if (type == typeof(Matrix4x4))
        {
            return 19;
        }

        if (type == typeof(SpatialPosition2))
        {
            return 20;
        }

        if (type == typeof(SpatialPosition3))
        {
            return 21;
        }

        return 0;
    }

    private static bool TryResolveNestedSchema(
        CompiledContentSchemaRegistry schemas,
        ContentFieldDescriptor field,
        out DataSchema dataSchema)
    {
        if (field.NestedSchemas.Length == 1)
        {
            if (schemas.TryGetSchema(field.NestedSchemas[0], out var nestedSchema))
            {
                dataSchema = nestedSchema;
                return true;
            }
        }

        if (field.NestedSchemas.IsEmpty && (field.NestedRoleMask != 0))
        {
            DataSchema? match = null;

            foreach (var candidate in schemas.Schemas)
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

    internal void ValidateAuthor(in TAuthor author)
        => _accessor.ValidateAuthor(in author);

    internal void ValidateRuntime(T value, IWorldAttachmentValidationContext context)
        => _accessor.ValidateRuntime(value, context);

    private readonly struct OptionalEntityReferenceAccessor<TRuntime, TAuthorReader> :
        IWorldAttachmentFieldAccessor<T, TAuthor>
        where TRuntime : struct, IComponentOptionalReader<T, Entity>
        where TAuthorReader : struct, IComponentOptionalReader<TAuthor, Guid>
    {
        private readonly TAuthorReader _author;
        private readonly TRuntime _runtime;

        internal OptionalEntityReferenceAccessor(TRuntime runtime, TAuthorReader author)
        {
            _runtime = runtime;
            _author = author;
        }

        public void ValidateAuthor(in TAuthor author)
        {
            if (_author.IsPresent(in author))
            {
                _ = _author.GetPresent(in author);
            }
        }

        public void ValidateRuntime(T value, IWorldAttachmentValidationContext context)
        {
            if (_runtime.IsPresent(in value))
            {
                context.ValidateEntity(_runtime.GetPresent(in value));
            }
        }
    }

    private readonly struct ValueAccessor<TValue, TRuntime, TAuthorReader> :
        IWorldAttachmentFieldAccessor<T, TAuthor>
        where TValue : unmanaged
        where TRuntime : struct, IComponentValueReader<T, TValue>
        where TAuthorReader : struct, IComponentValueReader<TAuthor, TValue>
    {
        private readonly TAuthorReader _author;
        private readonly TRuntime _runtime;

        internal ValueAccessor(TRuntime runtime, TAuthorReader author)
        {
            _runtime = runtime;
            _author = author;
        }

        public void ValidateAuthor(in TAuthor author)
            => _ = _author.Get(in author);

        public void ValidateRuntime(T value, IWorldAttachmentValidationContext context)
            => _ = _runtime.Get(in value);
    }
}
