// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal sealed class WorldResourceOperationControl
{
    private Exception? _failure;
    private readonly object _familyControl;
    private bool _isActive = true;
    private readonly WorldResourceOperationKind _operation;
    private readonly int _ownerThreadId;
    private readonly int _partitionCount;
    private readonly Type _partitionType;
    private readonly Guid _resourceId;
    private readonly ContentSchemaReference _schema;

    internal WorldResourceOperationControl(
        object familyControl,
        Guid resourceId,
        ContentSchemaReference schema,
        int partitionCount,
        Type partitionType,
        WorldResourceOperationKind operation)
    {
        ArgumentNullException.ThrowIfNull(familyControl);
        ArgumentOutOfRangeException.ThrowIfEqual(resourceId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(partitionCount);
        ArgumentNullException.ThrowIfNull(partitionType);

        if (operation is < WorldResourceOperationKind.Validation or > WorldResourceOperationKind.Write)
        {
            throw new ArgumentOutOfRangeException(nameof(operation));
        }

        _familyControl = familyControl;
        _resourceId = resourceId;
        _schema = schema;
        _partitionCount = partitionCount;
        _partitionType = partitionType;
        _operation = operation;
        _ownerThreadId = Environment.CurrentManagedThreadId;
    }

    internal Exception? Failure => _failure;

    internal WorldResourceOperationKind Operation => _operation;

    internal int PartitionCount => _partitionCount;

    internal Type PartitionType => _partitionType;

    internal Guid ResourceId => _resourceId;

    internal ContentSchemaReference Schema => _schema;

    internal void EnsureActive(WorldResourceOperationKind expectedOperation)
    {
        if (!_isActive)
        {
            throw RecordFailure(new InvalidOperationException("The resource operation binding has expired."));
        }

        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw RecordFailure(new InvalidOperationException("The resource operation binding is restricted to its issuing thread."));
        }

        if (_operation != expectedOperation)
        {
            throw RecordFailure(new InvalidOperationException("The resource binding does not authorize the selected operation."));
        }
    }

    internal void EnsureViewType<T>(WorldResourceStateView<T> view)
        where T : unmanaged
    {
        EnsureActive(_operation);

        if ((_partitionType != typeof(T)) || !ReferenceEquals(view.Operation, this))
        {
            throw RecordFailure(new InvalidOperationException("The resource view does not belong to this operation and partition type."));
        }
    }

    internal bool IsOwnedBy(object familyControl)
        => ReferenceEquals(_familyControl, familyControl);

    internal Exception RecordFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _failure ??= exception;
        return _failure;
    }

    internal void Revoke() => _isActive = false;
}
