// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using TerraFX;
using TerraFX.Animation;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Integration;
using TerraFX.Scenes;

namespace TerraFX.Hosting;

internal sealed class WorldLoadPreparedState : IDisposable, IAnimationRestoreAssetSourceOwner
{
    private readonly AnimationPreparedRestoreAssets? _animationAssets;
    private readonly AssetStore _assets;
    private readonly IWorldAttachmentRegistration? _attachmentSourceRegistration;
    private IDisposable? _commitSourceUse;
    private readonly long _completedTick;
    private Exception? _constructionCleanupFailure;
    private WorldConstructionPlan? _constructionPlan;
    private readonly Lock _gate = new();
    private bool _isAcquisitionClosed;
    private bool _isAssetReferencesValidated;
    private bool _isCaptured;
    private bool _isDisposed;
    private bool _isSourcesPrepared;
    private readonly EbinReadLimits _limits;
    private AssetPackageLease? _packageLease;
    private ImmutableArray<IWorldPreparedAttachmentSource> _preparedAttachments = [];
    private ImmutableArray<IWorldPreparedResourceSource> _preparedSources = [];
    private readonly WorldRegistration _registration;
    private readonly IWorldResourceRegistration? _resourceSourceRegistration;
    private WorldRetainedHistoryControl? _retainedHistory;
    private readonly WorldRestoreSourceControl _sourceControl;
    private WorldPreparedSourceStorage? _sourceStorage;

