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
    private readonly GraphicsResourceGraphState? _graphState;
    private readonly GraphicsOwnerRequestQueue.GraphicsRetirementReservation _retirementReservation;
    // 0 is live, 1 is retirement requested, 2 is retiring, and 3 is retired or quarantined.
    private int _retirementState;

    internal GraphicsResourceOwner(
        GraphicsDevice device,
        GraphicsResourceAllocationInfo? allocationInfo,
        IGraphicsBackendResource backendResource,
        GraphicsOwnerRequestQueue.GraphicsRetirementReservation retirementReservation,
        BufferDescription? bufferDescription = null,
        TextureDescription? textureDescription = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(backendResource);
        ArgumentNullException.ThrowIfNull(retirementReservation);

        if ((bufferDescription is not null) && (textureDescription is not null))
        {
            throw new ArgumentException("A Graphics resource cannot be both a buffer and a texture.");
        }

        if (((bufferDescription is not null) && (backendResource is not IGraphicsBackendBuffer))
            || ((textureDescription is not null) && (backendResource is not IGraphicsBackendTexture))
            || ((backendResource is IGraphicsBackendBuffer) && (bufferDescription is null))
            || ((backendResource is IGraphicsBackendTexture) && (textureDescription is null)))
        {
            throw new ArgumentException("The graph resource description must match the backend resource.", nameof(backendResource));
        }

        _allocationInfo = allocationInfo;
        _backendResource = backendResource;
        _device = device;
        _graphState = (bufferDescription is null) && (textureDescription is null)
            ? null
            : new GraphicsResourceGraphState(bufferDescription, textureDescription);
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

    internal (GraphResourceState State, bool ContentsValid) GetGraphImportState(GraphBufferRange range)
    {
        ObjectDisposedException.ThrowIf(!IsLiveForNewWork, typeof(GpuBuffer));
        var state = (_graphState
            ?? throw new InvalidOperationException("The Graphics resource does not support graph import."))
            .GetBufferState(range);
        ObjectDisposedException.ThrowIf(!IsLiveForNewWork, typeof(GpuBuffer));
        return state;
    }

    internal (GraphResourceState State, bool ContentsValid) GetGraphImportState(GraphTextureRange range)
    {
        ObjectDisposedException.ThrowIf(!IsLiveForNewWork, typeof(GpuTexture));
        var state = (_graphState
            ?? throw new InvalidOperationException("The Graphics resource does not support graph import."))
            .GetTextureState(range);
        ObjectDisposedException.ThrowIf(!IsLiveForNewWork, typeof(GpuTexture));
        return state;
    }

    internal void MarkQuarantined()
        => Volatile.Write(ref _retirementState, 3);

    internal void MarkRetired()
        => Volatile.Write(ref _retirementState, 3);

    internal void RecordBufferReadback()
        => (_graphState
            ?? throw new InvalidOperationException("The Graphics resource is not a buffer."))
            .RecordBufferReadback();

    internal void RecordBufferUpload(long destinationOffset, nint byteLength)
        => (_graphState
            ?? throw new InvalidOperationException("The Graphics resource is not a buffer."))
            .RecordBufferUpload(destinationOffset, byteLength);

    internal void RecordTextureReadback(TextureSubresource subresource)
        => (_graphState
            ?? throw new InvalidOperationException("The Graphics resource is not a texture."))
            .RecordTextureReadback(subresource);

    internal void RecordTextureUpload(TextureSubresource subresource)
        => (_graphState
            ?? throw new InvalidOperationException("The Graphics resource is not a texture."))
            .RecordTextureUpload(subresource);

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
