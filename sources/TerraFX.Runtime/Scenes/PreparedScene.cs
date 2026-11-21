// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Assets;
using TerraFX.Entities;

namespace TerraFX.Scenes;

/// <summary>Owns one bounded Scene preparation for its exact target World and AssetStore.</summary>
public sealed partial class PreparedScene : IDisposable
{
    private int _additionalCleanupFailureCount;
    private readonly AssetId _assetId;
    private readonly AssetOwnedReadDescriptor _assetReadDescriptor;
    private readonly AssetStore _assets;
    private long _bodyCleanupWorkspaceBytes;
    private SceneActivationRequest? _bodyRequest;
    private PreparationBodyState _bodyState;
    private CancellationToken _cancellationToken;
    private TaskCompletionSource<PreparedScene>? _completion;
    private PreparationPartState _entryPartState;
    private int _failedWorkspaceReturns;
    private Exception[]? _faultPair;
    private Exception? _firstCleanupFailure;
    private readonly Lock _gate = new();
    private SceneLoader? _loader;
    private readonly long _maximumPreparationBytes;
    private SceneLoadOptionsSnapshot _options;
    private long _optionsWorkspaceBytes;
    private ScenePreparationOutcome _outcome;
    private IDisposable? _pendingOwner;
    private bool _pendingOwnerCleanupFailed;
    private bool _pendingOwnerConstruction;
    private Task<SceneExpansionPlan>? _pendingPlanTask;
    private Task<IDisposable?>? _pendingResourcesTask;
    private IDisposable? _phaseOwner;
    private bool _phaseOwnerCleanupFailed;
    private SceneExpansionPlan? _plan;
    private PreparationPartState _planPartState;
    private readonly Action _planReadyCallback;
    private int _producerParts;
    private PreparationProducerState _producerState;
    private bool _producerStorageConstructionAttempted;
    private long _producerWorkspaceBytes;
    private long _producerWorkspaceClaimedBytes;
    private bool _publicationAccessEnded;
    private long _publicationWorkspaceBytes;
    private Exception? _registrationFailure;
    private PreparationPartState _resourcePartState;
    private readonly Action _resourcesReadyCallback;
    private long _sharedWorkspaceBytes;
    private bool _supplierProducersUnjoined;
    private long _temporaryOptionsWorkspaceBytes;
    private WorldGuidUse _use;
    private PreparationUseReleaseState _useReleaseState;
    private readonly World _world;

    private PreparedScene(
        World world,
        AssetStore assets,
        AssetId sceneId,
        WorldGuidUse use,
        long maximumPreparationBytes,
        PreparationFootprint footprint,
        AssetOwnedReadDescriptor assetReadDescriptor)
    {
        _world = world;
        _assets = assets;
        _assetId = sceneId;
        _assetReadDescriptor = assetReadDescriptor;
        _use = use;
        _maximumPreparationBytes = maximumPreparationBytes;
        _sharedWorkspaceBytes = footprint.SharedWorkspaceBytes;
        _producerWorkspaceBytes = footprint.ProducerWorkspaceBytes;
        _bodyCleanupWorkspaceBytes = footprint.BodyCleanupWorkspaceBytes;
        _publicationWorkspaceBytes = footprint.PublicationWorkspaceBytes;
        _completion = new TaskCompletionSource<PreparedScene>(TaskCreationOptions.RunContinuationsAsynchronously);
        _faultPair = new Exception[2];
        _planReadyCallback = OnPlanPrepared;
        _resourcesReadyCallback = OnResourcesPrepared;
    }

    // This compile-only candidate has no qualified J/K catalogue. Null is an unavailable
    // profile, not a zero-byte allowance; entry must not construct or execute the candidate.
    private static PreparationFootprint? QualifiedFootprint => null;

    /// <summary>Gets the prepared root Scene asset identity.</summary>
    public AssetId AssetId => _assetId;

    internal AssetOwnedReadDescriptor AssetReadDescriptor => _assetReadDescriptor;

    internal AssetStore Assets => _assets;

    internal Exception? CleanupFailure
    {
        get
        {
            lock (_gate)
            {
                return _firstCleanupFailure;
            }
        }
    }

    internal WorldGuidUseControl PreparationControl => _use.Control;

