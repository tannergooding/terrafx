// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class WorldDefaultDomainOwner
{
    private const int StateActive = 0;
    private const int StateClosed = 1;
    private const int StatePending = 2;
    private const int StateTransferred = 3;

    private readonly WorldAdmissionDomain _admissionDomain;
    private readonly WorldGuidGrant _bindingGrant;
    private int _state;

    internal WorldDefaultDomainOwner(
        WorldAdmissionDomain admissionDomain,
        in WorldGuidGrant bindingGrant,
        bool isPending = false)
    {
        ArgumentNullException.ThrowIfNull(admissionDomain);

        if (!bindingGrant.IsValid)
        {
            throw new ArgumentException("The default-domain owner requires an issued WorldGuid binding.", nameof(bindingGrant));
        }

        _admissionDomain = admissionDomain;
        _bindingGrant = bindingGrant;
        _state = isPending ? StatePending : StateActive;
    }

    internal void AbandonPending()
    {
        if (Interlocked.CompareExchange(ref _state, StateClosed, StatePending) != StatePending)
        {
            throw new InvalidOperationException("The provisional default-domain owner is no longer pending.");
        }
    }

    internal void Activate()
    {
        if (Interlocked.CompareExchange(ref _state, StateActive, StatePending) != StatePending)
        {
            throw new InvalidOperationException("The provisional default-domain owner cannot be activated.");
        }
    }

    internal void Close()
    {
        if (Interlocked.CompareExchange(ref _state, StateClosed, StateActive) == StateActive)
        {
            _admissionDomain.Dispose();
        }
    }

    internal bool IsCurrentFor(WorldAdmissionDomain admissionDomain, in WorldGuidGrant bindingGrant)
        => (Volatile.Read(ref _state) == StateActive)
            && ReferenceEquals(_admissionDomain, admissionDomain)
            && _bindingGrant.Equals(bindingGrant);

    internal void MarkTransferred()
    {
        if (Interlocked.CompareExchange(ref _state, StateTransferred, StateActive) != StateActive)
        {
            throw new InvalidOperationException("The current default-domain owner is no longer active.");
        }
    }
}
