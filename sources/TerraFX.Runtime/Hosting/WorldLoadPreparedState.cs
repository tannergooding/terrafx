// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Integration;
using TerraFX.Scenes;

namespace TerraFX.Hosting;

internal sealed class WorldLoadPreparedState : IDisposable
{
    private readonly AssetStore _assets;
    private readonly Lock _gate = new();
    private bool _isAcquisitionClosed;
    private bool _isCaptured;
    private bool _isDisposed;
    private AssetPackageLease? _packageLease;
    private readonly WorldRetainedHistoryControl _retainedHistory;

    internal WorldLoadPreparedState(
        AssetStore assets,
        WorldConstructionPlan constructionPlan,
        WorldGuidReplacementPreparation replacementPreparation,
        AssetPackageLease packageLease,
        WorldRetainedHistoryControl retainedHistory,
        WorldRestoreIntent restoreIntent)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(constructionPlan);
        ArgumentNullException.ThrowIfNull(replacementPreparation);
        ArgumentNullException.ThrowIfNull(packageLease);
        ArgumentNullException.ThrowIfNull(retainedHistory);
        _assets = assets;
        ConstructionPlan = constructionPlan;
        ReplacementPreparation = replacementPreparation;
        _packageLease = packageLease;
        _retainedHistory = retainedHistory;
        RestoreIntent = restoreIntent;
    }

    internal WorldPersistenceCheckpointContents Checkpoint => _retainedHistory.Checkpoint;

    internal WorldConstructionPlan ConstructionPlan { get; }

    internal byte[] FileRecord => _retainedHistory.FileRecord;

    internal WorldGuidReplacementPreparation ReplacementPreparation { get; }

    internal WorldRestoreIntent RestoreIntent { get; }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
        }

        try
        {
            ConstructionPlan.PreparedReplacement?.CloseAcquisition();
            ReplacementPreparation.Dispose();
        }
        finally
        {
            try
            {
                ConstructionPlan.ReleaseUntransferredRestoreResources();
            }
            finally
            {
                try
                {
                    _retainedHistory.Dispose();
                }
                finally
                {
                    _packageLease?.Dispose();
                }
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

        ConstructionPlan.PreparedReplacement?.CloseAcquisition();

        if (!isCaptured)
        {
            Dispose();
        }
    }

    [SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "The owner-boundary operation waits for exact package-backed Scene preparation to finish before candidate or package ownership can be released.")]
    internal World CreateRestoredCandidate(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var candidate = World.CreateRestoreCandidate(ConstructionPlan);
        ScenePersistenceRestorer.PreparedRestore? preparedSceneRestore = null;
        var candidateTransferred = false;

        try
        {
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
                cancellationToken);
            ScenePersistenceRestorer.RestorePrepared(candidate, preparedSceneRestore, cancellationToken);
            candidate.RestoreCheckpointClockAndControls(
                Checkpoint.WorldRecord,
                Checkpoint.WorldContents.Clock,
                Checkpoint.WorldContents.Cursors,
                Checkpoint.WorldContents.ControlRecordCount,
                Checkpoint.WorldContents.SceneControlPayloadBytes,
                cancellationToken);
            candidate.AdoptRestoreLifetimeLease(
                _packageLease
                    ?? throw new InvalidOperationException("The prepared load lost its exact package lease."));
            _packageLease = null;
            candidateTransferred = true;
            return candidate;
        }
        finally
        {
            try
            {
                preparedSceneRestore?.Dispose();
            }
            finally
            {
                if (!candidateTransferred)
                {
                    candidate.Dispose();
                }
            }
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

            var preparedReplacement = ConstructionPlan.PreparedReplacement
                ?? throw new InvalidOperationException("The prepared World replacement omitted its Entities-owned capability.");

            if (!preparedReplacement.TryCaptureCommit(
                    out operationCredits,
                    out defaultDomainHandoff,
                    out bindingRefusal,
                    out admissionRefusal))
            {
                return false;
            }

            _isCaptured = true;
            return true;
        }
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
}
