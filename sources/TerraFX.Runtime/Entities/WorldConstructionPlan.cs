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
    private readonly WorldGuidRegistry _registry;
    private readonly ResolvedWorldOptions _resolvedOptions;
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
        WorldDomainReservation domainReservation,
        WorldLineageGrant lineageGrant,
        WorldGuidRegistry registry,
        WorldGuidGrant worldGuidGrant,
        WorldDiagnosticsCapability diagnosticsCapability,
        StableIdentityLedger identityLedger)
    {
        _resolvedOptions = resolvedOptions;
        ReplayLineageId = replayLineageId;
        _identityBranchId = identityBranchId;
        _executionSessionId = executionSessionId;
        _admissionDomain = admissionDomain;
        _ownsAdmissionDomain = ownsAdmissionDomain;
        _domainReservation = domainReservation;
        _lineageGrant = lineageGrant;
        _registry = registry;
        _worldGuid = worldGuid;
        _worldGuidGrant = worldGuidGrant;
        _diagnosticsCapability = diagnosticsCapability;
        _identityLedger = identityLedger;
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

    internal WorldGuidRegistry Registry => _registry;

    internal Guid ReplayLineageId { get; }

    internal ResolvedWorldOptions ResolvedOptions => _resolvedOptions;

    internal Guid WorldGuid => _worldGuid;

    internal WorldGuidGrant WorldGuidGrant => _worldGuidGrant;

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
            || !_domainReservation.IsOwnedBy(_admissionDomain)
            || !_lineageGrant.IsOwnedBy(_admissionDomain)
            || !(_lineageGrant.Reservation?.IsOwnedBy(_admissionDomain) ?? false)
            || (_diagnosticsCapability is null)
            || (_diagnosticsCapability.WorldGuid != _worldGuid)
            || (_diagnosticsCapability.ExecutionSessionId != _executionSessionId)
            || (_identityLedger is null)
            || (_resolvedOptions.Options.Registration is null))
        {
            throw new InvalidOperationException("The standalone World construction plan is incomplete or inconsistent.");
        }
    }
}
