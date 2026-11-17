// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TerraFX.Graphics;

internal sealed class GraphicsResourceOwner : IDisposable
{
    private int _activeUseCount;
    private readonly SortedSet<UseLease> _activeUseSequences = [];
    private readonly GraphicsResourceAllocationInfo? _allocationInfo;
    private GraphicsResourceAllocationLease? _allocationLease;
    private readonly IGraphicsBackendResource _backendResource;
    private readonly GraphicsDevice _device;
    private bool _exclusiveUseInProgress;
    private readonly GraphicsResourceGraphState? _graphState;
    private long _lastSubmittedQueueFence;
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
        TextureDescription? textureDescription = null,
        GraphicsResourceAllocationLease? allocationLease = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(backendResource);
        ArgumentNullException.ThrowIfNull(retirementReservation);

        if ((bufferDescription is not null) && (textureDescription is not null))
        {
            throw new ArgumentException("A Graphics resource cannot be both a buffer and a texture.");
        }

        if ((allocationInfo is not null) && (allocationLease is not null))
        {
            throw new ArgumentException("A Graphics resource cannot own both an independent and shared allocation.", nameof(allocationLease));
        }

        if ((allocationLease is not null)
            && (((bufferDescription is not null)
                    && (allocationLease.BackendAllocation is not IGraphicsBackendBufferAllocation))
                || ((textureDescription is not null)
                    && (allocationLease.BackendAllocation is not IGraphicsBackendTextureAllocation))))
        {
            throw new ArgumentException("The shared allocation type must match the Graphics resource.", nameof(allocationLease));
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
        allocationLease?.AddReference();
        _allocationLease = allocationLease;
        _retirementReservation = retirementReservation;
    }

    internal GraphicsResourceAllocationInfo? AllocationInfo => _allocationInfo;

    internal GraphicsResourceAllocationLease? AllocationLease => Volatile.Read(ref _allocationLease);

    internal IGraphicsBackendResource BackendResource => _backendResource;

    internal GraphicsDevice Device => _device;

    internal uint DeviceEpoch => _device.DeviceEpoch;

    internal bool HasActiveUses => Volatile.Read(ref _activeUseCount) != 0;

    internal bool IsLiveForNewWork => Volatile.Read(ref _retirementState) == 0;

    internal bool IsRetirementRequested => Volatile.Read(ref _retirementState) == 1;

    internal long LastSubmittedQueueFence => Volatile.Read(ref _lastSubmittedQueueFence);

    internal uint OwnerId => _device.OwnerId;

