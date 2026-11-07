// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(resourceUseLeases);

        List<GraphicsResourceOwner.UseLease>? useLeases = null;
        var ownershipTransferred = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Status != GraphicsDeviceStatus.Ready)
            {
                throw new InvalidOperationException("The GraphicsDevice is not accepting graph submissions.");
            }

            if (submission.DeviceEpoch != DeviceEpoch)
            {
                throw new InvalidOperationException("The graph submission belongs to a stale GraphicsDevice epoch.");
            }

            useLeases = resourceUseLeases.TransferToSubmission(submission);
            submission.ValidateOffscreenRasterSubmission();

            var request = new SubmitGraphRequest(this, submission, useLeases);
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
            resourceUseLeases.Dispose();

            if (!ownershipTransferred)
            {
                if (useLeases is not null)
                {
                    ReleaseUseLeases(useLeases);
                }

                submission.DisposeRecordedPassCommandLists();
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

        QuarantineResource(submission.OffscreenColorTarget.ResourceOwner, exception);
        MarkDeviceLost();
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
            _ = resourceOwners.Add(submission.OffscreenColorTarget.ResourceOwner);

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
        private int _nativeSubmissionStarted;
        private readonly GraphicsGraphSubmission _submission;
        private readonly List<GraphicsResourceOwner.UseLease> _useLeases;

        internal SubmitGraphRequest(
            GraphicsDevice device,
            GraphicsGraphSubmission submission,
            List<GraphicsResourceOwner.UseLease> useLeases)
        {
            ArgumentNullException.ThrowIfNull(device);
            ArgumentNullException.ThrowIfNull(submission);
            ArgumentNullException.ThrowIfNull(useLeases);

            _device = device;
            _submission = submission;
            _useLeases = useLeases;
        }

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
            var submissionSequence = device.GetNextGraphSubmissionSequence();
            Volatile.Write(ref _nativeSubmissionStarted, 1);
            var completedFence = backendDevice.SubmitGraph(_submission);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(completedFence);
            _submission.RecordCompletedOwnerStates();
            ReleaseWork();
            _ = _completion.TrySetResult(new GraphicsGraphSubmissionResult(
                submissionSequence,
                completedFence,
                device.DeviceEpoch));
        }

        public void Fail(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);

            if ((Volatile.Read(ref _nativeSubmissionStarted) != 0)
                && (_device._backendDevice?.HasUncertainSubmission == true))
            {
                _device.QuarantineGraphSubmission(_submission, _useLeases, exception);
            }
            else
            {
                ReleaseWork();
            }

            _ = _completion.TrySetException(exception);
        }

        internal void AdmissionFailed()
            => ReleaseWork();

        private void ReleaseWork()
        {
            ReleaseUseLeases(_useLeases);
            _submission.DisposeRecordedPassCommandLists();
        }
    }
}
