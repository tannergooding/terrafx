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
    private readonly WorldLineageGrant _lineageGrant;
    private readonly bool _ownsAdmissionDomain;
    private readonly bool _ownsWorkerPool;
    private readonly WorldGuidRegistry _registry;
    private readonly WorldGuidReplacementPreparation? _replacementPreparation;
    private readonly ResolvedWorldOptions _resolvedOptions;
    private readonly WorldRestoreIdentityState? _restoreIdentityState;
    private int _restoreResourcesState;
    private readonly WorldSystemQuarantineReservation _systemQuarantineReservation;
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
        bool ownsAdmissionDomain,
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
        WorldGuidReplacementPreparation? replacementPreparation = null)
    {
        _resolvedOptions = resolvedOptions;
        ReplayLineageId = replayLineageId;
        _identityBranchId = identityBranchId;
        _executionSessionId = executionSessionId;
        _admissionDomain = admissionDomain;
        _ownsAdmissionDomain = ownsAdmissionDomain;
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
    }

    internal WorldAdmissionDomain AdmissionDomain => _admissionDomain;

    internal WorldDiagnosticsCapability DiagnosticsCapability => _diagnosticsCapability;

    internal WorldDomainReservation DomainReservation => _domainReservation;

    internal Guid ExecutionSessionId => _executionSessionId;

    internal Guid IdentityBranchId => _identityBranchId;

    internal StableIdentityLedger IdentityLedger => _identityLedger;

    internal WorldLineageGrant LineageGrant => _lineageGrant;

    internal WorldOptions Options => _resolvedOptions.Options;

    internal bool OwnsAdmissionDomain => _ownsAdmissionDomain;

    internal bool OwnsWorkerPool => _ownsWorkerPool;

    internal WorldGuidRegistry Registry => _registry;

    internal WorldGuidReplacementPreparation? ReplacementPreparation => _replacementPreparation;

    internal Guid ReplayLineageId { get; }

    internal ResolvedWorldOptions ResolvedOptions => _resolvedOptions;

    internal WorldRestoreIdentityState? RestoreIdentityState => _restoreIdentityState;

    internal WorldSystemQuarantineReservation SystemQuarantineReservation => _systemQuarantineReservation;

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

        try
        {
            if (!_admissionDomain.TryReleaseFreshLineage(_lineageGrant))
            {
                throw new InvalidOperationException("The untransferred restore lineage reservation could not be released.");
            }
        }
        finally
        {
            try
            {
                _systemQuarantineReservation.Release();
            }
            finally
            {
                _domainReservation.Dispose();
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
        if ((_worldGuid == Guid.Empty)
            || (ReplayLineageId == Guid.Empty)
            || (_identityBranchId == Guid.Empty)
            || (_executionSessionId == Guid.Empty)
            || !_worldGuidGrant.IsOwnedBy(_registry)
            || (_worldGuidGrant.WorldGuid != _worldGuid)
            || (_lineageGrant.WorldGuid != _worldGuid)
            || (_lineageGrant.LineageId != ReplayLineageId)
            || (_lineageGrant.IdentityBranchId != _identityBranchId)
            || (_lineageGrant.IssuanceNamespace != _identityBranchId)
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
            || ((_restoreIdentityState is not null)
                && ((_restoreIdentityState.WorldGuid != _worldGuid)
                    || (_restoreIdentityState.ReplayLineageId != ReplayLineageId)
                    || (_restoreIdentityState.LineageMembership.WorldGuid != _worldGuid)
                    || (_restoreIdentityState.LineageMembership.ReplayLineageId != ReplayLineageId)
                    || (_restoreIdentityState.ActiveBranchId != _identityBranchId)
                    || !ReferenceEquals(_restoreIdentityState.IdentityLedger, _identityLedger)
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
