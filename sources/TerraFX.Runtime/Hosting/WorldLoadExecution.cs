// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using TerraFX;
using TerraFX.Entities;
using TerraFX.Integration;

namespace TerraFX.Hosting;

internal sealed class WorldLoadExecution
{
    private EngineHost.WorldReplacementAdoptionPlan? _adoption;
    private readonly TaskCompletionSource<WorldLoadReceipt> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _gate = new();
    private bool _isBound;
    private bool _isCompleted;
    private int _isLoadOperationEnded;
    private bool _isOwnerExecutionStarted;
    private readonly TaskCompletionSource _oldWorldRetirement = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private WorldReplacementOperation? _operation;
    private WorldLoadReceipt? _ownerReceipt;
    private WorldPersistence? _persistence;
    private readonly WorldLoadPlan _plan;
    private readonly WorldPreparedReplacement _preparedReplacement;
    private readonly WorldLoadPreparedState _state;

    internal WorldLoadExecution(
        WorldLoadPlan plan,
        WorldLoadPreparedState state,
        WorldPreparedReplacement replacement)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(replacement);

        if (!ReferenceEquals(plan.TargetWorld, replacement.TargetWorld))
        {
            throw new ArgumentException("The load execution does not match the exact prepared target binding.", nameof(replacement));
        }

