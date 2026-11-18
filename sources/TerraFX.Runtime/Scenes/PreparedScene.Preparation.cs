// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Entities;

namespace TerraFX.Scenes;

public sealed partial class PreparedScene
{
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Unescaped entry cleanup retains its debt without replacing the original synchronous freeze failure.")]
    internal void AbortPreparationEntry()
    {
        try
        {
            CleanupBuildingBody();

            lock (_gate)
            {
                _completion = null;
                _faultPair = null;
                _publicationAccessEnded = true;
            }

            EndPreparationProducer();
        }
        catch (Exception cleanupFailure)
        {
            RecordPreparationCleanupFailure(cleanupFailure, stage: 1, category: 4);
        }
    }

    internal void CancelPendingOwnerConstruction()
    {
        bool canceled;

        lock (_gate)
        {
            canceled = _pendingOwnerConstruction && (_pendingOwner is null);

            if (canceled)
            {
                _pendingOwnerConstruction = false;
            }
        }

        if (!canceled)
        {
            throw new InvalidOperationException("The Scene preparation has no elected pending-owner construction to cancel.");
        }
    }

    [SuppressMessage(
        "Style",
        "IDE0059:Unnecessary assignment of a value",
        Justification = "The entry part spans synchronous ExecutionContext restoration and drops its driver references before ending.")]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Admitted preparation failures are delivered by the owned promise after body cleanup.")]
    internal void StartPreparation(SceneLoader loader, CancellationToken cancellationToken)
    {
        bool started;

        lock (_gate)
        {
            started = (_producerState is PreparationProducerState.Entry)
                && (_bodyState is PreparationBodyState.Building)
                && (_options.MaximumPreparationBytes == _maximumPreparationBytes);

            if (started)
            {
                _loader = loader;
                _cancellationToken = cancellationToken;
                _producerState = PreparationProducerState.Running;
            }
        }

        if (!started)
        {
            throw new InvalidOperationException("The Scene preparation cannot start its ordinary producer.");
        }

        ReserveProducerPart(PreparationProducerPart.Entry);
        EnterProducerPart(PreparationProducerPart.Entry);
        var entry = new PreparationEntry(this);
        var contextBoundary = AsyncTaskMethodBuilder.Create();

        try
        {
            // Start preserves the old async entry's context isolation, including suppressed
            // flow. No builder Task is requested; the public promise is exclusively the TCS.
            contextBoundary.Start(ref entry);
        }
        catch (Exception failure)
        {
            HandlePreparationFailure(failure);
        }
        finally
        {
            entry = default;
            contextBoundary = default;
            EndProducerPart(PreparationProducerPart.Entry);
        }
    }

    internal bool TryBeginPendingOwnerConstruction()
    {
        lock (_gate)
        {
            if ((_bodyState is not PreparationBodyState.Building)
                || (_pendingOwner is not null)
                || _pendingOwnerConstruction)
            {
                return false;
            }

            _pendingOwnerConstruction = true;
            return true;
        }
    }

    internal bool TryClaimProducerWorkspace(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);

