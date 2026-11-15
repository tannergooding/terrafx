// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class WorldDefaultDomainHandoff : IDisposable
{
    internal const long HandoffBytes = 256;

    private const int StateAbandoned = 2;
    private const int StateConsumed = 1;
    private const int StateDisposed = 3;
    private const int StatePending = 0;

    private readonly WorldGuidGrant _currentGrant;
    private readonly WorldDefaultDomainOwner _currentOwner;
    private readonly WorldAdmissionDomain _domain;
    private readonly Lock _gate = new();
    private readonly WorldDomainOperationCredit _operationCredit;
    private readonly WorldGuidGrant _replacementGrant;
    private readonly WorldDefaultDomainOwner _replacementOwner;
    private int _state;

    internal WorldDefaultDomainHandoff(
        WorldDefaultDomainOwner currentOwner,
        in WorldGuidGrant currentGrant,
        in WorldGuidGrant replacementGrant,
        WorldDomainOperationCredit operationCredit)
    {
        ArgumentNullException.ThrowIfNull(currentOwner);
        ArgumentNullException.ThrowIfNull(operationCredit);

        if (!currentOwner.IsCurrentFor(operationCredit.Domain, currentGrant))
        {
            throw new ArgumentException("The handoff does not match the current default-domain owner.", nameof(currentOwner));
        }

        if (!replacementGrant.IsValid)
        {
            throw new ArgumentException("The handoff requires an issued replacement WorldGuid binding.", nameof(replacementGrant));
        }

        _currentOwner = currentOwner;
        _currentGrant = currentGrant;
        _replacementGrant = replacementGrant;
        _domain = operationCredit.Domain;
        _operationCredit = operationCredit;
        _replacementOwner = new WorldDefaultDomainOwner(
            _domain,
            replacementGrant,
            isPending: true);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_state == StateDisposed)
            {
                return;
            }

            try
            {
                if (_state == StatePending)
                {
                    _state = StateAbandoned;
                    _replacementOwner.AbandonPending();
                }
            }
            finally
            {
                _state = StateDisposed;
                _operationCredit.Dispose();
            }
        }
    }

    internal WorldDefaultDomainOwner Consume(
        WorldDefaultDomainOwner currentOwner,
        WorldAdmissionDomain admissionDomain,
        in WorldGuidGrant currentGrant,
        in WorldGuidGrant replacementGrant)
    {
        ArgumentNullException.ThrowIfNull(currentOwner);
        ArgumentNullException.ThrowIfNull(admissionDomain);

        lock (_gate)
        {
            if ((_state != StatePending)
                || !ReferenceEquals(_currentOwner, currentOwner)
                || !ReferenceEquals(_domain, admissionDomain)
                || !_currentGrant.Equals(currentGrant)
                || !_replacementGrant.Equals(replacementGrant)
                || !currentOwner.IsCurrentFor(admissionDomain, currentGrant))
            {
                throw new InvalidOperationException("The default-domain handoff is stale or belongs to another replacement.");
            }

            currentOwner.MarkTransferred();
            _replacementOwner.Activate();
            _state = StateConsumed;
            return _replacementOwner;
        }
    }

    internal bool IsBoundTo(
        WorldDefaultDomainOwner currentOwner,
        WorldAdmissionDomain admissionDomain,
        in WorldGuidGrant currentGrant,
        in WorldGuidGrant replacementGrant)
    {
        ArgumentNullException.ThrowIfNull(currentOwner);
        ArgumentNullException.ThrowIfNull(admissionDomain);

        lock (_gate)
        {
            return (_state == StatePending)
                && ReferenceEquals(_currentOwner, currentOwner)
                && ReferenceEquals(_domain, admissionDomain)
                && _currentGrant.Equals(currentGrant)
                && _replacementGrant.Equals(replacementGrant)
                && currentOwner.IsCurrentFor(admissionDomain, currentGrant);
        }
    }
}
