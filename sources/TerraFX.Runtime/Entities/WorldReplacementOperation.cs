// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Threading;
using TerraFX;

namespace TerraFX.Entities;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The operation releases its preowned playback control only through the phase-specific abort, untransferred-plan, or consumer-cleanup owner.")]
internal sealed class WorldReplacementOperation : IWorldGuidRetentionOwner
{
    private const int StateAbandoned = 7;
    private const int StateAborted = 8;
    private const int StateCandidate = 3;
    private const int StateCaptured = 2;
    private const int StateEnded = 9;
    private const int StatePrepared = 1;
    private const int StatePreparedUnsealed = 0;
    private const int StatePublicationPrepared = 4;
    private const int StatePublished = 5;
    private const int StateRejected = 6;

    private World? _candidate;
    private readonly byte[] _checkpointDigest = new byte[32];
    private long _comparedCanonicalMutationRevision;
    private readonly WorldConstructionPlan _constructionPlan;
    private WorldDefaultDomainHandoff? _defaultDomainHandoff;
    private readonly Lock _gate = new();
    private bool _isEnding;
    private bool _isPlaybackAttached;
    private WorldDomainOperationCreditBundle? _operationCredits;
    private readonly byte[] _payloadDigest = new byte[32];
    private readonly WorldPlaybackControl _playbackControl;
    private readonly WorldGuidReplacementPreparation _preparation;
    private CommittedWorldPublication? _restoredPublication;
    private WorldResourceOperationReservation? _sourceBudget;
    private WorldRestoreSourceControl? _sourceControl;
    private WorldRestoreSourceControl.SourceUse? _sourceUse;
    private ReadOnlyMemory<byte> _sourceWorldRecord;
    private int _state;
    private readonly World _targetWorld;
    private WorldDomainOperationCredit? _transferredSourceCredit;

    internal WorldReplacementOperation(
        WorldConstructionPlan constructionPlan,
        World targetWorld,
        WorldGuidReplacementPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(constructionPlan);
        ArgumentNullException.ThrowIfNull(targetWorld);
        ArgumentNullException.ThrowIfNull(preparation);
        _constructionPlan = constructionPlan;
        _targetWorld = targetWorld;
        _preparation = preparation;
        _playbackControl = new WorldPlaybackControl(constructionPlan.AdmissionDomain);
    }

    internal WorldDomainOperationCredit MappingCredit
        => _operationCredits?.Data
            ?? throw new InvalidOperationException("The captured World replacement omitted its mapping credit.");

    internal World TargetWorld => _targetWorld;

    private static void ValidateFreshIdentityHistory(
        ReadOnlyMemory<byte> freshWorldRecord,
        ValidatedWorldIdentityHistory freshIdentityHistory)
    {
        var worldRecord = new EbinRecordReadView(freshWorldRecord.Span);
        ReadOnlySpan<byte> ecsRecord = default;

        for (var index = 0; index < worldRecord.FieldCount; index++)
        {
            var field = worldRecord.GetField(index);

            if (field.Tag == 5)
            {
                if (!ecsRecord.IsEmpty)
                {
                    throw new InvalidOperationException("The fresh World record contains duplicate ECS projections.");
                }

                ecsRecord = field.Payload;
            }
        }

        if (ecsRecord.IsEmpty)
        {
            throw new InvalidOperationException("The fresh World record omitted its ECS projection.");
        }

        var ecsView = new EbinRecordReadView(ecsRecord);
        ReadOnlySpan<byte> identityHistoryRecord = default;

        for (var index = 0; index < ecsView.FieldCount; index++)
        {
            var field = ecsView.GetField(index);

            if (field.Tag == 5)
            {
                if (!identityHistoryRecord.IsEmpty)
                {
                    throw new InvalidOperationException("The fresh ECS record contains duplicate identity-history projections.");
                }

                identityHistoryRecord = field.Payload;
            }
        }

        if (identityHistoryRecord.IsEmpty
            || !identityHistoryRecord.SequenceEqual(freshIdentityHistory.EncodedRecord.Span))
        {
            throw new InvalidOperationException("The decoded fresh identity history does not belong to the exact captured World record.");
        }
    }

