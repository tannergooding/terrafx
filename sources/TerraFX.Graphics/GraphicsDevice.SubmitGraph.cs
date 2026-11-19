// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace TerraFX.Graphics;

public sealed partial class GraphicsDevice
{
    private static GraphSubmissionRetentionAnchor? s_graphSubmissionRetentionRoot;
    private static readonly Lock s_graphSubmissionRetentionRootGate = new();
    private readonly GraphSubmissionRetentionAnchor _graphSubmissionRetentionAnchor;
    private long _nextGraphSubmissionSequence;

    private static void AddResourceUseLease(
        Dictionary<GraphicsResourceOwner, GraphicsResourceOwner.UseLease> useLeases,
        GraphicsResourceOwner resourceOwner,
        string parameterName)
    {
        var useLease = resourceOwner.AcquireUse(parameterName);

        try
        {
            useLeases.Add(resourceOwner, useLease);
        }
        catch
        {
            useLease.Dispose();
            throw;
        }
    }

    private static void RegisterGraphSubmissionRetentionAnchor(GraphSubmissionRetentionAnchor anchor)
    {
        ArgumentNullException.ThrowIfNull(anchor);

        lock (s_graphSubmissionRetentionRootGate)
        {
            if (anchor.IsRooted)
            {
                throw new InvalidOperationException("The graph-submission retention anchor is already rooted.");
            }

            anchor.Next = s_graphSubmissionRetentionRoot;
            s_graphSubmissionRetentionRoot = anchor;
            anchor.IsRooted = true;
        }
    }

    private static void ReleaseUseLeases(List<GraphicsResourceOwner.UseLease> useLeases)
    {
        for (var index = useLeases.Count - 1; index >= 0; index--)
        {
            useLeases[index].Dispose();
        }

        useLeases.Clear();
    }

    internal async Task<GraphFrameSlotReservation> AcquireGraphFrameSlotAsync(CancellationToken cancellationToken)
    {
        _ = Interlocked.Increment(ref _activeGraphSubmissionCount);
        var frameSlotAcquired = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Status != GraphicsDeviceStatus.Ready)
            {
                throw new InvalidOperationException("The GraphicsDevice is not accepting graph submissions.");
            }

            await _graphFrameSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
            frameSlotAcquired = true;

            if (Status != GraphicsDeviceStatus.Ready)
            {
                throw new InvalidOperationException("The GraphicsDevice stopped before graph submission admission.");
            }