    internal GraphicsOwnerRequestQueue.GraphicsRetirementReservation RetirementReservation => _retirementReservation;

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _retirementState, 1, 0) == 0)
        {
            _device.EnqueueResourceRetirement(this);
        }
    }

    internal UseLease AcquireExclusiveUse(string parameterName)
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

                if (_activeUseSequences.Count != 0)
                {
                    throw new InvalidOperationException("A host write requires an idle Graphics resource.");
                }

                var sequence = checked(_nextUseSequence + 1);
                var lease = new UseLease(this, sequence, isExclusive: true);

                if (!_activeUseSequences.Add(lease))
                {
                    throw new InvalidOperationException("The Graphics resource use sequence was reused.");
                }

                _nextUseSequence = sequence;
                _exclusiveUseInProgress = true;
                _ = Interlocked.Increment(ref _activeUseCount);
                return lease;
            }
        }
    }

    internal UseLease AcquireUse(string parameterName, GraphicsRecordingUseAdmission? recordingAdmission = null)
    {
        if (recordingAdmission is not null)
        {
            ObjectDisposedException.ThrowIf(!IsLiveForNewWork, parameterName);

            if (!_useGate.TryEnter())
            {
                GraphicsRecordingUseAdmission.ThrowUnavailable();
            }

            try
            {
                return AcquireUseUnderGate(parameterName, recordingAdmission);
            }
            finally
            {
                _useGate.Exit();
            }
        }

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

                return AcquireUseUnderGate(parameterName, recordingAdmission: null);
            }
        }
    }

    internal (GraphResourceState State, bool ContentsValid, long LastUseSequence, bool IsIdle) CaptureGraphImportState(
        GraphBufferRange range,
        UseLease? useLease = null)
    {
        lock (_useGate)
        {
            ObjectDisposedException.ThrowIf(!CanCaptureGraphState(useLease), typeof(GpuBuffer));
            (var state, var contentsValid) = (_graphState
                ?? throw new InvalidOperationException("The Graphics resource does not support graph import."))
                .GetBufferState(range);
            return (state, contentsValid, _nextUseSequence, _activeUseSequences.Count == 0);
        }
    }

    internal (GraphResourceState State, bool ContentsValid, long LastUseSequence, bool IsIdle) CaptureGraphImportState(
        GraphTextureRange range,
        UseLease? useLease = null)
    {
        lock (_useGate)
        {
            ObjectDisposedException.ThrowIf(!CanCaptureGraphState(useLease), typeof(GpuTexture));
            (var state, var contentsValid) = (_graphState
                ?? throw new InvalidOperationException("The Graphics resource does not support graph import."))
                .GetTextureState(range);
            return (state, contentsValid, _nextUseSequence, _activeUseSequences.Count == 0);
        }
    }

    internal (bool StateMatches, bool ContentsMatch, long LastUseSequence, bool IsIdle) EvaluateGraphImportOptions(
        GraphBufferRange range,
        GraphImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        (var actualState, var contentsValid, var lastUseSequence, var isIdle) = CaptureGraphImportState(range);
        return (
            options.InitialState == actualState,
            options.ContentsValid == contentsValid,
            lastUseSequence,
            isIdle);
    }

    internal (bool StateMatches, bool ContentsMatch, long LastUseSequence, bool IsIdle) EvaluateGraphImportOptions(
        GraphTextureRange range,
        GraphImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        (var actualState, var contentsValid, var lastUseSequence, var isIdle) = CaptureGraphImportState(range);
        return (
            options.InitialState == actualState,
            options.ContentsValid == contentsValid,
            lastUseSequence,
            isIdle);
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

    internal void RecordGraphAccess(GraphResourceAccess access)
    {
        var graphState = _graphState
            ?? throw new InvalidOperationException("The Graphics resource does not support graph access state.");

        if (access.IsTexture)
        {
            graphState.RecordTextureGraphAccess(
                access.TextureRange,
                access.RequiredState,
                access.IsWrite,
                access.ContentsValid);
        }
        else
        {
            graphState.RecordBufferGraphAccess(
                access.BufferRange,
                access.RequiredState,
                access.IsWrite,
                access.ContentsValid);
        }
    }

    internal void RecordSubmittedQueueFence(long fenceValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fenceValue);

        lock (_useGate)
        {
            var lastSubmittedFence = _lastSubmittedQueueFence;

            if (fenceValue < lastSubmittedFence)
            {
                throw new InvalidOperationException("A Graphics resource received an out-of-order queue fence.");
            }

            Volatile.Write(ref _lastSubmittedQueueFence, fenceValue);
        }
    }

    internal void RecordTextureReadback(TextureSubresource subresource)
        => (_graphState
            ?? throw new InvalidOperationException("The Graphics resource is not a texture."))
            .RecordTextureReadback(subresource);

    internal void RecordTextureUpload(TextureSubresource subresource)
        => (_graphState
            ?? throw new InvalidOperationException("The Graphics resource is not a texture."))
            .RecordTextureUpload(subresource);

    internal GraphicsResourceAllocationInfo? ReleaseAllocationLease()
    {
        var allocationLease = Volatile.Read(ref _allocationLease);

        if (allocationLease is null)
        {
            return null;
        }

        var allocationInfo = allocationLease.Release();
        _ = Interlocked.CompareExchange(ref _allocationLease, null, allocationLease);
        return (allocationInfo.SizeBytes == 0) ? null : allocationInfo;
    }

    internal void RequestRetirementForShutdown()
        => _ = Interlocked.CompareExchange(ref _retirementState, 1, 0);

    internal bool TryBeginRetirement(long completedQueueFence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(completedQueueFence);

        lock (_useGate)
        {
            if (HasActiveUses || (LastSubmittedQueueFence > completedQueueFence))
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

    private UseLease AcquireUseUnderGate(string parameterName, GraphicsRecordingUseAdmission? recordingAdmission)
    {
        ObjectDisposedException.ThrowIf(!IsLiveForNewWork, parameterName);

        if (_exclusiveUseInProgress)
        {
            throw new InvalidOperationException("The Graphics resource is being written by the host.");
        }

        var sequence = checked(_nextUseSequence + 1);
        recordingAdmission?.ClaimUse();
        var lease = new UseLease(this, sequence, recordingAdmission: recordingAdmission);

        if (!_activeUseSequences.Add(lease))
        {
            throw new InvalidOperationException("The Graphics resource use sequence was reused.");
        }

        _nextUseSequence = sequence;
        _ = Interlocked.Increment(ref _activeUseCount);
        return lease;
    }

    private bool CanCaptureGraphState(UseLease? useLease)
        => (useLease is null)
            ? IsLiveForNewWork
            : useLease.IsOwnedBy(this) && _activeUseSequences.Contains(useLease);

    private bool HasActiveUseAtOrBefore(long sequence)
        => (_activeUseSequences.Min is UseLease first) && (first.Sequence <= sequence);

    private void ReleaseUse(UseLease lease, bool isExclusive)
    {
        int activeUseCount;

        lock (_useGate)
        {
            if (!_activeUseSequences.Remove(lease))
            {
                throw new InvalidOperationException("A Graphics resource use credit was released more than once.");
            }

            if (isExclusive)
            {
                if (!_exclusiveUseInProgress)
                {
                    throw new InvalidOperationException("The Graphics resource exclusive use was released more than once.");
                }

                _exclusiveUseInProgress = false;
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

    internal sealed class UseLease : IDisposable, IComparable<UseLease>
    {
        private readonly bool _isExclusive;
        private GraphicsResourceOwner? _owner;
        private readonly GraphicsRecordingUseAdmission? _recordingAdmission;
        private readonly long _sequence;

        internal UseLease(
            GraphicsResourceOwner owner,
            long sequence,
            bool isExclusive = false,
            GraphicsRecordingUseAdmission? recordingAdmission = null)
        {
            ArgumentNullException.ThrowIfNull(owner);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
            _owner = owner;
            _isExclusive = isExclusive;
            _recordingAdmission = recordingAdmission;
            _sequence = sequence;
        }

        internal long Sequence => _sequence;

        public int CompareTo(UseLease? other)
            => (other is null) ? 1 : _sequence.CompareTo(other._sequence);

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.ReleaseUse(this, _isExclusive);
        }

        internal bool IsCoveredBy(GraphicsRecordingUseAdmission admission)
            => ReferenceEquals(_recordingAdmission, admission);

        internal bool IsOwnedBy(GraphicsResourceOwner owner)
            => ReferenceEquals(Volatile.Read(ref _owner), owner);

        internal void RecordSubmittedQueueFence(long fenceValue)
        {
            var owner = Volatile.Read(ref _owner)
                ?? throw new ObjectDisposedException(nameof(UseLease));
            owner.RecordSubmittedQueueFence(fenceValue);
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