        lock (_gate)
        {
            if ((_bodyState is not PreparationBodyState.Building)
                || (_producerState is not PreparationProducerState.Running)
                || (bytes > (_producerWorkspaceBytes - _producerWorkspaceClaimedBytes)))
            {
                return false;
            }

            _producerWorkspaceClaimedBytes = checked(_producerWorkspaceClaimedBytes + bytes);
            return true;
        }
    }

    internal bool TryCommitPendingOwnerConstruction(IDisposable owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        lock (_gate)
        {
            if ((_bodyState is not PreparationBodyState.Building)
                || !_pendingOwnerConstruction
                || (_pendingOwner is not null)
                || ReferenceEquals(_phaseOwner, owner))
            {
                return false;
            }

            _pendingOwner = owner;
            _pendingOwnerCleanupFailed = false;
            _pendingOwnerConstruction = false;
            return true;
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "A failed preclaimed receiver construction returns only its original scalar subclaim before rethrowing.")]
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The Scene bundle caller immediately stages the preclaimed reservation and owns its cancellation/transfer path.")]
    internal WorldSceneStorageReservation? TryReserveProducerStorage(
        IDisposable expectedOwner,
        IWorldSceneParticipant participant,
        long dataBytes,
        long retainedDataClaimBytes)
    {
        ArgumentNullException.ThrowIfNull(expectedOwner);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dataBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(retainedDataClaimBytes);

        if (retainedDataClaimBytes != dataBytes)
        {
            throw new ArgumentException("The retained data claim must exactly match the reserved Scene storage data.", nameof(retainedDataClaimBytes));
        }

        var reservationBytes = WorldSceneStorageReservation.GetRequiredBytes(dataBytes);
        var constructionBytes = reservationBytes - retainedDataClaimBytes;

        lock (_gate)
        {
            if ((_bodyState is not PreparationBodyState.Building)
                || (_producerState is not PreparationProducerState.Running)
                || !ReferenceEquals(_pendingOwner, expectedOwner)
                || _producerStorageConstructionAttempted
                || (_producerWorkspaceClaimedBytes != retainedDataClaimBytes)
                || (constructionBytes > (_producerWorkspaceBytes - _producerWorkspaceClaimedBytes)))
            {
                return null;
            }

            _producerStorageConstructionAttempted = true;
            _producerWorkspaceClaimedBytes = checked(_producerWorkspaceClaimedBytes + constructionBytes);
        }

        WorldSceneStorageReservation? reservation;

        try
        {
            reservation = _world.TryReserveSceneWorkspaceFromPreclaimed(participant, _use.Control, dataBytes);
        }
        catch
        {
            if (!TryReturnProducerWorkspaceClaim(constructionBytes))
            {
                throw new InvalidOperationException("The failed preclaimed Scene storage construction lost its scalar workspace claim.");
            }

            throw;
        }

        if (reservation is null)
        {
            if (!TryReturnProducerWorkspaceClaim(constructionBytes))
            {
                throw new InvalidOperationException("The refused preclaimed Scene storage construction lost its scalar workspace claim.");
            }

            return null;
        }

        lock (_gate)
        {
            if ((_producerWorkspaceClaimedBytes < reservationBytes)
                || (_producerWorkspaceBytes < reservationBytes))
            {
                throw new InvalidOperationException("The admitted preclaimed Scene storage construction lost its scalar workspace claim.");
            }

            _producerWorkspaceClaimedBytes -= reservationBytes;
            _producerWorkspaceBytes -= reservationBytes;
        }

        return reservation;
    }

    internal bool TryReturnProducerWorkspaceClaim(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);

        lock (_gate)
        {
            if (_producerWorkspaceClaimedBytes < bytes)
            {
                return false;
            }

            _producerWorkspaceClaimedBytes -= bytes;
            return true;
        }
    }

    internal void ValidatePendingOwnerResult(IDisposable? owner)
    {
        bool matches;

        lock (_gate)
        {
            matches = !_pendingOwnerConstruction
                && ((owner is null)
                    ? (_pendingOwner is null)
                    : ReferenceEquals(_pendingOwner, owner));
        }

        if (!matches)
        {
            throw new InvalidOperationException("The resource producer did not return its exact pre-staged preparation owner.");
        }
    }

    private void AttachPendingPreparedAssets()
    {
        SceneExpansionPlan? plan;
        IDisposable? pendingOwner;

        lock (_gate)
        {
            plan = _phaseOwner as SceneExpansionPlan;
            pendingOwner = _pendingOwner;
        }

        if ((plan is null) || (pendingOwner is null))
        {
            throw new InvalidOperationException("The Scene preparation lost its pending resource receiver.");
        }

        plan.AttachPreparedAssets(pendingOwner, this);
        AcknowledgePendingOwnerTransfer(pendingOwner, plan);
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "A failed body-cleanup claim return is sampled before the owned outcome is published.")]
    private void CleanupBuildingBody()
    {
        lock (_gate)
        {
            if (_bodyState is PreparationBodyState.Building)
            {
                _bodyState = PreparationBodyState.Cleaning;
            }

            _loader = null;
            _cancellationToken = default;
            _pendingPlanTask = null;
            _pendingResourcesTask = null;
            _options = default;
        }

        CleanupPreparationOwner(pending: true);
        CleanupPreparationOwner(pending: false);
        _ = ReturnWorkspace(PreparationWorkspace.TemporaryOptions, stage: 2);
        _ = ReturnWorkspace(PreparationWorkspace.Options, stage: 2);
        bool cleaned;

        lock (_gate)
        {
            cleaned = (_bodyState is PreparationBodyState.Cleaning)
                && (_pendingOwner is null)
                && !_pendingOwnerConstruction
                && (_phaseOwner is null);
        }

        if (cleaned)
        {
            try
            {
                CompleteBodyCleanup(request: null);
            }
            catch (Exception cleanupFailure)
            {
                RecordPreparationCleanupFailure(cleanupFailure, stage: 2, category: 4);
            }
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Independent cleanup continues while failed owners and exact release debts remain anchored.")]
    private void CleanupPreparationOwner(bool pending)
    {
        IDisposable? owner;
        bool cleanupFailed;

        lock (_gate)
        {
            owner = pending ? _pendingOwner : _phaseOwner;
            cleanupFailed = pending ? _pendingOwnerCleanupFailed : _phaseOwnerCleanupFailed;
        }

        if ((owner is null) || cleanupFailed)
        {
            return;
        }

        try
        {
            owner.Dispose();
            AcknowledgePreparationOwnerRelease(owner);
        }
        catch (Exception failure)
        {
            RecordPreparationCleanupFailure(failure, stage: 2, category: 2, owner);
        }
    }

    private void CompleteReadyPreparation()
    {
        _cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _loader = null;
            _cancellationToken = default;
            _pendingPlanTask = null;
            _pendingResourcesTask = null;
        }

        InstallPreparedBody();
        PublishPreparationOutcome(ScenePreparationOutcome.Prepared, failure: null, cancellationToken: default);
    }

    private void EndPreparationProducer()
    {
        lock (_gate)
        {
            if ((_producerState is PreparationProducerState.Ended or PreparationProducerState.Quarantined)
                || !_publicationAccessEnded
                || (_producerParts != 0)
                || _supplierProducersUnjoined)
            {
                return;
            }

            _producerState = PreparationProducerState.Ended;
        }

        var cleanupFailure = ReturnWorkspace(PreparationWorkspace.Publication, stage: 4);

        if (cleanupFailure is not null)
        {
            throw cleanupFailure;
        }

        cleanupFailure = ReturnWorkspace(PreparationWorkspace.Producer, stage: 4);

        if (cleanupFailure is not null)
        {
            throw cleanupFailure;
        }

        TryReleasePreparationUse();
    }

    private void EndProducerPart(PreparationProducerPart part)
    {
        bool ended;
        ref var state = ref GetProducerPartState(part);

        lock (_gate)
        {
            ended = (state is PreparationPartState.Running) && (_producerParts > 0);

            if (ended)
            {
                state = PreparationPartState.Ended;
                _producerParts--;
            }
        }

        if (!ended)
        {
            throw new InvalidOperationException("The Scene preparation producer part cannot end twice or before entering.");
        }

        EndPreparationProducer();
    }

    private void EnterProducerPart(PreparationProducerPart part)
    {
        bool entered;
        ref var state = ref GetProducerPartState(part);

        lock (_gate)
        {
            entered = state is PreparationPartState.Pending;

            if (entered)
            {
                state = PreparationPartState.Running;
            }
        }

        if (!entered)
        {
            throw new InvalidOperationException("The Scene preparation callback does not own its reserved producer part.");
        }
    }

    private ref PreparationPartState GetProducerPartState(PreparationProducerPart part)
    {
        switch (part)
        {
            case PreparationProducerPart.Entry:
            {
                return ref _entryPartState;
            }

            case PreparationProducerPart.PlanCompletion:
            {
                return ref _planPartState;
            }

            case PreparationProducerPart.ResourceCompletion:
            {
                return ref _resourcePartState;
            }

            default:
            {
                throw new InvalidOperationException("The Scene preparation selected an unknown producer part.");
            }
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "An original execution failure and one original cleanup sample share the bounded preparation fault channel.")]
    private void HandlePreparationFailure(Exception failure)
    {
        bool outcomeSelected;

        lock (_gate)
        {
            outcomeSelected = _outcome is not ScenePreparationOutcome.None;
        }

        if (outcomeSelected)
        {
            QuarantinePreparationProducer(failure);
            return;
        }

        CleanupBuildingBody();

        try
        {
            if (failure is OperationCanceledException canceled)
            {
                PublishPreparationOutcome(ScenePreparationOutcome.Canceled, failure: null, canceled.CancellationToken);
            }
            else
            {
                PublishPreparationOutcome(ScenePreparationOutcome.Faulted, failure, cancellationToken: default);
            }
        }
        catch (Exception publicationFailure)
        {
            QuarantinePreparationProducer(publicationFailure);
        }
    }

    private void InstallPreparedBody()
    {
        bool installed;

        lock (_gate)
        {
            installed = (_bodyState is PreparationBodyState.Building)
                && (_phaseOwner is SceneExpansionPlan)
                && (_pendingOwner is null)
                && (_options.MaximumPreparationBytes == _maximumPreparationBytes)
                && (_registrationFailure is null);

            if (installed)
            {
                _plan = (SceneExpansionPlan?)_phaseOwner;
                _phaseOwner = null;
                _bodyState = PreparationBodyState.Available;
            }
        }

        if (!installed)
        {
            throw new InvalidOperationException("The Scene preparation cannot publish an incomplete prepared body.");
        }
    }

    private void MarkPublicationAccessEnded()
    {
        lock (_gate)
        {
            _completion = null;
            _faultPair = null;
            _registrationFailure = null;
            _publicationAccessEnded = true;
        }

        EndPreparationProducer();
    }

    [SuppressMessage(
        "Style",
        "IDE0059:Unnecessary assignment of a value",
        Justification = "Drop child-task and plan aliases before another stage can publish or hand off the body.")]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "The reserved completion part delivers admitted plan failures through the public preparation promise.")]
    private void OnPlanPrepared()
    {
        EnterProducerPart(PreparationProducerPart.PlanCompletion);
        Task<SceneExpansionPlan>? task = null;
        SceneExpansionPlan? plan = null;
        Task<IDisposable?>? resourcesTask = null;

        try
        {
            task = _pendingPlanTask
                ?? throw new InvalidOperationException("The Scene preparation lost its pending plan Task.");
            plan = task.GetAwaiter().GetResult();
            task = null;
            _pendingPlanTask = null;
            ValidateCurrentPreparationPlan(plan);
            plan = null;
            ThrowIfProducerRegistrationFailed();
            resourcesTask = PrepareResourcesForPlan();
            _loader = null;

            if (resourcesTask is null)
            {
                CompleteReadyPreparation();
            }
            else
            {
                _pendingResourcesTask = resourcesTask;
                resourcesTask = null;
                ReserveProducerPart(PreparationProducerPart.ResourceCompletion);
                RegisterResourceCompletion();
            }
        }
        catch (Exception failure)
        {
            task = null;
            plan = null;
            resourcesTask = null;
            HandlePreparationFailure(failure);
        }
        finally
        {
            EndProducerPart(PreparationProducerPart.PlanCompletion);
        }
    }

    [SuppressMessage(
        "Style",
        "IDE0059:Unnecessary assignment of a value",
        Justification = "Drop child-task and resource aliases before publishing or ending their preparation-funded consumers.")]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "The reserved completion part delivers admitted resource failures through the public preparation promise.")]
    private void OnResourcesPrepared()
    {
        EnterProducerPart(PreparationProducerPart.ResourceCompletion);
        Task<IDisposable?>? task = null;
        IDisposable? resources = null;

        try
        {
            task = _pendingResourcesTask
                ?? throw new InvalidOperationException("The Scene preparation lost its pending resource Task.");
            resources = task.GetAwaiter().GetResult();
            ValidatePendingOwnerResult(resources);

            resources = null;
            task = null;
            _pendingResourcesTask = null;
            ThrowIfProducerRegistrationFailed();

            if (_pendingOwner is not null)
            {
                AttachPendingPreparedAssets();
            }

            CompleteReadyPreparation();
        }
        catch (Exception failure)
        {
            task = null;
            resources = null;
            HandlePreparationFailure(failure);
        }
        finally
        {
            EndProducerPart(PreparationProducerPart.ResourceCompletion);
        }
    }

    private Task<IDisposable?>? PrepareResourcesForPlan()
    {
        var loader = _loader
            ?? throw new InvalidOperationException("The Scene preparation lost its loader before validation.");
        var plan = _phaseOwner as SceneExpansionPlan
            ?? throw new InvalidOperationException("The Scene preparation lost its expansion plan before validation.");
        var preparer = loader.ValidatePreparedPlan(plan, _options, _cancellationToken);
        return (preparer is null)
            ? null
            : loader.StartResourcePreparation(preparer, plan, this, _cancellationToken);
    }

    [SuppressMessage(
        "Style",
        "IDE0059:Unnecessary assignment of a value",
        Justification = "The ordinary publisher must stop accessing its promise and failure array before marking publication returned.")]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Publication failure never recursively completes a replacement Task or rolls back a possibly published body.")]
    private void PublishPreparationOutcome(
        ScenePreparationOutcome outcome,
        Exception? failure,
        CancellationToken cancellationToken)
    {
        TaskCompletionSource<PreparedScene>? completion;
        Exception[]? faultPair;
        Exception? cleanupFailure;
        bool selected;

        lock (_gate)
        {
            selected = _outcome is ScenePreparationOutcome.None;
            completion = _completion;
            faultPair = _faultPair;
            cleanupFailure = _firstCleanupFailure;

            if (selected)
            {
                _outcome = outcome;

                if (_producerState is not PreparationProducerState.Quarantined)
                {
                    _producerState = PreparationProducerState.Publishing;
                }
            }
        }

        if (!selected || (completion is null) || (faultPair is null))
        {
            throw new InvalidOperationException("The Scene preparation outcome or publisher was already consumed.");
        }

        try
        {
            bool published;

            if ((outcome is ScenePreparationOutcome.Faulted)
                && (failure is not null)
                && (cleanupFailure is not null)
                && !ReferenceEquals(failure, cleanupFailure))
            {
                faultPair[0] = failure;
                faultPair[1] = cleanupFailure;
                published = completion.TrySetException(faultPair);
            }
            else if (cleanupFailure is not null)
            {
                published = completion.TrySetException(cleanupFailure);
            }
            else
            {
                published = outcome switch {
                    ScenePreparationOutcome.Prepared => completion.TrySetResult(this),
                    ScenePreparationOutcome.Faulted => completion.TrySetException(
                        failure ?? throw new InvalidOperationException("The Scene preparation fault has no original exception.")),
                    ScenePreparationOutcome.Canceled => completion.TrySetCanceled(cancellationToken),
                    _ => throw new InvalidOperationException("The Scene preparation selected an invalid publication outcome."),
                };
            }

            if (!published)
            {
                throw new InvalidOperationException("The Scene preparation promise was completed by another publisher.");
            }
        }
        catch (Exception publicationFailure)
        {
            QuarantinePreparationProducer(publicationFailure);
            throw;
        }

        completion = null;
        Array.Clear(faultPair);
        faultPair = null;
        failure = null;
        cleanupFailure = null;
        MarkPublicationAccessEnded();
    }

    private void QuarantinePreparationProducer(Exception failure)
    {
        lock (_gate)
        {
            _registrationFailure ??= failure;
            _producerState = PreparationProducerState.Quarantined;
            _useReleaseState = PreparationUseReleaseState.Quarantined;
        }

        PreparationControl.Quarantine(this, failureStage: 2, failureCategory: 5);
    }

    [SuppressMessage(
        "Style",
        "IDE0059:Unnecessary assignment of a value",
        Justification = "An inline callback must not inherit a child Task alias from its registering producer.")]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Uncertain registration retains the reserved callback part rather than refunding a possibly published callback.")]
    private void RegisterPlanCompletion()
    {
        var awaiter = (_pendingPlanTask
            ?? throw new InvalidOperationException("The Scene preparation has no plan Task to register."))
            .ConfigureAwait(false).GetAwaiter();

        if (awaiter.IsCompleted)
        {
            awaiter = default;
            OnPlanPrepared();
            return;
        }

        try
        {
            awaiter.OnCompleted(_planReadyCallback);
        }
        catch (Exception failure)
        {
            QuarantinePreparationProducer(failure);
        }
    }

    [SuppressMessage(
        "Style",
        "IDE0059:Unnecessary assignment of a value",
        Justification = "An inline callback must not inherit a child Task alias from its registering producer.")]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Uncertain registration retains the reserved callback part rather than refunding a possibly published callback.")]
    private void RegisterResourceCompletion()
    {
        var awaiter = (_pendingResourcesTask
            ?? throw new InvalidOperationException("The Scene preparation has no resource Task to register."))
            .ConfigureAwait(false).GetAwaiter();

        if (awaiter.IsCompleted)
        {
            awaiter = default;
            OnResourcesPrepared();
            return;
        }

        try
        {
            awaiter.OnCompleted(_resourcesReadyCallback);
        }
        catch (Exception failure)
        {
            QuarantinePreparationProducer(failure);
        }
    }

    private void ReserveProducerPart(PreparationProducerPart part)
    {
        bool reserved;
        ref var state = ref GetProducerPartState(part);

        lock (_gate)
        {
            reserved = (state is PreparationPartState.Unused) && (_producerParts < 3);

            if (reserved)
            {
                state = PreparationPartState.Pending;
                _producerParts++;
            }
        }

        if (!reserved)
        {
            throw new InvalidOperationException("The Scene preparation producer part was already reserved.");
        }
    }

    [SuppressMessage(
        "Style",
        "IDE0059:Unnecessary assignment of a value",
        Justification = "End entry snapshot and Task aliases before enabling the plan callback.")]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Admitted supplier invocation failures are published only after rooted body cleanup.")]
    private void StartPlanPreparationCore()
    {
        Task<SceneExpansionPlan>? task = null;
        SceneLoadOptionsSnapshot options = default;
        SceneLoader? loader = null;

        try
        {
            loader = _loader
                ?? throw new InvalidOperationException("The Scene preparation lost its starting loader.");
            options = _options;

            // No supplier return acknowledgment exists yet. Observing either child Task
            // never clears this latch or releases supplier/registration provision.
            _supplierProducersUnjoined = true;
            task = loader.StartPlanPreparation(_assetId, options, this, _cancellationToken);
            options = default;
            loader = null;
            _pendingPlanTask = task;
            task = null;
            ReserveProducerPart(PreparationProducerPart.PlanCompletion);
            RegisterPlanCompletion();
        }
        catch (Exception failure)
        {
            task = null;
            options = default;
            loader = null;
            HandlePreparationFailure(failure);
        }
    }

    private void ThrowIfProducerRegistrationFailed()
    {
        Exception? failure;

        lock (_gate)
        {
            failure = _registrationFailure;
        }

        if (failure is not null)
        {
            throw failure;
        }
    }

    private void ValidateCurrentPreparationPlan(SceneExpansionPlan plan)
    {
        bool matched;

        lock (_gate)
        {
            matched = ReferenceEquals(_phaseOwner, plan);
        }

        if (!matched)
        {
            throw new InvalidOperationException("The Scene planner did not install its returned plan in the stable preparation root.");
        }
    }

    private readonly struct PreparationEntry(PreparedScene preparation) : IAsyncStateMachine
    {
        void IAsyncStateMachine.MoveNext()
            => preparation.StartPlanPreparationCore();

        void IAsyncStateMachine.SetStateMachine(IAsyncStateMachine stateMachine)
            => throw new InvalidOperationException("The Scene entry context boundary does not create an async state-machine box.");
    }
}
