// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class WorldResourcePartitionControl
{
    private readonly object _access;
    private Exception? _failure;
    private int _isActive = 1;
    private readonly int _ownerThreadId;

    internal WorldResourcePartitionControl(object access, bool canWrite)
    {
        ArgumentNullException.ThrowIfNull(access);
        _access = access;
        CanWrite = canWrite;
        _ownerThreadId = Environment.CurrentManagedThreadId;
    }

    internal bool CanWrite { get; }

    internal Exception? Failure => _failure;

    internal void EnsureValidView() => EnsureActive();

    internal T Read<T>(int partitionIndex)
        where T : unmanaged
    {
        EnsureActive();

        if (_access is not IWorldResourcePartitionAccess<T> access)
        {
            throw RecordFailure(new InvalidOperationException("The resource partition view has the wrong registered type."));
        }

        return access.Read(partitionIndex);
    }

    internal void Revoke()
        => _ = Interlocked.Exchange(ref _isActive, 0);

    internal void Write<T>(int partitionIndex, in T value)
        where T : unmanaged
    {
        EnsureActive();

        if (!CanWrite)
        {
            throw RecordFailure(new InvalidOperationException("The resource partition view has no write grant."));
        }

        if (_access is not IWorldResourcePartitionAccess<T> access)
        {
            throw RecordFailure(new InvalidOperationException("The resource partition view has the wrong registered type."));
        }

        access.Write(partitionIndex, in value);
    }

    private void EnsureActive()
    {
        if (Volatile.Read(ref _isActive) == 0)
        {
            throw RecordFailure(new InvalidOperationException("The resource partition view has expired."));
        }

        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw RecordFailure(new InvalidOperationException("The resource partition view is restricted to its issuing thread."));
        }
    }

    private Exception RecordFailure(Exception exception)
    {
        _failure ??= exception;
        return _failure;
    }
}
