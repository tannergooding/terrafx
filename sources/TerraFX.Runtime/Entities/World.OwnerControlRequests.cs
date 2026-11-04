// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Globalization;
using System.Threading;
using TerraFX;

namespace TerraFX.Entities;

public sealed partial class World
{
    internal void DrainOwnerControlRequests()
    {
        EnsureOwnerThread();
        int batchCount;

        lock (_ownerControlGate)
        {
            batchCount = _ownerControlRequestCount;
        }

        for (var index = 0; index < batchCount; index++)
        {
            WorldOwnerControlRequest? request;

            lock (_ownerControlGate)
            {
                if (_ownerControlRequestCount == 0)
                {
                    throw new InvalidOperationException("The owner-control batch lost an admitted request.");
                }

                request = _ownerControlRequests[_ownerControlRequestHead]
                    ?? throw new InvalidOperationException("The owner-control queue contains an empty head slot.");
                _ownerControlRequests[_ownerControlRequestHead] = null;
                _ownerControlRequestHead = (_ownerControlRequestHead + 1) % _ownerControlRequests.Length;
                _ownerControlRequestCount--;
            }

            request.ProcessOwnerControl(this);

            lock (_ownerControlGate)
            {
                if (request.OwnerRequestSequence != checked(_completedOwnerRequests + 1))
                {
                    throw new InvalidOperationException("The owner-control queue did not complete requests in sequence order.");
                }

                _completedOwnerRequests = request.OwnerRequestSequence;
            }
        }
    }

    internal void FailPendingOwnerControlRequests(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        while (true)
        {
            WorldOwnerControlRequest? request;

            lock (_ownerControlGate)
            {
                if (_ownerControlRequestCount == 0)
                {
                    _ownerControlRequestHead = 0;
                    return;
                }

                request = _ownerControlRequests[_ownerControlRequestHead]
                    ?? throw new InvalidOperationException("The owner-control queue contains an empty head slot.");
                _ownerControlRequests[_ownerControlRequestHead] = null;
                _ownerControlRequestHead = (_ownerControlRequestHead + 1) % _ownerControlRequests.Length;
                _ownerControlRequestCount--;
            }

            request.Reject(exception);

            lock (_ownerControlGate)
            {
                if (request.OwnerRequestSequence != checked(_completedOwnerRequests + 1))
                {
                    throw new InvalidOperationException("The owner-control queue did not retire requests in sequence order.");
                }

                _completedOwnerRequests = request.OwnerRequestSequence;
            }
        }
    }

    private Diagnostic CreateOwnerQueueFullDiagnostic(int dimension, WorldOwnerControlQueueSnapshot snapshot)
    {
        var diagnostic = new Diagnostic(
            "WorldOwnerQueueFull",
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
            operation: "EngineHost.ChangeFrameAsync",
            nativeResult: null,
            message: "The bounded World owner-control queue refused a frame request.",
            context: [
                new DiagnosticContextEntry("Dimension", dimension.ToString(CultureInfo.InvariantCulture)),
                new DiagnosticContextEntry("PendingCount", snapshot.PendingCount.ToString(CultureInfo.InvariantCulture)),
                new DiagnosticContextEntry("PendingLimit", snapshot.PendingLimit.ToString(CultureInfo.InvariantCulture)),
                new DiagnosticContextEntry("NextOwnerRequestSequence", snapshot.NextOwnerRequestSequence.ToString(CultureInfo.InvariantCulture)),
                new DiagnosticContextEntry("AcceptedOwnerRequests", snapshot.AcceptedOwnerRequests.ToString(CultureInfo.InvariantCulture)),
                new DiagnosticContextEntry("NextControlSequence", snapshot.NextControlSequence.ToString(CultureInfo.InvariantCulture)),
            ]);
        _diagnosticsCapability.Write(diagnostic);
        return diagnostic;
    }

    private void RejectPendingOwnerControlRequestsForDisposal()
        => FailPendingOwnerControlRequests(new ObjectDisposedException(nameof(World)));
}
