// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Numerics;

namespace TerraFX.Entities;

internal sealed class WorldOwnerControlRequest
{
    private readonly CancellationToken _cancellationToken;
    private readonly SpatialFrame2? _frame2;
    private readonly SpatialFrame3? _frame3;
    private readonly TaskCompletionSource<FrameChangeReceipt>? _frameCompletion;
    private readonly WorldOwnerControlRequestKind _kind;
    private long _ownerRequestSequence;
    private readonly float _timeScale;
    private readonly TaskCompletionSource<WorldTimeScaleChangeOutcome>? _timeScaleCompletion;

    private WorldOwnerControlRequest(
        WorldOwnerControlRequestKind kind,
        SpatialFrame2? frame2,
        SpatialFrame3? frame3,
        float timeScale,
        CancellationToken cancellationToken)
    {
        _kind = kind;
        _frame2 = frame2;
        _frame3 = frame3;
        _timeScale = timeScale;
        _cancellationToken = cancellationToken;
        _frameCompletion = kind is WorldOwnerControlRequestKind.Frame2 or WorldOwnerControlRequestKind.Frame3
            ? new TaskCompletionSource<FrameChangeReceipt>(TaskCreationOptions.RunContinuationsAsynchronously)
            : null;
        _timeScaleCompletion = kind is WorldOwnerControlRequestKind.TimeScale
            ? new TaskCompletionSource<WorldTimeScaleChangeOutcome>(TaskCreationOptions.RunContinuationsAsynchronously)
            : null;
    }

    internal Task<FrameChangeReceipt> FrameCompletion
        => _frameCompletion?.Task
            ?? throw new InvalidOperationException("The owner-control request is not a frame change.");

    internal long OwnerRequestSequence
        => (_ownerRequestSequence > 0)
            ? _ownerRequestSequence
            : throw new InvalidOperationException("The World owner-control request has not been admitted.");

    internal Task<WorldTimeScaleChangeOutcome> TimeScaleCompletion
        => _timeScaleCompletion?.Task
            ?? throw new InvalidOperationException("The owner-control request is not a time-scale change.");

    internal static WorldOwnerControlRequest CreateFrameChange(SpatialFrame2 frame)
        => new(WorldOwnerControlRequestKind.Frame2, frame, null, default, CancellationToken.None);

    internal static WorldOwnerControlRequest CreateFrameChange(SpatialFrame3 frame)
        => new(WorldOwnerControlRequestKind.Frame3, null, frame, default, CancellationToken.None);

    internal static WorldOwnerControlRequest CreateTimeScaleChange(float timeScale, CancellationToken cancellationToken)
        => new(WorldOwnerControlRequestKind.TimeScale, null, null, timeScale, cancellationToken);

    internal void AssignOwnerRequestSequence(long sequence)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);

        if (_ownerRequestSequence != 0)
        {
            throw new InvalidOperationException("The World owner-control request was already assigned a sequence.");
        }

        _ownerRequestSequence = sequence;
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Owner-operation failures are delivered through the accepted request's task and do not terminate the host pump.")]
    internal void ProcessOwnerControl(World world)
    {
        ArgumentNullException.ThrowIfNull(world);

        switch (_kind)
        {
            case WorldOwnerControlRequestKind.Frame2:
            {
                try
                {
                    var frame = _frame2
                        ?? throw new InvalidOperationException("The 2D frame request has no frame candidate.");
                    _ = _frameCompletion?.TrySetResult(world.SetFrame(frame));
                }
                catch (Exception exception)
                {
                    _ = _frameCompletion?.TrySetException(exception);
                }

                break;
            }

            case WorldOwnerControlRequestKind.Frame3:
            {
                try
                {
                    var frame = _frame3
                        ?? throw new InvalidOperationException("The 3D frame request has no frame candidate.");
                    _ = _frameCompletion?.TrySetResult(world.SetFrame(frame));
                }
                catch (Exception exception)
                {
                    _ = _frameCompletion?.TrySetException(exception);
                }

                break;
            }

            case WorldOwnerControlRequestKind.TimeScale:
            {
                if (_cancellationToken.IsCancellationRequested)
                {
                    _ = _timeScaleCompletion?.TrySetCanceled(_cancellationToken);
                    break;
                }

                try
                {
                    var outcome = world.ApplyQueuedTimeScale(_timeScale, _cancellationToken);
                    _ = _timeScaleCompletion?.TrySetResult(outcome);
                }
                catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
                {
                    _ = _timeScaleCompletion?.TrySetCanceled(_cancellationToken);
                }
                catch (Exception exception)
                {
                    _ = _timeScaleCompletion?.TrySetException(exception);
                }

                break;
            }

            default:
            {
                var exception = new InvalidOperationException("The World owner-control request kind is invalid.");
                _ = _frameCompletion?.TrySetException(exception);
                _ = _timeScaleCompletion?.TrySetException(exception);
                break;
            }
        }
    }

    internal void Reject(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _ = _frameCompletion?.TrySetException(exception);
        _ = _timeScaleCompletion?.TrySetException(exception);
    }
}