    internal void AbandonBeforeOwnerBoundary()
    {
        lock (_gate)
        {
            if (_state != StateCaptured)
            {
                throw new InvalidOperationException("Only a captured replacement that has not started owner execution can be abandoned.");
            }

            _state = StateAbandoned;
        }
    }

    internal void AbortAtOwnerBoundary()
    {
        World? candidate;
        bool playbackAttached;

        lock (_gate)
        {
            if (_state is StatePublished or StateEnded)
            {
                throw new InvalidOperationException("A published World replacement cannot be aborted.");
            }

            candidate = _candidate;
            playbackAttached = _isPlaybackAttached;
        }

        if (candidate is not null)
        {
            candidate.Dispose();

            if (!playbackAttached)
            {
                _sourceBudget?.Dispose();
                _sourceBudget = null;
                _playbackControl.Dispose();
            }

            lock (_gate)
            {
                _candidate = null;
                _restoredPublication = null;
                _state = StateAborted;
            }
        }
        else
        {
            lock (_gate)
            {
                _restoredPublication = null;
                _state = StateAborted;
            }

            if (!playbackAttached)
            {
                _playbackControl.Dispose();
            }
        }
    }

    internal void Capture(
        WorldDomainOperationCreditBundle operationCredits,
        WorldDefaultDomainHandoff? defaultDomainHandoff)
    {
        _operationCredits = operationCredits;
        _defaultDomainHandoff = defaultDomainHandoff;
        Volatile.Write(ref _state, StateCaptured);
    }

    internal void EndAfterConsumerCleanup()
    {
        lock (_gate)
        {
            if (_state == StateEnded)
            {
                return;
            }

            if (_isEnding)
            {
                throw new InvalidOperationException("World replacement cleanup has already started and cannot be retried.");
            }

            if (_state is not (StatePublished or StateRejected or StateAbandoned or StateAborted))
            {
                throw new InvalidOperationException("World replacement ownership cannot end before a terminal decision.");
            }

            _isEnding = true;
        }

        _sourceUse?.Dispose();
        _sourceUse = null;
        _sourceBudget?.Dispose();
        _sourceBudget = null;
        _transferredSourceCredit?.Dispose();
        _transferredSourceCredit = null;
        _operationCredits?.Dispose();
        _operationCredits = null;
        _defaultDomainHandoff?.Dispose();
        _defaultDomainHandoff = null;
        _preparation.Dispose();
        _constructionPlan.ReleaseUntransferredRestoreResources();
        Volatile.Write(ref _state, StateEnded);
    }

