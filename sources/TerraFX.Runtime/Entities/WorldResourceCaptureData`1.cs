// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Entities;

internal sealed class WorldResourceCaptureData<T> : IWorldResourceCaptureData
    where T : unmanaged
{
    private readonly ImmutableArray<IWorldResourceFieldAccess<T>> _fields;
    private readonly T[] _partitions;

    internal WorldResourceCaptureData(
        T[] partitions,
        ImmutableArray<IWorldResourceFieldAccess<T>> fields)
    {
        ArgumentNullException.ThrowIfNull(partitions);

        if (fields.IsDefault)
        {
            throw new ArgumentException("Resource capture field accessors must be initialized.", nameof(fields));
        }

        _partitions = partitions;
        _fields = ImmutableArrayCopy.Create(fields);
    }

    public int GetCount(WorldResourceFieldDescriptor field)
    {
        _ = FindField(field);
        return _partitions.Length;
    }

    public object GetElement(WorldResourceFieldDescriptor field, int partitionIndex, Type elementType)
    {
        ArgumentNullException.ThrowIfNull(elementType);
        ArgumentOutOfRangeException.ThrowIfNegative(partitionIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(partitionIndex, _partitions.Length);
        var accessor = FindField(field);

        if (accessor.ElementType != elementType)
        {
            throw new ArgumentException("The selected resource capture element type does not match the field binding.", nameof(elementType));
        }

        return accessor.ReadPartition(in _partitions[partitionIndex]);
    }

    private IWorldResourceFieldAccess<T> FindField(WorldResourceFieldDescriptor field)
    {
        ArgumentNullException.ThrowIfNull(field);

        foreach (var candidate in _fields)
        {
            if (ReferenceEquals(candidate.Descriptor, field))
            {
                return candidate;
            }
        }

        throw new ArgumentException("The field does not belong to this resource capture family.", nameof(field));
    }
}
