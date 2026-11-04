// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using TerraFX;

namespace TerraFX.Entities;

public sealed partial class World
{
    internal WorldTimeScaleChangeOutcome ApplyQueuedTimeScale(float timeScale, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnterStructuralOperation();

        try
        {
            if (!float.IsFinite(timeScale) || (timeScale < 0) || (timeScale > 4))
            {
                throw new ArgumentOutOfRangeException(nameof(timeScale));
            }

            var controlSequence = GetNextControlSequence();
            var segmentSequence = checked(_timeScaleSegmentSequence + 1);
            var nextMutationRevision = checked(_canonicalMutationRevision + 1);
            var control = new WorldControlRecord(
                controlSequence,
                _nextTickIndex,
                WorldControlKind.TimeScale,
                timeScaleValue: timeScale,
                segmentSequence: segmentSequence);
            using var preparedAppend = TryPrepareControlAppend(appendBranch: false, extraPreparationBytes: 0);

            if (preparedAppend is null)
            {
                return CreateRejectedTimeScaleControl(
                    "WorldTimeScalePreflightCapacityExceeded",
                    "The World cannot reserve another recorded time-scale control.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            _canonicalMutationRevision = nextMutationRevision;
            _timeScale = timeScale;
            _timeScaleSegmentSequence = segmentSequence;
            preparedAppend.Commit(control, branch: null);

            return new WorldTimeScaleChangeOutcome(true, timeScale, []);
        }
        finally
        {
            _ = Interlocked.Exchange(ref _isStructuralOperationActive, 0);
        }
    }

    private WorldTimeScaleChangeOutcome CreateRejectedTimeScaleControl(string code, string message)
    {
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
            operation: "TerraFX.Entities.World.SetTimeScale",
            nativeResult: null,
            message: message,
            context: []);
        _diagnosticsCapability.Write(diagnostic);
        return new WorldTimeScaleChangeOutcome(false, null, [diagnostic]);
    }
}
