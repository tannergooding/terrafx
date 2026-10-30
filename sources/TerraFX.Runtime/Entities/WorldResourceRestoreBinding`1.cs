// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Provides operation-scoped one-write-per-index restoration of resource partitions.</summary>
/// <typeparam name="T">The unmanaged resource partition type.</typeparam>
public sealed class WorldResourceRestoreBinding<T>
    where T : unmanaged
{
    private WorldResourceOperationControl? _operation;
    private T[]? _partitions;
    private bool[]? _written;

    internal WorldResourceRestoreBinding(WorldResourceOperationControl operation, T[] partitions)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(partitions);

        if ((operation.Operation != WorldResourceOperationKind.Restore)
            || (operation.PartitionType != typeof(T))
            || (operation.PartitionCount != partitions.Length))
        {
            throw new ArgumentException("The resource restore array does not match its operation.", nameof(partitions));
        }

        _operation = operation;
        _partitions = partitions;
        _written = new bool[partitions.Length];
    }

    /// <summary>Gets the complete partition count.</summary>
    public int PartitionCount => GetOperation().PartitionCount;

    /// <summary>Gets the resource identity.</summary>
    public Guid ResourceId => GetOperation().ResourceId;

    /// <summary>Gets the exact resource state schema tuple.</summary>
    public ContentSchemaReference Schema => GetOperation().Schema;

    /// <summary>Installs one restored partition value at its exact index.</summary>
    /// <param name="partitionIndex">The zero-based resource partition index.</param>
    /// <param name="value">The restored resource partition value.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="partitionIndex"/> is outside the partition domain.</exception>
    /// <exception cref="InvalidOperationException">The binding has expired or the partition was already set.</exception>
    public void SetPartition(int partitionIndex, in T value)
    {
        var operation = GetOperation();
        var partitions = _partitions
            ?? throw operation.RecordFailure(new InvalidOperationException("The resource restore binding has no active partition storage."));
        var written = _written
            ?? throw operation.RecordFailure(new InvalidOperationException("The resource restore binding has no active partition map."));

        if ((uint)partitionIndex >= (uint)partitions.Length)
        {
            throw operation.RecordFailure(new ArgumentOutOfRangeException(nameof(partitionIndex)));
        }

        if (written[partitionIndex])
        {
            throw operation.RecordFailure(new InvalidOperationException("A resource restore partition can be set only once."));
        }

        partitions[partitionIndex] = value;
        written[partitionIndex] = true;
    }

    internal void Revoke()
    {
        _partitions = null;
        _written = null;
        _operation?.Revoke();
        _operation = null;
    }

    internal void ValidateComplete()
    {
        var operation = GetOperation();
        var written = _written
            ?? throw operation.RecordFailure(new InvalidOperationException("The resource restore binding has no active partition map."));

        for (var index = 0; index < written.Length; index++)
        {
            if (!written[index])
            {
                throw operation.RecordFailure(new InvalidOperationException("Resource restoration must set every partition exactly once."));
            }
        }
    }

    private WorldResourceOperationControl GetOperation()
    {
        var operation = _operation
            ?? throw new InvalidOperationException("The default resource restore binding is not issued.");
        operation.EnsureActive(WorldResourceOperationKind.Restore);
        return operation;
    }
}