        _plan = plan;
        _state = state;
        _preparedReplacement = replacement;
    }

    internal EngineHost.WorldReplacementAdoptionPlan? Adoption => _adoption;

    internal Task<WorldLoadReceipt> Completion => _completion.Task;

    internal WorldPersistence Persistence
        => _persistence ?? throw new InvalidOperationException("The World load execution has not been bound to its captured persistence service.");

    internal World TargetWorld => _plan.TargetWorld;

    private static WorldLoadReceipt CreateLoadRejectedReceipt(WorldGuidRefusal refusal, Guid worldGuid)
    {
        if (refusal.Code == WorldGuidRefusal.TargetBindingStale)
        {
            return WorldPersistence.CreateLoadRejectedReceipt(
                "load-target-binding-stale",
                "The exact target World binding became stale before candidate construction or publication.",
                worldGuid);
        }

        if (refusal.AdmissionRefusal is WorldAdmissionRefusal.DomainClosed)
        {
            return WorldPersistence.CreateLoadRejectedReceipt(
                "load-domain-closed",
                "The target World admission domain closed before commit capture.",
                worldGuid);
        }

        throw new InvalidOperationException("The World replacement refusal is not a supported bounded commit result.");
    }

    internal void BindCaptured(WorldPersistence persistence, WorldReplacementOperation operation)
    {
        ArgumentNullException.ThrowIfNull(persistence);
        ArgumentNullException.ThrowIfNull(operation);

        if (!_preparedReplacement.IsCapturedOperation(operation)
            || !ReferenceEquals(TargetWorld, operation.TargetWorld))
        {
            throw new InvalidOperationException("The captured operation does not belong to this exact World load plan.");
        }

        lock (_gate)
        {
            if (_isBound || _isCompleted)
            {
                throw new InvalidOperationException("The World load execution can bind its captured operation only once.");
            }

            _persistence = persistence;
            _operation = operation;
            _isBound = true;
        }
    }

    internal void CompleteAfterOwnerBoundary(WorldLoadReceipt receipt)
        => CompleteAfterOwnerBoundary(receipt, postPublicationFailure: null);

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Each independently owned cleanup is attempted and the original failure is delivered through the preallocated receipt channels.")]
    internal WorldLoadReceipt ExecuteAtOwnerBoundary()
    {
        WorldPersistence persistence;
        WorldReplacementOperation operation;

        lock (_gate)
        {
            if (!_isBound || _isOwnerExecutionStarted || _isCompleted)
            {
                throw new InvalidOperationException("The World load execution is not ready for its single owner-boundary execution.");
            }

            _isOwnerExecutionStarted = true;
            persistence = _persistence
                ?? throw new InvalidOperationException("The captured World load execution omitted its persistence service.");
            operation = _operation
                ?? throw new InvalidOperationException("The captured World load execution omitted its lower operation.");
        }

        WorldIdentityRemap? identityRemap = null;

        try
        {
            if (!operation.TryCreateCandidateAtOwnerBoundary(out var candidate, out var refusal))
            {
                var rejection = CreateLoadRejectedReceipt(refusal, TargetWorld.WorldGuid);
                lock (_gate)
                {
                    _ownerReceipt = rejection;
                }

                return rejection;
            }

            var restoredCandidate = candidate
                ?? throw new InvalidOperationException("The lower World replacement created no candidate.");
            _state.RestoreCandidate(restoredCandidate, CancellationToken.None);
            var limits = WorldPersistenceEbinLimits.Create(restoredCandidate.Options);
            var captureLease = restoredCandidate.AcquireRestoreValidationCaptureLease();
            using (captureLease)
            {
                captureLease.ValidateCurrent();

                if (!captureLease.IsRestoreValidation || !captureLease.IsBoundTo(restoredCandidate))
                {
                    throw new InvalidOperationException("The restore-validation capture lease is not bound to the exact candidate.");
                }

                var status = WorldPersistenceCaptureCoordinator.Capture(
                    restoredCandidate,
                    captureLease,
                    restoredCandidate.Options.Registration.Schemas,
                    out var captureReservation,
                    out var freshWorldRecord,
                    out _,
                    out var diagnostic,
                    CancellationToken.None);

                if (status is not EbinWriteStatus.Complete)
                {
                    throw new InvalidOperationException(
                        diagnostic?.Category ?? $"Fresh restore validation capture returned {status}.");
                }

                using var retainedCaptureReservation = captureReservation
                    ?? throw new InvalidOperationException("The fresh restore validation capture omitted its admission reservation.");
                var record = freshWorldRecord
                    ?? throw new InvalidOperationException("The fresh restore validation capture omitted its World record.");
                var identityAssignmentCount = WorldPersistenceWorldRecordReader.MeasureIdentityAssignmentCount(record, limits);
                var lineageMembershipScratchCapacity =
                    WorldPersistenceWorldRecordReader.MeasureLineageMembershipScratchCapacity(record, limits);
                var identityScratch = new WorldPersistenceIdentityScratchEntry[identityAssignmentCount];
                var lineageMembershipScratch = new Guid[lineageMembershipScratchCapacity];
                var strictLayoutBuffers = default(WorldPersistenceStrictLayoutReadBuffers);
                var freshContents = WorldPersistenceWorldRecordReader.Read(
                    record,
                    limits,
                    restoredCandidate.Options.Registration,
                    identityScratch,
                    lineageMembershipScratch,
                    ref strictLayoutBuffers);
                var sourceIdentityHistory = _state.Checkpoint.WorldContents.Ecs.ValidatedIdentityHistory
                    ?? throw new InvalidOperationException("The validated source omitted its identity history.");
                var targetIdentityHistory = freshContents.Ecs.ValidatedIdentityHistory
                    ?? throw new InvalidOperationException("The fresh candidate capture omitted its identity history.");
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
                    operation.MappingCredit);

                var host = persistence.Host;
                var hostTimestamp = host?.GetWorldReplacementHostTimestamp() ?? 0;
                var adoption = host?.PrepareWorldReplacementAdoption(TargetWorld, restoredCandidate, hostTimestamp);
                lock (_gate)
                {
                    _adoption = adoption;
                }

                operation.PreparePublicationAtOwnerBoundary(
                    in captureLease,
                    record,
                    targetIdentityHistory);
            }

            var receipt = new WorldLoadReceipt(
                WorldLoadStatus.Loaded,
                restoredCandidate,
                _oldWorldRetirement.Task,
                identityRemap,
                _plan.SourceWorldGuid,
                _plan.TargetRecordWorldGuid,
                _plan.SnapshotTick,
                rejection: null);
            var outcome = operation.PublishAtOwnerBoundary();

            if (outcome.Kind is WorldReplacementOutcomeKind.Rejected)
            {
                identityRemap.Dispose();
                identityRemap = null;
                var rejection = CreateLoadRejectedReceipt(outcome.Refusal, TargetWorld.WorldGuid);
                lock (_gate)
                {
                    _adoption = null;
                    _ownerReceipt = rejection;
                }

                return rejection;
            }

            if ((outcome.Kind is not WorldReplacementOutcomeKind.Published)
                || !ReferenceEquals(outcome.ReplacementWorld, restoredCandidate))
            {
                throw new InvalidOperationException("The lower World replacement returned an invalid publication outcome.");
            }

            identityRemap = null;
            lock (_gate)
            {
                _ownerReceipt = receipt;
            }

            return receipt;
        }
        catch (Exception failure)
        {
            try
            {
                identityRemap?.Dispose();
            }
            catch (Exception disposeFailure)
            {
                failure = new AggregateException(failure, disposeFailure);
            }

            try
            {
                operation.AbortAtOwnerBoundary();
            }
            catch (Exception abortFailure)
            {
                failure = new AggregateException(failure, abortFailure);
            }

            ExceptionDispatchInfo.Capture(failure).Throw();
            throw;
        }
    }

    internal void FailAfterOwnerBoundary(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        WorldLoadReceipt? receipt;

        lock (_gate)
        {
            if (!_isOwnerExecutionStarted || _isCompleted)
            {
                throw new InvalidOperationException("Only an executing World load can fail after the owner boundary starts.");
            }

            receipt = _ownerReceipt;
        }

        if (receipt is not null && receipt.Status is WorldLoadStatus.Loaded)
        {
            CompleteAfterOwnerBoundary(receipt, failure);
            return;
        }

        var operation = GetBoundOperation();
        DisposeRejectedExecution(operation, failure);
    }

    internal void FailBeforeOwnerBoundary(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        lock (_gate)
        {
            if (!_isBound || _isOwnerExecutionStarted || _isCompleted)
            {
                throw new InvalidOperationException("Only a captured execution that has not started owner work can fail before the owner boundary.");
            }
        }

        var operation = GetBoundOperation();
        operation.AbandonBeforeOwnerBoundary();
        DisposeRejectedExecution(operation, failure);
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "The captured operation's source and lower cleanup owners remain rooted when any individual cleanup fails.")]
    internal void FailUnboundCapture(
        WorldPersistence persistence,
        WorldReplacementOperation operation,
        Exception failure)
    {
        ArgumentNullException.ThrowIfNull(persistence);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(failure);

        lock (_gate)
        {
            if (_isBound || _isCompleted)
            {
                throw new InvalidOperationException("The unbound-capture cleanup path cannot consume a bound World load execution.");
            }

            _isCompleted = true;
        }

        var cleanupFailure = failure;

        void Release(Action cleanup)
        {
            try
            {
                cleanup();
            }
            catch (Exception exception)
            {
                cleanupFailure = new AggregateException(cleanupFailure, exception);
            }
        }

        operation.AbandonBeforeOwnerBoundary();
        Release(_state.Dispose);
        Release(operation.EndAfterConsumerCleanup);

        if (Interlocked.Exchange(ref _isLoadOperationEnded, 1) == 0)
        {
            Release(persistence.EndLoadOperation);
        }

        _ = _completion.TrySetException(cleanupFailure);
    }

    internal WorldLoadReceipt RejectOwnerQueueFull()
    {
        var operation = GetBoundOperation();
        operation.AbandonBeforeOwnerBoundary();
        var receipt = WorldPersistence.CreateLoadRejectedReceipt(
            "load-owner-queue-full",
            "The EngineHost owner queue is full; no replacement was published.",
            TargetWorld.WorldGuid);
        CompleteAfterOwnerBoundary(receipt);
        return receipt;
    }

    internal void ValidateCaptureReady()
        => _state.ValidateCommitCapture();

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Each independent retirement and operation owner is attempted, with failures reported through the preallocated retirement and receipt tasks.")]
    private void CompleteAfterOwnerBoundary(WorldLoadReceipt receipt, Exception? postPublicationFailure)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var operation = GetBoundOperation();

        lock (_gate)
        {
            if (((!_isOwnerExecutionStarted) && (receipt.Status is WorldLoadStatus.Loaded)) || _isCompleted)
            {
                throw new InvalidOperationException("The World load execution has no incomplete owner-boundary result.");
            }

            _isCompleted = true;
        }

        var cleanupFailure = postPublicationFailure;

        void Release(Action cleanup)
        {
            try
            {
                cleanup();
            }
            catch (Exception exception)
            {
                cleanupFailure = (cleanupFailure is { } currentFailure)
                    ? new AggregateException(currentFailure, exception)
                    : exception;
            }
        }

        if (receipt.Status is WorldLoadStatus.Loaded)
        {
            Release(TargetWorld.Dispose);
            Release(_state.Dispose);
            Release(operation.EndAfterConsumerCleanup);

            if (cleanupFailure is null)
            {
                _ = _oldWorldRetirement.TrySetResult();
            }
            else
            {
                _ = _oldWorldRetirement.TrySetException(
                    cleanupFailure
                        ?? throw new InvalidOperationException("The old World retirement failure was lost."));
            }
        }
        else
        {
            Release(_state.Dispose);
            Release(operation.AbortAtOwnerBoundary);
            Release(operation.EndAfterConsumerCleanup);

            if (cleanupFailure is not null)
            {
                _ = _oldWorldRetirement.TrySetException(
                    cleanupFailure
                        ?? throw new InvalidOperationException("The rejected World retirement failure was lost."));
            }
        }

        Release(EndLoadOperation);

        if (cleanupFailure is null || (receipt.Status is WorldLoadStatus.Loaded))
        {
            _ = _completion.TrySetResult(receipt);
        }
        else
        {
            _ = _completion.TrySetException(
                cleanupFailure
                    ?? throw new InvalidOperationException("The World load completion failure was lost."));
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "The failure is delivered through the accepted load task while remaining cleanup owners stay rooted.")]
    private void DisposeRejectedExecution(WorldReplacementOperation operation, Exception failure)
    {
        var cleanupFailure = failure;

        void Release(Action cleanup)
        {
            try
            {
                cleanup();
            }
            catch (Exception exception)
            {
                cleanupFailure = new AggregateException(cleanupFailure, exception);
            }
        }

        Release(_state.Dispose);
        Release(operation.EndAfterConsumerCleanup);
        Release(EndLoadOperation);

        lock (_gate)
        {
            _isCompleted = true;
        }

        _ = _completion.TrySetException(cleanupFailure);
    }

    private void EndLoadOperation()
    {
        if (Interlocked.Exchange(ref _isLoadOperationEnded, 1) == 0)
        {
            Persistence.EndLoadOperation();
        }
    }

    private WorldReplacementOperation GetBoundOperation()
    {
        lock (_gate)
        {
            return _operation
                ?? throw new InvalidOperationException("The World load execution has not been bound to its captured operation.");
        }
    }
}
