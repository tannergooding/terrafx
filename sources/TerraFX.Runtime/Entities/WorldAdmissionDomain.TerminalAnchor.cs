// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;

namespace TerraFX.Entities;

public sealed partial class WorldAdmissionDomain
{
    private const int TerminalAnchorStateBinding = 1;
    private const int TerminalAnchorStateBindingReleased = 10;
    private const int TerminalAnchorStateCompletionPending = 9;
    private const int TerminalAnchorStateDetaching = 3;
    private const int TerminalAnchorStateEscrowed = 6;
    private const int TerminalAnchorStateInvoking = 5;
    private const int TerminalAnchorStateNone = 0;
    private const int TerminalAnchorStatePrepared = 2;
    private const int TerminalAnchorStatePublished = 7;
    private const int TerminalAnchorStateRetained = 4;
    private const int TerminalAnchorStateReturned = 8;

    private int _terminalAnchorState;
    private Exception? _terminalFailure;
    private WorldGuidGrant _terminalGrant;
    private WorldLifetimeControl? _terminalLifetime;
    private WorldGuidRegistry? _terminalRegistry;
    private bool _terminalWorldReturnReady;

    internal bool CompleteTerminalAnchorBindingRelease(WorldGuidRegistry registry, in WorldGuidGrant grant)
    {
        ArgumentNullException.ThrowIfNull(registry);

        using (ReentrantGate.EnterScope(ref _growthGate))
        using (ReentrantGate.EnterScope(ref _gate))
        {
            if ((_terminalAnchorState != TerminalAnchorStateCompletionPending)
                || !ReferenceEquals(_terminalRegistry, registry)
                || !_terminalGrant.Equals(grant)
                || (_terminalLifetime is null)
                || !_isDisposed
                || !_terminalWorldReturnReady
                || (_activeInterestCount != 0)
                || (_activeOperationCreditCount != _activeRetainedDiagnosticSourceCreditCount)
                || (_activeReservationCount != 0))
            {
                return false;
            }

            if (_activeOperationCreditCount == 0)
            {
                if (_registryCharge != RegistrarControlBytes)
                {
                    return false;
                }

                if (_retirement.Task.IsCompleted)
                {
                    _registryCharge = 0;
                    _terminalWorldReturnReady = false;
                    _terminalAnchorState = TerminalAnchorStateNone;
                }
                else
                {
                    _terminalAnchorState = TerminalAnchorStateBindingReleased;
                }
            }
            else
            {
                _terminalAnchorState = TerminalAnchorStateBindingReleased;
            }

            _terminalRegistry = null;
            _terminalGrant = default;
            _terminalLifetime = null;
            _terminalFailure = null;
            return true;
        }
    }

    internal bool IsTerminalAnchorBound(WorldGuidRegistry registry, in WorldGuidGrant grant, WorldLifetimeControl lifetime)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(lifetime);

