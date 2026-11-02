// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TerraFX.Graphics;

internal sealed class GraphicsResourceOwner : IDisposable
{
    private int _activeUseCount;
    private readonly SortedSet<long> _activeUseSequences = [];
    private readonly GraphicsResourceAllocationInfo? _allocationInfo;
    private readonly IGraphicsBackendResource _backendResource;
    private readonly GraphicsDevice _device;
    private readonly GraphicsResourceGraphState? _graphState;
    private long _nextUseSequence;
    private readonly GraphicsOwnerRequestQueue.GraphicsRetirementReservation _retirementReservation;
    // 0 is live, 1 is retirement requested, 2 is retiring, and 3 is retired or quarantined.
    private int _retirementState;
    private readonly List<UseCompletionWaiter> _useCompletionWaiters = [];
    private readonly Lock _useGate = new();

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

            lock (_useGate)
            {
                if (!IsLiveForNewWork)
                {
                    continue;
                }

                var sequence = checked(_nextUseSequence + 1);
                var lease = new UseLease(this, sequence);

                if (!_activeUseSequences.Add(sequence))
                {
                    throw new InvalidOperationException("The Graphics resource use sequence was reused.");
                }

                _nextUseSequence = sequence;
                _ = Interlocked.Increment(ref _activeUseCount);
                return lease;
            }
        }
    }

    internal (GraphResourceState State, bool ContentsValid, long LastUseSequence, bool IsIdle) CaptureGraphImportState(
        GraphBufferRange range)
    {
        ObjectDisposedException.ThrowIf(!IsLiveForNewWork, typeof(GpuBuffer));

        lock (_useGate)
        {
            (var state, var contentsValid) = (_graphState
                ?? throw new InvalidOperationException("The Graphics resource does not support graph import."))
                .GetBufferState(range);
            ObjectDisposedException.ThrowIf(!IsLiveForNewWork, typeof(GpuBuffer));
            return (state, contentsValid, _nextUseSequence, _activeUseSequences.Count == 0);
        }
    }

    internal (GraphResourceState State, bool ContentsValid, long LastUseSequence, bool IsIdle) CaptureGraphImportState(
        GraphTextureRange range)
    {
        ObjectDisposedException.ThrowIf(!IsLiveForNewWork, typeof(GpuTexture));

        lock (_useGate)
        {
            (var state, var contentsValid) = (_graphState
                ?? throw new InvalidOperationException("The Graphics resource does not support graph import."))
                .GetTextureState(range);
            ObjectDisposedException.ThrowIf(!IsLiveForNewWork, typeof(GpuTexture));
            return (state, contentsValid, _nextUseSequence, _activeUseSequences.Count == 0);
        }
    }

    internal (GraphResourceState State, bool ContentsValid) GetGraphImportState(GraphBufferRange range)
    {
        (var state, var contentsValid, _, _) = CaptureGraphImportState(range);
        return (state, contentsValid);
    }

    internal (GraphResourceState State, bool ContentsValid) GetGraphImportState(GraphTextureRange range)
    {
        (var state, var contentsValid, _, _) = CaptureGraphImportState(range);
        return (state, contentsValid);
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
        lock (_useGate)
        {
            if (HasActiveUses)
            {
                return false;
            }

            while (true)
            {
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
    }

    internal Task WaitForUsesAsync(long sequence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sequence);

        lock (_useGate)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(sequence, _nextUseSequence);

            if (!HasActiveUseAtOrBefore(sequence))
            {
                return Task.CompletedTask;
            }

            var waiter = new UseCompletionWaiter(sequence);
            _useCompletionWaiters.Add(waiter);
            return waiter.Task;
        }
    }

    private bool HasActiveUseAtOrBefore(long sequence)
        => (_activeUseSequences.Count != 0) && (_activeUseSequences.Min <= sequence);

    private void ReleaseUse(long sequence)
    {
        int activeUseCount;

        lock (_useGate)
        {
            if (!_activeUseSequences.Remove(sequence))
            {
                throw new InvalidOperationException("A Graphics resource use credit was released more than once.");
            }

            activeUseCount = Interlocked.Decrement(ref _activeUseCount);

            if (activeUseCount < 0)
            {
                throw new InvalidOperationException("The Graphics resource active-use count became negative.");
            }

            for (var index = _useCompletionWaiters.Count - 1; index >= 0; index--)
            {
                var waiter = _useCompletionWaiters[index];

                if (!HasActiveUseAtOrBefore(waiter.Sequence))
                {
                    _useCompletionWaiters.RemoveAt(index);
                    _ = waiter.Completion.TrySetResult();
                }
            }
        }

        if ((activeUseCount == 0) && IsRetirementRequested)
        {
            _device.NotifyResourceUseReleased();
        }
    }

    internal sealed class UseLease : IDisposable
    {
        private GraphicsResourceOwner? _owner;
        private readonly long _sequence;

        internal UseLease(GraphicsResourceOwner owner, long sequence)
        {
            ArgumentNullException.ThrowIfNull(owner);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
            _owner = owner;
            _sequence = sequence;
        }

        internal long Sequence => _sequence;

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.ReleaseUse(_sequence);
        }
    }

    private sealed class UseCompletionWaiter
    {
        internal UseCompletionWaiter(long sequence)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
            Sequence = sequence;
            Completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        internal TaskCompletionSource Completion { get; }

        internal long Sequence { get; }

        internal Task Task => Completion.Task;
    }
}
