// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;

namespace TerraFX.Entities;

[SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "The closed resource record and author types are required by the typed aggregate field contract.")]
internal sealed class WorldResourceRecordFieldBinding<T, TAuthor>
    where T : unmanaged
    where TAuthor : notnull
{
    private static readonly ContentSchemaReference s_assetReferenceSchema = new(
        new ContentSchemaIdentityAssignment("native/asset-reference").SchemaId, 1, 0, ContentSchemaScope.Content);
    private readonly IWorldResourceRecordFieldAccessor<T, TAuthor> _accessor;
    private readonly int _storageBytes;
    private readonly ImmutableArray<ComponentStoragePath> _storagePaths;
    private readonly ImmutableArray<int> _targetMajors;
    private readonly Guid _targetSchemaId;

    private WorldResourceRecordFieldBinding(
        ContentFieldDescriptor field,
        ImmutableArray<ComponentStoragePath> storagePaths,
        IWorldResourceRecordFieldAccessor<T, TAuthor> accessor,
        int storageBytes,
        IWorldResourceRecordBindingNode? child = null,
        Guid targetSchemaId = default,
        ImmutableArray<int> targetMajors = default)
    {
        if (storagePaths.IsDefaultOrEmpty)
        {
            throw new ArgumentException("Resource record field storage paths must be initialized and nonempty.", nameof(storagePaths));
        }

        foreach (var path in storagePaths)
        {
            ArgumentNullException.ThrowIfNull(path);
        }

        Field = field;
        Child = child;
        _storagePaths = ImmutableArrayCopy.Create(storagePaths);
        _accessor = accessor;
        _storageBytes = storageBytes;
        _targetSchemaId = targetSchemaId;
        _targetMajors = targetMajors;
    }

    internal IWorldResourceRecordBindingNode? Child { get; }

    internal ContentFieldDescriptor Field { get; }

    internal static WorldResourceRecordFieldBinding<T, TAuthor> AssetReference<TRuntime, TAuthorReader>(
        ContentFieldDescriptor field,
        ImmutableArray<ComponentStoragePath> storagePaths,
        Guid targetSchemaId,
        ImmutableArray<int> targetMajors,
        TRuntime runtime,
        TAuthorReader author)
        where TRuntime : struct, IComponentValueReader<T, WorldAssetReference>
        where TAuthorReader : struct, IComponentValueReader<TAuthor, WorldAssetReference>
    {
        var majors = ValidateAssetShape(field, required: true, targetSchemaId, targetMajors);
        return new WorldResourceRecordFieldBinding<T, TAuthor>(
            field, storagePaths, new AssetAccessor<TRuntime, TAuthorReader>(runtime, author, majors),
            sizeof(int), targetSchemaId: targetSchemaId, targetMajors: majors);
    }

    internal static WorldResourceRecordFieldBinding<T, TAuthor> Optional<V, TRuntime, TAuthorReader>(
        ContentFieldDescriptor field,
        ContentReferenceKind reference,
        ImmutableArray<ComponentStoragePath> storagePaths,
        TRuntime runtime,
        TAuthorReader author)
        where V : unmanaged
        where TRuntime : struct, IComponentOptionalReader<T, V>
        where TAuthorReader : struct, IComponentOptionalReader<TAuthor, V>
    {
        ValidateValueShape<V>(field, reference, required: false);
        return new WorldResourceRecordFieldBinding<T, TAuthor>(
            field, storagePaths, new OptionalAccessor<V, TRuntime, TAuthorReader>(runtime, author), Unsafe.SizeOf<V>());
    }

    internal static WorldResourceRecordFieldBinding<T, TAuthor> OptionalAssetReference<TRuntime, TAuthorReader>(
        ContentFieldDescriptor field,
        ImmutableArray<ComponentStoragePath> storagePaths,
        Guid targetSchemaId,
        ImmutableArray<int> targetMajors,
        TRuntime runtime,
        TAuthorReader author)
        where TRuntime : struct, IComponentOptionalReader<T, WorldAssetReference>
        where TAuthorReader : struct, IComponentOptionalReader<TAuthor, WorldAssetReference>
    {
        var majors = ValidateAssetShape(field, required: false, targetSchemaId, targetMajors);
        return new WorldResourceRecordFieldBinding<T, TAuthor>(
            field, storagePaths, new OptionalAssetAccessor<TRuntime, TAuthorReader>(runtime, author, majors),
            sizeof(int), targetSchemaId: targetSchemaId, targetMajors: majors);
    }

    internal static WorldResourceRecordFieldBinding<T, TAuthor> OptionalRecords<C, CA, TRuntime, TAuthorReader>(
        ContentFieldDescriptor field,
        ImmutableArray<ComponentStoragePath> storagePaths,
        WorldResourceRecordBinding<C, CA> child,
        TRuntime runtime,
        TAuthorReader author)
        where C : unmanaged
        where CA : notnull
        where TRuntime : struct, IWorldResourceOptionalArrayReader<T, C>
        where TAuthorReader : struct, IWorldResourceOptionalArrayReader<TAuthor, CA>
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(child);

        if (field.IsRequired || (field.WireKind != 24)
            || (field.Reference != ContentReferenceKind.None)
            || (field.NestedRoleMask != 0) || (field.NestedSchemas.Length != 1)
            || (field.NestedSchemas[0] != child.Schema)
            || (field.MaximumCount > 4) || (field.DefaultValue is not null))
        {
            throw new ArgumentException("The selected field is not a bounded optional resource RecordArray.", nameof(field));
        }

        return new WorldResourceRecordFieldBinding<T, TAuthor>(
            field, storagePaths,
            new RecordsAccessor<C, CA, TRuntime, TAuthorReader>(runtime, author, child, field.MinimumCount, field.MaximumCount),
            checked((int)field.MaximumCount * child.Storage.LogicalBytes), child);
    }

    internal static WorldResourceRecordFieldBinding<T, TAuthor> Value<V, TRuntime, TAuthorReader>(
        ContentFieldDescriptor field,
        ImmutableArray<ComponentStoragePath> storagePaths,
        TRuntime runtime,
        TAuthorReader author)
        where V : unmanaged
        where TRuntime : struct, IComponentValueReader<T, V>
        where TAuthorReader : struct, IComponentValueReader<TAuthor, V>
    {
        ValidateValueShape<V>(field, ContentReferenceKind.None, required: true);
        return new WorldResourceRecordFieldBinding<T, TAuthor>(
            field, storagePaths, new ValueAccessor<V, TRuntime, TAuthorReader>(runtime, author), Unsafe.SizeOf<V>());
    }

    private static bool IsAssetField(ContentFieldDescriptor field, uint tag, ushort kind, ContentReferenceKind reference)
        => (field.Tag == tag) && (field.WireKind == kind) && field.IsRequired
            && (field.MinimumCount == 1) && (field.MaximumCount == 1)
            && field.NestedSchemas.IsEmpty && (field.NestedRoleMask == 0)
            && (field.Unit == ContentUnit.None) && (field.Reference == reference)
            && (field.AuthoringFlags == ContentAuthoring.None) && (field.DefaultValue is null);

    private static ImmutableArray<int> ValidateAssetShape(
        ContentFieldDescriptor field, bool required, Guid targetSchemaId, ImmutableArray<int> targetMajors)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentOutOfRangeException.ThrowIfEqual(targetSchemaId, Guid.Empty);

        if ((field.IsRequired != required) || (field.WireKind != 22)
            || (field.Reference != ContentReferenceKind.Asset)
            || (field.MinimumCount != 1) || (field.MaximumCount != 1)
            || (field.NestedRoleMask != 0) || (field.NestedSchemas.Length != 1)
            || (field.NestedSchemas[0] != s_assetReferenceSchema)
            || (field.DefaultValue is not null) || targetMajors.IsDefaultOrEmpty)
        {
            throw new ArgumentException("The selected field does not match its exact AssetReference policy.", nameof(field));
        }

        for (var index = 0; index < targetMajors.Length; index++)
        {
            if ((targetMajors[index] <= 0) || ((index > 0) && (targetMajors[index - 1] >= targetMajors[index])))
            {
                throw new ArgumentException("AssetReference target majors must be positive, ascending, and unique.", nameof(targetMajors));
            }
        }

        return ImmutableArrayCopy.Create(targetMajors);
    }

    private static void ValidateAssetValue(WorldAssetReference value, ImmutableArray<int> majors)
    {
        if (!value.IsValid || !majors.Contains(value.SchemaMajor))
        {
            throw new InvalidDataException("A resource AssetReference has an invalid identity or schema major.");
        }
    }

    private static void ValidateValueShape<V>(ContentFieldDescriptor field, ContentReferenceKind reference, bool required)
        where V : unmanaged
    {
        ArgumentNullException.ThrowIfNull(field);
        var kind = WorldResourceFieldBinding<T, TAuthor>.GetWireKind<V>();

        if (kind is not 1 and not 8 and not 9 and not 10 and not 11 and not 15 and not 17
            || (field.WireKind != kind) || (field.IsRequired != required)
            || (field.Reference != reference) || (field.MinimumCount != 1) || (field.MaximumCount != 1)
            || !field.NestedSchemas.IsEmpty || (field.NestedRoleMask != 0) || (field.DefaultValue is not null)
            || (reference is not ContentReferenceKind.None and not ContentReferenceKind.HistoricalEntity)
            || ((reference == ContentReferenceKind.HistoricalEntity) && (typeof(V) != typeof(Guid))))
        {
            throw new ArgumentException("The resource field does not match its exact primitive and reference shape.", nameof(field));
        }
    }

    internal void ValidateAuthor(in TAuthor value) => _accessor.ValidateAuthor(in value);

    internal void ValidateRuntime(in T value) => _accessor.ValidateRuntime(in value);

    internal void ValidateSchema(CompiledContentSchemaRegistry schemas)
    {
        if (_targetSchemaId == Guid.Empty)
        {
            return;
        }

        if (!schemas.TryGetSchema(s_assetReferenceSchema, out var helper)
            || (helper.Role != ContentSchemaRole.Descriptor) || (helper.Owner != ContentSchemaOwner.Assets)
            || (helper.Storage != ContentSchemaStorage.NotComponent)
            || (helper.TransientRestore != ContentTransientRestore.NotApplicable) || (helper.Fields.Length != 3)
            || !IsAssetField(helper.Fields[0], 1, 3, ContentReferenceKind.Asset)
            || !IsAssetField(helper.Fields[1], 2, 11, ContentReferenceKind.Schema)
            || !IsAssetField(helper.Fields[2], 3, 3, ContentReferenceKind.None))
        {
            throw new ArgumentException("The resource AssetReference helper must close its exact three fields.", nameof(schemas));
        }

        foreach (var major in _targetMajors)
        {
            if (!schemas.TryGetSchema(new ContentSchemaReference(_targetSchemaId, major, 0, ContentSchemaScope.Content), out var target)
                || (target.Role != ContentSchemaRole.NativeAsset))
            {
                throw new ArgumentException("A resource AssetReference target must be an exact compiled NativeAsset.", nameof(schemas));
            }
        }
    }

    internal void ValidateStorage(ComponentStorageDeclaration storage, bool[] claimed)
    {
        var bytes = 0;
        var firstOffset = -1;

        for (var index = 0; index < _storagePaths.Length; index++)
        {
            var leafIndex = -1;

            for (var candidate = 0; candidate < storage.Leaves.Length; candidate++)
            {
                if (storage.Leaves[candidate].Path.Equals(_storagePaths[index]))
                {
                    leafIndex = candidate;
                    break;
                }
            }

            if ((leafIndex < 0) || claimed[leafIndex]
                || (storage.Leaves[leafIndex].Purpose != ComponentStoragePurpose.SchemaValue))
            {
                throw new ArgumentException("Resource record storage paths must claim distinct schema-value leaves.", nameof(storage));
            }

            var leaf = storage.Leaves[leafIndex];
            firstOffset = firstOffset < 0 ? leaf.ByteOffset : firstOffset;
            claimed[leafIndex] = true;
            bytes = checked(bytes + leaf.ByteExtent);

            if (Child is { } child)
            {
                var childLeaves = child.Storage.Leaves;
                var expectedCount = checked((int)Field.MaximumCount * childLeaves.Length);

                if (_storagePaths.Length != expectedCount)
                {
                    throw new ArgumentException("An inline record array must close every physical child repeat.", nameof(storage));
                }

                var childLeaf = childLeaves[index % childLeaves.Length];
                var expectedOffset = checked(firstOffset + (index / childLeaves.Length * child.Storage.LogicalBytes) + childLeaf.ByteOffset);

                if ((leaf.ByteOffset != expectedOffset) || (leaf.ByteExtent != childLeaf.ByteExtent)
                    || (leaf.Kind != childLeaf.Kind) || (leaf.InlineCount != childLeaf.InlineCount)
                    || (leaf.InlineStride != childLeaf.InlineStride))
                {
                    throw new ArgumentException("Inline resource records must exactly match their captured child layout.", nameof(storage));
                }
            }
        }

        if (bytes != _storageBytes)
        {
            throw new ArgumentException("The resource field storage extent does not match its typed value.", nameof(storage));
        }
    }

    private readonly struct AssetAccessor<TRuntime, TAuthorReader> : IWorldResourceRecordFieldAccessor<T, TAuthor>
        where TRuntime : struct, IComponentValueReader<T, WorldAssetReference>
        where TAuthorReader : struct, IComponentValueReader<TAuthor, WorldAssetReference>
    {
        private readonly TAuthorReader _author;
        private readonly ImmutableArray<int> _majors;
        private readonly TRuntime _runtime;

        internal AssetAccessor(TRuntime runtime, TAuthorReader author, ImmutableArray<int> majors)
        {
            _runtime = runtime;
            _author = author;
            _majors = majors;
        }

        public void ValidateAuthor(in TAuthor value) => ValidateAssetValue(_author.Get(in value), _majors);

        public void ValidateRuntime(in T value) => ValidateAssetValue(_runtime.Get(in value), _majors);
    }

    private readonly struct OptionalAccessor<V, TRuntime, TAuthorReader> : IWorldResourceRecordFieldAccessor<T, TAuthor>
        where V : unmanaged
        where TRuntime : struct, IComponentOptionalReader<T, V>
        where TAuthorReader : struct, IComponentOptionalReader<TAuthor, V>
    {
        private readonly TAuthorReader _author;
        private readonly TRuntime _runtime;

        internal OptionalAccessor(TRuntime runtime, TAuthorReader author)
        {
            _runtime = runtime;
            _author = author;
        }

        public void ValidateAuthor(in TAuthor value)
        {
            if (_author.IsPresent(in value))
            {
                _ = _author.GetPresent(in value);
            }
        }

        public void ValidateRuntime(in T value)
        {
            if (_runtime.IsPresent(in value))
            {
                _ = _runtime.GetPresent(in value);
            }
        }
    }

    private readonly struct OptionalAssetAccessor<TRuntime, TAuthorReader> : IWorldResourceRecordFieldAccessor<T, TAuthor>
        where TRuntime : struct, IComponentOptionalReader<T, WorldAssetReference>
        where TAuthorReader : struct, IComponentOptionalReader<TAuthor, WorldAssetReference>
    {
        private readonly TAuthorReader _author;
        private readonly ImmutableArray<int> _majors;
        private readonly TRuntime _runtime;

        internal OptionalAssetAccessor(TRuntime runtime, TAuthorReader author, ImmutableArray<int> majors)
        {
            _runtime = runtime;
            _author = author;
            _majors = majors;
        }

        public void ValidateAuthor(in TAuthor value)
        {
            if (_author.IsPresent(in value))
            {
                ValidateAssetValue(_author.GetPresent(in value), _majors);
            }
        }

        public void ValidateRuntime(in T value)
        {
            if (_runtime.IsPresent(in value))
            {
                ValidateAssetValue(_runtime.GetPresent(in value), _majors);
            }
        }
    }

    private readonly struct RecordsAccessor<C, CA, TRuntime, TAuthorReader> : IWorldResourceRecordFieldAccessor<T, TAuthor>
        where C : unmanaged
        where CA : notnull
        where TRuntime : struct, IWorldResourceOptionalArrayReader<T, C>
        where TAuthorReader : struct, IWorldResourceOptionalArrayReader<TAuthor, CA>
    {
        private readonly TAuthorReader _author;
        private readonly WorldResourceRecordBinding<C, CA> _child;
        private readonly ulong _minimumCount;
        private readonly ulong _maximumCount;
        private readonly TRuntime _runtime;

        internal RecordsAccessor(
            TRuntime runtime,
            TAuthorReader author,
            WorldResourceRecordBinding<C, CA> child,
            ulong minimumCount,
            ulong maximumCount)
        {
            _runtime = runtime;
            _author = author;
            _child = child;
            _minimumCount = minimumCount;
            _maximumCount = maximumCount;
        }

        public void ValidateAuthor(in TAuthor value)
        {
            if (!_author.IsPresent(in value))
            {
                return;
            }

            var count = _author.GetCount(in value);
            ValidateCount(count);

            for (var index = 0; index < count; index++)
            {
                var element = _author.GetElement(in value, index);
                _child.ValidateAuthor(in element);
            }
        }

        public void ValidateRuntime(in T value)
        {
            if (!_runtime.IsPresent(in value))
            {
                return;
            }

            var count = _runtime.GetCount(in value);
            ValidateCount(count);

            for (var index = 0; index < count; index++)
            {
                var element = _runtime.GetElement(in value, index);
                _child.ValidateRuntime(in element);
            }
        }

        private void ValidateCount(int count)
        {
            if ((count < 0) || ((ulong)count < _minimumCount) || ((ulong)count > _maximumCount))
            {
                throw new InvalidDataException("A resource record-array count violates its captured bounds.");
            }
        }
    }

    private readonly struct ValueAccessor<V, TRuntime, TAuthorReader> : IWorldResourceRecordFieldAccessor<T, TAuthor>
        where V : unmanaged
        where TRuntime : struct, IComponentValueReader<T, V>
        where TAuthorReader : struct, IComponentValueReader<TAuthor, V>
    {
        private readonly TAuthorReader _author;
        private readonly TRuntime _runtime;

        internal ValueAccessor(TRuntime runtime, TAuthorReader author)
        {
            _runtime = runtime;
            _author = author;
        }

        public void ValidateAuthor(in TAuthor value) => _ = _author.Get(in value);

        public void ValidateRuntime(in T value) => _ = _runtime.Get(in value);
    }
}
