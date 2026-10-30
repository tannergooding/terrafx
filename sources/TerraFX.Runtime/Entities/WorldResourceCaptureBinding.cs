// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Provides operation-scoped field access for resource validation and capture callbacks.</summary>
public sealed class WorldResourceCaptureBinding
{
    private IWorldResourceCaptureData? _data;
    private WorldResourceOperationControl? _operation;

    internal WorldResourceCaptureBinding(
        WorldResourceOperationControl operation,
        IWorldResourceCaptureData data)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(data);

        if (operation.Operation is not WorldResourceOperationKind.Count and not WorldResourceOperationKind.Write)
        {
            throw new InvalidOperationException("A resource capture binding requires a Count or Write operation.");
        }

        _operation = operation;
        _data = data;
    }

    /// <summary>Gets the complete partition count.</summary>
    public int PartitionCount => GetOperation().PartitionCount;

    /// <summary>Gets the resource identity.</summary>
    public Guid ResourceId => GetOperation().ResourceId;

    /// <summary>Gets the exact resource state schema tuple.</summary>
    public ContentSchemaReference Schema => GetOperation().Schema;

    /// <summary>Gets the complete fixed-array count for one selected resource field.</summary>
    /// <param name="field">The exact registered resource field.</param>
    /// <returns>The resource's complete partition count.</returns>
    /// <exception cref="ArgumentException"><paramref name="field"/> is not from this registration.</exception>
    /// <exception cref="InvalidOperationException">The binding has expired.</exception>
    public int GetCount(WorldResourceFieldDescriptor field)
    {
        var operation = GetOperation();
        var data = _data
            ?? throw operation.RecordFailure(new InvalidOperationException("The resource capture binding has no active field data."));
        return data.GetCount(field);
    }

    /// <summary>Reads one field element from the selected resource partition.</summary>
    /// <typeparam name="TElement">The unmanaged fixed-array element type.</typeparam>
    /// <param name="field">The exact registered resource field.</param>
    /// <param name="partitionIndex">The zero-based resource partition index.</param>
    /// <returns>The copied field element.</returns>
    /// <exception cref="ArgumentException">The field or element type is not from this registration.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="partitionIndex"/> is outside the partition domain.</exception>
    /// <exception cref="InvalidOperationException">The binding has expired.</exception>
    public TElement GetElement<TElement>(WorldResourceFieldDescriptor field, int partitionIndex)
        where TElement : unmanaged
    {
        var operation = GetOperation();
        var data = _data
            ?? throw operation.RecordFailure(new InvalidOperationException("The resource capture binding has no active field data."));
        var value = data.GetElement(field, partitionIndex, typeof(TElement));
        return value is TElement element
            ? element
            : throw operation.RecordFailure(new InvalidOperationException("The captured resource field returned an invalid element type."));
    }

    internal void Revoke()
    {
        _data = null;
        _operation?.Revoke();
        _operation = null;
    }

    private WorldResourceOperationControl GetOperation()
    {
        var operation = _operation
            ?? throw new InvalidOperationException("The default resource capture binding is not issued.");
        operation.EnsureActive(operation.Operation);
        return operation;
    }
}