    internal void PreparePublicationAtOwnerBoundary(
        in WorldPersistenceCaptureLease captureLease,
        ReadOnlyMemory<byte> freshWorldRecord,
        ValidatedWorldIdentityHistory freshIdentityHistory)
    {
        ArgumentNullException.ThrowIfNull(freshIdentityHistory);
        World candidate;
        WorldRestoreSourceControl sourceControl;
        ReadOnlyMemory<byte> sourceWorldRecord;

        lock (_gate)
        {
            if (_state != StateCandidate)
            {
                throw new InvalidOperationException("Publication preparation requires the exact unpublished restore candidate.");
            }

            candidate = _candidate
                ?? throw new InvalidOperationException("The captured replacement omitted its unpublished candidate.");
            sourceControl = _sourceControl
                ?? throw new InvalidOperationException("The sealed replacement omitted its source control.");
            sourceWorldRecord = _sourceWorldRecord;
        }

        captureLease.ValidateCurrent();

        if (!captureLease.IsRestoreValidation || !captureLease.IsBoundTo(candidate))
        {
            throw new InvalidOperationException("The fresh World record is not held by the exact candidate's live restore-validation capture lease.");
        }

        Span<byte> actualPayloadDigest = stackalloc byte[32];
        _ = SHA256.HashData(sourceWorldRecord.Span, actualPayloadDigest);

        if (!CryptographicOperations.FixedTimeEquals(_payloadDigest, actualPayloadDigest))
        {
            throw new InvalidOperationException("The sealed source payload digest no longer matches its retained World record.");
        }

        ValidateFreshIdentityHistory(freshWorldRecord, freshIdentityHistory);
        WorldPersistenceRestoreValidator.Validate(
            sourceWorldRecord,
            freshWorldRecord,
            sourceControl.Owner.IdentityHistory,
            freshIdentityHistory,
            _constructionPlan,
            _checkpointDigest,
            checked(sourceControl.Owner.CompletedTick + 1));

        var restoredPublication = candidate.PrepareRestoredPublication();
        var comparedRevision = candidate.CanonicalMutationRevision;
        TransferSourceToPlayback(_playbackControl);

        lock (_gate)
        {
            if (_state != StateCandidate)
            {
                throw new InvalidOperationException("The replacement changed phase during publication preparation.");
            }

            _sourceWorldRecord = default;
            _sourceControl = null;
            _restoredPublication = restoredPublication;
            _comparedCanonicalMutationRevision = comparedRevision;
            _state = StatePublicationPrepared;
        }
    }

    internal WorldReplacementOutcome PublishAtOwnerBoundary()
    {
        World candidate;
        CommittedWorldPublication restoredPublication;
        WorldDomainOperationCreditBundle operationCredits;

        lock (_gate)
        {
            if (_state != StatePublicationPrepared)
            {
                throw new InvalidOperationException("World replacement publication requires the exact prepared candidate.");
            }

            candidate = _candidate
                ?? throw new InvalidOperationException("The prepared World replacement omitted its candidate.");
            restoredPublication = _restoredPublication
                ?? throw new InvalidOperationException("The prepared World replacement omitted its publication marker.");
            operationCredits = _operationCredits
                ?? throw new InvalidOperationException("The captured World replacement omitted its operation credits.");
        }

        if (!_targetWorld.TryCommitLoadReplacement(
                candidate,
                restoredPublication,
                _preparation,
                operationCredits,
                _defaultDomainHandoff,
                _comparedCanonicalMutationRevision,
                out var bindingRefusal))
        {
            if (bindingRefusal.IsNone)
            {
                throw new InvalidOperationException("A rejected World publication omitted its bounded target-binding refusal.");
            }

            Volatile.Write(ref _state, StateRejected);
            return new WorldReplacementOutcome(WorldReplacementOutcomeKind.Rejected, replacementWorld: null, bindingRefusal);
        }

        lock (_gate)
        {
            _state = StatePublished;
            _candidate = null;
            _restoredPublication = null;
        }

        return new WorldReplacementOutcome(WorldReplacementOutcomeKind.Published, candidate, default);
    }

    internal void ReleaseUntransferred()
    {
        lock (_gate)
        {
            if (_state is StateCaptured or StateCandidate or StatePublicationPrepared or StatePublished or StateRejected or StateAbandoned or StateAborted)
            {
                return;
            }

            if (_state == StateEnded)
            {
                return;
            }
        }

        _sourceUse?.Dispose();
        _sourceUse = null;
        _playbackControl.Dispose();
        _preparation.Dispose();
        Volatile.Write(ref _state, StateEnded);
    }

