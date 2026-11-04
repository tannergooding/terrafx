// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Globalization;
using System.Threading;
using TerraFX;
using TerraFX.Numerics;

namespace TerraFX.Entities;

public sealed partial class World
{
    internal WorldOwnerQueueFullException CreateOwnerQueueFullException(
        int dimension,
        WorldOwnerControlQueueSnapshot snapshot)
    {
        var diagnostic = CreateOwnerQueueFullDiagnostic(dimension, snapshot);
        return new WorldOwnerQueueFullException(
            diagnostic,
            _worldGuid,
            dimension,
            snapshot.PendingCount,
            snapshot.PendingLimit,
            snapshot.NextOwnerRequestSequence,
            snapshot.AcceptedOwnerRequests,
            snapshot.NextControlSequence);
    }

    internal WorldTimeScaleChangeOutcome CreateRejectedTimeScaleOutcome(
        string code,
        string message,
        WorldOwnerControlQueueSnapshot? queueSnapshot = null)
    {
        var context = queueSnapshot is WorldOwnerControlQueueSnapshot snapshot
            ? new[] {
                new DiagnosticContextEntry("PendingCount", snapshot.PendingCount.ToString(CultureInfo.InvariantCulture)),
                new DiagnosticContextEntry("PendingLimit", snapshot.PendingLimit.ToString(CultureInfo.InvariantCulture)),
                new DiagnosticContextEntry("NextOwnerRequestSequence", snapshot.NextOwnerRequestSequence.ToString(CultureInfo.InvariantCulture)),
                new DiagnosticContextEntry("AcceptedOwnerRequests", snapshot.AcceptedOwnerRequests.ToString(CultureInfo.InvariantCulture)),
                new DiagnosticContextEntry("NextControlSequence", snapshot.NextControlSequence.ToString(CultureInfo.InvariantCulture)),
            }
            : [];
        var diagnostic = new Diagnostic(
            code,
            DiagnosticSeverity.Warning,
            _diagnosticsCapability.GetTimestamp(),
            hostId: null,
            worldGuid: _worldGuid,
            deviceId: null,
            surfaceId: null,
            deviceEpoch: null,
            surfaceEpoch: null,
            swapchainEpoch: null,
            tick: null,
            systemId: null,
            passId: null,
            assetId: null,
            commandIdentity: null,
            operation: "EngineHost.ChangeTimeScaleAsync",
            nativeResult: null,
            message: message,
            context: [.. context]);
        _diagnosticsCapability.Write(diagnostic);
        return new WorldTimeScaleChangeOutcome(false, null, [diagnostic]);
    }

    internal WorldOwnerControlAdmissionStatus TryAdmitOwnerControl(
        WorldOwnerControlRequest request,
        out WorldOwnerControlQueueSnapshot queueSnapshot)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_ownerControlGate)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                queueSnapshot = default;
                return WorldOwnerControlAdmissionStatus.WorldDisposed;
            }

            if (Volatile.Read(ref _faulted) != 0)
            {
                queueSnapshot = default;
                return WorldOwnerControlAdmissionStatus.WorldFaulted;
            }

            if (_isPersistenceCaptureLeaseActive)
            {
                queueSnapshot = default;
                return WorldOwnerControlAdmissionStatus.PersistenceCaptureActive;
            }

            if (_ownerControlRequestCount >= _maximumPendingOwnerRequests)
            {
                queueSnapshot = new WorldOwnerControlQueueSnapshot(
                    _ownerControlRequestCount,
                    _maximumPendingOwnerRequests,
                    _nextOwnerRequestSequence,
                    _acceptedOwnerRequests,
                    Volatile.Read(ref _nextControlSequence));
                return WorldOwnerControlAdmissionStatus.QueueFull;
            }

            if (_acceptedOwnerRequests >= (long.MaxValue - 1))
            {
                queueSnapshot = default;
                return WorldOwnerControlAdmissionStatus.SequenceExhausted;
            }

            if (_nextOwnerRequestSequence != checked(_acceptedOwnerRequests + 1))
            {
                throw new InvalidOperationException("The World owner-request cursor is inconsistent.");
            }

            if (_nextOwnerRequestSequence == long.MaxValue)
            {
                queueSnapshot = default;
                return WorldOwnerControlAdmissionStatus.SequenceExhausted;
            }

            var sequence = _nextOwnerRequestSequence;
            request.AssignOwnerRequestSequence(sequence);
            var tail = (_ownerControlRequestHead + _ownerControlRequestCount) % _ownerControlRequests.Length;

            if (_ownerControlRequests[tail] is not null)
            {
                throw new InvalidOperationException("The World owner-control queue tail is not empty.");
            }

            _ownerControlRequests[tail] = request;
            _ownerControlRequestCount++;
            _acceptedOwnerRequests = checked(_acceptedOwnerRequests + 1);
            Volatile.Write(ref _nextOwnerRequestSequence, checked(sequence + 1));
            queueSnapshot = default;
            return WorldOwnerControlAdmissionStatus.Accepted;
        }
    }

    internal void ValidateHostFrameCandidate(SpatialFrame2 frame)
    {
        ThrowIfUnavailableForHostControlRequest();
        ValidateFrameOwner(frame.OwnerId, frame.ProfileId, frame.Settings, nameof(frame));

        if (_initialFrame2 is null)
        {
            throw new InvalidOperationException("The World has 2D frame admission disabled.");
        }
    }

    internal void ValidateHostFrameCandidate(SpatialFrame3 frame)
    {
        ThrowIfUnavailableForHostControlRequest();
        ValidateFrameOwner(frame.OwnerId, frame.ProfileId, frame.Settings, nameof(frame));
    }

    internal void ValidateHostTimeScaleChange(float timeScale)
    {
        ThrowIfUnavailableForHostControlRequest();

        if (!float.IsFinite(timeScale) || (timeScale < 0) || (timeScale > 4))
        {
            throw new ArgumentOutOfRangeException(nameof(timeScale));
        }
    }

    private void ThrowIfUnavailableForHostControlRequest()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ThrowIfFaulted();
    }
}
