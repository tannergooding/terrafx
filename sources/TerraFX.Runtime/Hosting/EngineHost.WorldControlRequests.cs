// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using System.Threading.Tasks;
using TerraFX;
using TerraFX.Entities;
using TerraFX.Numerics;

namespace TerraFX.Hosting;

public sealed partial class EngineHost
{
    private static async Task<TimeScaleReceipt> CompleteTimeScaleChangeAsync(
        Task<WorldTimeScaleChangeOutcome> application)
    {
        var outcome = await application.ConfigureAwait(false);
        var status = outcome.IsApplied
            ? TimeScaleChangeStatus.Applied
            : TimeScaleChangeStatus.Rejected;
        return new TimeScaleReceipt(status, outcome.AppliedTimeScale, outcome.Diagnostics);
    }

    private static InvalidOperationException CreateOwnerControlAdmissionException(WorldOwnerControlAdmissionStatus status)
    {
        return status switch {
            WorldOwnerControlAdmissionStatus.WorldDisposed => new ObjectDisposedException(nameof(World)),
            WorldOwnerControlAdmissionStatus.WorldFaulted => new InvalidOperationException("The hosted World is faulted."),
            WorldOwnerControlAdmissionStatus.PersistenceCaptureActive
                => new InvalidOperationException("The hosted World is capturing persistence state."),
            WorldOwnerControlAdmissionStatus.SequenceExhausted
                => new InvalidOperationException("The hosted World exhausted its owner-request sequence."),
            _ => new InvalidOperationException("The hosted World owner-control admission returned an inconsistent status."),
        };
    }

    /// <summary>Queues one 2D frame change for the exact World hosted by this EngineHost.</summary>
    /// <param name="world">The exact hosted World target.</param>
    /// <param name="frame">The immutable frame candidate.</param>
    /// <param name="cancellationToken">The token checked only before owner-request admission.</param>
    /// <returns>The task for the owner's applied or rejected frame receipt.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="world"/> is null.</exception>
    /// <exception cref="ArgumentException">The World is not hosted here or the frame candidate is invalid for it.</exception>
    /// <exception cref="WorldOwnerQueueFullException">The request was refused before admission because the World owner-control queue is full.</exception>
    public Task<FrameChangeReceipt> ChangeFrameAsync(
        World world,
        SpatialFrame2 frame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(world);
        var request = WorldOwnerControlRequest.CreateFrameChange(frame);
        world.ValidateHostFrameCandidate(frame);
        bool hostStopping;
        var targetIsHosted = false;
        var requestIsCanceled = false;
        var admissionStatus = WorldOwnerControlAdmissionStatus.WorldDisposed;
        var queueSnapshot = default(WorldOwnerControlQueueSnapshot);

        lock (_ownerRequestGate)
        {
            hostStopping = _stopping;

            if (!hostStopping)
            {
                targetIsHosted = IsHostedWorld(world);

                if (targetIsHosted)
                {
                    requestIsCanceled = cancellationToken.IsCancellationRequested;

                    if (!requestIsCanceled)
                    {
                        admissionStatus = world.TryAdmitOwnerControl(request, out queueSnapshot);
                    }
                }
            }
        }

        ObjectDisposedException.ThrowIf(hostStopping, this);

        if (!targetIsHosted)
        {
            throw new ArgumentException("The World is not bound to this EngineHost.", nameof(world));
        }

        if (requestIsCanceled)
        {
            return Task.FromCanceled<FrameChangeReceipt>(cancellationToken);
        }

        return admissionStatus switch {
            WorldOwnerControlAdmissionStatus.Accepted => request.FrameCompletion,
            WorldOwnerControlAdmissionStatus.QueueFull
                => Task.FromException<FrameChangeReceipt>(world.CreateOwnerQueueFullException(2, queueSnapshot)),
            _ => throw CreateOwnerControlAdmissionException(admissionStatus),
        };
    }