        using (ReentrantGate.EnterScope(ref _gate))
        {
            return ReferenceEquals(_terminalRegistry, registry)
                && _terminalGrant.Equals(grant)
                && ReferenceEquals(_terminalLifetime, lifetime)
                && (_terminalAnchorState is not TerminalAnchorStateNone and not TerminalAnchorStateBinding);
        }
    }

    internal bool TryAcceptTerminalCallerReturn(WorldLifetimeControl lifetime)
    {
        ArgumentNullException.ThrowIfNull(lifetime);

        WorldGuidRegistry registry;
        WorldGuidGrant grant;
        WorldGuidRetirementPermit permit;

        using (ReentrantGate.EnterScope(ref _gate))
        {
            if ((_terminalAnchorState is not (TerminalAnchorStateEscrowed or TerminalAnchorStatePublished))
                || !_terminalWorldReturnReady
                || !_isDisposed
                || (_activeInterestCount != 0)
                || (_activeReservationCount != 0)
                || (_activeOperationCreditCount != _activeRetainedDiagnosticSourceCreditCount)
                || !ReferenceEquals(_terminalLifetime, lifetime))
            {
                return false;
            }

            registry = _terminalRegistry
                ?? throw new InvalidOperationException("The terminal anchor lost its WorldGuid registry.");
            grant = _terminalGrant;
            permit = lifetime.RetirementPermit;
        }

        if (!registry.TryMarkTerminalPublisherReturned(permit, grant, this))
        {
            return false;
        }

        using (ReentrantGate.EnterScope(ref _gate))
        {
            if ((_terminalAnchorState is not (TerminalAnchorStateEscrowed or TerminalAnchorStatePublished))
                || !_terminalWorldReturnReady
                || !ReferenceEquals(_terminalRegistry, registry)
                || !_terminalGrant.Equals(grant)
                || !ReferenceEquals(_terminalLifetime, lifetime))
            {
                throw new InvalidOperationException("The terminal anchor changed while accepting its enclosing caller return.");
            }

            _terminalAnchorState = TerminalAnchorStateReturned;
        }

        return true;
    }

    internal bool TryAttachTerminalAnchor(WorldGuidRegistry registry, in WorldGuidGrant grant)
    {
        ArgumentNullException.ThrowIfNull(registry);

        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (_isDisposed || (_terminalAnchorState != TerminalAnchorStateNone))
            {
                return false;
            }

            _terminalRegistry = registry;
            _terminalGrant = grant;
            _terminalLifetime = null;
            _terminalWorldReturnReady = false;
            _terminalAnchorState = TerminalAnchorStateBinding;
        }

        if (!registry.TryAttachTerminalAnchor(grant, this))
        {
            using var scope = ReentrantGate.EnterScope(ref _gate);
            _terminalRegistry = null;
            _terminalGrant = default;
            _terminalLifetime = null;
            _terminalWorldReturnReady = false;
            _terminalAnchorState = TerminalAnchorStateNone;
            return false;
        }

        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (_terminalAnchorState != TerminalAnchorStateBinding)
            {
                throw new InvalidOperationException("The terminal anchor changed during its binding transaction.");
            }

            _terminalAnchorState = TerminalAnchorStatePrepared;
        }

        return true;
    }

    internal bool TryAttachTerminalAnchor(WorldLifetimeControl lifetime, WorldDomainReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(reservation);

        var registry = lifetime.Registry;
        var grant = lifetime.WorldGuidGrant;

        if (!grant.IsOwnedBy(registry)
            || !reservation.IsOwnedBy(this))
        {
            return false;
        }

        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (_isDisposed
                || (_terminalAnchorState != TerminalAnchorStateNone)
                || (_activeInterestCount != 1)
                || (_activeOperationCreditCount != _activeRetainedDiagnosticSourceCreditCount)
                || (_activeReservationCount != 0)
                || (_pendingGrowthBytes != 0)
                || (_prospectiveInterestCount != 0)
                || (_worldUseRawTicket.State == WorldUseRawPending)
                || !IsActiveWorldReservation(reservation))
            {
                return false;
            }

            ref var interest = ref _interests[reservation.InterestIndex];

            if (!ReferenceEquals(interest.WorldLifetime, lifetime)
                || (interest.WorldCloseProgress != 0)
                || (interest.WorldReturnPartCount != 0)
                || (interest.SceneConstructionBytes != 0)
                || (interest.SceneStorageBytes != 0)
                || (interest.PendingSceneConstructionBytes != 0)
                || (interest.SceneConstructorCount != 0)
                || (reservation.IndependentUseBytes != 0))
            {
                return false;
            }

            _terminalRegistry = registry;
            _terminalGrant = grant;
            _terminalLifetime = lifetime;
            _terminalWorldReturnReady = false;
            _terminalAnchorState = TerminalAnchorStateBinding;
        }

        if (!registry.TryAttachTerminalAnchor(grant, this, lifetime))
        {
            using var scope = ReentrantGate.EnterScope(ref _gate);
            _terminalRegistry = null;
            _terminalGrant = default;
            _terminalLifetime = null;
            _terminalWorldReturnReady = false;
            _terminalAnchorState = TerminalAnchorStateNone;
            return false;
        }

        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (_terminalAnchorState != TerminalAnchorStateBinding)
            {
                throw new InvalidOperationException("The terminal anchor changed during its binding transaction.");
            }

            _terminalAnchorState = TerminalAnchorStatePrepared;
        }

        return true;
    }

    internal bool TryBeginTerminalInvocation(WorldGuidRegistry registry, in WorldGuidGrant grant, WorldLifetimeControl lifetime)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(lifetime);

        using (ReentrantGate.EnterScope(ref _gate))
        {
            if ((_terminalAnchorState != TerminalAnchorStatePrepared)
                || !ReferenceEquals(_terminalRegistry, registry)
                || !_terminalGrant.Equals(grant)
                || !ReferenceEquals(_terminalLifetime, lifetime))
            {
                return false;
            }

            _terminalAnchorState = TerminalAnchorStateBinding;
        }

        if (!registry.TryBeginTerminalInvocation(grant, this))
        {
            using var scope = ReentrantGate.EnterScope(ref _gate);

            if (_terminalAnchorState == TerminalAnchorStateBinding)
            {
                _terminalAnchorState = TerminalAnchorStatePrepared;
            }

            return false;
        }

        using (ReentrantGate.EnterScope(ref _gate))
        {
            if ((_terminalAnchorState != TerminalAnchorStateBinding)
                || !ReferenceEquals(_terminalRegistry, registry)
                || !_terminalGrant.Equals(grant)
                || !ReferenceEquals(_terminalLifetime, lifetime))
            {
                throw new InvalidOperationException("The terminal anchor changed while beginning its enclosing invocation.");
            }

            _terminalAnchorState = TerminalAnchorStateInvoking;
        }

        return true;
    }

    internal bool TryCancelTerminalAnchor()
    {
        WorldGuidRegistry registry;
        WorldGuidGrant grant;

        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (_terminalAnchorState != TerminalAnchorStatePrepared)
            {
                return false;
            }

            registry = _terminalRegistry
                ?? throw new InvalidOperationException("The prepared terminal anchor lost its WorldGuid registry.");
            grant = _terminalGrant;
            _terminalAnchorState = TerminalAnchorStateDetaching;
        }

        if (!registry.TryDetachTerminalAnchor(grant, this))
        {
            using var scope = ReentrantGate.EnterScope(ref _gate);

            if (_terminalAnchorState == TerminalAnchorStateDetaching)
            {
                _terminalAnchorState = TerminalAnchorStatePrepared;
            }

            return false;
        }

        using (ReentrantGate.EnterScope(ref _gate))
        {
            if ((_terminalAnchorState != TerminalAnchorStateDetaching)
                || !ReferenceEquals(_terminalRegistry, registry)
                || !_terminalGrant.Equals(grant))
            {
                throw new InvalidOperationException("The terminal anchor changed during its cancellation.");
            }

            _terminalRegistry = null;
            _terminalGrant = default;
            _terminalLifetime = null;
            _terminalWorldReturnReady = false;
            _terminalAnchorState = TerminalAnchorStateNone;
        }

        return true;
    }

    internal bool TryCommitTerminalAnchorCompletion(WorldGuidRegistry registry, in WorldGuidGrant grant)
    {
        ArgumentNullException.ThrowIfNull(registry);

        using (ReentrantGate.EnterScope(ref _growthGate))
        using (ReentrantGate.EnterScope(ref _gate))
        {
            if ((_terminalAnchorState != TerminalAnchorStateReturned)
                || !ReferenceEquals(_terminalRegistry, registry)
                || !_terminalGrant.Equals(grant)
                || (_terminalLifetime is null)
                || !_isDisposed
                || !_terminalWorldReturnReady
                || (_activeInterestCount != 0)
                || (_activeOperationCreditCount != _activeRetainedDiagnosticSourceCreditCount)
                || (_activeReservationCount != 0))
            {
                return false;
            }

            if ((_activeOperationCreditCount == 0) && (_registryCharge != RegistrarControlBytes))
            {
                return false;
            }

            _terminalAnchorState = TerminalAnchorStateCompletionPending;
            return true;
        }
    }

    internal bool TryEscrowTerminalRetirement(WorldLifetimeControl lifetime, WorldGuidRetirementPermit permit)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(permit);

        WorldGuidRegistry registry;
        WorldGuidGrant grant;

        using (ReentrantGate.EnterScope(ref _gate))
        {
            if ((_terminalAnchorState != TerminalAnchorStateInvoking)
                || !ReferenceEquals(_terminalLifetime, lifetime))
            {
                return false;
            }

            registry = _terminalRegistry
                ?? throw new InvalidOperationException("The invoking terminal anchor lost its WorldGuid registry.");
            grant = _terminalGrant;
            _terminalAnchorState = TerminalAnchorStateBinding;
        }

        if (!registry.TryEscrowTerminalRetirement(permit, this, lifetime, grant))
        {
            using var scope = ReentrantGate.EnterScope(ref _gate);

            if (_terminalAnchorState == TerminalAnchorStateBinding)
            {
                _terminalAnchorState = TerminalAnchorStateInvoking;
            }

            return false;
        }

        using (ReentrantGate.EnterScope(ref _gate))
        {
            if ((_terminalAnchorState != TerminalAnchorStateBinding)
                || !ReferenceEquals(_terminalRegistry, registry)
                || !_terminalGrant.Equals(grant)
                || !ReferenceEquals(_terminalLifetime, lifetime))
            {
                throw new InvalidOperationException("The terminal anchor changed while escrowing its retirement permit.");
            }

            _terminalAnchorState = TerminalAnchorStateEscrowed;
        }

        return true;
    }

    internal bool TryRetainTerminalFailure(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        WorldGuidRegistry registry;
        WorldGuidGrant grant;

        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (_terminalAnchorState is TerminalAnchorStateNone or TerminalAnchorStateRetained)
            {
                return false;
            }

            registry = _terminalRegistry
                ?? throw new InvalidOperationException("The prepared terminal anchor lost its WorldGuid registry.");
            grant = _terminalGrant;
            _terminalAnchorState = TerminalAnchorStateDetaching;
        }

        if (!registry.TryRetainTerminalFailure(grant, this, failure))
        {
            using var scope = ReentrantGate.EnterScope(ref _gate);

            if (_terminalAnchorState == TerminalAnchorStateDetaching)
            {
                _terminalAnchorState = TerminalAnchorStatePrepared;
            }

            return false;
        }

        using (ReentrantGate.EnterScope(ref _gate))
        {
            if ((_terminalAnchorState != TerminalAnchorStateDetaching)
                || !ReferenceEquals(_terminalRegistry, registry)
                || !_terminalGrant.Equals(grant))
            {
                throw new InvalidOperationException("The terminal anchor changed while retaining its failure.");
            }

            _terminalFailure = failure;
            _terminalAnchorState = TerminalAnchorStateRetained;
        }

        return true;
    }
}