            return new GraphFrameSlotReservation(this, _graphFrameSlots);
        }
        catch
        {
            if (frameSlotAcquired)
            {
                _ = _graphFrameSlots.Release();
            }

            _ = Interlocked.Decrement(ref _activeGraphSubmissionCount);
            _ownerQueue.SignalOwnerWork();
            throw;
        }
    }

    internal GraphResourceUseLeaseSet AcquireGraphResourceUseLeases(
        IEnumerable<GraphicsResourceOwner> resourceOwners)
    {
        ArgumentNullException.ThrowIfNull(resourceOwners);

        if (Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("The GraphicsDevice is not accepting graph submissions.");
        }

        var useLeases = new Dictionary<GraphicsResourceOwner, GraphicsResourceOwner.UseLease>();

        try
        {
            foreach (var resourceOwner in resourceOwners)
            {
                ArgumentNullException.ThrowIfNull(resourceOwner);

                if (!ReferenceEquals(resourceOwner.Device, this)
                    || (resourceOwner.DeviceEpoch != DeviceEpoch))
                {
                    throw new ArgumentException("Every graph resource must belong to this GraphicsDevice generation.", nameof(resourceOwners));
                }

                if (!useLeases.ContainsKey(resourceOwner))
                {
                    AddResourceUseLease(useLeases, resourceOwner, nameof(resourceOwners));
                }
            }

            return new GraphResourceUseLeaseSet(this, useLeases);
        }
        catch
        {
            foreach (var useLease in useLeases.Values)
            {
                useLease.Dispose();
            }

            throw;
        }
    }

    internal async Task<GraphicsGraphSubmissionResult> SubmitGraphAsync(
        GraphicsGraphSubmission submission,
        GraphResourceUseLeaseSet resourceUseLeases,
        GraphFrameSlotReservation frameSlotReservation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(resourceUseLeases);
        ArgumentNullException.ThrowIfNull(frameSlotReservation);

        List<GraphicsResourceOwner.UseLease>? useLeases = null;
        var ownershipTransferred = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Status != GraphicsDeviceStatus.Ready)
            {
                throw new InvalidOperationException("The GraphicsDevice is not accepting graph submissions.");
            }

            if (!frameSlotReservation.IsOwnedBy(this))
            {
                throw new ArgumentException("The graph frame-slot reservation belongs to a different GraphicsDevice.", nameof(frameSlotReservation));
            }

            if (submission.DeviceEpoch != DeviceEpoch)
            {
                throw new InvalidOperationException("The graph submission belongs to a stale GraphicsDevice epoch.");
            }

            useLeases = resourceUseLeases.TransferToSubmission(submission);
            submission.ValidateNativeRasterSubmission();

            if (submission.RequiresDiagnosticQueueAdmission)
            {
                var backendDevice = _backendDevice
                    ?? throw new InvalidOperationException("The GraphicsDevice has no live backend owner.");
                submission.AttachBackendStorage(backendDevice.CreateGraphSubmissionStorage(submission));
            }

            var request = new SubmitGraphRequest(this, submission, useLeases, frameSlotReservation);

            if (submission.RequiresDiagnosticQueueAdmission)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!_ownerQueue.TryWriteDiagnosticRequest(request, GraphicsOwnerRequestQueue.KnownRequestBackingByteCharge))
                {
                    ThrowDiagnosticQueueUnavailable();
                }
            }
            else
            {
                await _ownerQueue.WriteRequestAsync(request, cancellationToken).ConfigureAwait(false);
            }

            ownershipTransferred = true;
            return await request.Task.ConfigureAwait(false);
        }
        finally
        {
            try
            {
                resourceUseLeases.Dispose();
            }
            finally
            {
                if (!ownershipTransferred)
                {
                    try
                    {
                        if (useLeases is not null)
                        {
                            ReleaseUseLeases(useLeases);
                        }

                        submission.DisposeRecordedPassCommandLists();
                    }
                    finally
                    {
                        try
                        {
                            submission.DisposeBackendStorage();
                        }
                        finally
                        {
                            frameSlotReservation.Dispose();
                        }
                    }
                }
            }
        }
    }

    private long GetNextGraphSubmissionSequence()
    {
        if (_nextGraphSubmissionSequence >= long.MaxValue)
        {
            throw new InvalidOperationException("The GraphicsDevice exhausted its graph-submission sequence space.");
        }

        return ++_nextGraphSubmissionSequence;
    }

    private void QuarantineGraphSubmission(
        GraphicsGraphSubmission submission,
        Exception exception)
    {
        submission.RetainRecordingForUncertainSubmission();

        foreach (var import in submission.BufferImports)
        {
            QuarantineResource(import.Buffer.ResourceOwner, exception);
        }

        foreach (var import in submission.TextureImports)
        {
            QuarantineResource(import.Texture.ResourceOwner, exception);
        }

        QuarantineResource(submission.OutputColorTarget.ResourceOwner, exception);

        if (submission.SurfaceTarget is Surface surface)
        {
            QuarantineSurface(surface, exception);
        }

        MarkDeviceLost(exception switch {
            GraphicsDeviceLostException deviceLostException => deviceLostException.NativeError,
            COMException nativeException => nativeException.ErrorCode,
            _ => null,
        });
    }

    private void ReleaseGraphSubmissionRetentionCell(SubmitGraphRequest request, int cellIndex)
    {
        var cells = _graphSubmissionRetentionAnchor.Cells;

        if ((cellIndex < 0)
            || (cellIndex >= cells.Length)
            || !ReferenceEquals(cells[cellIndex], request))
        {
            throw new InvalidOperationException("The graph-submission retention cell does not belong to this request.");
        }

        cells[cellIndex] = null;
    }

    private bool TryClaimGraphSubmissionRetentionCell(SubmitGraphRequest request, out int cellIndex)
    {
        var cells = _graphSubmissionRetentionAnchor.Cells;

        for (var index = 0; index < cells.Length; index++)
        {
            if (cells[index] is not null)
            {
                continue;
            }

            cells[index] = request;
            cellIndex = index;
            return true;
        }

        cellIndex = -1;
        return false;
    }

    private void UnregisterGraphSubmissionRetentionAnchor()
    {
        lock (s_graphSubmissionRetentionRootGate)
        {
            var anchor = _graphSubmissionRetentionAnchor;

            if (!ReferenceEquals(anchor.Owner, this))
            {
                throw new InvalidOperationException("The graph-submission retention anchor belongs to another device.");
            }

            foreach (var request in anchor.Cells)
            {
                if (request is not null)
                {
                    throw new InvalidOperationException("The GraphicsDevice still owns retained graph-submission cells.");
                }
            }

            GraphSubmissionRetentionAnchor? previous = null;
            var current = s_graphSubmissionRetentionRoot;

            while ((current is not null) && !ReferenceEquals(current, anchor))
            {
                previous = current;
                current = current.Next;
            }

            if ((current is null) || !anchor.IsRooted)
            {
                throw new InvalidOperationException("The graph-submission retention anchor is not rooted.");
            }

            if (previous is null)
            {
                s_graphSubmissionRetentionRoot = anchor.Next;
            }
            else
            {
                previous.Next = anchor.Next;
            }

            anchor.Next = null;
            anchor.IsRooted = false;
        }
    }

    internal sealed class GraphFrameSlotReservation : IDisposable
    {
        private readonly GraphicsDevice _device;
        private SemaphoreSlim? _frameSlots;
        private int _requestTransferState;

        internal GraphFrameSlotReservation(GraphicsDevice device, SemaphoreSlim frameSlots)
        {
            ArgumentNullException.ThrowIfNull(device);
            ArgumentNullException.ThrowIfNull(frameSlots);
            _device = device;
            _frameSlots = frameSlots;
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _requestTransferState, 2, 0) == 0)
            {
                ReleaseFrameSlot();
            }
        }

        internal bool IsOwnedBy(GraphicsDevice device)
            => ReferenceEquals(_device, device) && (Volatile.Read(ref _frameSlots) is not null);

        internal void ReleaseFromRequest()
        {
            var previousState = Interlocked.CompareExchange(ref _requestTransferState, 2, 1);

            if (previousState is 1 or 2)
            {
                if (previousState == 1)
                {
                    ReleaseFrameSlot();
                }

                return;
            }

            throw new InvalidOperationException("The graph frame-slot reservation was not transferred to a request.");
        }

        internal void RollbackRequestTransfer()
        {
            if (Interlocked.CompareExchange(ref _requestTransferState, 0, 1) != 1)
            {
                throw new InvalidOperationException("The graph frame-slot request transfer cannot be rolled back.");
            }
        }

        internal bool TryTransferToRequest()
            => Interlocked.CompareExchange(ref _requestTransferState, 1, 0) == 0;

        private void ReleaseFrameSlot()
        {
            var frameSlots = Interlocked.Exchange(ref _frameSlots, null);

            if (frameSlots is not null)
            {
                _ = frameSlots.Release();
                _ = Interlocked.Decrement(ref _device._activeGraphSubmissionCount);
                _device._ownerQueue.SignalOwnerWork();
            }
        }
    }

    internal sealed class GraphResourceUseLeaseSet : IDisposable
    {
        private readonly GraphicsDevice _device;
        private Dictionary<GraphicsResourceOwner, GraphicsResourceOwner.UseLease>? _useLeases;

        internal GraphResourceUseLeaseSet(
            GraphicsDevice device,
            Dictionary<GraphicsResourceOwner, GraphicsResourceOwner.UseLease> useLeases)
        {
            ArgumentNullException.ThrowIfNull(device);
            ArgumentNullException.ThrowIfNull(useLeases);
            _device = device;
            _useLeases = useLeases;
        }

        public void Dispose()
        {
            var useLeases = _useLeases;
            _useLeases = null;

            if (useLeases is not null)
            {
                foreach (var useLease in useLeases.Values)
                {
                    useLease.Dispose();
                }

                useLeases.Clear();
            }
        }

        internal List<GraphicsResourceOwner.UseLease> TransferToSubmission(GraphicsGraphSubmission submission)
        {
            ArgumentNullException.ThrowIfNull(submission);
            var useLeases = _useLeases
                ?? throw new ObjectDisposedException(nameof(GraphResourceUseLeaseSet));
            var resourceOwners = new HashSet<GraphicsResourceOwner>();
            _ = resourceOwners.Add(submission.OutputColorTarget.ResourceOwner);

            foreach (var import in submission.BufferImports)
            {
                _ = resourceOwners.Add(import.Buffer.ResourceOwner);
            }

            foreach (var import in submission.TextureImports)
            {
                _ = resourceOwners.Add(import.Texture.ResourceOwner);
            }

            foreach (var resourceOwner in resourceOwners)
            {
                if (!ReferenceEquals(resourceOwner.Device, _device)
                    || (resourceOwner.DeviceEpoch != submission.DeviceEpoch))
                {
                    throw new ArgumentException(
                        "Every graph resource must belong to this GraphicsDevice generation.",
                        nameof(submission));
                }

                if (!useLeases.ContainsKey(resourceOwner))
                {
                    AddResourceUseLease(useLeases, resourceOwner, nameof(submission));
                }
            }

            if (useLeases.Count != resourceOwners.Count)
            {
                throw new ArgumentException(
                    "The retained graph resources do not match the submission.",
                    nameof(submission));
            }

            var transferredUseLeases = new List<GraphicsResourceOwner.UseLease>(useLeases.Values);
            _useLeases = null;
            return transferredUseLeases;
        }
    }

    private sealed class GraphSubmissionRetentionAnchor
    {
        private readonly SubmitGraphRequest?[] _cells;
        private readonly GraphicsDevice _device;
        private bool _isRooted;
        private GraphSubmissionRetentionAnchor? _next;

        internal GraphSubmissionRetentionAnchor(GraphicsDevice device, int cellCount)
        {
            ArgumentNullException.ThrowIfNull(device);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellCount);
            _cells = new SubmitGraphRequest[cellCount];
            _device = device;
        }

        internal SubmitGraphRequest?[] Cells => _cells;

        internal bool IsRooted
        {
            get
            {
                return _isRooted;
            }
            set
            {
                _isRooted = value;
            }
        }

        internal GraphSubmissionRetentionAnchor? Next
        {
            get
            {
                return _next;
            }
            set
            {
                _next = value;
            }
        }

        internal GraphicsDevice Owner => _device;
    }

    private sealed class SubmitGraphRequest : IGraphicsDeviceWork, IGraphicsGraphRetentionCellWork
    {
        private readonly TaskCompletionSource<GraphicsGraphSubmissionResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly GraphicsDevice _device;
        private Task? _fenceWaitSignalTask;
        private Task? _fenceWaitTask;
        private int _frameSlotReleased;
        private readonly GraphFrameSlotReservation _frameSlotReservation;
        private int _nativeSubmissionStarted;
        private int _ownsFrameSlotReservation;
        private int _retentionCellIndex = -1;
        private bool _retentionCellQuarantined;
        private readonly GraphicsGraphSubmission _submission;
        private long _submissionSequence;
        private long _submittedFence;
        private readonly List<GraphicsResourceOwner.UseLease> _useLeases;
        private int _workReleased;

        internal SubmitGraphRequest(
            GraphicsDevice device,
            GraphicsGraphSubmission submission,
            List<GraphicsResourceOwner.UseLease> useLeases,
            GraphFrameSlotReservation frameSlotReservation)
        {
            ArgumentNullException.ThrowIfNull(device);
            ArgumentNullException.ThrowIfNull(submission);
            ArgumentNullException.ThrowIfNull(useLeases);
            ArgumentNullException.ThrowIfNull(frameSlotReservation);

            _device = device;
            _submission = submission;
            _useLeases = useLeases;
            _frameSlotReservation = frameSlotReservation;
        }

        internal bool IsFenceWaitCompleted
            => _fenceWaitTask?.IsCompleted == true;

        internal long SubmittedFence => Volatile.Read(ref _submittedFence);

        internal Task<GraphicsGraphSubmissionResult> Task => _completion.Task;

        public void Execute(GraphicsDevice device)
        {
            if (!ReferenceEquals(device, _device))
            {
                throw new InvalidOperationException("The graph submission was delivered to a different GraphicsDevice.");
            }

            if (device.Status != GraphicsDeviceStatus.Ready)
            {
                throw new InvalidOperationException("The GraphicsDevice stopped before graph submission.");
            }

            var backendDevice = device._backendDevice
                ?? throw new InvalidOperationException("The GraphicsDevice has no live backend owner.");

            if (_submission.SurfaceTarget is Surface surface)
            {
                ObjectDisposedException.ThrowIf(surface.IsRetired || surface.IsQuarantined, surface);
            }

            var submissionSequence = device.GetNextGraphSubmissionSequence();
            Volatile.Write(ref _submissionSequence, submissionSequence);
            Volatile.Write(ref _nativeSubmissionStarted, 1);
            var submittedFence = backendDevice.SubmitGraph(_submission);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(submittedFence);
            Volatile.Write(ref _submittedFence, submittedFence);

            if ((_submission.SurfaceTarget is not null) != _submission.PresentationSequence.HasValue)
            {
                throw new InvalidOperationException("The backend presentation result does not match the graph output target.");
            }

            foreach (var useLease in _useLeases)
            {
                useLease.RecordSubmittedQueueFence(submittedFence);
            }

            _submission.RecordSubmittedQueueFence(submittedFence);
            _submission.RecordSubmittedOwnerStates();
            device.AddPendingGraphSubmission(this, backendDevice);
        }

        public void Fail(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);

            if ((SubmittedFence > 0)
                || ((Volatile.Read(ref _nativeSubmissionStarted) != 0)
                    && (_device._backendDevice?.HasUncertainSubmission == true)))
            {
                ((IGraphicsGraphRetentionCellWork)this).MarkRetentionCellQuarantined();
                _device.QuarantineGraphSubmission(_submission, exception);
                ReleaseFrameSlot();
            }
            else
            {
                ReleaseWork();
            }

            _ = _completion.TrySetException(exception);
        }

        internal void Complete()
        {
            var submittedFence = SubmittedFence;

            if (submittedFence <= 0)
            {
                throw new InvalidOperationException("A completed graph submission has no submitted fence.");
            }

            ReleaseWork();
            _ = _completion.TrySetResult(new GraphicsGraphSubmissionResult(
                Volatile.Read(ref _submissionSequence),
                submittedFence,
                _submission.PresentationSequence,
                _device.DeviceEpoch,
                _submission.SurfaceEpoch,
                _submission.SwapchainEpoch));
        }

        internal void ObserveFenceWait()
        {
            var fenceWaitTask = _fenceWaitTask
                ?? throw new InvalidOperationException("A graph submission has no fence-wait operation.");
            fenceWaitTask.GetAwaiter().GetResult();
            var fenceWaitSignalTask = _fenceWaitSignalTask
                ?? throw new InvalidOperationException("A graph submission has no fence-wait notification.");
            fenceWaitSignalTask.GetAwaiter().GetResult();
        }

        internal void StartFenceWait(IGraphicsBackendDevice backendDevice)
        {
            ArgumentNullException.ThrowIfNull(backendDevice);
            var submittedFence = SubmittedFence;

            if (submittedFence <= 0)
            {
                throw new InvalidOperationException("A graph fence wait requires a positive submitted fence.");
            }

            var fenceWaitTask = System.Threading.Tasks.Task.Run(
                () => backendDevice.WaitForGraphFence(submittedFence));
            _fenceWaitTask = fenceWaitTask;
            _fenceWaitSignalTask = fenceWaitTask.ContinueWith(
                static (completedTask, state) => {
                    _ = completedTask.Exception;
                    ((GraphicsDevice)state!)._ownerQueue.SignalOwnerWork();
                },
                _device,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        void IGraphicsGraphRetentionCellWork.MarkRetentionCellQuarantined()
        {
            if ((_retentionCellIndex < 0) || _retentionCellQuarantined)
            {
                throw new InvalidOperationException("The graph request has no active retention cell to quarantine.");
            }

            _retentionCellQuarantined = true;
        }

        private void ReleaseFrameSlot()
        {
            if ((Interlocked.Exchange(ref _frameSlotReleased, 1) == 0)
                && (Interlocked.Exchange(ref _ownsFrameSlotReservation, 0) != 0))
            {
                _frameSlotReservation.ReleaseFromRequest();
            }
        }

        void IGraphicsGraphRetentionCellWork.ReleaseRetentionCell()
        {
            var cellIndex = _retentionCellIndex;

            if (cellIndex < 0)
            {
                return;
            }

            if (_retentionCellQuarantined)
            {
                throw new InvalidOperationException("A quarantined graph request cannot release its retention cell.");
            }

            _device.ReleaseGraphSubmissionRetentionCell(this, cellIndex);
            _retentionCellIndex = -1;
        }

        private void ReleaseWork()
        {
            if (Interlocked.Exchange(ref _workReleased, 1) != 0)
            {
                return;
            }

            ReleaseUseLeases(_useLeases);
            _submission.DisposeRecordedPassCommandLists();
            _submission.DisposeBackendStorage();
            _device._ownerQueue.ReleaseGraphRetentionCell(this);
            ReleaseFrameSlot();
        }

        bool IGraphicsGraphRetentionCellWork.TryClaimRetentionCell()
        {
            if (_retentionCellIndex >= 0)
            {
                throw new InvalidOperationException("The graph request already owns a retention cell.");
            }

            if (!_frameSlotReservation.TryTransferToRequest())
            {
                return false;
            }

            Volatile.Write(ref _ownsFrameSlotReservation, 1);

            if (_device.TryClaimGraphSubmissionRetentionCell(this, out _retentionCellIndex))
            {
                _submission.MarkGraphRetentionCellClaimed();
                return true;
            }

            _frameSlotReservation.RollbackRequestTransfer();
            Volatile.Write(ref _ownsFrameSlotReservation, 0);
            return false;
        }
    }
}
