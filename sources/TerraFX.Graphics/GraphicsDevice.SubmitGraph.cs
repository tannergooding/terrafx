// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace TerraFX.Graphics;

public sealed partial class GraphicsDevice
{
    private static readonly ConcurrentBag<GraphicsGraphSubmission> s_quarantinedGraphSubmissions = [];
    private static readonly ConcurrentBag<GraphicsResourceOwner.UseLease[]> s_quarantinedGraphUseLeases = [];
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

            var request = new SubmitGraphRequest(this, submission, useLeases, frameSlotReservation);
            ownershipTransferred = true;

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
                        frameSlotReservation.Dispose();
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
        List<GraphicsResourceOwner.UseLease> useLeases,
        Exception exception)
    {
        submission.RetainRecordingForUncertainSubmission();
        s_quarantinedGraphSubmissions.Add(submission);
        s_quarantinedGraphUseLeases.Add([.. useLeases]);
        useLeases.Clear();

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

    internal sealed class GraphFrameSlotReservation : IDisposable
    {
        private readonly GraphicsDevice _device;
        private SemaphoreSlim? _frameSlots;

        internal GraphFrameSlotReservation(GraphicsDevice device, SemaphoreSlim frameSlots)
        {
            ArgumentNullException.ThrowIfNull(device);
            ArgumentNullException.ThrowIfNull(frameSlots);
            _device = device;
            _frameSlots = frameSlots;
        }

        public void Dispose()
        {
            var frameSlots = Interlocked.Exchange(ref _frameSlots, null);

            if (frameSlots is not null)
            {
                _ = frameSlots.Release();
                _ = Interlocked.Decrement(ref _device._activeGraphSubmissionCount);
                _device._ownerQueue.SignalOwnerWork();
            }
        }

        internal bool IsOwnedBy(GraphicsDevice device)
            => ReferenceEquals(_device, device) && (Volatile.Read(ref _frameSlots) is not null);
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

    private sealed class SubmitGraphRequest : IGraphicsDeviceWork
    {
        private readonly TaskCompletionSource<GraphicsGraphSubmissionResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly GraphicsDevice _device;
        private Task? _fenceWaitSignalTask;
        private Task? _fenceWaitTask;
        private int _frameSlotReleased;
        private readonly GraphFrameSlotReservation _frameSlotReservation;
        private int _nativeSubmissionStarted;
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
                _device.QuarantineGraphSubmission(_submission, _useLeases, exception);
                ReleaseFrameSlot();
            }
            else
            {
                ReleaseWork();
            }

            _ = _completion.TrySetException(exception);
        }

        internal void AdmissionFailed()
            => ReleaseWork();

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

        private void ReleaseFrameSlot()
        {
            if (Interlocked.Exchange(ref _frameSlotReleased, 1) == 0)
            {
                _frameSlotReservation.Dispose();
            }
        }

        private void ReleaseWork()
        {
            if (Interlocked.Exchange(ref _workReleased, 1) != 0)
            {
                return;
            }

            ReleaseUseLeases(_useLeases);
            _submission.DisposeRecordedPassCommandLists();
            ReleaseFrameSlot();
        }
    }
}
