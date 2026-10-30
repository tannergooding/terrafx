// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Provides scoped read-only access to one complete resource partition set.</summary>
/// <typeparam name="T">The unmanaged resource partition type.</typeparam>
public readonly ref struct WorldResourceStateView<T>
    where T : unmanaged
{
    private readonly WorldResourceOperationControl? _operation;
    private readonly ReadOnlySpan<T> _partitions;

    internal WorldResourceStateView(WorldResourceOperationControl operation, ReadOnlySpan<T> partitions)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (partitions.Length != operation.PartitionCount)
        {
            throw new ArgumentException("The resource view must expose the complete partition set.", nameof(partitions));
        }

        if ((operation.Operation is not WorldResourceOperationKind.Validation
            and not WorldResourceOperationKind.Count
            and not WorldResourceOperationKind.Write)
            || (operation.PartitionType != typeof(T)))
        {
            throw new InvalidOperationException("The operation does not authorize this resource state view.");
        }

        _operation = operation;
        _partitions = partitions;
    }

    /// <summary>Gets the complete partition count.</summary>
    public int PartitionCount
    {
        get
        {
            var operation = GetOperation();
            return operation.PartitionCount;
        }
    }

    /// <summary>Gets the resource identity.</summary>
    public Guid ResourceId
    {
        get
        {
            var operation = GetOperation();
            return operation.ResourceId;
        }
    }

    /// <summary>Gets the exact resource state schema tuple.</summary>
    public ContentSchemaReference Schema
    {
        get
        {
            var operation = GetOperation();
            return operation.Schema;
        }
    }

    internal WorldResourceOperationControl Operation => GetOperation();

    /// <summary>Reads one copied resource partition value.</summary>
    /// <param name="partitionIndex">The zero-based partition index.</param>
    /// <returns>The copied resource partition.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="partitionIndex"/> is outside the partition domain.</exception>
    /// <exception cref="InvalidOperationException">The view has expired or is used from another thread.</exception>
    public T ReadPartition(int partitionIndex)
    {
        _ = GetOperation();
        ArgumentOutOfRangeException.ThrowIfNegative(partitionIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(partitionIndex, _partitions.Length);
        return _partitions[partitionIndex];
    }

    private WorldResourceOperationControl GetOperation()
    {
        var operation = _operation
            ?? throw new InvalidOperationException("The default resource state view is not issued.");
        operation.EnsureActive(operation.Operation);
        return operation;
    }
}
