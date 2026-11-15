// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class WorldLifetimeControl : IWorldGuidRetentionOwner
{
    private const int StateActive = 3;
    private const int StateFreshPrepared = 0;
    private const int StateQuarantined = 6;
    private const int StateReplacementCandidateOwned = 2;
    private const int StateReplacementCandidatePrepared = 1;
    private const int StateRetired = 5;
    private const int StateRevoked = 4;

    private readonly WorldAdmissionDomain _admissionDomain;
    private WorldDefaultDomainOwner? _defaultDomainOwner;
    private WorldDomainReservation? _domainReservation;
    private readonly Lock _gate = new();
    private bool _hasCommittedHistory;
    private WorldLineageGrant _lineageGrant;
    private readonly WorldGuidRegistry _registry;
    private int _state;
    private readonly WorldGuidGrant _worldGuidGrant;

    internal WorldLifetimeControl(
        WorldGuidRegistry registry,
        WorldAdmissionDomain admissionDomain,
        in WorldGuidGrant worldGuidGrant,
        WorldDefaultDomainOwner? defaultDomainOwner,
        WorldDomainReservation domainReservation,
        in WorldLineageGrant lineageGrant,
        bool isReplacementCandidate)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(admissionDomain);
        ArgumentNullException.ThrowIfNull(domainReservation);

        if (!worldGuidGrant.IsOwnedBy(registry))
        {
            throw new ArgumentException("The World lifetime requires its exact WorldGuid registry binding.", nameof(worldGuidGrant));
        }

        if (!domainReservation.IsOwnedBy(admissionDomain)
            || !lineageGrant.IsOwnedBy(admissionDomain)
            || !ReferenceEquals(lineageGrant.Reservation, domainReservation)
            || (lineageGrant.WorldGuid != worldGuidGrant.WorldGuid))
        {
            throw new ArgumentException("The World lifetime requires its exact admission-domain interest and lineage root.", nameof(lineageGrant));
        }

        if ((defaultDomainOwner is not null)
            && !defaultDomainOwner.IsCurrentFor(admissionDomain, worldGuidGrant))
        {
            throw new ArgumentException("The default-domain owner does not match the World binding.", nameof(defaultDomainOwner));
        }

        _registry = registry;
        _admissionDomain = admissionDomain;
        _worldGuidGrant = worldGuidGrant;
        _defaultDomainOwner = defaultDomainOwner;
        _domainReservation = domainReservation;
        _lineageGrant = lineageGrant;
        _state = isReplacementCandidate ? StateReplacementCandidatePrepared : StateFreshPrepared;
    }

    internal WorldDefaultDomainOwner? DefaultDomainOwner => _defaultDomainOwner;

    internal bool HasCommittedHistory => Volatile.Read(ref _hasCommittedHistory);

    internal bool IsQuarantined => Volatile.Read(ref _state) == StateQuarantined;

    internal WorldGuidRegistry Registry => _registry;

    internal WorldGuidGrant WorldGuidGrant => _worldGuidGrant;

    internal void AbandonUnpublished()
    {
        if (!BeginRetirement() || !CompleteRetirement())
        {
            throw new InvalidOperationException("The unpublished World binding could not be retired.");
        }

        CloseDefaultDomain();
    }

    internal void AdoptReplacementGrant()
    {
        lock (_gate)
        {
            if (_state != StateReplacementCandidatePrepared)
            {
                throw new InvalidOperationException("The restore candidate cannot adopt this WorldGuid grant.");
            }

            _state = StateReplacementCandidateOwned;
        }
    }

    internal bool BeginRetirement()
    {
        lock (_gate)
        {
            if (_state is StateRetired or StateRevoked or StateQuarantined or StateReplacementCandidatePrepared)
            {
                return true;
            }

            if (_state is StateActive or StateFreshPrepared or StateReplacementCandidateOwned)
            {
                if (!_registry.TryRevokeForRetirement(_worldGuidGrant))
                {
                    return false;
                }

                _state = StateRevoked;
            }

            return _state is StateFreshPrepared or StateReplacementCandidateOwned or StateRevoked;
        }
    }

    internal void CloseDefaultDomain()
    {
        WorldDefaultDomainOwner? defaultDomainOwner;

        lock (_gate)
        {
            if (_state is not (StateFreshPrepared
                or StateReplacementCandidatePrepared
                or StateReplacementCandidateOwned
                or StateRevoked
                or StateQuarantined
                or StateRetired))
            {
                throw new InvalidOperationException("Only a revoked or retired World lifetime can close its default domain.");
            }

            defaultDomainOwner = _defaultDomainOwner;
            _defaultDomainOwner = null;
        }

        defaultDomainOwner?.Close();
    }

    internal bool CompleteRetirement()
    {
        lock (_gate)
        {
            if (_state == StateRetired)
            {
                return true;
            }

            if (_state == StateReplacementCandidatePrepared)
            {
                _state = StateRetired;
                _hasCommittedHistory = false;
                _domainReservation = null;
                _lineageGrant = default;
                return true;
            }

            if (_state is not (StateFreshPrepared or StateReplacementCandidateOwned or StateRevoked))
            {
                return false;
            }

            var permit = new WorldGuidRetirementPermit(this, _registry, _worldGuidGrant);

            if (!_registry.TryRetire(permit))
            {
                return false;
            }

            _state = StateRetired;
            _hasCommittedHistory = false;
            _domainReservation = null;
            _lineageGrant = default;
            return true;
        }
    }

    internal bool IsBoundTo(
        WorldGuidRegistry registry,
        WorldAdmissionDomain admissionDomain,
        in WorldGuidGrant worldGuidGrant,
        bool isReplacementCandidate)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(admissionDomain);
        lock (_gate)
        {
            return ReferenceEquals(_registry, registry)
                && ReferenceEquals(_admissionDomain, admissionDomain)
                && _worldGuidGrant.Equals(worldGuidGrant)
                && (_state == (isReplacementCandidate ? StateReplacementCandidatePrepared : StateFreshPrepared));
        }
    }

    internal bool IsQuarantinePermitCurrent(in WorldGuidGrant worldGuidGrant)
        => _worldGuidGrant.Equals(worldGuidGrant)
            && (Volatile.Read(ref _state) == StateRevoked);

    internal bool IsReadyToReplaceWith(
        WorldLifetimeControl replacement,
        WorldDefaultDomainHandoff? defaultDomainHandoff)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        lock (_gate)
        {
            lock (replacement._gate)
            {
                if ((_state != StateActive)
                    || (replacement._state != StateReplacementCandidateOwned)
                    || !ReferenceEquals(_admissionDomain, replacement._admissionDomain)
                    || (replacement._defaultDomainOwner is not null))
                {
                    return false;
                }

                if (_defaultDomainOwner is null)
                {
                    return defaultDomainHandoff is null;
                }

                return (defaultDomainHandoff is not null)
                    && defaultDomainHandoff.IsBoundTo(
                        _defaultDomainOwner,
                        _admissionDomain,
                        _worldGuidGrant,
                        replacement._worldGuidGrant);
            }
        }
    }

    internal bool IsRetirementPermitCurrent(in WorldGuidGrant worldGuidGrant)
        => _worldGuidGrant.Equals(worldGuidGrant)
            && (Volatile.Read(ref _state) is StateFreshPrepared or StateReplacementCandidateOwned or StateRevoked);

    internal void PublishFresh()
    {
        lock (_gate)
        {
            if (_state != StateFreshPrepared)
            {
                throw new InvalidOperationException("The fresh World lifetime cannot be published.");
            }

            _state = StateActive;
            _hasCommittedHistory = true;
        }
    }

    internal bool QuarantineAfterCleanupFailure()
    {
        lock (_gate)
        {
            if ((_state != StateRevoked)
                || (_domainReservation is null)
                || !_lineageGrant.IsOwnedBy(_admissionDomain)
                || !ReferenceEquals(_lineageGrant.Reservation, _domainReservation))
            {
                return false;
            }

            var permit = new WorldGuidRetirementPermit(this, _registry, _worldGuidGrant);

            if (!_registry.TryQuarantine(permit))
            {
                return false;
            }

            _state = StateQuarantined;
            return true;
        }
    }

    internal void TransferToReplacement(
        WorldLifetimeControl replacement,
        WorldDefaultDomainHandoff? defaultDomainHandoff)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        lock (_gate)
        {
            lock (replacement._gate)
            {
                if ((_state != StateActive)
                    || (replacement._state != StateReplacementCandidateOwned)
                    || !ReferenceEquals(_admissionDomain, replacement._admissionDomain)
                    || (replacement._defaultDomainOwner is not null)
                    || ((_defaultDomainOwner is null) != (defaultDomainHandoff is null)))
                {
                    throw new InvalidOperationException("The World lifetime handoff is not ready.");
                }

                if (_defaultDomainOwner is not null)
                {
                    replacement._defaultDomainOwner = defaultDomainHandoff!.Consume(
                        _defaultDomainOwner,
                        _admissionDomain,
                        _worldGuidGrant,
                        replacement._worldGuidGrant);
                    _defaultDomainOwner = null;
                }

                _state = StateRevoked;
                replacement._state = StateActive;
                replacement._hasCommittedHistory = true;
            }
        }
    }
}