    internal Task<PreparedScene> PreparationTask
        => (_completion ?? throw new InvalidOperationException("The preparation promise is no longer owned by its producer.")).Task;

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Unescaped construction failure retains exact unreleased claims in the preowned use anchor.")]
    internal static PreparedScene CreatePreparation(
        World world,
        AssetStore assets,
        AssetId sceneId,
        long maximumPreparationBytes)
    {
        var footprint = QualifiedFootprint
            ?? throw new InvalidOperationException(
                "Scene preparation is a compile-only candidate until its J/K catalogue and producer last-access gates are closed.");
        var workspaceBytes = checked(
            footprint.SharedWorkspaceBytes
            + footprint.ProducerWorkspaceBytes
            + footprint.BodyCleanupWorkspaceBytes
            + footprint.PublicationWorkspaceBytes);

        if (world.GetCpuExtractionParticipant<ISceneExpansionResourcePreparer>() is not null)
        {
            throw new NotSupportedException(
                "Scene preparation with CPU resource preparation is unavailable until its operation and resource profiles are admitted.");
        }

        var assetReadDescriptor = SceneSupport.DescribeSceneAssetRead(assets, sceneId);
        var use = world.AcquireGuidUse(maximumPreparationBytes);
        var control = use.Control;

        if (!control.TryReserveWorkspace(workspaceBytes))
        {
            use.Dispose();
            throw new InvalidOperationException("The selected Scene preparation cap cannot fund its preparation and publication bootstrap.");
        }

        PreparedScene? preparation = null;

        try
        {
            preparation = new PreparedScene(
                world,
                assets,
                sceneId,
                use,
                maximumPreparationBytes,
                footprint,
                assetReadDescriptor);
            control.SetRetainedOwner(preparation);
            return preparation;
        }
        catch
        {
            try
            {
                if (!control.TryReleaseWorkspace(workspaceBytes))
                {
                    throw new InvalidOperationException("The unescaped Scene preparation could not return its bootstrap claims.");
                }

                use.Dispose();
            }
            catch (Exception cleanupFailure)
            {
                if (preparation is not null)
                {
                    preparation.RecordPreparationCleanupFailure(cleanupFailure, stage: 1, category: 4);
                }
                else
                {
                    control.Quarantine(control, failureStage: 1, failureCategory: 4);
                }
            }

            throw;
        }
    }

    internal static void ThrowForPreparationAccess(ScenePreparationAccess access)
    {
        switch (access)
        {
            case ScenePreparationAccess.Available:
            {
                return;
            }

            case ScenePreparationAccess.AlreadyAdmitted:
            {
                throw new InvalidOperationException("A prepared Scene can be admitted for activation only once.");
            }

            case ScenePreparationAccess.WrongWorld:
            {
                throw new ArgumentException("The prepared Scene is bound to another exact World.");
            }

            case ScenePreparationAccess.WrongAssetStore:
            {
                throw new ArgumentException("The prepared Scene is bound to another exact AssetStore.");
            }

            case ScenePreparationAccess.PolicyMismatch:
            {
                throw new InvalidOperationException("The Scene activation policy does not match its preparation.");
            }

            case ScenePreparationAccess.NotAvailable:
            {
                throw new ObjectDisposedException(nameof(PreparedScene));
            }

            default:
            {
                throw new InvalidOperationException("The Scene preparation returned an unknown access decision.");
            }
        }
    }

    /// <summary>Releases the prepared Scene's package-backed asset leases.</summary>
    [SuppressMessage(
        "Usage",
        "CA1065:Do not raise exceptions in unexpected locations",
        Justification = "A failed physical cleanup or workspace-credit return is surfaced with its exact graph still anchored.")]
    [SuppressMessage(
        "Style",
        "IDE0059:Unnecessary assignment of a value",
        Justification = "End the disposed plan alias before returning the body's preparation claims.")]
    public void Dispose()
    {
        SceneExpansionPlan? plan;

        lock (_gate)
        {
            if (_bodyState is not PreparationBodyState.Available)
            {
                return;
            }

            _bodyState = PreparationBodyState.Cleaning;
            plan = _plan;
        }

        try
        {
            plan?.Dispose();
        }
        catch (Exception failure)
        {
            RecordPreparationCleanupFailure(failure, stage: 1, category: 1);
            ClearBodyOptions();
            _ = ReturnWorkspace(PreparationWorkspace.Options, stage: 1);
            throw;
        }

        lock (_gate)
        {
            _plan = null;
        }

        plan = null;
        ClearBodyOptions();
        var cleanupFailure = ReturnWorkspace(PreparationWorkspace.Options, stage: 1);

        if (cleanupFailure is not null)
        {
            throw cleanupFailure;
        }

        CompleteBodyCleanup(request: null);
    }

