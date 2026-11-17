// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;

namespace TerraFX.Entities;

[SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "The closed attachment and author types are required by the typed aggregate field contract.")]
internal sealed class WorldAttachmentRecordFieldBinding<T, TAuthor>
    where T : class
    where TAuthor : notnull
{
    private static readonly ContentSchemaReference s_assetReferenceSchema = new(
        new ContentSchemaIdentityAssignment("native/asset-reference").SchemaId,
        1,
        0,
        ContentSchemaScope.Content);
    private readonly IWorldAttachmentRecordFieldAccessor<T, TAuthor> _accessor;
    private readonly IWorldAttachmentRecordBindingNode? _child;
    private readonly ContentFieldDescriptor _field;
    private readonly bool _isAssetReference;
    private readonly ImmutableArray<int> _targetMajors;

    private readonly Guid? _targetSchemaId;

    private WorldAttachmentRecordFieldBinding(
        ContentFieldDescriptor field,
        IWorldAttachmentRecordBindingNode? child,
        bool isAssetReference,
        IWorldAttachmentRecordFieldAccessor<T, TAuthor> accessor,
        Guid? targetSchemaId = null,
        ImmutableArray<int> targetMajors = default)
    {
        _field = field;
        _child = child;
        _isAssetReference = isAssetReference;
        _accessor = accessor;
        _targetSchemaId = targetSchemaId;
        _targetMajors = targetMajors;
    }

    internal IWorldAttachmentRecordBindingNode? Child => _child;

    internal ContentFieldDescriptor Field => _field;

    internal bool IsAssetReference => _isAssetReference;

    internal ImmutableArray<int> TargetMajors => _targetMajors;

    internal Guid? TargetSchemaId => _targetSchemaId;

    internal static WorldAttachmentRecordFieldBinding<T, TAuthor> AssetReference<TRuntime, TAuthorReader>(
        ContentFieldDescriptor field,
        Guid targetSchemaId,
        ImmutableArray<int> targetMajors,
        TRuntime runtime,
        TAuthorReader author)
        where TRuntime : struct, IComponentValueReader<T, WorldAssetReference>
        where TAuthorReader : struct, IComponentValueReader<TAuthor, WorldAssetReference>
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentOutOfRangeException.ThrowIfEqual(targetSchemaId, Guid.Empty);

        if (targetMajors.IsDefaultOrEmpty)
        {
            throw new ArgumentException("An AssetReference binding requires initialized target majors.", nameof(targetMajors));
        }

        var copiedMajors = ImmutableArrayCopy.Create(targetMajors);

        for (var index = 0; index < copiedMajors.Length; index++)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(copiedMajors[index]);

            if ((index > 0) && (copiedMajors[index - 1] >= copiedMajors[index]))
            {
                throw new ArgumentException("AssetReference target majors must be ascending and unique.", nameof(targetMajors));
            }
        }

        if (!field.IsRequired
            || (field.WireKind != 22)
            || (field.Reference != ContentReferenceKind.Asset)
            || (field.NestedSchemas.Length != 1)
            || (field.NestedSchemas[0] != s_assetReferenceSchema))
        {
            throw new ArgumentException("The selected field is not a required native/asset-reference record.", nameof(field));
        }

        return new WorldAttachmentRecordFieldBinding<T, TAuthor>(
            field,
            child: null,
            isAssetReference: true,
            new AssetReferenceAccessor<TRuntime, TAuthorReader>(runtime, author, copiedMajors),
            targetSchemaId,
            copiedMajors);
    }

    internal static WorldAttachmentRecordFieldBinding<T, TAuthor> Bytes<TRuntime, TAuthorReader>(
        ContentFieldDescriptor field,
        TRuntime runtime,
        TAuthorReader author)
        where TRuntime : struct, IComponentArrayReader<T, byte>
        where TAuthorReader : struct, IComponentArrayReader<TAuthor, byte>
    {
        ArgumentNullException.ThrowIfNull(field);

        if (!field.IsRequired || (field.WireKind != 13) || (field.Reference != ContentReferenceKind.None))
        {
            throw new ArgumentException("The selected field is not a required opaque Bytes array.", nameof(field));
        }

        return new WorldAttachmentRecordFieldBinding<T, TAuthor>(
            field,
            child: null,
            isAssetReference: false,
            new BytesAccessor<TRuntime, TAuthorReader>(runtime, author, field.MinimumCount, field.MaximumCount));
    }

    internal static WorldAttachmentRecordFieldBinding<T, TAuthor> FixedValues<V, TRuntime, TAuthorReader>(
        ContentFieldDescriptor field,
        TRuntime runtime,
        TAuthorReader author)
        where V : unmanaged
        where TRuntime : struct, IComponentArrayReader<T, V>
        where TAuthorReader : struct, IComponentArrayReader<TAuthor, V>
    {
        ArgumentNullException.ThrowIfNull(field);
        var elementKind = WorldAttachmentFieldBinding<T, TAuthor>.ToWireKind<V>();

        if (!field.IsRequired
            || (field.WireKind != 23)
            || (field.ElementKind != elementKind)
            || (elementKind == 0)
            || (field.Reference != ContentReferenceKind.None))
        {
            throw new ArgumentException("The selected field is not a matching required FixedArray.", nameof(field));
        }

        return new WorldAttachmentRecordFieldBinding<T, TAuthor>(
            field,
            child: null,
            isAssetReference: false,
            new FixedValuesAccessor<V, TRuntime, TAuthorReader>(runtime, author, field.MinimumCount, field.MaximumCount));
    }

    internal static WorldAttachmentRecordFieldBinding<T, TAuthor> Optional<V, TRuntime, TAuthorReader>(
        ContentFieldDescriptor field,
        ContentReferenceKind reference,
        TRuntime runtime,
        TAuthorReader author)
        where V : unmanaged
        where TRuntime : struct, IComponentOptionalReader<T, V>
        where TAuthorReader : struct, IComponentOptionalReader<TAuthor, V>
    {
        ArgumentNullException.ThrowIfNull(field);
        ValidateValueShape<V>(field, reference, isRequired: false);
        return new WorldAttachmentRecordFieldBinding<T, TAuthor>(
            field,
            child: null,
            isAssetReference: false,
            new OptionalAccessor<V, TRuntime, TAuthorReader>(runtime, author, reference));
    }

    internal static WorldAttachmentRecordFieldBinding<T, TAuthor> OptionalRecord<C, CA, TRuntime, TAuthorReader>(
        ContentFieldDescriptor field,
        WorldAttachmentRecordBinding<C, CA> child,
        TRuntime runtime,
        TAuthorReader author)
        where C : class
        where CA : notnull
        where TRuntime : struct, IComponentOptionalReader<T, C>
        where TAuthorReader : struct, IComponentOptionalReader<TAuthor, CA>
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(child);
        ValidateRecordShape(field, child.Schema, required: false);
        return new WorldAttachmentRecordFieldBinding<T, TAuthor>(
            field,
            child,
            isAssetReference: false,
            new OptionalRecordAccessor<C, CA, TRuntime, TAuthorReader>(runtime, author, child));
    }

    internal static WorldAttachmentRecordFieldBinding<T, TAuthor> Record<C, CA, TRuntime, TAuthorReader>(
        ContentFieldDescriptor field,
        WorldAttachmentRecordBinding<C, CA> child,
        TRuntime runtime,
        TAuthorReader author)
        where C : class
        where CA : notnull
        where TRuntime : struct, IComponentValueReader<T, C>
        where TAuthorReader : struct, IComponentValueReader<TAuthor, CA>
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(child);
        ValidateRecordShape(field, child.Schema, required: true);
        return new WorldAttachmentRecordFieldBinding<T, TAuthor>(
            field,
            child,
            isAssetReference: false,
            new RecordAccessor<C, CA, TRuntime, TAuthorReader>(runtime, author, child));
    }

    internal static WorldAttachmentRecordFieldBinding<T, TAuthor> Records<C, CA, TRuntime, TAuthorReader>(
        ContentFieldDescriptor field,
        WorldAttachmentRecordBinding<C, CA> child,
        TRuntime runtime,
        TAuthorReader author)
        where C : class
        where CA : notnull
        where TRuntime : struct, IComponentArrayReader<T, C>
        where TAuthorReader : struct, IComponentArrayReader<TAuthor, CA>
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(child);

        if (!field.IsRequired
            || (field.WireKind != 24)
            || (field.Reference != ContentReferenceKind.None)
            || (field.NestedSchemas.Length != 1)
            || (field.NestedSchemas[0] != child.Schema))
        {
            throw new ArgumentException("The selected field is not a required Records array for its exact child schema.", nameof(field));
        }

        return new WorldAttachmentRecordFieldBinding<T, TAuthor>(
            field,
            child,
            isAssetReference: false,
            new RecordsAccessor<C, CA, TRuntime, TAuthorReader>(runtime, author, child, field.MinimumCount, field.MaximumCount));
    }

    internal static WorldAttachmentRecordFieldBinding<T, TAuthor> Value<V, TRuntime, TAuthorReader>(
        ContentFieldDescriptor field,
        ContentReferenceKind reference,
        TRuntime runtime,
        TAuthorReader author)
        where V : unmanaged
        where TRuntime : struct, IComponentValueReader<T, V>
        where TAuthorReader : struct, IComponentValueReader<TAuthor, V>
    {
        ArgumentNullException.ThrowIfNull(field);
        ValidateValueShape<V>(field, reference, isRequired: true);
        return new WorldAttachmentRecordFieldBinding<T, TAuthor>(
            field,
            child: null,
            isAssetReference: false,
            new ValueAccessor<V, TRuntime, TAuthorReader>(runtime, author, reference));
    }

    private static Guid GetGuid<V>(V value)
        where V : unmanaged
    {
        if (typeof(V) != typeof(Guid))
        {
            throw new InvalidOperationException("An Entity or SceneInstance attachment reference must use Guid storage.");
        }

        return Unsafe.As<V, Guid>(ref value);
    }

    private static void ValidateCount(int count, ulong minimumCount, ulong maximumCount)
    {
        if ((count < 0) || ((ulong)count < minimumCount) || ((ulong)count > maximumCount))
        {
            throw new InvalidDataException("An attachment array count is outside its registered schema bounds.");
        }
    }

    private static void ValidateRecordShape(ContentFieldDescriptor field, ContentSchemaReference childSchema, bool required)
    {
        if ((field.WireKind != 22)
            || (field.IsRequired != required)
            || (field.Reference != ContentReferenceKind.None)
            || (field.NestedSchemas.Length != 1)
            || (field.NestedSchemas[0] != childSchema))
        {
            throw new ArgumentException("The selected field does not match its exact typed Record child.", nameof(field));
        }
    }

    private static void ValidateValue<V>(
        V value,
        ContentReferenceKind reference,
        IWorldAttachmentValidationContext? context)
        where V : unmanaged
    {
        if (reference is not ContentReferenceKind.Entity and not ContentReferenceKind.SceneInstance)
        {
            return;
        }

        var entityId = GetGuid(value);

        if (entityId == Guid.Empty)
        {
            throw new InvalidDataException("An attachment World identity reference cannot be empty.");
        }

        if ((reference == ContentReferenceKind.Entity) && (context is not null))
        {
            context.ValidateEntityId(entityId);
        }
    }

    private static void ValidateValueShape<V>(ContentFieldDescriptor field, ContentReferenceKind reference, bool isRequired)
        where V : unmanaged
    {
        var wireKind = WorldAttachmentFieldBinding<T, TAuthor>.ToWireKind<V>();

        if ((wireKind == 0)
            || (field.WireKind != wireKind)
            || (field.IsRequired != isRequired)
            || (field.Reference != reference)
            || (reference is not ContentReferenceKind.None and not ContentReferenceKind.Entity and not ContentReferenceKind.SceneInstance)
            || ((reference is ContentReferenceKind.Entity or ContentReferenceKind.SceneInstance) && (typeof(V) != typeof(Guid))))
        {
            throw new ArgumentException("The selected field does not match its required/optional value and reference shape.", nameof(field));
        }
    }

    internal void ValidateAuthor(in TAuthor author)
        => _accessor.ValidateAuthor(in author);

    internal void ValidateRuntime(T value, IWorldAttachmentValidationContext context)
        => _accessor.ValidateRuntime(value, context);

    private readonly struct AssetReferenceAccessor<TRuntime, TAuthorReader> :
        IWorldAttachmentRecordFieldAccessor<T, TAuthor>
        where TRuntime : struct, IComponentValueReader<T, WorldAssetReference>
        where TAuthorReader : struct, IComponentValueReader<TAuthor, WorldAssetReference>
    {
        private readonly TAuthorReader _author;
        private readonly ImmutableArray<int> _targetMajors;
        private readonly TRuntime _runtime;

        internal AssetReferenceAccessor(
            TRuntime runtime,
            TAuthorReader author,
            ImmutableArray<int> targetMajors)
        {
            _runtime = runtime;
            _author = author;
            _targetMajors = targetMajors;
        }

        public void ValidateAuthor(in TAuthor author)
            => Validate(_author.Get(in author));

        public void ValidateRuntime(T value, IWorldAttachmentValidationContext context)
            => Validate(_runtime.Get(in value));

        private void Validate(WorldAssetReference reference)
        {
            if (!reference.IsValid || !_targetMajors.Contains(reference.SchemaMajor))
            {
                throw new InvalidDataException("An attachment AssetReference is invalid or selects an unsupported schema major.");
            }
        }
    }

    private readonly struct BytesAccessor<TRuntime, TAuthorReader> :
        IWorldAttachmentRecordFieldAccessor<T, TAuthor>
        where TRuntime : struct, IComponentArrayReader<T, byte>
        where TAuthorReader : struct, IComponentArrayReader<TAuthor, byte>
    {
        private readonly TAuthorReader _author;
        private readonly ulong _maximumCount;
        private readonly ulong _minimumCount;
        private readonly TRuntime _runtime;

        internal BytesAccessor(TRuntime runtime, TAuthorReader author, ulong minimumCount, ulong maximumCount)
        {
            _runtime = runtime;
            _author = author;
            _minimumCount = minimumCount;
            _maximumCount = maximumCount;
        }

        public void ValidateAuthor(in TAuthor author)
        {
            var count = _author.GetCount(in author);
            ValidateCount(count, _minimumCount, _maximumCount);

            for (var index = 0; index < count; index++)
            {
                _ = _author.GetElement(in author, index);
            }
        }

        public void ValidateRuntime(T value, IWorldAttachmentValidationContext context)
        {
            var count = _runtime.GetCount(in value);
            ValidateCount(count, _minimumCount, _maximumCount);

            for (var index = 0; index < count; index++)
            {
                _ = _runtime.GetElement(in value, index);
            }
        }
    }

    private readonly struct FixedValuesAccessor<V, TRuntime, TAuthorReader> :
        IWorldAttachmentRecordFieldAccessor<T, TAuthor>
        where V : unmanaged
        where TRuntime : struct, IComponentArrayReader<T, V>
        where TAuthorReader : struct, IComponentArrayReader<TAuthor, V>
    {
        private readonly TAuthorReader _author;
        private readonly ulong _maximumCount;
        private readonly ulong _minimumCount;
        private readonly TRuntime _runtime;

        internal FixedValuesAccessor(TRuntime runtime, TAuthorReader author, ulong minimumCount, ulong maximumCount)
        {
            _runtime = runtime;
            _author = author;
            _minimumCount = minimumCount;
            _maximumCount = maximumCount;
        }

        public void ValidateAuthor(in TAuthor author)
        {
            var count = _author.GetCount(in author);
            ValidateCount(count, _minimumCount, _maximumCount);

            for (var index = 0; index < count; index++)
            {
                _ = _author.GetElement(in author, index);
            }
        }

        public void ValidateRuntime(T value, IWorldAttachmentValidationContext context)
        {
            var count = _runtime.GetCount(in value);
            ValidateCount(count, _minimumCount, _maximumCount);

            for (var index = 0; index < count; index++)
            {
                _ = _runtime.GetElement(in value, index);
            }
        }
    }

    private readonly struct OptionalAccessor<V, TRuntime, TAuthorReader> :
        IWorldAttachmentRecordFieldAccessor<T, TAuthor>
        where V : unmanaged
        where TRuntime : struct, IComponentOptionalReader<T, V>
        where TAuthorReader : struct, IComponentOptionalReader<TAuthor, V>
    {
        private readonly TAuthorReader _author;
        private readonly ContentReferenceKind _reference;
        private readonly TRuntime _runtime;

        internal OptionalAccessor(TRuntime runtime, TAuthorReader author, ContentReferenceKind reference)
        {
            _runtime = runtime;
            _author = author;
            _reference = reference;
        }

        public void ValidateAuthor(in TAuthor author)
        {
            if (_author.IsPresent(in author))
            {
                var value = _author.GetPresent(in author);
                ValidateValue(value, _reference, context: null);
            }
        }

        public void ValidateRuntime(T value, IWorldAttachmentValidationContext context)
        {
            if (_runtime.IsPresent(in value))
            {
                var fieldValue = _runtime.GetPresent(in value);
                ValidateValue(fieldValue, _reference, context);
            }
        }
    }

    private readonly struct OptionalRecordAccessor<C, CA, TRuntime, TAuthorReader> :
        IWorldAttachmentRecordFieldAccessor<T, TAuthor>
        where C : class
        where CA : notnull
        where TRuntime : struct, IComponentOptionalReader<T, C>
        where TAuthorReader : struct, IComponentOptionalReader<TAuthor, CA>
    {
        private readonly TAuthorReader _author;
        private readonly WorldAttachmentRecordBinding<C, CA> _child;
        private readonly TRuntime _runtime;

        internal OptionalRecordAccessor(
            TRuntime runtime,
            TAuthorReader author,
            WorldAttachmentRecordBinding<C, CA> child)
        {
            _runtime = runtime;
            _author = author;
            _child = child;
        }

        public void ValidateAuthor(in TAuthor author)
        {
            if (_author.IsPresent(in author))
            {
                var value = _author.GetPresent(in author) ?? throw new InvalidDataException("A present attachment OptionalRecord cannot contain null.");

                _child.ValidateAuthor(in value);
            }
        }

        public void ValidateRuntime(T value, IWorldAttachmentValidationContext context)
        {
            if (_runtime.IsPresent(in value))
            {
                var child = _runtime.GetPresent(in value)
                    ?? throw new InvalidDataException("A present attachment OptionalRecord cannot contain null.");
                _child.ValidateRuntime(child, context);
            }
        }
    }

    private readonly struct RecordAccessor<C, CA, TRuntime, TAuthorReader> :
        IWorldAttachmentRecordFieldAccessor<T, TAuthor>
        where C : class
        where CA : notnull
        where TRuntime : struct, IComponentValueReader<T, C>
        where TAuthorReader : struct, IComponentValueReader<TAuthor, CA>
    {
        private readonly TAuthorReader _author;
        private readonly WorldAttachmentRecordBinding<C, CA> _child;
        private readonly TRuntime _runtime;

        internal RecordAccessor(
            TRuntime runtime,
            TAuthorReader author,
            WorldAttachmentRecordBinding<C, CA> child)
        {
            _runtime = runtime;
            _author = author;
            _child = child;
        }

        public void ValidateAuthor(in TAuthor author)
        {
            var child = _author.Get(in author) ?? throw new InvalidDataException("A required attachment Record cannot contain null.");

            _child.ValidateAuthor(in child);
        }

        public void ValidateRuntime(T value, IWorldAttachmentValidationContext context)
        {
            var child = _runtime.Get(in value)
                ?? throw new InvalidDataException("A required attachment Record cannot contain null.");
            _child.ValidateRuntime(child, context);
        }
    }

    private readonly struct RecordsAccessor<C, CA, TRuntime, TAuthorReader> :
        IWorldAttachmentRecordFieldAccessor<T, TAuthor>
        where C : class
        where CA : notnull
        where TRuntime : struct, IComponentArrayReader<T, C>
        where TAuthorReader : struct, IComponentArrayReader<TAuthor, CA>
    {
        private readonly TAuthorReader _author;
        private readonly WorldAttachmentRecordBinding<C, CA> _child;
        private readonly ulong _maximumCount;
        private readonly ulong _minimumCount;
        private readonly TRuntime _runtime;

        internal RecordsAccessor(
            TRuntime runtime,
            TAuthorReader author,
            WorldAttachmentRecordBinding<C, CA> child,
            ulong minimumCount,
            ulong maximumCount)
        {
            _runtime = runtime;
            _author = author;
            _child = child;
            _minimumCount = minimumCount;
            _maximumCount = maximumCount;
        }

        public void ValidateAuthor(in TAuthor author)
        {
            var count = _author.GetCount(in author);
            ValidateCount(count, _minimumCount, _maximumCount);

            for (var index = 0; index < count; index++)
            {
                var child = _author.GetElement(in author, index) ?? throw new InvalidDataException("An attachment Records array cannot contain null.");

                _child.ValidateAuthor(in child);
            }
        }

        public void ValidateRuntime(T value, IWorldAttachmentValidationContext context)
        {
            var count = _runtime.GetCount(in value);
            ValidateCount(count, _minimumCount, _maximumCount);

            for (var index = 0; index < count; index++)
            {
                var child = _runtime.GetElement(in value, index)
                    ?? throw new InvalidDataException("An attachment Records array cannot contain null.");
                _child.ValidateRuntime(child, context);
            }
        }
    }

    private readonly struct ValueAccessor<V, TRuntime, TAuthorReader> :
        IWorldAttachmentRecordFieldAccessor<T, TAuthor>
        where V : unmanaged
        where TRuntime : struct, IComponentValueReader<T, V>
        where TAuthorReader : struct, IComponentValueReader<TAuthor, V>
    {
        private readonly TAuthorReader _author;
        private readonly ContentReferenceKind _reference;
        private readonly TRuntime _runtime;

        internal ValueAccessor(TRuntime runtime, TAuthorReader author, ContentReferenceKind reference)
        {
            _runtime = runtime;
            _author = author;
            _reference = reference;
        }

        public void ValidateAuthor(in TAuthor author)
            => ValidateValue(_author.Get(in author), _reference, context: null);

        public void ValidateRuntime(T value, IWorldAttachmentValidationContext context)
            => ValidateValue(_runtime.Get(in value), _reference, context);
    }
}