    /// <summary>Queues one 3D frame change for the exact World hosted by this EngineHost.</summary>
    /// <param name="world">The exact hosted World target.</param>
    /// <param name="frame">The immutable frame candidate.</param>
    /// <param name="cancellationToken">The token checked only before owner-request admission.</param>
    /// <returns>The task for the owner's applied or rejected frame receipt.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="world"/> is null.</exception>
    /// <exception cref="ArgumentException">The World is not hosted here or the frame candidate is invalid for it.</exception>
    /// <exception cref="WorldOwnerQueueFullException">The request was refused before admission because the World owner-control queue is full.</exception>
    public Task<FrameChangeReceipt> ChangeFrameAsync(
        World world,
        SpatialFrame3 frame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(world);
        var request = WorldOwnerControlRequest.CreateFrameChange(frame);
        world.ValidateHostFrameCandidate(frame);
        bool hostStopping;
        var targetIsHosted = false;
        var requestIsCanceled = false;
        var admissionStatus = WorldOwnerControlAdmissionStatus.WorldDisposed;
        var queueSnapshot = default(WorldOwnerControlQueueSnapshot);

        lock (_ownerRequestGate)
        {
            hostStopping = _stopping;

            if (!hostStopping)
            {
                targetIsHosted = IsHostedWorld(world);

                if (targetIsHosted)
                {
                    requestIsCanceled = cancellationToken.IsCancellationRequested;

                    if (!requestIsCanceled)
                    {
                        admissionStatus = world.TryAdmitOwnerControl(request, out queueSnapshot);
                    }
                }
            }
        }

        ObjectDisposedException.ThrowIf(hostStopping, this);

        if (!targetIsHosted)
        {
            throw new ArgumentException("The World is not bound to this EngineHost.", nameof(world));
        }

        if (requestIsCanceled)
        {
            return Task.FromCanceled<FrameChangeReceipt>(cancellationToken);
        }

        return admissionStatus switch {
            WorldOwnerControlAdmissionStatus.Accepted => request.FrameCompletion,
            WorldOwnerControlAdmissionStatus.QueueFull
                => Task.FromException<FrameChangeReceipt>(world.CreateOwnerQueueFullException(3, queueSnapshot)),
            _ => throw CreateOwnerControlAdmissionException(admissionStatus),
        };
    }

    /// <summary>Queues one time-scale change for the exact World hosted by this EngineHost.</summary>
    /// <param name="world">The exact hosted World target.</param>
    /// <param name="timeScale">The finite multiplier in the closed interval [0, 4].</param>
    /// <param name="cancellationToken">The token that prevents admission or cancels the request before commit.</param>
    /// <returns>The task for the owner's applied or rejected time-scale receipt.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="world"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeScale"/> is nonfinite or outside [0, 4].</exception>
    public Task<TimeScaleReceipt> ChangeTimeScaleAsync(
        World world,
        float timeScale,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(world);

        if (!float.IsFinite(timeScale) || (timeScale < 0) || (timeScale > 4))
        {
            throw new ArgumentOutOfRangeException(nameof(timeScale));
        }

        world.ValidateHostTimeScaleChange(timeScale);
        var request = WorldOwnerControlRequest.CreateTimeScaleChange(timeScale, cancellationToken);
        bool hostStopping;
        var targetIsHosted = false;
        var requestIsCanceled = false;
        var admissionStatus = WorldOwnerControlAdmissionStatus.WorldDisposed;
        var queueSnapshot = default(WorldOwnerControlQueueSnapshot);

        lock (_ownerRequestGate)
        {
            hostStopping = _stopping;

            if (!hostStopping)
            {
                targetIsHosted = IsHostedWorld(world);

                if (targetIsHosted)
                {
                    requestIsCanceled = cancellationToken.IsCancellationRequested;

                    if (!requestIsCanceled)
                    {
                        admissionStatus = world.TryAdmitOwnerControl(request, out queueSnapshot);
                    }
                }
            }
        }

        ObjectDisposedException.ThrowIf(hostStopping, this);

        if (!targetIsHosted)
        {
            throw new ArgumentException("The World is not bound to this EngineHost.", nameof(world));
        }

        if (requestIsCanceled)
        {
            return Task.FromCanceled<TimeScaleReceipt>(cancellationToken);
        }

        if (admissionStatus is WorldOwnerControlAdmissionStatus.QueueFull)
        {
            var rejected = world.CreateRejectedTimeScaleOutcome(
                "WorldOwnerQueueFull",
                "The World owner-control queue is full.",
                queueSnapshot);
            return Task.FromResult(new TimeScaleReceipt(
                TimeScaleChangeStatus.Rejected,
                rejected.AppliedTimeScale,
                rejected.Diagnostics));
        }

        return admissionStatus is WorldOwnerControlAdmissionStatus.Accepted
            ? CompleteTimeScaleChangeAsync(request.TimeScaleCompletion)
            : throw CreateOwnerControlAdmissionException(admissionStatus);
    }

    private bool IsHostedWorld(World world)
    {
        foreach (var schedule in _worlds)
        {
            if (ReferenceEquals(schedule.World, world))
            {
                return true;
            }
        }

        return false;
    }
}
