// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

internal sealed class GraphicsResourceAllocationLease
{
    private readonly GraphicsResourceAllocationInfo _allocationInfo;
    private IGraphicsBackendAllocation? _backendAllocation;
    private int _referenceCount;

    internal GraphicsResourceAllocationLease(
        IGraphicsBackendAllocation backendAllocation,
        GraphicsResourceAllocationInfo allocationInfo)
    {
        ArgumentNullException.ThrowIfNull(backendAllocation);
        _backendAllocation = backendAllocation;
        _allocationInfo = allocationInfo;
    }

    internal IGraphicsBackendAllocation BackendAllocation
        => _backendAllocation
            ?? throw new ObjectDisposedException(nameof(GraphicsResourceAllocationLease));

    internal bool IsReleased => _backendAllocation is null;

    internal int ReferenceCount => _referenceCount;

    internal void AddReference()
    {
        ObjectDisposedException.ThrowIf(_backendAllocation is null, this);
        _referenceCount = checked(_referenceCount + 1);
    }

    internal GraphicsResourceAllocationInfo Release()
    {
        if (_referenceCount <= 0)
        {
            throw new InvalidOperationException("The shared Graphics allocation has no resource references.");
        }

        if (_referenceCount > 1)
        {
            _referenceCount--;
            return default;
        }

        var backendAllocation = _backendAllocation
            ?? throw new ObjectDisposedException(nameof(GraphicsResourceAllocationLease));
        backendAllocation.Dispose();
        _backendAllocation = null;
        _referenceCount = 0;
        return _allocationInfo;
    }

    internal GraphicsResourceAllocationInfo ReleaseUnreferenced()
    {
        if (_referenceCount != 0)
        {
            throw new InvalidOperationException("A shared Graphics allocation still has resource references.");
        }

        var backendAllocation = _backendAllocation
            ?? throw new ObjectDisposedException(nameof(GraphicsResourceAllocationLease));
        backendAllocation.Dispose();
        _backendAllocation = null;
        return _allocationInfo;
    }
}
