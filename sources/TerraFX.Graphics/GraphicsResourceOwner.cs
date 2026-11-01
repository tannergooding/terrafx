// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Graphics;

internal sealed class GraphicsResourceOwner : IDisposable
{
    private int _activeUseCount;
    private readonly GraphicsResourceAllocationInfo? _allocationInfo;
    private readonly IGraphicsBackendResource _backendResource;
    private readonly GraphicsDevice _device;
    private readonly GraphicsOwnerRequestQueue.GraphicsRetirementReservation _retirementReservation;
    // 0 is live, 1 is retirement requested, 2 is retiring, and 3 is retired or quarantined.
    private int _retirementState;

    internal GraphicsResourceOwner(
        GraphicsDevice device,
        GraphicsResourceAllocationInfo? allocationInfo,
        IGraphicsBackendResource backendResource,
        GraphicsOwnerRequestQueue.GraphicsRetirementReservation retirementReservation)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(backendResource);
        ArgumentNullException.ThrowIfNull(retirementReservation);

        _allocationInfo = allocationInfo;
        _backendResource = backendResource;
        _device = device;
        _retirementReservation = retirementReservation;
    }

    internal GraphicsResourceAllocationInfo? AllocationInfo => _allocationInfo;

    internal IGraphicsBackendResource BackendResource => _backendResource;

    internal GraphicsDevice Device => _device;

    internal uint DeviceEpoch => _device.DeviceEpoch;

    internal bool HasActiveUses => Volatile.Read(ref _activeUseCount) != 0;

    internal bool IsLiveForNewWork => Volatile.Read(ref _retirementState) == 0;

    internal bool IsRetirementRequested => Volatile.Read(ref _retirementState) == 1;

    internal uint OwnerId => _device.OwnerId;

    internal GraphicsOwnerRequestQueue.GraphicsRetirementReservation RetirementReservation => _retirementReservation;

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _retirementState, 1, 0) == 0)
        {
            _device.EnqueueResourceRetirement(this);
        }
    }

    internal UseLease AcquireUse(string parameterName)
    {
        while (true)
        {
            if (!IsLiveForNewWork)
            {
                throw new ObjectDisposedException(parameterName, "The Graphics resource no longer accepts new work.");
            }

            _ = Interlocked.Increment(ref _activeUseCount);

            if (IsLiveForNewWork)
            {
                try
                {
                    return new UseLease(this);
                }
                catch
                {
                    ReleaseUse();
                    throw;
                }
            }

            ReleaseUse();
        }
    }

    internal void MarkQuarantined()
        => Volatile.Write(ref _retirementState, 3);

    internal void MarkRetired()
        => Volatile.Write(ref _retirementState, 3);

    internal void RequestRetirementForShutdown()
        => _ = Interlocked.CompareExchange(ref _retirementState, 1, 0);

    internal bool TryBeginRetirement()
    {
        while (true)
        {
            if (HasActiveUses)
            {
                return false;
            }

            var currentState = Volatile.Read(ref _retirementState);

            if (currentState >= 2)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _retirementState, 2, currentState) == currentState)
            {
                return true;
            }
        }
    }

    private void ReleaseUse()
    {
        var activeUseCount = Interlocked.Decrement(ref _activeUseCount);

        if (activeUseCount < 0)
        {
            throw new InvalidOperationException("A Graphics resource use credit was released more than once.");
        }

        if ((activeUseCount == 0) && IsRetirementRequested)
        {
            _device.NotifyResourceUseReleased();
        }
    }

    internal sealed class UseLease : IDisposable
    {
        private GraphicsResourceOwner? _owner;

        internal UseLease(GraphicsResourceOwner owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.ReleaseUse();
        }
    }
}