    internal WorldLoadPreparedState(
        AssetStore assets,
        WorldConstructionPlan constructionPlan,
        WorldGuidReplacementPreparation replacementPreparation,
        AssetPackageLease packageLease,
        WorldRetainedHistoryControl retainedHistory,
        WorldRestoreIntent restoreIntent,
        EbinReadLimits limits,
        WorldPreparedSourceStorage sourceStorage)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(constructionPlan);
        ArgumentNullException.ThrowIfNull(replacementPreparation);
        ArgumentNullException.ThrowIfNull(packageLease);
        ArgumentNullException.ThrowIfNull(retainedHistory);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(sourceStorage);
        _assets = assets;
        _constructionPlan = constructionPlan;
        _registration = constructionPlan.Options.Registration;
        ReplacementPreparation = replacementPreparation;
        _packageLease = packageLease;
        _retainedHistory = retainedHistory;
        RestoreIntent = restoreIntent;
        _completedTick = checked(retainedHistory.Checkpoint.NextTick - 1);
        _limits = limits;
        _sourceStorage = sourceStorage;
        SelectRegistrations(Registration, out _resourceSourceRegistration, out _attachmentSourceRegistration);
        _isAssetReferencesValidated = _resourceSourceRegistration is null;
        _animationAssets = _resourceSourceRegistration is null ? null : new AnimationPreparedRestoreAssets();
        _sourceControl = new WorldRestoreSourceControl(
            this, retainedHistory.Checkpoint.WorldContents.EcsRecord,
            retainedHistory.Checkpoint.WorldContents.ScenesRecord,
            _resourceSourceRegistration, _attachmentSourceRegistration, sourceStorage, ReleaseOwnedResources);
    }

    public long CompletedTick => _completedTick;

    public ValidatedWorldIdentityHistory IdentityHistory
        => Checkpoint.WorldContents.Ecs.ValidatedIdentityHistory
            ?? throw new InvalidOperationException("The prepared source omitted its validated identity history.");

    public EbinReadLimits Limits => _limits;

    public WorldRegistration Registration => _registration;

    internal WorldPersistenceCheckpointContents Checkpoint
    {
        get
        {
            lock (_gate)
            {
                return _retainedHistory?.Checkpoint
                    ?? throw new InvalidOperationException("The prepared World load no longer owns its source checkpoint.");
            }
        }
    }

    internal WorldConstructionPlan ConstructionPlan
        => _constructionPlan ?? throw new InvalidOperationException("The prepared source no longer owns its construction plan.");

    internal WorldGuidReplacementPreparation ReplacementPreparation { get; }

    internal WorldRestoreIntent RestoreIntent { get; }

    internal static bool IsM05AssetProfileSelected(WorldRegistration registration)
        => registration.TryGetResource<RootMotionRequest>(AnimationResources.RootMotionRequestsId, out _, out var resource)
            && (resource is not null)
            && resource.IsAggregate
            && (resource.PartitionCount == 4096)
            && (resource.Schema == RootMotionResourceCodec.Schema)
            && (resource.SourceAuthorType == typeof(RootMotionResourceAuthor))
            && registration.TryGetAttachment(AttachmentScope.World, typeof(AnimationRuntimeState), out var attachment)
            && (attachment is not null)
            && (attachment.Descriptor.Schema == new ContentSchemaReference(
                new ContentSchemaIdentityAssignment("animation/world-state").SchemaId, 1, 0, ContentSchemaScope.Content))
            && (attachment.SourceAuthorType == typeof(AnimationRuntimeAuthor));

    internal static long MeasureSourceStorageBytes(
        WorldRegistration registration,
        WorldPersistenceCheckpointContents checkpoint)
    {
        SelectRegistrations(registration, out var resource, out var attachment);
        var bytes = (long)WorldPreparedSourceStorage.BaseByteReservation;

        if (resource is not null)
        {
            _ = WorldRestoreSourceLease.SelectResource(checkpoint.WorldContents.EcsRecord.Span, resource);
            bytes = checked(bytes
                + RootMotionResourceProvider.MeasureOperationByteReservation(WorldResourceOperationKind.Restore)
                + RootMotionResourceProvider.MeasureOperationByteReservation(WorldResourceOperationKind.Validation)
                + AnimationPreparedRestoreAssets.MeasureByteReservation());
            var assignmentCount = (checkpoint.WorldContents.Ecs.ValidatedIdentityHistory
                ?? throw new InvalidOperationException("The source resource requires its complete validated history."))
                .AssignmentsByOrdinal.Length;
            bytes = checked(bytes + 64 + ((((long)assignmentCount * sizeof(uint)) + 7) & ~7L));
        }

        if (attachment is not null)
        {
            var payload = WorldRestoreSourceLease.SelectWorldAttachment(checkpoint.WorldContents.EcsRecord.Span, attachment);
            bytes = checked(bytes + AnimationRuntimeStateCodec.MeasureReadByteReservation(payload));
        }

        return bytes;
    }

    private static void SelectRegistrations(
        WorldRegistration registration,
        out IWorldResourceRegistration? resource,
        out IWorldAttachmentRegistration? attachment)
    {
        resource = null;
        attachment = null;
        var attachmentSchema = new ContentSchemaReference(
            new ContentSchemaIdentityAssignment("animation/world-state").SchemaId, 1, 0, ContentSchemaScope.Content);

        foreach (var candidate in registration.Resources)
        {
            if (candidate.ResourceId == AnimationResources.RootMotionRequestsId)
            {
                if (!candidate.IsAggregate || (candidate.Schema != RootMotionResourceCodec.Schema)
                    || (candidate.PartitionCount != 4096) || (candidate.SourceAuthorType != typeof(RootMotionResourceAuthor)))
                {
                    throw new InvalidOperationException("The selected root-motion resource has a different captured schema, author family, or partition domain.");
                }

                resource = candidate;
            }
        }

        foreach (var candidate in registration.Attachments)
        {
            if ((candidate.Scope == AttachmentScope.World) && (candidate.Descriptor.Schema.SchemaId == attachmentSchema.SchemaId))
            {
                if ((candidate.Descriptor.Schema != attachmentSchema) || (candidate.SourceAuthorType != typeof(AnimationRuntimeAuthor)))
                {
                    throw new InvalidOperationException("The selected Animation World attachment has a different captured source family.");
                }

                attachment = candidate;
            }
        }

        if ((resource is null) != (attachment is null))
        {
            throw new InvalidOperationException("The selected Animation restore requires both its registered request resource and World playback attachment families.");
        }
    }

    public AnimationRestoreAssetSourceLease AcquireAnimationAssets()
    {
        lock (_gate)
        {
            _sourceControl.ThrowIfFailed();
            ObjectDisposedException.ThrowIf(_isDisposed || _isAcquisitionClosed, this);

            if (!_isSourcesPrepared
                || (_preparedSources is not [WorldResourceSource<RootMotionResourceAuthor> requests])
                || (_preparedAttachments is not [WorldAttachmentSource<AnimationRuntimeAuthor> playback]))
            {
                throw new InvalidOperationException("Animation asset preparation requires both exact cached typed source occurrences.");
            }

            var sourceUse = _sourceControl.AcquireUse();
            WorldAttachmentSource<AnimationRuntimeAuthor>? retainedPlayback = null;
            WorldResourceSource<RootMotionResourceAuthor>? retainedRequests = null;

            try
            {
                retainedPlayback = playback.Retain();
                retainedRequests = requests.Retain();
                var lease = new AnimationRestoreAssetSourceLease(
                    this, retainedPlayback, retainedRequests,
                    _packageLease ?? throw new InvalidOperationException("The prepared load lost its exact package lease."),
                    sourceUse);
                retainedPlayback = null;
                retainedRequests = null;
                sourceUse = null;
                return lease;
            }
            finally
            {
                try
                {
                    retainedPlayback?.Dispose();
                }
                finally
                {
                    try
                    {
                        retainedRequests?.Dispose();
                    }
                    finally
                    {
                        sourceUse?.Dispose();
                    }
                }
            }
        }
    }

    public WorldRestoreSourceLease AcquireRead()
    {
        lock (_gate)
        {
            _sourceControl.ThrowIfFailed();
            ObjectDisposedException.ThrowIf(_isDisposed || _isAcquisitionClosed, this);
            var checkpoint = Checkpoint;
            var use = _sourceControl.AcquireUse();

            try
            {
                return new WorldRestoreSourceLease(
                    this, checkpoint.WorldContents.EcsRecord, checkpoint.WorldContents.ScenesRecord, use);
            }
            catch
            {
                use.Dispose();
                throw;
            }
        }
    }

    public void Dispose()
    {
        IDisposable? commitSourceUse;
        ImmutableArray<IWorldPreparedResourceSource> preparedSources;
        ImmutableArray<IWorldPreparedAttachmentSource> preparedAttachments;

        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _isAcquisitionClosed = true;
            commitSourceUse = _commitSourceUse;
            _commitSourceUse = null;
            preparedSources = _preparedSources;
            preparedAttachments = _preparedAttachments;
            _preparedSources = [];
            _preparedAttachments = [];
        }

        try
        {
            ReleaseConstructionResources();
        }
        finally
        {
            try
            {
                try
                {
                    foreach (var source in preparedSources)
                    {
                        source.Dispose();
                    }
                }
                finally
                {
                    try
                    {
                        foreach (var source in preparedAttachments)
                        {
                            source.Dispose();
                        }
                    }
                    finally
                    {
                        commitSourceUse?.Dispose();
                    }
                }
            }
            finally
            {
                _sourceControl.Close();
            }
        }
    }

    internal void CloseAcquisition()
    {
        bool isCaptured;

        lock (_gate)
        {
            _isAcquisitionClosed = true;
            isCaptured = _isCaptured;
        }

        _constructionPlan?.PreparedReplacement?.CloseAcquisition();

        if (!isCaptured)
        {
            Dispose();
        }
    }

    [SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "The owner-boundary operation waits for exact package-backed Scene preparation to finish before candidate or package ownership can be released.")]
    internal (World Candidate, WorldPlaybackControl PlaybackControl) CreateRestoredCandidate(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_isDisposed || !_isCaptured || !_isSourcesPrepared || !_isAssetReferencesValidated || (_commitSourceUse is null))
            {
                throw new InvalidOperationException("Candidate construction requires this prepared source owner's captured commit use.");
            }

            _sourceControl.ThrowIfFailed();
            _animationAssets?.ValidateSourceOwner(
                this, _packageLease ?? throw new InvalidOperationException("The prepared load lost its exact package lease."));
        }

        var candidate = World.CreateRestoreCandidate(ConstructionPlan);
        WorldResourceOperationReservation? sourceBudget = null;
        ScenePersistenceRestorer.PreparedRestore? preparedSceneRestore = null;
        IDisposable? playbackSourceUse = null;
        WorldPlaybackControl? playbackControl = null;
        var candidateTransferred = false;

        try
        {
            sourceBudget = candidate.RetainPreparedRestoreSourceBudget(_sourceControl.StorageByteReservation);
            World.InitializeRestoreCandidate(candidate);
            preparedSceneRestore = ScenePersistenceRestorer.PrepareAsync(
                candidate,
                _assets,
                _packageLease
                    ?? throw new InvalidOperationException("The prepared load lost its exact package lease."),
                Checkpoint.WorldContents.ScenesRecord,
                Checkpoint.WorldContents.Scenes,
                cancellationToken).GetAwaiter().GetResult();
            candidate.RestoreEcsRows(
                Checkpoint.WorldContents.EcsRecord,
                Checkpoint.WorldContents.Ecs,
                CompletedTick,
                _preparedSources,
                _preparedAttachments,
                cancellationToken);
            ScenePersistenceRestorer.RestorePrepared(candidate, preparedSceneRestore, cancellationToken);
            candidate.RestoreCheckpointClockAndControls(
                Checkpoint.WorldRecord,
                Checkpoint.WorldContents.Clock,
                Checkpoint.WorldContents.Cursors,
                Checkpoint.WorldContents.ControlRecordCount,
                Checkpoint.WorldContents.SceneControlPayloadBytes,
                cancellationToken);
            lock (_gate)
            {
                // This transferred use retains the source caches, package, and prepared asset pins together.
                playbackSourceUse = _sourceControl.AcquireUse();
            }

            playbackControl = new WorldPlaybackControl(
                _packageLease ?? throw new InvalidOperationException("The prepared load lost its exact package lease."),
                playbackSourceUse,
                sourceBudget);
            playbackSourceUse = null;
            sourceBudget = null;
            candidate.AdoptRestoreLifetimeLease(playbackControl);
            var candidatePlaybackControl = playbackControl;
            playbackControl = null;
            candidateTransferred = true;
            return (candidate, candidatePlaybackControl);
        }
        finally
        {
            try
            {
                preparedSceneRestore?.Dispose();
            }
            finally
            {
                try
                {
                    playbackControl?.Dispose();
                }
                finally
                {
                    try
                    {
                        playbackSourceUse?.Dispose();
                    }
                    finally
                    {
                        if (!candidateTransferred)
                        {
                            try
                            {
                                candidate.Dispose();
                            }
                            finally
                            {
                                sourceBudget?.Dispose();
                            }
                        }
                    }
                }
            }
        }
    }

    internal ImmutableArray<Diagnostic> PrepareSources(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed || _isAcquisitionClosed, this);

            if (_isSourcesPrepared)
            {
                throw new InvalidOperationException("The prepared load sources can be decoded only once.");
            }
        }

        if (_resourceSourceRegistration is { } resource)
        {
            WorldResourceSource<RootMotionResourceAuthor>? source = null;

            try
            {
                if (!Registration.TryReadResourceSource(
                        resource.ResourceId, this, out source, out var diagnostics, cancellationToken))
                {
                    return diagnostics;
                }

                lock (_gate)
                {
                    ObjectDisposedException.ThrowIf(_isDisposed || _isAcquisitionClosed, this);
                    _preparedSources = [source ?? throw new InvalidOperationException("The successful resource source read omitted its owner.")];
                    source = null;
                }
            }
            finally
            {
                source?.Dispose();
            }
        }

        if (_attachmentSourceRegistration is { } attachment)
        {
            WorldAttachmentSource<AnimationRuntimeAuthor>? source = null;

            try
            {
                if (!Registration.TryReadWorldAttachmentSource(
                        attachment.Descriptor.Schema, this, out source, out var diagnostics, cancellationToken))
                {
                    return diagnostics;
                }

                lock (_gate)
                {
                    ObjectDisposedException.ThrowIf(_isDisposed || _isAcquisitionClosed, this);
                    _preparedAttachments = [source ?? throw new InvalidOperationException("The successful attachment source read omitted its owner.")];
                    source = null;
                }
            }
            finally
            {
                source?.Dispose();
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        _sourceControl.ValidatePreparedSources(_preparedSources, _preparedAttachments);
        (ConstructionPlan.PreparedReplacement
            ?? throw new InvalidOperationException("The prepared load omitted its construction binding."))
            .BindPreparedResourceSourceOwner(this);
        _isSourcesPrepared = true;
        return [];
    }

    internal void TransferRetainedHistoryToPlayback(
        WorldPlaybackControl playbackControl,
        WorldDomainOperationCredit sourceCredit)
    {
        ArgumentNullException.ThrowIfNull(playbackControl);
        ArgumentNullException.ThrowIfNull(sourceCredit);

        lock (_gate)
        {
            if (!_isCaptured || _isDisposed)
            {
                throw new InvalidOperationException("Source playback ownership can transfer only from a captured active load.");
            }

            var retainedHistory = _retainedHistory
                ?? throw new InvalidOperationException("The prepared load no longer owns its source history.");
            playbackControl.AttachRetainedHistory(retainedHistory, sourceCredit);
        }
    }

    internal bool TryCaptureCommit(
        out WorldDomainOperationCreditBundle? operationCredits,
        out WorldDefaultDomainHandoff? defaultDomainHandoff,
        out WorldGuidRefusal bindingRefusal,
        out WorldAdmissionRefusal admissionRefusal)
    {
        lock (_gate)
        {
            operationCredits = null;
            defaultDomainHandoff = null;

            if (_isAcquisitionClosed || _isDisposed || _isCaptured)
            {
                bindingRefusal = new WorldGuidRefusal(WorldGuidRefusal.TargetBindingStale);
                admissionRefusal = WorldAdmissionRefusal.None;
                return false;
            }

            _sourceControl.ThrowIfFailed();

            if (!_isSourcesPrepared || !_isAssetReferencesValidated)
            {
                throw new InvalidOperationException("The prepared load has not completed its source reads and package-reference validation.");
            }

            var preparedReplacement = ConstructionPlan.PreparedReplacement
                ?? throw new InvalidOperationException("The prepared World replacement omitted its Entities-owned capability.");

            var sourceUse = _sourceControl.AcquireUse();

            try
            {
                if (!preparedReplacement.TryCaptureCommit(
                        out operationCredits,
                        out defaultDomainHandoff,
                        out bindingRefusal,
                        out admissionRefusal))
                {
                    return false;
                }

                _commitSourceUse = sourceUse;
                sourceUse = null;
                _isCaptured = true;
                return true;
            }
            finally
            {
                sourceUse?.Dispose();
            }
        }
    }

    internal async Task<ImmutableArray<Diagnostic>> ValidateAssetReferencesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _sourceControl.ThrowIfFailed();
            ObjectDisposedException.ThrowIf(_isDisposed || _isAcquisitionClosed, this);

            if (!_isSourcesPrepared)
            {
                throw new InvalidOperationException("Package-reference validation requires the prepared source reads.");
            }

            if (_isAssetReferencesValidated)
            {
                return [];
            }
        }

        using var source = AcquireRead();
        var schemas = Registration.Schemas;
        var referenceCount = WorldPersistenceAssetIdSetWriter.MeasureReferenceCount(
            schemas, source.EcsRecord, source.ScenesRecord, Limits);
        var packageLease = _packageLease
            ?? throw new InvalidOperationException("The prepared load lost its exact package lease.");
        var workspaceBytes = WorldPersistence.MeasureAssetReferenceValidationWorkspaceBytes(
            referenceCount, packageLease.MaximumDecodedAssetBytes);
        var targetWorld = ConstructionPlan.TargetWorld
            ?? throw new InvalidOperationException("The prepared load lost its exact target World.");

        var admitted = targetWorld.TryReservePersistenceAdmissionBytes(workspaceBytes, out var reservation);
        using (reservation)
        {
            if (!admitted || (reservation is null))
            {
                return [WorldPersistence.CreateLoadDiagnostic(
                    "load-asset-reference-admission-rejected",
                    "The complete AssetReference tuple and package-inspection workspace does not fit the load admission budget.",
                    targetWorld.WorldGuid)];
            }

            using var workspaceUse = reservation.AcquireRetainedUse(targetWorld, workspaceBytes);
            reservation.Dispose();
            var references = new AssetReference[referenceCount];
            var uniqueCount = WorldPersistenceAssetIdSetWriter.CopySortedUniqueAssetReferences(
                schemas, source.EcsRecord, source.ScenesRecord, Limits, references);

            for (var index = 0; index < uniqueCount; index++)
            {
                var reference = references[index];
                var requiredMinor = WorldPersistence.GetM05RequiredAssetMinor(reference);
                var matches = requiredMinor is int minor
                    ? await packageLease.MatchesAssetReferenceAsync(reference, minor, cancellationToken).ConfigureAwait(false)
                    : await packageLease.MatchesAssetReferenceAsync(reference, cancellationToken).ConfigureAwait(false);
                source.ThrowIfFailed();

                if (!matches)
                {
                    return [WorldPersistence.CreateLoadDiagnostic(
                        "load-package-asset-schema-mismatch",
                        $"Asset {reference.AssetId} does not match its saved schema reference or selected minor profile.",
                        targetWorld.WorldGuid)];
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            source.ThrowIfFailed();
        }

        await (_animationAssets
            ?? throw new InvalidOperationException("The selected Animation source omitted its prepared asset owner."))
            .PrepareAsync(this, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        source.ThrowIfFailed();

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed || _isAcquisitionClosed, this);
            _isAssetReferencesValidated = true;
        }

        return [];
    }

    internal ValidatedWorldIdentityHistory ValidateRestoredCandidate(World candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        using var lease = candidate.AcquireRestoreValidationCaptureLease();
        var limits = WorldPersistenceEbinLimits.Create(candidate.Options);
        var status = WorldPersistenceCaptureCoordinator.Capture(
            candidate,
            lease,
            candidate.Options.Registration.Schemas,
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
            candidate.Options.Registration,
            identityScratch,
            lineageMembershipScratch,
            ref strictLayoutBuffers);
        var sourceIdentityHistory = Checkpoint.WorldContents.Ecs.ValidatedIdentityHistory
            ?? throw new InvalidOperationException("The validated source omitted its identity history.");
        var targetIdentityHistory = freshContents.Ecs.ValidatedIdentityHistory
            ?? throw new InvalidOperationException("The fresh candidate capture omitted its identity history.");

        WorldPersistenceRestoreValidator.Validate(
            Checkpoint.WorldRecord,
            record,
            sourceIdentityHistory,
            targetIdentityHistory,
            ConstructionPlan,
            Checkpoint.CheckpointDigest.Span,
            Checkpoint.NextTick);
        retainedCaptureReservation.Dispose();
        candidate.ReleaseUnusedRestoreValidationWorkspace();
        return targetIdentityHistory;
    }

    [SuppressMessage("Usage", "CA1031:Do not catch general exception types", Justification = "Failed construction claims retain the complete source owner and its credits in quarantine.")]
    private void ReleaseConstructionResources()
    {
        var constructionPlan = _constructionPlan;

        try
        {
            // Unused candidate claims are closed independently of admitted source readers and authors.
            try
            {
                constructionPlan?.PreparedReplacement?.CloseAcquisition();
            }
            finally
            {
                try
                {
                    ReplacementPreparation.Dispose();
                }
                finally
                {
                    constructionPlan?.ReleaseUntransferredRestoreResources();
                }
            }

            _constructionPlan = null;
        }
        catch (Exception exception)
        {
            _constructionCleanupFailure = exception;
            throw;
        }
    }

    [SuppressMessage("Usage", "CA1031:Do not catch general exception types", Justification = "Every independently owned cleanup is attempted; failed owners stay rooted by the source control's quarantine anchor.")]
    private void ReleaseOwnedResources()
    {
        List<Exception>? failures = null;

        if (_constructionCleanupFailure is { } constructionFailure)
        {
            throw constructionFailure;
        }

        void Release(Action cleanup)
        {
            try
            {
                cleanup();
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        var assetsReleased = false;
        Release(() => {
            _animationAssets?.Dispose();
            assetsReleased = true;
        });

        if (assetsReleased)
        {
            Release(() => {
                _sourceStorage?.Dispose();
                _sourceStorage = null;
            });
        }

        Release(() => {
            _retainedHistory?.Dispose();
            _retainedHistory = null;
        });
        Release(() => {
            _packageLease?.Dispose();
            _packageLease = null;
        });

        if (failures is not null)
        {
            throw new AggregateException(failures);
        }
    }
}
