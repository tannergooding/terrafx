// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal sealed class WorldConstructionPlan
{
    private readonly WorldAdmissionDomain _admissionDomain;
    private readonly WorldDiagnosticsCapability _diagnosticsCapability;
    private readonly WorldDomainReservation _domainReservation;
    private readonly Guid _executionSessionId;
    private readonly Guid _identityBranchId;
    private readonly StableIdentityLedger _identityLedger;
    private readonly WorldLifetimeControl _lifetimeControl;
    private readonly WorldLineageGrant _lineageGrant;
    private readonly bool _ownsWorkerPool;
    private readonly WorldPreparedReplacement? _preparedReplacement;
    private readonly WorldGuidRegistry _registry;
    private readonly WorldGuidReplacementPreparation? _replacementPreparation;
    private readonly ResolvedWorldOptions _resolvedOptions;
    private readonly int _restoreAttachmentCount;
    private readonly long _restoreAttachmentStorageByteReservation;
    private readonly long _restoreComponentWorkspaceByteReservation;
    private readonly int _restoreControlRecordCount;
    private readonly long _restoreControlStorageByteReservation;
    private readonly long _restoreEcsArchetypeMetadataByteReservation;
    private readonly WorldRestoreEcsPlan? _restoreEcsPlan;
    private readonly long _restoreEcsStorageByteReservation;
    private readonly WorldRestoreIdentityState? _restoreIdentityState;
    private int _restoreResourcesState;
    private readonly long _restoreSceneControlPayloadByteReservation;
    private readonly long _restoreSceneHierarchyWorkspaceByteReservation;
    private readonly long _restoreValidationWorkspaceByteReservation;
    private readonly long _restoreWorkPlanByteReservation;
    private readonly int _restoreWorkPlanCapacity;
    private readonly WorldSystemQuarantineReservation _systemQuarantineReservation;
    private readonly World? _targetWorld;
    private readonly WorldWorkerPool _workerPool;
    private readonly Guid _worldGuid;
    private readonly WorldGuidGrant _worldGuidGrant;

    internal WorldConstructionPlan(
        ResolvedWorldOptions resolvedOptions,
        Guid worldGuid,
        Guid replayLineageId,
        Guid identityBranchId,
        Guid executionSessionId,
        WorldAdmissionDomain admissionDomain,
        WorldLifetimeControl lifetimeControl,
        bool ownsWorkerPool,
        WorldDomainReservation domainReservation,
        WorldLineageGrant lineageGrant,
        WorldGuidRegistry registry,
        WorldGuidGrant worldGuidGrant,
        WorldDiagnosticsCapability diagnosticsCapability,
        StableIdentityLedger identityLedger,
        WorldSystemQuarantineReservation systemQuarantineReservation,
        WorldWorkerPool workerPool,
        WorldRestoreIdentityState? restoreIdentityState = null,
        WorldGuidReplacementPreparation? replacementPreparation = null,
        WorldRestoreEcsPlan? restoreEcsPlan = null,
        long restoreEcsArchetypeMetadataByteReservation = 0,
        long restoreEcsStorageByteReservation = 0,
        int restoreWorkPlanCapacity = 0,
        long restoreWorkPlanByteReservation = 0,
        long restoreComponentWorkspaceByteReservation = 0,
        int restoreControlRecordCount = 0,
        long restoreControlStorageByteReservation = 0,
        long restoreSceneControlPayloadByteReservation = 0,
        int restoreAttachmentCount = 0,
        long restoreAttachmentStorageByteReservation = 0,
        long restoreSceneHierarchyWorkspaceByteReservation = 0,
        long restoreValidationWorkspaceByteReservation = 0,
        World? targetWorld = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(restoreAttachmentCount);
        ArgumentOutOfRangeException.ThrowIfNegative(restoreAttachmentStorageByteReservation);
        ArgumentOutOfRangeException.ThrowIfNegative(restoreControlRecordCount);
        ArgumentOutOfRangeException.ThrowIfNegative(restoreControlStorageByteReservation);
        ArgumentOutOfRangeException.ThrowIfNegative(restoreSceneControlPayloadByteReservation);
        ArgumentOutOfRangeException.ThrowIfNegative(restoreSceneHierarchyWorkspaceByteReservation);
        ArgumentOutOfRangeException.ThrowIfNegative(restoreValidationWorkspaceByteReservation);
        ArgumentOutOfRangeException.ThrowIfNegative(restoreEcsArchetypeMetadataByteReservation);
        ArgumentOutOfRangeException.ThrowIfNegative(restoreEcsStorageByteReservation);
        ArgumentOutOfRangeException.ThrowIfNegative(restoreWorkPlanCapacity);
        ArgumentOutOfRangeException.ThrowIfNegative(restoreWorkPlanByteReservation);
        ArgumentOutOfRangeException.ThrowIfNegative(restoreComponentWorkspaceByteReservation);

        if ((restoreIdentityState is null)
            && ((restoreEcsPlan is not null)
                || (restoreEcsArchetypeMetadataByteReservation != 0)
                || (restoreEcsStorageByteReservation != 0)
                || (restoreWorkPlanCapacity != 0)
                || (restoreWorkPlanByteReservation != 0)
                || (restoreComponentWorkspaceByteReservation != 0)
                || (restoreControlRecordCount != 0)
                || (restoreControlStorageByteReservation != 0)
                || (restoreSceneControlPayloadByteReservation != 0)
                || (restoreSceneHierarchyWorkspaceByteReservation != 0)
                || (restoreValidationWorkspaceByteReservation != 0)
                || (restoreAttachmentCount != 0)
                || (restoreAttachmentStorageByteReservation != 0)
                || (targetWorld is not null)))
        {
            throw new ArgumentException("Fresh World construction cannot own restore ECS storage reservations.", nameof(restoreIdentityState));
        }

        if ((restoreIdentityState is not null)
            && ((replacementPreparation is null) || (targetWorld is null)))
        {
            throw new ArgumentException("A restore construction plan requires its exact target and replacement preparation.", nameof(replacementPreparation));
        }

        _resolvedOptions = resolvedOptions;
        ReplayLineageId = replayLineageId;
        _identityBranchId = identityBranchId;
        _executionSessionId = executionSessionId;
        _admissionDomain = admissionDomain;
        ArgumentNullException.ThrowIfNull(lifetimeControl);
        _lifetimeControl = lifetimeControl;
        _ownsWorkerPool = ownsWorkerPool;
        _domainReservation = domainReservation;
        _lineageGrant = lineageGrant;
        _registry = registry;
        _worldGuid = worldGuid;
        _worldGuidGrant = worldGuidGrant;
        _diagnosticsCapability = diagnosticsCapability;
        _identityLedger = identityLedger;
        _systemQuarantineReservation = systemQuarantineReservation;
        _workerPool = workerPool;
        _restoreIdentityState = restoreIdentityState;
        _replacementPreparation = replacementPreparation;
        _restoreEcsPlan = restoreEcsPlan;
        _restoreEcsArchetypeMetadataByteReservation = restoreEcsArchetypeMetadataByteReservation;
        _restoreEcsStorageByteReservation = restoreEcsStorageByteReservation;
        _restoreWorkPlanCapacity = restoreWorkPlanCapacity;
        _restoreWorkPlanByteReservation = restoreWorkPlanByteReservation;
        _restoreComponentWorkspaceByteReservation = restoreComponentWorkspaceByteReservation;
        _restoreAttachmentCount = restoreAttachmentCount;
        _restoreAttachmentStorageByteReservation = restoreAttachmentStorageByteReservation;
        _targetWorld = targetWorld;
        _restoreControlRecordCount = restoreControlRecordCount;
        _restoreControlStorageByteReservation = restoreControlStorageByteReservation;
        _restoreSceneControlPayloadByteReservation = restoreSceneControlPayloadByteReservation;
        _restoreSceneHierarchyWorkspaceByteReservation = restoreSceneHierarchyWorkspaceByteReservation;
        _restoreValidationWorkspaceByteReservation = restoreValidationWorkspaceByteReservation;
        _preparedReplacement = (restoreIdentityState is null)
            ? null
            : new WorldPreparedReplacement(
                new WorldConstructionPlanControl(
                    this,
                    targetWorld
                        ?? throw new InvalidOperationException("The restore plan omitted its target World."),
                    replacementPreparation
                        ?? throw new InvalidOperationException("The restore plan omitted its replacement preparation.")));
    }

    internal WorldAdmissionDomain AdmissionDomain => _admissionDomain;

    internal WorldDiagnosticsCapability DiagnosticsCapability => _diagnosticsCapability;

    internal WorldDomainReservation DomainReservation => _domainReservation;

    internal Guid ExecutionSessionId => _executionSessionId;

    internal Guid IdentityBranchId => _identityBranchId;

    internal StableIdentityLedger IdentityLedger => _identityLedger;

    internal WorldLifetimeControl LifetimeControl => _lifetimeControl;

    internal WorldLineageGrant LineageGrant => _lineageGrant;

    internal WorldOptions Options => _resolvedOptions.Options;

    internal bool OwnsWorkerPool => _ownsWorkerPool;

    internal WorldPreparedReplacement? PreparedReplacement => _preparedReplacement;

    internal WorldGuidRegistry Registry => _registry;

    internal WorldGuidReplacementPreparation? ReplacementPreparation => _replacementPreparation;

    internal Guid ReplayLineageId { get; }

    internal ResolvedWorldOptions ResolvedOptions => _resolvedOptions;

    internal int RestoreAttachmentCount => _restoreAttachmentCount;

    internal long RestoreAttachmentStorageByteReservation => _restoreAttachmentStorageByteReservation;

    internal long RestoreComponentWorkspaceByteReservation => _restoreComponentWorkspaceByteReservation;

    internal int RestoreControlRecordCount => _restoreControlRecordCount;

    internal long RestoreControlStorageByteReservation => _restoreControlStorageByteReservation;

    internal long RestoreEcsArchetypeMetadataByteReservation => _restoreEcsArchetypeMetadataByteReservation;

    internal WorldRestoreEcsPlan? RestoreEcsPlan => _restoreEcsPlan;

    internal long RestoreEcsStorageByteReservation => _restoreEcsStorageByteReservation;

    internal WorldRestoreIdentityState? RestoreIdentityState => _restoreIdentityState;

    internal long RestoreSceneControlPayloadByteReservation => _restoreSceneControlPayloadByteReservation;

    internal long RestoreSceneHierarchyWorkspaceByteReservation => _restoreSceneHierarchyWorkspaceByteReservation;

    internal long RestoreValidationWorkspaceByteReservation => _restoreValidationWorkspaceByteReservation;

    internal long RestoreWorkPlanByteReservation => _restoreWorkPlanByteReservation;

    internal int RestoreWorkPlanCapacity => _restoreWorkPlanCapacity;

    internal WorldSystemQuarantineReservation SystemQuarantineReservation => _systemQuarantineReservation;

    internal World? TargetWorld => _targetWorld;

    internal WorldWorkerPool WorkerPool => _workerPool;

    internal Guid WorldGuid => _worldGuid;

    internal WorldGuidGrant WorldGuidGrant => _worldGuidGrant;

    internal void ReleaseUntransferredRestoreResources()
    {
        if ((_restoreIdentityState is null) || (_restoreResourcesState != 0))
        {
            return;
        }

        _restoreResourcesState = 2;
        var enrolled = _admissionDomain.IsEnrolledWorld(_domainReservation);
        var cleanupSucceeded = false;
        var quarantineReturned = false;
        var capacityReturned = false;
        _preparedReplacement?.ReleaseUntransferredOperation();

        try
        {
            if (enrolled)
            {
                _admissionDomain.BeginWorldClose(_domainReservation);

                if (!_lifetimeControl.BeginRetirement())
                {
                    throw new InvalidOperationException("The untransferred World could not revoke its original binding.");
                }
            }
            else if (!_admissionDomain.TryReleaseFreshLineage(_lineageGrant))
            {
                throw new InvalidOperationException("The untransferred restore lineage reservation could not be released.");
            }

            cleanupSucceeded = true;
        }
        finally
        {
            try
            {
                _systemQuarantineReservation.Release();
                quarantineReturned = true;
            }
            finally
            {
                try
                {
                    WorldSystemInstanceRegistry.ReleaseLifetimeControlCapacity(_systemQuarantineReservation);
                    capacityReturned = true;
                }
                finally
                {
                    if (enrolled)
                    {
                        _admissionDomain.CompleteWorldClose(
                            _domainReservation,
                            cleanupSucceeded && quarantineReturned && capacityReturned);
                    }
                    else
                    {
                        _domainReservation.Dispose();
                    }
                }
            }
        }
    }

    internal void TransferRestoreResourcesToWorld()
    {
        if (_restoreIdentityState is null)
        {
            return;
        }

        if (_restoreResourcesState != 0)
        {
            throw new InvalidOperationException("The restore construction reservations were already transferred or released.");
        }

        _restoreResourcesState = 1;
    }

    internal void ValidateForConstruction()
    {
        if (Options.Registration.CosmeticCreditStorageSelected)
        {
            throw new InvalidOperationException(WorldRegistration.CosmeticCreditStorageUnavailableMessage);
        }

        var expectedIssuanceNamespace = _restoreIdentityState?.IssuanceNamespace ?? _worldGuid;
        var hasRestoreEntityRows = (_restoreIdentityState?.EntitySlots.LiveEntityCount ?? 0) != 0;
        var hasRestoreAttachments = _restoreAttachmentCount != 0;
        var hasRestoreArchetype = (_restoreEcsPlan is not null) && _restoreEcsPlan.HasArchetypes;
        var hasRestoreEcsWorkspace = (_restoreEcsPlan is not null) && (_restoreEcsPlan.ArchetypeCount != 0);
        var hasRestoreControlRecords = _restoreControlRecordCount != 0;
        var hasRestoreControlStorage = _restoreControlStorageByteReservation != 0;

        if ((_worldGuid == Guid.Empty)
            || (ReplayLineageId == Guid.Empty)
            || (_identityBranchId == Guid.Empty)
            || (_executionSessionId == Guid.Empty)
            || !_worldGuidGrant.IsOwnedBy(_registry)
            || (_worldGuidGrant.WorldGuid != _worldGuid)
            || !(_lifetimeControl?.IsBoundTo(
                _registry,
                _admissionDomain,
                _worldGuidGrant,
                _restoreIdentityState is not null) ?? false)
            || (_lineageGrant.WorldGuid != _worldGuid)
            || (_lineageGrant.LineageId != ReplayLineageId)
            || (_lineageGrant.IdentityBranchId != _identityBranchId)
            || (_lineageGrant.IssuanceNamespace != expectedIssuanceNamespace)
            || !_domainReservation.IsOwnedBy(_admissionDomain)
            || !_lineageGrant.IsOwnedBy(_admissionDomain)
            || !(_lineageGrant.Reservation?.IsOwnedBy(_admissionDomain) ?? false)
            || (_diagnosticsCapability is null)
            || (_diagnosticsCapability.WorldGuid != _worldGuid)
            || (_diagnosticsCapability.ExecutionSessionId != _executionSessionId)
            || (_identityLedger is null)
            || (_systemQuarantineReservation is null)
            || (_workerPool is null)
            || (_workerPool.WorkerCount != _resolvedOptions.CpuWorkerCount)
            || (_resolvedOptions.Options.Registration is null)
            || ((_restoreIdentityState is null) != (_preparedReplacement is null))
            || ((_restoreIdentityState is not null)
                && (!_preparedReplacement!.IsBoundTo(this)
                    || !ReferenceEquals(_preparedReplacement.TargetWorld, _targetWorld)))
            || (_restoreControlRecordCount > _resolvedOptions.Options.MaximumRecordedControls)
            || hasRestoreControlRecords != hasRestoreControlStorage
            || hasRestoreAttachments != (_restoreAttachmentStorageByteReservation != 0)
            || (_restoreAttachmentStorageByteReservation
                != WorldAttachmentTable.GetStorageByteReservationForCount(_restoreAttachmentCount))
            || (_restoreWorkPlanByteReservation > _resolvedOptions.Options.SchedulingLimits.MaximumWorkPlanBytes)
            || ((_restoreIdentityState is not null)
                && ((_restoreIdentityState.WorldGuid != _worldGuid)
                    || (_restoreIdentityState.ReplayLineageId != ReplayLineageId)
                    || (_restoreIdentityState.LineageMembership.WorldGuid != _worldGuid)
                    || (_restoreIdentityState.LineageMembership.ReplayLineageId != ReplayLineageId)
                    || (_restoreIdentityState.ActiveBranchId != _identityBranchId)
                    || !ReferenceEquals(_restoreIdentityState.IdentityLedger, _identityLedger)
                    || (_restoreEcsPlan is null)
                    || (_restoreEcsPlan.EntityCount != _restoreIdentityState.EntitySlots.LiveEntityCount)
                    || hasRestoreEntityRows != (_restoreEcsStorageByteReservation > 0)
                    || hasRestoreEntityRows != (_restoreWorkPlanCapacity > 0)
                    || hasRestoreEntityRows != (_restoreWorkPlanByteReservation > 0)
                    || hasRestoreArchetype != (_restoreEcsArchetypeMetadataByteReservation > 0)
                    || hasRestoreEcsWorkspace != (_restoreComponentWorkspaceByteReservation > 0)
                    || (_replacementPreparation is null))))
        {
            throw new InvalidOperationException("The standalone World construction plan is incomplete or inconsistent.");
        }

        if (_restoreIdentityState is not null
            && (!_replacementPreparation!.TryGetReplacementGrant(out var replacementGrant)
                || !replacementGrant.Equals(_worldGuidGrant)))
        {
            throw new InvalidOperationException("The restore construction plan does not own its exact provisional WorldGuid grant.");
        }
    }
}
