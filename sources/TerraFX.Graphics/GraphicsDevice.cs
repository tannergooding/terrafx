// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace TerraFX.Graphics;

/// <summary>Owns one host-created native graphics device on its dedicated owner thread.</summary>
[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The event is released by the dedicated graphics owner thread after shutdown; callers control lifetime through ShutdownAsync.")]
public sealed class GraphicsDevice
{
    private const long DefaultOwnedAllocationBudgetBytes = 512L * 1024 * 1024;

    // Keep an owner rooted if native disposal cannot confirm that its resources were released.
    private static readonly ConcurrentBag<IGraphicsBackendBuffer> s_quarantinedBackendBuffers = [];
    private static readonly ConcurrentBag<IGraphicsBackendDevice> s_quarantinedBackendDevices = [];
    private static readonly ConcurrentBag<GpuBuffer> s_quarantinedGpuBuffers = [];
    private static readonly ConcurrentBag<GraphicsDevice> s_quarantinedGraphicsDevices = [];

    private long _allocatedResourceBytes;
    private readonly Dictionary<int, long> _allocatedBytesByHeap = [];
    private IGraphicsBackendDevice? _backendDevice;
    private readonly HashSet<GpuBuffer> _buffers = [];
    private readonly GraphicsCapabilities _capabilities;
    private bool _hasQuarantinedBuffers;
    private ulong _nextBufferIndex;
    private readonly uint _ownerId;
    private readonly GraphicsOwnerRequestQueue _ownerQueue = new();
    private readonly Thread _ownerThread;
    private Exception? _retirementFailure;
    private readonly TaskCompletionSource _shutdownCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _shutdownTask;
    private int _status;

    private GraphicsDevice(uint ownerId, IGraphicsBackendDevice backendDevice)
    {
        _ownerId = ownerId;
        _backendDevice = backendDevice;
        _capabilities = backendDevice.Capabilities;
        _ownerThread = Thread.CurrentThread;
        DeviceEpoch = 1;
        _status = (int)GraphicsDeviceStatus.Ready;
        _shutdownTask = WaitForShutdownAndOwnerExitAsync(_shutdownCompletion.Task, _ownerThread);
    }

    /// <summary>Gets the immutable capabilities reported by the selected backend.</summary>
    public GraphicsCapabilities Capabilities => _capabilities;

    /// <summary>Gets the device epoch, initially one for this host-issued owner.</summary>
    public uint DeviceEpoch { get; }

    /// <summary>Gets the current lifecycle state.</summary>
    public GraphicsDeviceStatus Status
        => (GraphicsDeviceStatus)Volatile.Read(ref _status);

    /// <summary>Gets the host-issued owner identity used by device resource handles.</summary>
    internal uint OwnerId => _ownerId;

    internal static Task<GraphicsDevice> CreateAsync(
        uint ownerId,
        GraphicsBackendRegistration registration,
        GraphicsDeviceProfile profile,
        GraphicsPlatformKind platformKind,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfZero(ownerId);
        ArgumentNullException.ThrowIfNull(registration);
        ValidateStartupProfile(registration.Backend, profile, platformKind);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<GraphicsDevice>(cancellationToken);
        }

        return new GraphicsDeviceStartup(
            ownerId,
            registration,
            profile,
            platformKind,
            cancellationToken).Start();
    }

    internal static void ValidateStartupProfile(
        GraphicsBackend backend,
        GraphicsDeviceProfile profile,
        GraphicsPlatformKind platformKind)
    {
        if ((backend == GraphicsBackend.None) || !Enum.IsDefined(backend))
        {
            throw new ArgumentOutOfRangeException(nameof(backend));
        }

        if ((profile == GraphicsDeviceProfile.None) || !Enum.IsDefined(profile))
        {
            throw new ArgumentOutOfRangeException(nameof(profile));
        }

        if (!Enum.IsDefined(platformKind))
        {
            throw new ArgumentOutOfRangeException(nameof(platformKind));
        }

        if (profile == GraphicsDeviceProfile.OffscreenNative)
        {
            if (platformKind != GraphicsPlatformKind.None)
            {
                throw new ArgumentException("OffscreenNative device creation must not select a native window platform.", nameof(platformKind));
            }

            return;
        }

        if (platformKind == GraphicsPlatformKind.None)
        {
            throw new ArgumentException("Interactive device creation requires an initialized native window platform.", nameof(platformKind));
        }

        if ((backend == GraphicsBackend.D3D12) && (platformKind != GraphicsPlatformKind.Win32))
        {
            throw new PlatformNotSupportedException("Interactive D3D12 device creation requires Win32.");
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "A work-item failure is delivered through that operation's task without terminating the dedicated owner loop.")]
    private static void ExecuteOwnerWork(IGraphicsDeviceWork work, GraphicsDevice device)
    {
        try
        {
            work.Execute(device);
        }
        catch (Exception exception)
        {
            work.Fail(exception);
        }
    }

    private static async Task WaitForShutdownAndOwnerExitAsync(Task shutdownTask, Thread ownerThread)
    {
        try
        {
            await shutdownTask.ConfigureAwait(false);
        }
        finally
        {
            await Task.Run(ownerThread.Join).ConfigureAwait(false);
        }
    }

    /// <summary>Creates one owned GPU buffer on the dedicated graphics owner.</summary>
    /// <param name="description">The validated buffer size, usage, memory kind, and optional stride.</param>
    /// <param name="cancellationToken">A token that cancels admission or observation before completion.</param>
    /// <returns>The owned buffer after native allocation succeeds.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="description"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The buffer exceeds the selected device's maximum size.</exception>
    /// <exception cref="InvalidOperationException">The device is not accepting resource requests.</exception>
    public async Task<GpuBuffer> CreateBufferAsync(BufferDescription description, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(description);
        cancellationToken.ThrowIfCancellationRequested();

        if (Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("The GraphicsDevice is not accepting resource requests.");
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(description.ByteLength, Capabilities.Limits.MaximumBufferByteLength);

        var reservation = await _ownerQueue.ReserveRetirementAsync(cancellationToken).ConfigureAwait(false);
        var request = new CreateBufferRequest(this, description, reservation, cancellationToken);

        try
        {
            await _ownerQueue.WriteRequestAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            request.AdmissionFailed();
            throw;
        }

        return await request.Task.ConfigureAwait(false);
    }

    internal void EnqueueBufferRetirement(GpuBuffer buffer)
    {
        _ = _ownerQueue.TryEnqueueRetirement(
            new RetireBufferRequest(this, buffer),
            buffer.RetirementReservation);
    }

    internal Task ShutdownAsync()
    {
        while (true)
        {
            var currentStatus = Volatile.Read(ref _status);

            if (currentStatus is (int)GraphicsDeviceStatus.Stopping or (int)GraphicsDeviceStatus.Disposed)
            {
                return _shutdownTask;
            }

            if ((currentStatus == (int)GraphicsDeviceStatus.None)
                || !Enum.IsDefined((GraphicsDeviceStatus)currentStatus))
            {
                throw new InvalidOperationException("The GraphicsDevice has an invalid lifecycle state.");
            }

            if (Interlocked.CompareExchange(ref _status, (int)GraphicsDeviceStatus.Stopping, currentStatus) == currentStatus)
            {
                _ownerQueue.CompleteRequests();
                return _shutdownTask;
            }
        }
    }

    private GpuBuffer CreateBufferOnOwner(
        BufferDescription description,
        GraphicsOwnerRequestQueue.GraphicsRetirementReservation retirementReservation)
    {
        var backendDevice = _backendDevice
            ?? throw new InvalidOperationException("The GraphicsDevice has no live backend owner.");

        if (Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("The GraphicsDevice stopped before the buffer request was executed.");
        }

        var allocationInfo = backendDevice.GetBufferAllocationInfo(description);
        ReserveResourceAllocation(description.MemoryKind, allocationInfo);

        IGraphicsBackendBuffer? backendBuffer = null;

        try
        {
            backendBuffer = backendDevice.CreateBuffer(description);
            var handle = new GpuBufferHandle(_ownerId, DeviceEpoch, GetNextBufferIndex(), generation: 1);
            var buffer = new GpuBuffer(
                this,
                handle,
                description.ByteLength,
                DeviceEpoch,
                allocationInfo,
                backendBuffer,
                retirementReservation);

            if (!_buffers.Add(buffer))
            {
                throw new InvalidOperationException("The newly created Graphics buffer already exists in its owner registry.");
            }

            backendBuffer = null;
            return buffer;
        }
        catch (Exception exception)
        {
            if (backendBuffer is not null)
            {
                try
                {
                    backendBuffer.Dispose();
                }
                catch (Exception cleanupException)
                {
                    QuarantineBackendBuffer(backendBuffer, new AggregateException(exception, cleanupException));
                    throw new AggregateException(exception, cleanupException);
                }
            }

            ReleaseResourceAllocation(allocationInfo);
            throw;
        }
    }

    private uint GetNextBufferIndex()
    {
        if (_nextBufferIndex > uint.MaxValue)
        {
            throw new InvalidOperationException("The GraphicsDevice exhausted its buffer handle slots.");
        }

        return (uint)_nextBufferIndex++;
    }

    private void MarkDeviceLost()
    {
        while (true)
        {
            var currentStatus = Volatile.Read(ref _status);

            if (currentStatus != (int)GraphicsDeviceStatus.Ready)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _status, (int)GraphicsDeviceStatus.Lost, currentStatus) == currentStatus)
            {
                _ownerQueue.CompleteRequests();
                _ownerQueue.CompleteRetirementReservations();
                return;
            }
        }
    }

    private void QuarantineBackendBuffer(IGraphicsBackendBuffer backendBuffer, Exception exception)
    {
        s_quarantinedBackendBuffers.Add(backendBuffer);
        s_quarantinedGraphicsDevices.Add(this);
        _hasQuarantinedBuffers = true;
        _retirementFailure ??= exception;
    }

    private void QuarantineBuffer(GpuBuffer buffer, Exception exception)
    {
        buffer.MarkQuarantined();
        s_quarantinedGpuBuffers.Add(buffer);
        s_quarantinedGraphicsDevices.Add(this);
        _hasQuarantinedBuffers = true;
        _retirementFailure ??= exception;
        buffer.RetirementReservation.Dispose();
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Shutdown failure handling must continue quarantining other accepted work and resources after an individual cleanup failure.")]
    private Exception QuarantineOwnerState(Exception exception)
    {
        _ownerQueue.CompleteRequests();
        _ownerQueue.CompleteRetirements();

        while (_ownerQueue.TryReadRequest(out var request))
        {
            try
            {
                request.Fail(exception);
            }
            catch (Exception failureException)
            {
                exception = new AggregateException(exception, failureException);
            }
        }

        while (_ownerQueue.TryReadRetirement(out var retirement))
        {
            try
            {
                retirement.Fail(exception);
            }
            catch (Exception failureException)
            {
                exception = new AggregateException(exception, failureException);
            }
        }

        var activeBuffers = new GpuBuffer[_buffers.Count];
        _buffers.CopyTo(activeBuffers);

        foreach (var buffer in activeBuffers)
        {
            try
            {
                QuarantineBuffer(buffer, exception);
            }
            catch (Exception failureException)
            {
                exception = new AggregateException(exception, failureException);
            }
        }

        s_quarantinedGraphicsDevices.Add(this);
        return exception;
    }

    private void ReleaseResourceAllocation(GraphicsResourceAllocationInfo allocationInfo)
    {
        if (!_allocatedBytesByHeap.TryGetValue(allocationInfo.HeapIndex, out var heapBytes)
            || (heapBytes < allocationInfo.SizeBytes))
        {
            throw new InvalidOperationException("The Graphics buffer allocation budget was released inconsistently.");
        }

        var remainingTotalBytes = checked(_allocatedResourceBytes - allocationInfo.SizeBytes);
        var remainingBytes = heapBytes - allocationInfo.SizeBytes;

        if (remainingBytes == 0)
        {
            _ = _allocatedBytesByHeap.Remove(allocationInfo.HeapIndex);
        }
        else
        {
            _allocatedBytesByHeap[allocationInfo.HeapIndex] = remainingBytes;
        }

        _allocatedResourceBytes = remainingTotalBytes;
    }

    [SuppressMessage(
        "Usage",
        "CA2201:Do not raise reserved exception types",
        Justification = "The Graphics allocation contract uses contextual OutOfMemoryException for exhausted resource budgets.")]
    private void ReserveResourceAllocation(GpuMemoryKind memoryKind, GraphicsResourceAllocationInfo allocationInfo)
    {
        if (allocationInfo.SizeBytes > (DefaultOwnedAllocationBudgetBytes - _allocatedResourceBytes))
        {
            throw new OutOfMemoryException("The GraphicsDevice's persistent allocation budget is exhausted.");
        }

        GraphicsMemoryHeapCapabilities? selectedHeap = null;

        foreach (var heap in Capabilities.MemoryHeaps)
        {
            if (heap.Index == allocationInfo.HeapIndex)
            {
                selectedHeap = heap;
                break;
            }
        }

        if (selectedHeap is null)
        {
            throw new NotSupportedException($"The backend selected unreported memory heap {allocationInfo.HeapIndex}.");
        }

        var requiredHeapProperties = memoryKind == GpuMemoryKind.DeviceLocal
            ? GraphicsMemoryHeapProperties.DeviceLocal
            : GraphicsMemoryHeapProperties.HostVisible;

        if ((selectedHeap.Properties & requiredHeapProperties) != requiredHeapProperties)
        {
            throw new NotSupportedException(
                $"The backend selected memory heap {allocationInfo.HeapIndex} without the required {requiredHeapProperties} property.");
        }

        var heapBudget = allocationInfo.BudgetBytes ?? selectedHeap.BudgetBytes ?? selectedHeap.SizeBytes;
        var allocatedOnHeap = _allocatedBytesByHeap.GetValueOrDefault(allocationInfo.HeapIndex);

        if (allocationInfo.SizeBytes > (heapBudget - allocatedOnHeap))
        {
            throw new OutOfMemoryException($"The GraphicsDevice's native memory heap {allocationInfo.HeapIndex} budget is exhausted.");
        }

        var totalBytes = checked(_allocatedResourceBytes + allocationInfo.SizeBytes);
        var heapBytes = checked(allocatedOnHeap + allocationInfo.SizeBytes);
        _allocatedBytesByHeap[allocationInfo.HeapIndex] = heapBytes;
        _allocatedResourceBytes = totalBytes;
    }

    private void RetireBufferOnOwner(GpuBuffer buffer)
    {
        if (!buffer.TryBeginRetirement())
        {
            return;
        }

        buffer.BackendBuffer.Dispose();

        if (!_buffers.Remove(buffer))
        {
            throw new InvalidOperationException("A retired Graphics buffer was not registered with its owner.");
        }

        ReleaseResourceAllocation(buffer.AllocationInfo);
        buffer.RetirementReservation.Dispose();
        buffer.MarkRetired();
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Every failed native release is quarantined and retained while shutdown proceeds for unrelated resources.")]
    private void RetireBuffersForShutdown()
    {
        var activeBuffers = new GpuBuffer[_buffers.Count];
        _buffers.CopyTo(activeBuffers);

        foreach (var buffer in activeBuffers)
        {
            try
            {
                RetireBufferOnOwner(buffer);
            }
            catch (Exception exception)
            {
                QuarantineBuffer(buffer, exception);
            }
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "The owner thread must relay initialization and native-release failures through its startup or shutdown task.")]
    private void RunOwnerLoop()
    {
        try
        {
            while (true)
            {
                var didWork = false;

                if (_ownerQueue.TryReadRetirement(out var retirement))
                {
                    ExecuteOwnerWork(retirement, this);
                    didWork = true;
                }

                if (_ownerQueue.TryReadRequest(out var request))
                {
                    ExecuteOwnerWork(request, this);
                    didWork = true;
                }

                if (_ownerQueue.RequestsCompleted && Status == GraphicsDeviceStatus.Stopping)
                {
                    _ownerQueue.CompleteRetirements();

                    while (_ownerQueue.TryReadRetirement(out retirement))
                    {
                        ExecuteOwnerWork(retirement, this);
                    }

                    RetireBuffersForShutdown();

                    if (_hasQuarantinedBuffers)
                    {
                        s_quarantinedGraphicsDevices.Add(this);
                        Volatile.Write(ref _status, (int)GraphicsDeviceStatus.Stopping);
                        _ = _shutdownCompletion.TrySetException(
                            _retirementFailure
                            ?? new InvalidOperationException("A Graphics resource could not be retired safely."));
                        return;
                    }

                    break;
                }

                if (!didWork)
                {
                    _ownerQueue.WaitForWork();
                }
            }

            var backendDevice = _backendDevice
                ?? throw new InvalidOperationException("A ready GraphicsDevice must own its backend device.");
            backendDevice.Dispose();
            _backendDevice = null;
            Volatile.Write(ref _status, (int)GraphicsDeviceStatus.Disposed);
            _ = _shutdownCompletion.TrySetResult();
        }
        catch (Exception exception)
        {
            exception = QuarantineOwnerState(exception);
            var backendDevice = _backendDevice;

            if (backendDevice is not null)
            {
                s_quarantinedBackendDevices.Add(backendDevice);
                _backendDevice = null;
            }

            Volatile.Write(ref _status, (int)GraphicsDeviceStatus.Stopping);
            _ = _shutdownCompletion.TrySetException(exception);
        }
        finally
        {
            _ownerQueue.Dispose();
        }
    }

    private sealed class CreateBufferRequest : IGraphicsDeviceWork
    {
        private readonly CancellationTokenRegistration _cancellationRegistration;
        private readonly CancellationToken _cancellationToken;
        private readonly TaskCompletionSource<GpuBuffer> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private GpuBuffer? _createdBuffer;
        private readonly BufferDescription _description;
        private readonly GraphicsDevice _device;
        private GraphicsOwnerRequestQueue.GraphicsRetirementReservation? _retirementReservation;
        // 0 is pending, 1 is canceled observation, and 2 is terminal.
        private int _state;

        internal CreateBufferRequest(
            GraphicsDevice device,
            BufferDescription description,
            GraphicsOwnerRequestQueue.GraphicsRetirementReservation retirementReservation,
            CancellationToken cancellationToken)
        {
            _description = description;
            _device = device;
            _retirementReservation = retirementReservation;
            _cancellationToken = cancellationToken;
            _cancellationRegistration = cancellationToken.Register(
                static state => ((CreateBufferRequest)state!).Cancel(),
                this);
        }

        internal Task<GpuBuffer> Task => _completion.Task;

        public void Execute(GraphicsDevice device)
        {
            if (Volatile.Read(ref _state) == 1)
            {
                ReleaseReservation();
                _cancellationRegistration.Dispose();
                return;
            }

            try
            {
                var reservation = _retirementReservation
                    ?? throw new InvalidOperationException("The buffer request has no retirement reservation.");
                var buffer = device.CreateBufferOnOwner(_description, reservation);
                _createdBuffer = buffer;
                _retirementReservation = null;

                if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
                {
                    _ = _completion.TrySetResult(buffer);
                }
                else
                {
                    device.RetireBufferOnOwner(buffer);
                }
            }
            finally
            {
                _cancellationRegistration.Dispose();
            }
        }

        public void Fail(Exception exception)
        {
            if (exception is GraphicsDeviceLostException)
            {
                _device.MarkDeviceLost();
            }

            if (_createdBuffer is not null)
            {
                _device.QuarantineBuffer(_createdBuffer, exception);
            }

            ReleaseReservation();

            if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
            {
                _ = _completion.TrySetException(exception);
            }

            _cancellationRegistration.Dispose();
        }

        internal void AdmissionFailed()
        {
            _ = Interlocked.Exchange(ref _state, 2);
            ReleaseReservation();
            _cancellationRegistration.Dispose();
        }

        private void Cancel()
        {
            if (Interlocked.CompareExchange(ref _state, 1, 0) == 0)
            {
                _ = _completion.TrySetCanceled(_cancellationToken);
            }
        }

        private void ReleaseReservation()
        {
            var reservation = Interlocked.Exchange(ref _retirementReservation, null);
            reservation?.Dispose();
        }
    }

    private sealed class GraphicsDeviceStartup
    {
        private readonly CancellationToken _cancellationToken;
        private readonly TaskCompletionSource<GraphicsDevice> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly uint _ownerId;
        private readonly GraphicsPlatformKind _platformKind;
        private readonly GraphicsDeviceProfile _profile;
        private readonly GraphicsBackendRegistration _registration;

        internal GraphicsDeviceStartup(
            uint ownerId,
            GraphicsBackendRegistration registration,
            GraphicsDeviceProfile profile,
            GraphicsPlatformKind platformKind,
            CancellationToken cancellationToken)
        {
            _ownerId = ownerId;
            _registration = registration;
            _profile = profile;
            _platformKind = platformKind;
            _cancellationToken = cancellationToken;
        }

        internal Task<GraphicsDevice> Start()
        {
            var ownerThread = new Thread(RunOwner) {
                IsBackground = true,
                Name = "TerraFX.Graphics.Owner",
            };
            ownerThread.Start();
            return _completion.Task;
        }

        [SuppressMessage(
            "Usage",
            "CA1031:Do not catch general exception types",
            Justification = "The startup task must receive factory, admission, cancellation, and unwind failures from the dedicated owner thread.")]
        private void RunOwner()
        {
            IGraphicsBackendDevice? backendDevice = null;

            try
            {
                _cancellationToken.ThrowIfCancellationRequested();
                backendDevice = _registration.Factory.Create(
                    _ownerId,
                    _profile,
                    _platformKind,
                    _cancellationToken);
                ArgumentNullException.ThrowIfNull(backendDevice);
                _cancellationToken.ThrowIfCancellationRequested();

                var capabilities = backendDevice.Capabilities;

                if (capabilities.Backend != _registration.Backend)
                {
                    throw new InvalidOperationException("The registered factory created a device for a different backend.");
                }

                var missingRequirements = GraphicsCapabilityAdmission.GetMissingRequirements(capabilities, _profile);

                if (!missingRequirements.IsEmpty)
                {
                    throw new NotSupportedException(
                        $"The selected adapter does not satisfy the {_profile} profile: {string.Join("; ", missingRequirements)}.");
                }

                _cancellationToken.ThrowIfCancellationRequested();

                var device = new GraphicsDevice(_ownerId, backendDevice);
                backendDevice = null;
                _completion.SetResult(device);
                device.RunOwnerLoop();
            }
            catch (Exception exception)
            {
                if (backendDevice is not null)
                {
                    try
                    {
                        backendDevice.Dispose();
                    }
                    catch (Exception cleanupException)
                    {
                        s_quarantinedBackendDevices.Add(backendDevice);
                        exception = new AggregateException(exception, cleanupException);
                    }
                }

                if (_cancellationToken.IsCancellationRequested && (exception is OperationCanceledException))
                {
                    _ = _completion.TrySetCanceled(_cancellationToken);
                }
                else
                {
                    _ = _completion.TrySetException(exception);
                }
            }
        }
    }

    private sealed class RetireBufferRequest : IGraphicsDeviceWork
    {
        private readonly GpuBuffer _buffer;
        private readonly GraphicsDevice _device;

        internal RetireBufferRequest(GraphicsDevice device, GpuBuffer buffer)
        {
            _buffer = buffer;
            _device = device;
        }

        public void Execute(GraphicsDevice device)
            => device.RetireBufferOnOwner(_buffer);

        public void Fail(Exception exception)
            => _device.QuarantineBuffer(_buffer, exception);
    }
}
