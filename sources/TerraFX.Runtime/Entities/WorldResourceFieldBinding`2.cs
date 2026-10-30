// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using TerraFX.Numerics;

namespace TerraFX.Entities;

/// <summary>Binds one resource state field to concrete partition and author readers.</summary>
/// <typeparam name="T">The unmanaged resource partition type.</typeparam>
/// <typeparam name="TAuthor">The immutable symbolic author type.</typeparam>
[SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "The closed resource and author types are required by the typed accessor factory contract.")]
public sealed class WorldResourceFieldBinding<T, TAuthor>
    where T : unmanaged
    where TAuthor : notnull
{
    private readonly object _accessorState;
    private readonly WorldResourceFieldDescriptor _descriptor;
    private readonly ImmutableArray<ComponentStoragePath> _partitionStoragePaths;

    private WorldResourceFieldBinding(
        WorldResourceFieldDescriptor descriptor,
        ImmutableArray<ComponentStoragePath> partitionStoragePaths,
        object accessorState)
    {
        _descriptor = descriptor;
        _partitionStoragePaths = partitionStoragePaths;
        _accessorState = accessorState;
    }

    /// <summary>Gets the selected Core resource-state field.</summary>
    public WorldResourceFieldDescriptor Descriptor => _descriptor;

    /// <summary>Gets the immutable physical storage paths selected by the runtime reader.</summary>
    public ImmutableArray<ComponentStoragePath> PartitionStoragePaths => _partitionStoragePaths;

    internal object AccessorState => _accessorState;

    internal IWorldResourceFieldAccess<T> FieldAccess
        => _accessorState as IWorldResourceFieldAccess<T>
            ?? throw new InvalidOperationException("The resource field accessor state has the wrong family type.");

    /// <summary>Creates a binding for the complete fixed-array partition domain.</summary>
    /// <typeparam name="TElement">The unmanaged fixed-array element type.</typeparam>
    /// <typeparam name="TRuntime">The concrete resource-partition reader struct.</typeparam>
    /// <typeparam name="TAuthorReader">The concrete author-array reader struct.</typeparam>
    /// <param name="field">The selected required FixedArray resource-state field.</param>
    /// <param name="partitionStoragePaths">The physical paths read from each resource partition.</param>
    /// <param name="runtime">The concrete runtime reader, copied by value.</param>
    /// <param name="author">The concrete author reader, copied by value.</param>
    /// <returns>The immutable resource field binding.</returns>
    /// <exception cref="ArgumentException">The field shape or partition paths are incompatible.</exception>
    public static WorldResourceFieldBinding<T, TAuthor> PartitionArray<TElement, TRuntime, TAuthorReader>(
        WorldResourceFieldDescriptor field,
        ImmutableArray<ComponentStoragePath> partitionStoragePaths,
        TRuntime runtime,
        TAuthorReader author)
        where TElement : unmanaged
        where TRuntime : struct, IComponentValueReader<T, TElement>
        where TAuthorReader : struct, IComponentArrayReader<TAuthor, TElement>
    {
        ArgumentNullException.ThrowIfNull(field);

        if ((field.Metadata.WireKind != 23)
            || (field.Metadata.ElementKind != GetWireKind<TElement>())
            || !field.Metadata.IsRequired
            || (field.Metadata.Reference != ContentReferenceKind.None))
        {
            throw new ArgumentException("The selected field is not a matching required FixedArray.", nameof(field));
        }

        var storagePaths = CopyStoragePaths(partitionStoragePaths);
        var readers = new CapturedReaders<TRuntime, TAuthorReader, TElement>(field, runtime, author);
        return new WorldResourceFieldBinding<T, TAuthor>(field, storagePaths, readers);
    }

    private static ImmutableArray<ComponentStoragePath> CopyStoragePaths(
        ImmutableArray<ComponentStoragePath> storagePaths)
    {
        if (storagePaths.IsDefaultOrEmpty)
        {
            throw new ArgumentException("Partition storage paths must be initialized and nonempty.", nameof(storagePaths));
        }

        for (var index = 0; index < storagePaths.Length; index++)
        {
            var path = storagePaths[index]
                ?? throw new ArgumentException("Partition storage paths cannot contain null entries.", nameof(storagePaths));

            for (var previousIndex = 0; previousIndex < index; previousIndex++)
            {
                if (path.Equals(storagePaths[previousIndex]))
                {
                    throw new ArgumentException("Partition storage paths must be unique.", nameof(storagePaths));
                }
            }
        }

        return ImmutableArrayCopy.Create(storagePaths);
    }

    private static ushort GetWireKind<TValue>()
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

        if (type == typeof(float))
        {
            return 10;
        }

        if (type == typeof(double))
        {
            return 0;
        }

        if (type == typeof(bool))
        {
            return 9;
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

    private sealed class CapturedReaders<TRuntime, TAuthorReader, TElement> : IWorldResourceFieldAccess<T>
        where TRuntime : struct, IComponentValueReader<T, TElement>
        where TAuthorReader : struct, IComponentArrayReader<TAuthor, TElement>
    {
        private readonly TAuthorReader _author;
        private readonly WorldResourceFieldDescriptor _descriptor;
        private readonly TRuntime _runtime;

        internal CapturedReaders(
            WorldResourceFieldDescriptor descriptor,
            TRuntime runtime,
            TAuthorReader author)
        {
            _descriptor = descriptor;
            _runtime = runtime;
            _author = author;
        }

        public WorldResourceFieldDescriptor Descriptor => _descriptor;

        public Type ElementType => typeof(TElement);

        public int GetAuthorCount(object author)
        {
            if (author is not TAuthor typedAuthor)
            {
                throw new InvalidOperationException("The resource author root has the wrong type.");
            }

            return _author.GetCount(in typedAuthor);
        }

        public object ReadAuthorElement(object author, int index)
        {
            if (author is not TAuthor typedAuthor)
            {
                throw new InvalidOperationException("The resource author root has the wrong type.");
            }

            return _author.GetElement(in typedAuthor, index)!;
        }

        public object ReadPartition(in T partition)
            => _runtime.Get(in partition)!;
    }
}