    internal void AcknowledgePendingOwnerTransfer(IDisposable expectedPendingOwner, IDisposable receiver)
    {
        bool transferred;

        lock (_gate)
        {
            transferred = ReferenceEquals(_pendingOwner, expectedPendingOwner)
                && ReferenceEquals(_phaseOwner, receiver)
                && !_pendingOwnerCleanupFailed;

            if (transferred)
            {
                _pendingOwner = null;
            }
        }

        if (!transferred)
        {
            throw new InvalidOperationException("The Scene preparation cannot acknowledge this pending receiver transfer.");
        }
    }

    internal void AcknowledgePreparationOwnerRelease(IDisposable owner)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_phaseOwner, owner) && !_phaseOwnerCleanupFailed)
            {
                _phaseOwner = null;
            }

            if (ReferenceEquals(_pendingOwner, owner) && !_pendingOwnerCleanupFailed)
            {
                _pendingOwner = null;
            }
        }
    }

    internal PreparedAssetsLifetime.Lease? AcquirePreparedAssets()
    {
        ScenePreparationAccess access;
        SceneExpansionPlan? plan;

        lock (_gate)
        {
            access = GetBodyAccess();
            plan = (access is ScenePreparationAccess.Available) ? _plan : null;
        }

        ThrowForPreparationAccess(access);
        return (plan ?? throw new ObjectDisposedException(nameof(PreparedScene))).AcquirePreparedAssets();
    }

    internal ScenePreparationAccess CaptureActivationHeader(
        World world,
        AssetStore assets,
        out long maximumPreparationBytes)
    {
        lock (_gate)
        {
            var access = GetActivationAccess(world, assets);
            maximumPreparationBytes = (access is ScenePreparationAccess.Available)
                ? _maximumPreparationBytes
                : 0;
            return access;
        }
    }

    internal void CommitPreparationOwner(IDisposable? expectedOwner)
    {
        bool committed;

        lock (_gate)
        {
            committed = ReferenceEquals(_phaseOwner, expectedOwner)
                && (_pendingOwner is not null)
                && !_phaseOwnerCleanupFailed
                && !_pendingOwnerCleanupFailed;

            if (committed)
            {
                _phaseOwner = _pendingOwner;
                _pendingOwner = null;
            }
        }

        if (!committed)
        {
            throw new InvalidOperationException("The Scene preparation cannot commit this staged owner.");
        }
    }

    internal void CompleteAdmittedBody(SceneActivationRequest request)
    {
        bool matched;

        lock (_gate)
        {
            matched = (_bodyState is PreparationBodyState.Admitted)
                && ReferenceEquals(_bodyRequest, request);

            if (matched)
            {
                _bodyState = PreparationBodyState.AdmittedCleaning;
            }
        }

        if (!matched)
        {
            throw new InvalidOperationException("The Scene preparation body does not belong to this activation request.");
        }

        var cleanupFailure = ReturnWorkspace(PreparationWorkspace.Options, stage: 3);

        if (cleanupFailure is not null)
        {
            throw cleanupFailure;
        }

        CompleteBodyCleanup(request);
    }

    internal ScenePreparationAccess DetachForActivation(
        World world,
        AssetStore assets,
        SceneActivationRequest request,
        long expectedMaximumPreparationBytes)
    {
        lock (_gate)
        {
            var access = GetActivationAccess(world, assets);

            if (access is not ScenePreparationAccess.Available)
            {
                return access;
            }

            if (expectedMaximumPreparationBytes != _maximumPreparationBytes)
            {
                return ScenePreparationAccess.PolicyMismatch;
            }

            if (_plan is not { } plan)
            {
                return ScenePreparationAccess.NotAvailable;
            }

            request.AcceptPreparationBody(this, plan, _options);
            _bodyRequest = request;
            _plan = null;
            _options = default;
            _bodyState = PreparationBodyState.Admitted;
            return ScenePreparationAccess.Available;
        }
    }

    internal void FreezeOptions(SceneLoadOptions options, long bindingArrayBytes)
    {
        var freezeWorkspaceBytes = checked(2 * bindingArrayBytes);

        if ((freezeWorkspaceBytes > 0) && !PreparationControl.TryReserveWorkspace(freezeWorkspaceBytes))
        {
            throw new InvalidOperationException("The selected Scene preparation cap cannot fund its external-binding copies.");
        }

        _optionsWorkspaceBytes = bindingArrayBytes;
        _temporaryOptionsWorkspaceBytes = bindingArrayBytes;
        _options = options.ValidateAndFreeze();

        if (_options.MaximumPreparationBytes != _maximumPreparationBytes)
        {
            throw new InvalidOperationException("The frozen Scene preparation cap changed after admission.");
        }

        var cleanupFailure = ReturnWorkspace(PreparationWorkspace.TemporaryOptions, stage: 1);

        if (cleanupFailure is not null)
        {
            throw cleanupFailure;
        }
    }

    internal void QuarantineAdmittedBody(
        SceneActivationRequest request,
        Exception cleanupFailure,
        int stage,
        int category)
    {
        bool matched;

        lock (_gate)
        {
            matched = ReferenceEquals(_bodyRequest, request)
                && (_bodyState is PreparationBodyState.Admitted
                    or PreparationBodyState.AdmittedCleaning
                    or PreparationBodyState.AdmittedQuarantined);
        }

        if (!matched)
        {
            throw new InvalidOperationException("The failed Scene preparation body does not belong to this activation request.");
        }

        RecordPreparationCleanupFailure(cleanupFailure, stage, category);
        _ = ReturnWorkspace(PreparationWorkspace.Options, stage);
    }

    internal void RecordPreparationCleanupFailure(
        Exception failure,
        int stage,
        int category,
        IDisposable? failedOwner = null,
        WorldGuidUseControl? useControl = null)
    {
        lock (_gate)
        {
            if (_firstCleanupFailure is null)
            {
                _firstCleanupFailure = failure;
            }
            else if (!ReferenceEquals(_firstCleanupFailure, failure)
                && (_additionalCleanupFailureCount != int.MaxValue))
            {
                _additionalCleanupFailureCount++;
            }

            if (ReferenceEquals(_phaseOwner, failedOwner) && (failedOwner is not null))
            {
                _phaseOwnerCleanupFailed = true;
            }

            if (ReferenceEquals(_pendingOwner, failedOwner) && (failedOwner is not null))
            {
                _pendingOwnerCleanupFailed = true;
            }

            _bodyState = (_bodyRequest is null)
                ? PreparationBodyState.Quarantined
                : PreparationBodyState.AdmittedQuarantined;
            _useReleaseState = PreparationUseReleaseState.Quarantined;
        }

        (useControl ?? _use.Control).Quarantine(this, stage, category);
    }

    internal void StagePreparationOwner(IDisposable nextOwner)
    {
        bool staged;

        lock (_gate)
        {
            staged = (_bodyState is PreparationBodyState.Building)
                && (_pendingOwner is null)
                && !_pendingOwnerConstruction
                && !ReferenceEquals(_phaseOwner, nextOwner);

            if (staged)
            {
                _pendingOwner = nextOwner;
                _pendingOwnerCleanupFailed = false;
            }
        }

        if (!staged)
        {
            throw new InvalidOperationException("The Scene preparation already owns a pending receiver or has ended body construction.");
        }
    }

    private void ClearBodyOptions()
    {
        lock (_gate)
        {
            _options = default;
        }
    }

    private void CompleteBodyCleanup(SceneActivationRequest? request)
    {
        var cleanupFailure = ReturnWorkspace(PreparationWorkspace.BodyCleanup, stage: 3);

        if (cleanupFailure is not null)
        {
            throw cleanupFailure;
        }

        lock (_gate)
        {
            _bodyState = (request is null)
                ? PreparationBodyState.Ended
                : PreparationBodyState.AdmittedEnded;
            _bodyRequest = null;
        }

        TryReleasePreparationUse();
    }

    private ScenePreparationAccess GetActivationAccess(World world, AssetStore assets)
    {
        if (!ReferenceEquals(_world, world))
        {
            return ScenePreparationAccess.WrongWorld;
        }

        if (!ReferenceEquals(_assets, assets))
        {
            return ScenePreparationAccess.WrongAssetStore;
        }

        return GetBodyAccess();
    }

    private ScenePreparationAccess GetBodyAccess()
    {
        return _bodyState switch {
            PreparationBodyState.Available => ScenePreparationAccess.Available,
            PreparationBodyState.Admitted
                or PreparationBodyState.AdmittedCleaning
                or PreparationBodyState.AdmittedEnded
                or PreparationBodyState.AdmittedQuarantined => ScenePreparationAccess.AlreadyAdmitted,
            _ => ScenePreparationAccess.NotAvailable,
        };
    }

    private ref long GetWorkspaceLedger(PreparationWorkspace workspace)
    {
        switch (workspace)
        {
            case PreparationWorkspace.Shared:
            {
                return ref _sharedWorkspaceBytes;
            }

            case PreparationWorkspace.Producer:
            {
                return ref _producerWorkspaceBytes;
            }

            case PreparationWorkspace.BodyCleanup:
            {
                return ref _bodyCleanupWorkspaceBytes;
            }

            case PreparationWorkspace.Publication:
            {
                return ref _publicationWorkspaceBytes;
            }

            case PreparationWorkspace.Options:
            {
                return ref _optionsWorkspaceBytes;
            }

            case PreparationWorkspace.TemporaryOptions:
            {
                return ref _temporaryOptionsWorkspaceBytes;
            }

            default:
            {
                throw new InvalidOperationException("The Scene preparation selected an unknown workspace ledger.");
            }
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "A failed credit return retains its numeric debt and original cleanup sample in the stable root.")]
    private Exception? ReturnWorkspace(PreparationWorkspace workspace, int stage)
    {
        ref var ledger = ref GetWorkspaceLedger(workspace);
        var failureMask = 1 << (int)workspace;
        long bytes;
        bool returnFailed;
        bool producerClaimsOutstanding;
        Exception? previousFailure;

        lock (_gate)
        {
            bytes = ledger;
            returnFailed = (_failedWorkspaceReturns & failureMask) != 0;
            producerClaimsOutstanding = (workspace is PreparationWorkspace.Producer)
                && (_producerWorkspaceClaimedBytes != 0);
            previousFailure = _firstCleanupFailure;
        }

        if (returnFailed)
        {
            return previousFailure
                ?? new InvalidOperationException("The Scene preparation lost its failed workspace-return sample.");
        }

        if (producerClaimsOutstanding)
        {
            var failure = new InvalidOperationException("The Scene preparation cannot return producer workspace while a child claim is still live.");
            RecordPreparationCleanupFailure(failure, stage, category: 4);

            lock (_gate)
            {
                _failedWorkspaceReturns |= failureMask;
            }

            return failure;
        }

        if (bytes == 0)
        {
            return null;
        }

        try
        {
            if (!PreparationControl.TryReleaseWorkspace(bytes))
            {
                throw new InvalidOperationException("The Scene preparation could not return its owned workspace claim.");
            }

            lock (_gate)
            {
                ledger = 0;
            }

            return null;
        }
        catch (Exception failure)
        {
            RecordPreparationCleanupFailure(failure, stage, category: 4);

            lock (_gate)
            {
                _failedWorkspaceReturns |= failureMask;
            }

            return failure;
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Final use-release failure must preserve the stable anchor and actual lower-owner state.")]
    private void TryReleasePreparationUse()
    {
        lock (_gate)
        {
            if ((_useReleaseState is not PreparationUseReleaseState.Owned)
                || (_producerState is not PreparationProducerState.Ended)
                || (_bodyState is not (PreparationBodyState.Ended or PreparationBodyState.AdmittedEnded)))
            {
                return;
            }

            _useReleaseState = PreparationUseReleaseState.Releasing;
        }

        var cleanupFailure = ReturnWorkspace(PreparationWorkspace.Shared, stage: 4);

        if (cleanupFailure is not null)
        {
            throw cleanupFailure;
        }

        WorldGuidUse use;
        WorldGuidUseControl useControl;

        lock (_gate)
        {
            use = _use;
            useControl = use.Control;
            _use = default;
            _useReleaseState = PreparationUseReleaseState.Released;
        }

        try
        {
            use.Dispose();
        }
        catch (Exception failure)
        {
            RecordPreparationCleanupFailure(failure, stage: 4, category: 3, useControl: useControl);
            throw;
        }
    }

    private enum PreparationBodyState
    {
        Building,
        Available,
        Admitted,
        Cleaning,
        Ended,
        Quarantined,
        AdmittedCleaning,
        AdmittedEnded,
        AdmittedQuarantined,
    }

    private readonly record struct PreparationFootprint(
        long SharedWorkspaceBytes,
        long ProducerWorkspaceBytes,
        long BodyCleanupWorkspaceBytes,
        long PublicationWorkspaceBytes);

    private enum PreparationPartState
    {
        Unused,
        Pending,
        Running,
        Ended,
    }

    private enum PreparationProducerPart
    {
        Entry,
        PlanCompletion,
        ResourceCompletion,
    }

    private enum PreparationProducerState
    {
        Entry,
        Running,
        Publishing,
        Ended,
        Quarantined,
    }

    private enum PreparationUseReleaseState
    {
        Owned,
        Releasing,
        Released,
        Quarantined,
    }

    private enum PreparationWorkspace
    {
        Shared,
        Producer,
        BodyCleanup,
        Publication,
        Options,
        TemporaryOptions,
    }

    private enum ScenePreparationOutcome
    {
        None,
        Prepared,
        Faulted,
        Canceled,
    }
}
