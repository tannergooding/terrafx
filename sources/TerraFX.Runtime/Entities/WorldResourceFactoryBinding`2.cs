// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace TerraFX.Entities;

/// <summary>Captures typed resource defaults and schema-field readers for one World resource factory.</summary>
/// <typeparam name="T">The unmanaged resource partition type.</typeparam>
/// <typeparam name="TAuthor">The immutable symbolic author type.</typeparam>
[SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "The closed resource and author types are required by the typed factory contract.")]
public sealed class WorldResourceFactoryBinding<T, TAuthor>
    where T : unmanaged
    where TAuthor : notnull
{
    private readonly ResourceFactoryFamily<T, TAuthor> _family;

    private WorldResourceFactoryBinding(ResourceFactoryFamily<T, TAuthor> family)
    {
        _family = family;
    }

    /// <summary>Gets the immutable factory descriptor captured by this binding.</summary>
    public WorldResourceFactoryDescriptor Descriptor => _family.Control.Descriptor;

    /// <summary>Gets the erased registration binding that shares this exact captured family.</summary>
    public WorldResourceFactoryBinding<T> RegistrationBinding => _family.RegistrationBinding;

    internal ResourceFactoryFamily<T, TAuthor> Family => _family;

    /// <summary>Captures the typed resource defaults and complete fixed-array field bindings.</summary>
    /// <param name="factoryId">The nonempty stable factory identity.</param>
    /// <param name="factoryVersion">The positive factory version.</param>
    /// <param name="schema">The exact compiled resource state schema.</param>
    /// <param name="partitionStorage">The complete physical layout of one resource partition.</param>
    /// <param name="fields">The complete ordered binding of resource state FixedArray fields.</param>
    /// <param name="defaults">The typed default provider captured by value.</param>
    /// <returns>The immutable typed resource factory binding.</returns>
    /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
    /// <exception cref="ArgumentException">The schema, field closure, or storage declaration is inconsistent.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="factoryVersion"/> is not positive.</exception>
    public static WorldResourceFactoryBinding<T, TAuthor> Create(
        Guid factoryId,
        int factoryVersion,
        WorldResourceSchema<T> schema,
        ComponentStorageDeclaration partitionStorage,
        ImmutableArray<WorldResourceFieldBinding<T, TAuthor>> fields,
        IWorldResourceDefaultProvider<T> defaults)
    {
        if (factoryId == Guid.Empty)
        {
            throw new ArgumentException("A resource factory identity cannot be empty.", nameof(factoryId));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(factoryVersion);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(partitionStorage);
        ArgumentNullException.ThrowIfNull(defaults);

        if (partitionStorage.LogicalBytes != Unsafe.SizeOf<T>())
        {
            throw new ArgumentException("The resource partition storage size must match its unmanaged value type.", nameof(partitionStorage));
        }

        var capturedFields = CopyAndValidateFields(schema, partitionStorage, fields);
        var descriptors = ImmutableArray.CreateBuilder<WorldResourceFieldDescriptor>(capturedFields.Length);

        foreach (var field in capturedFields)
        {
            descriptors.Add(field.Descriptor);
        }

        var descriptor = new WorldResourceFactoryDescriptor(
            schema.Schema,
            factoryId,
            factoryVersion,
            partitionStorage,
            descriptors.MoveToImmutable());
        var family = new ResourceFactoryFamily<T, TAuthor>(descriptor, capturedFields, defaults);
        return new WorldResourceFactoryBinding<T, TAuthor>(family);
    }

    private static void CollectFixedArrayFields(
        WorldResourceSchema<T> schema,
        DataSchema currentSchema,
        List<ComponentPropertySegment> prefix,
        List<DataSchema> schemaStack,
        List<WorldResourceFieldDescriptor> fields,
        string parameterName)
    {
        foreach (var activeSchema in schemaStack)
        {
            if (ReferenceEquals(activeSchema, currentSchema))
            {
                throw new ArgumentException("The selected resource schema contains a recursive record closure.", parameterName);
            }
        }

        schemaStack.Add(currentSchema);

        foreach (var field in currentSchema.Fields)
        {
            if (prefix.Count >= 64)
            {
                throw new ArgumentException("The selected resource schema exceeds the supported field-path depth.", parameterName);
            }

            prefix.Add(ComponentPropertySegment.FieldTag(field.Tag));

            if ((field.WireKind == 23) && field.IsRequired)
            {
                fields.Add(schema.GetField(new ComponentFieldPath([.. prefix])));
            }
            else if ((field.WireKind == 22)
                && field.IsRequired
                && (field.Reference == ContentReferenceKind.None)
                && schema.TryResolveNestedSchema(field, out var nestedSchema))
            {
                CollectFixedArrayFields(schema, nestedSchema, prefix, schemaStack, fields, parameterName);
            }
            else
            {
                throw new ArgumentException("Every resource state field must resolve to a required FixedArray.", parameterName);
            }

            prefix.RemoveAt(prefix.Count - 1);
        }

        schemaStack.RemoveAt(schemaStack.Count - 1);
    }

    private static ImmutableArray<WorldResourceFieldBinding<T, TAuthor>> CopyAndValidateFields(
        WorldResourceSchema<T> schema,
        ComponentStorageDeclaration partitionStorage,
        ImmutableArray<WorldResourceFieldBinding<T, TAuthor>> fields)
    {
        if (fields.IsDefault)
        {
            throw new ArgumentException("Resource field bindings must be initialized.", nameof(fields));
        }

        var expectedFields = new List<WorldResourceFieldDescriptor>();
        CollectFixedArrayFields(schema, schema.Metadata, [], [], expectedFields, nameof(fields));

        if (fields.Length != expectedFields.Count)
        {
            throw new ArgumentException("Resource field bindings must close every required FixedArray schema field.", nameof(fields));
        }

        var capturedFields = ImmutableArray.CreateBuilder<WorldResourceFieldBinding<T, TAuthor>>(fields.Length);
        var matchedStorage = new bool[partitionStorage.Leaves.Length];

        for (var index = 0; index < fields.Length; index++)
        {
            var binding = fields[index]
                ?? throw new ArgumentException("Resource field bindings cannot contain null entries.", nameof(fields));
            var descriptor = binding.Descriptor;
            var path = descriptor.Path.Segments;
            var expected = expectedFields[index];

            if ((descriptor.Schema != schema.Schema)
                || !ReferenceEquals(descriptor.Schemas, schema.Schemas)
                || !ReferenceEquals(descriptor.DataSchema, expected.DataSchema)
                || !ReferenceEquals(descriptor.Metadata, expected.Metadata)
                || !path.AsSpan().SequenceEqual(expected.Path.Segments.AsSpan()))
            {
                throw new ArgumentException("Resource field bindings must match every FixedArray declaration in canonical schema order.", nameof(fields));
            }

            foreach (var storagePath in binding.PartitionStoragePaths)
            {
                var leafIndex = FindStorageLeaf(partitionStorage.Leaves, storagePath);

                if (leafIndex < 0
                    || (partitionStorage.Leaves[leafIndex].Purpose != ComponentStoragePurpose.SchemaValue)
                    || (partitionStorage.Leaves[leafIndex].Kind is ComponentStorageKind.EntityHandle
                        or ComponentStorageKind.OptionalEntityHandle
                        or ComponentStorageKind.TransientNativeValue))
                {
                    throw new ArgumentException("Resource field paths must select portable schema-value storage leaves.", nameof(fields));
                }

                if (matchedStorage[leafIndex])
                {
                    throw new ArgumentException("Resource field bindings cannot map one storage leaf more than once.", nameof(fields));
                }

                matchedStorage[leafIndex] = true;
            }

            capturedFields.Add(binding);
        }

        for (var index = 0; index < partitionStorage.Leaves.Length; index++)
        {
            var leaf = partitionStorage.Leaves[index];

            if (leaf.Kind is ComponentStorageKind.EntityHandle
                or ComponentStorageKind.OptionalEntityHandle
                or ComponentStorageKind.TransientNativeValue)
            {
                throw new ArgumentException("Resource partitions cannot contain Entity handles or transient native values.", nameof(partitionStorage));
            }

            if ((leaf.Purpose == ComponentStoragePurpose.SchemaValue) && !matchedStorage[index])
            {
                throw new ArgumentException("Every resource schema-value storage leaf must be selected by one field binding.", nameof(fields));
            }
        }

        return capturedFields.MoveToImmutable();
    }

    private static int FindStorageLeaf(
        ImmutableArray<ComponentStorageLeaf> leaves,
        ComponentStoragePath path)
    {
        for (var index = 0; index < leaves.Length; index++)
        {
            if (leaves[index].Path.Equals(path))
            {
                return index;
            }
        }

        return -1;
    }
}
