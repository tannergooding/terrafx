// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Entities;

namespace TerraFX.Hosting;

internal sealed partial class WorldReplacementOperation : IWorldGuidRetentionOwner
{
    private int _captureAttempted;
    private int _isCaptured;
    private int _isExecuted;
    private int _isLoadEnded;
    private WorldDomainOperationCreditBundle? _operationCredits;
    private readonly WorldPersistence _persistence;
    private readonly WorldLoadPlan _plan;
    private WorldLoadPreparedState? _preparedState;

    internal WorldReplacementOperation(WorldPersistence persistence, WorldLoadPlan plan)
    {
        ArgumentNullException.ThrowIfNull(persistence);
        ArgumentNullException.ThrowIfNull(plan);
        _persistence = persistence;
        _plan = plan;
    }

    internal World TargetWorld => _plan.TargetWorld;

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Postpublication retirement failures are delivered through OldWorldRetirement and cannot change a successful replacement into a rejection.")]
    internal WorldLoadReceipt ExecuteAtOwnerBoundary(EngineHost? host = null)
    {
        if (Volatile.Read(ref _isCaptured) == 0)
        {
            throw new InvalidOperationException("The World replacement operation has not captured its prepared plan.");
        }

        if (Interlocked.CompareExchange(ref _isExecuted, 1, 0) != 0)
        {
            throw new InvalidOperationException("The World replacement operation can execute only once.");
        }

        var state = _preparedState
            ?? throw new InvalidOperationException("The captured World replacement omitted its prepared state.");
        var targetWorld = _plan.TargetWorld;
        targetWorld.EnsureLoadCommitOwnerThread();

        if (!state.ReplacementPreparation.IsTargetCurrent())
        {
            AbandonCapturedState();
            return WorldPersistence.CreateLoadRejectedReceipt(
                "load-target-binding-stale",
                "The exact target World binding became stale before candidate construction.",
                _plan.TargetWorldGuid);
        }

        World? candidate = null;
        WorldIdentityRemap? identityRemap = null;
        var operationCredits = _operationCredits
            ?? throw new InvalidOperationException("The captured World replacement omitted its operation credits.");
        TaskCompletionSource retirement = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var publicationCommitted = false;

        try
        {
            candidate = state.CreateRestoredCandidate(CancellationToken.None);
            var sourceIdentityHistory = state.Checkpoint.WorldContents.Ecs.ValidatedIdentityHistory
                ?? throw new InvalidOperationException("The prepared checkpoint omitted its validated identity history.");
            var targetIdentityHistory = state.ValidateRestoredCandidate(candidate);
            identityRemap = new WorldIdentityRemap();
            identityRemap.InitializeFromIdentityHistories(
                sourceIdentityHistory.WorldGuid,
                sourceIdentityHistory.ReplayLineageId,
                sourceIdentityHistory.ActiveBranchId,
                targetIdentityHistory.WorldGuid,
                targetIdentityHistory.ReplayLineageId,
                targetIdentityHistory.ActiveBranchId,
                _plan.SourceCheckpointDigest,
                _plan.SourcePayloadDigest,
                sourceIdentityHistory.AssignmentsByOrdinal.AsSpan(),
                targetIdentityHistory.AssignmentsByOrdinal.AsSpan(),
                operationCredits.Data);
            var restoredPublication = candidate.PrepareRestoredPublication();
            var hostTimestamp = host?.GetWorldReplacementHostTimestamp() ?? 0;
            var hostAdoption = host?.PrepareWorldReplacementAdoption(targetWorld, candidate, hostTimestamp);
            var loadedReceipt = new WorldLoadReceipt(
                WorldLoadStatus.Loaded,
                candidate,
                retirement.Task,
                identityRemap,
                _plan.SourceWorldGuid,
                _plan.TargetRecordWorldGuid,
                _plan.SnapshotTick,
                rejection: null);

            if (!targetWorld.TryCommitLoadReplacement(
                    candidate,
                    restoredPublication,
                    state.ReplacementPreparation,
                    operationCredits,
                    out var bindingRefusal))
            {
                candidate.Dispose();
                candidate = null;
                identityRemap.Dispose();
                identityRemap = null;
                AbandonCapturedState();
                return WorldPersistence.CreateLoadRejectedReceipt(
                    (bindingRefusal.Code == WorldGuidRefusal.TargetBindingStale)
                        ? "load-target-binding-stale"
                        : "load-commit-rejected",
                    (bindingRefusal.Code == WorldGuidRefusal.TargetBindingStale)
                        ? "The exact target World binding became stale before replacement publication."
                        : "The target World refused the prepared replacement.",
                    _plan.TargetWorldGuid);
            }

            publicationCommitted = true;
            candidate = null;
            identityRemap = null;
            Exception? retirementFailure = null;

            try
            {
                host?.AdoptWorldReplacement(
                    hostAdoption
                        ?? throw new InvalidOperationException("The hosted replacement omitted its prepared schedule adoption."));
            }
            catch (Exception exception)
            {
                retirementFailure = exception;
            }

            try
            {
                targetWorld.Dispose();
            }
            catch (Exception exception)
            {
                retirementFailure = (retirementFailure is null)
                    ? exception
                    : new AggregateException(retirementFailure, exception);
            }

            try
            {
                state.Dispose();
            }
            catch (Exception exception)
            {
                retirementFailure = (retirementFailure is null)
                    ? exception
                    : new AggregateException(retirementFailure, exception);
            }

            try
            {
                operationCredits.Dispose();
            }
            catch (Exception exception)
            {
                retirementFailure = (retirementFailure is null)
                    ? exception
                    : new AggregateException(retirementFailure, exception);
            }
            finally
            {
                _operationCredits = null;
            }

            _preparedState = null;

            if (retirementFailure is null)
            {
                _ = retirement.TrySetResult();
            }
            else
            {
                _ = retirement.TrySetException(retirementFailure);
            }

            return loadedReceipt;
        }
        catch
        {
            if (!publicationCommitted)
            {
                try
                {
                    candidate?.Dispose();
                }
                finally
                {
                    try
                    {
                        identityRemap?.Dispose();
                    }
                    finally
                    {
                        AbandonCapturedState();
                    }
                }
            }

            throw;
        }
    }
}