    internal void SealInputs(
        WorldRestoreSourceControl.SourceUse sourceUse,
        ReadOnlyMemory<byte> validatedWorldRecord,
        scoped ReadOnlySpan<byte> checkpointDigest,
        scoped ReadOnlySpan<byte> payloadDigest)
    {
        ArgumentNullException.ThrowIfNull(sourceUse);

        lock (_gate)
        {
            if (_state != StatePreparedUnsealed)
            {
                throw new InvalidOperationException("The replacement inputs can be sealed only once before capture.");
            }

            var sourceControl = sourceUse.Control;
            sourceControl.ValidateReplacementInputs(sourceUse, validatedWorldRecord, checkpointDigest, payloadDigest);
            checkpointDigest.CopyTo(_checkpointDigest);
            payloadDigest.CopyTo(_payloadDigest);
            _sourceControl = sourceControl;
            _sourceUse = sourceUse;
            _sourceWorldRecord = validatedWorldRecord;
            _state = StatePrepared;
        }
    }

    internal bool TryCreateCandidateAtOwnerBoundary(out World? candidate, out WorldGuidRefusal refusal)
    {
        lock (_gate)
        {
            if (_state != StateCaptured)
            {
                throw new InvalidOperationException("Candidate construction requires the exact captured World replacement.");
            }

            _state = StateCandidate;
        }

        _targetWorld.EnsureLoadCommitOwnerThread();

        if (!_preparation.IsTargetCurrent())
        {
            refusal = new WorldGuidRefusal(WorldGuidRefusal.TargetBindingStale);
            candidate = null;
            Volatile.Write(ref _state, StateRejected);
            return false;
        }

        var createdCandidate = World.CreateRestoreCandidate(_constructionPlan);
        lock (_gate)
        {
            _candidate = createdCandidate;
        }

        var sourceUse = _sourceUse
            ?? throw new InvalidOperationException("The sealed World replacement omitted its source use.");
        var sourceBudget = createdCandidate.RetainPreparedRestoreSourceBudget(sourceUse.Control.StorageByteReservation);
        lock (_gate)
        {
            _sourceBudget = sourceBudget;
        }

        World.InitializeRestoreCandidate(createdCandidate);
        candidate = createdCandidate;
        refusal = default;
        return true;
    }

    private void TransferSourceToPlayback(WorldPlaybackControl playback)
    {
        ArgumentNullException.ThrowIfNull(playback);
        World candidate;
        WorldRestoreSourceControl sourceControl;

        lock (_gate)
        {
            candidate = _candidate
                ?? throw new InvalidOperationException("The captured replacement omitted its unpublished candidate.");
            sourceControl = _sourceControl
                ?? throw new InvalidOperationException("The sealed replacement omitted its source control.");
        }

        var sourceUse = _sourceUse
            ?? throw new InvalidOperationException("The sealed replacement omitted its source use.");
        var sourceBudget = _sourceBudget
            ?? throw new InvalidOperationException("The candidate omitted its prepaid source budget.");
        var operationCredits = _operationCredits
            ?? throw new InvalidOperationException("The captured replacement omitted its operation credits.");

        if (!ReferenceEquals(playback, _playbackControl)
            || !ReferenceEquals(sourceUse.Control, sourceControl)
            || !sourceBudget.IsBoundTo(candidate)
            || (sourceBudget.Bytes != sourceControl.StorageByteReservation)
            || !ReferenceEquals(operationCredits.Domain, _constructionPlan.AdmissionDomain))
        {
            throw new InvalidOperationException("The playback transfer does not match the exact source, candidate budget, and World domain.");
        }

        _transferredSourceCredit = operationCredits.TransferSourceCredit();

        if (!ReferenceEquals(_transferredSourceCredit.Domain, _constructionPlan.AdmissionDomain))
        {
            throw new InvalidOperationException("The playback source credit belongs to a different World domain.");
        }

        playback.Initialize(sourceUse, _transferredSourceCredit, sourceBudget);
        _sourceUse = null;
        _transferredSourceCredit = null;
        _sourceBudget = null;
        candidate.AdoptRestoreLifetimeLease(playback);
        _isPlaybackAttached = true;
    }
}
