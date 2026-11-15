// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class WorldGuidReplacementPreparation : IDisposable
{
    private readonly Lock _gate = new();
    private WorldDomainOperationCreditBundle? _credits;
    private readonly WorldGuidGrant _currentGrant;
    private readonly WorldAdmissionDomain? _domain;
    private bool _isCaptured;
    private bool _isCommitted;
    private bool _isDisposed;
    private bool _isReplacementGrantTransferred;
    private readonly WorldGuidRegistry _registry;
    private readonly WorldGuidGrant _replacementGrant;

    internal WorldGuidReplacementPreparation(
        WorldGuidRegistry registry,
        WorldGuidGrant currentGrant,
        WorldGuidGrant replacementGrant,
        WorldDomainOperationCreditBundle? credits = null)
    {
        _registry = registry;
        _currentGrant = currentGrant;
        _replacementGrant = replacementGrant;
        _credits = credits;
        _domain = credits?.Domain;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;

            if (!_isCommitted)
            {
                try
                {
                    if (!_isReplacementGrantTransferred && !_registry.TryReleaseFresh(_replacementGrant))
                    {
                        throw new InvalidOperationException("The prepared WorldGuid successor grant could not be released.");
                    }
                }
                finally
                {
                    _credits?.Dispose();
                    _credits = null;
                }
            }
        }
    }

    internal IDisposable AcquireRetainedUse(
        long sourceWorkspaceBytes,
        long targetWorkspaceBytes,
        long dataWorkspaceBytes,
        long cleanupWorkspaceBytes)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (_isCommitted)
            {
                throw new InvalidOperationException("The committed replacement no longer owns its operation-credit bundle.");
            }

            var credits = _credits
                ?? throw new InvalidOperationException("The replacement preparation has no load operation-credit bundle.");
            return credits.AcquireRetainedUse(
                sourceWorkspaceBytes,
                targetWorkspaceBytes,
                dataWorkspaceBytes,
                cleanupWorkspaceBytes);
        }
    }

    internal bool IsBoundTo(WorldGuidRegistry registry, in WorldGuidGrant currentGrant)
    {
        ArgumentNullException.ThrowIfNull(registry);

        lock (_gate)
        {
            return !_isDisposed
                && !_isCommitted
                && ReferenceEquals(_registry, registry)
                && _currentGrant.Equals(currentGrant);
        }
    }

    internal bool IsTargetCurrent()
    {
        lock (_gate)
        {
            return !_isDisposed
                && !_isCommitted
                && _registry.IsActive(_currentGrant);
        }
    }

    internal bool MarkCapturedUnderAdmissionGate()
    {
        if (_isDisposed || _isCommitted || _isCaptured || (_credits is null))
        {
            return false;
        }

        _isCaptured = true;
        return true;
    }

    internal WorldDomainOperationCreditBundle TransferCapturedCredits()
    {
        lock (_gate)
        {
            if (_isDisposed || _isCommitted || !_isCaptured)
            {
                throw new InvalidOperationException("Only a captured replacement can transfer its operation credits.");
            }

            var credits = _credits
                ?? throw new InvalidOperationException("The captured replacement has no remaining operation credits.");
            _credits = null;
            return credits;
        }
    }

    internal void TransferReplacementGrantToCandidate()
    {
        lock (_gate)
        {
            if (_isDisposed || _isCommitted || _isReplacementGrantTransferred)
            {
                throw new InvalidOperationException("The prepared WorldGuid grant cannot be transferred to this candidate.");
            }

            _isReplacementGrantTransferred = true;
        }
    }

    internal bool TryCaptureCommit(
        out WorldGuidRefusal bindingRefusal,
        out WorldAdmissionRefusal admissionRefusal)
    {
        lock (_gate)
        {
            if (_isDisposed || _isCommitted || _isCaptured || (_credits is null))
            {
                bindingRefusal = new WorldGuidRefusal(WorldGuidRefusal.TargetBindingStale);
                admissionRefusal = WorldAdmissionRefusal.None;
                return false;
            }

            return _registry.TryCaptureReplacement(
                _currentGrant,
                _replacementGrant,
                _credits.Domain,
                this,
                out bindingRefusal,
                out admissionRefusal);
        }
    }

    internal bool TryCommit(out WorldGuidGrant replacementGrant)
    {
        lock (_gate)
        {
            if (_isDisposed || _isCommitted)
            {
                replacementGrant = default;
                return false;
            }

            if (_credits is not null)
            {
                throw new InvalidOperationException("A load replacement must transfer its operation credits when committed.");
            }

            if (!_registry.TryCommitReplacement(_currentGrant, _replacementGrant))
            {
                if (!_isReplacementGrantTransferred && !_registry.TryReleaseFresh(_replacementGrant))
                {
                    throw new InvalidOperationException("The rejected WorldGuid successor grant could not be released.");
                }

                _isDisposed = true;
                replacementGrant = default;
                return false;
            }

            _isCommitted = true;
            replacementGrant = _replacementGrant;
            return true;
        }
    }

    internal bool TryCommitWithCredits(
        WorldDomainOperationCreditBundle credits,
        out WorldGuidGrant replacementGrant,
        WorldLineageGrant? replacementLineageGrant = null)
    {
        ArgumentNullException.ThrowIfNull(credits);
        lock (_gate)
        {
            if (_isDisposed || _isCommitted || !_isCaptured || (_credits is not null))
            {
                replacementGrant = default;
                return false;
            }

            if (!ReferenceEquals(_domain, credits.Domain))
            {
                throw new InvalidOperationException("The replacement operation credits belong to another admission domain.");
            }

            var replacementCommitted = (replacementLineageGrant is WorldLineageGrant lineageGrant)
                ? _registry.TryCommitReplacementWithLineage(
                    _currentGrant,
                    _replacementGrant,
                    credits.Domain,
                    lineageGrant)
                : _registry.TryCommitReplacement(_currentGrant, _replacementGrant);

            if (!replacementCommitted)
            {
                if (!_isReplacementGrantTransferred && !_registry.TryReleaseFresh(_replacementGrant))
                {
                    throw new InvalidOperationException("The rejected WorldGuid successor grant could not be released.");
                }

                _isDisposed = true;
                replacementGrant = default;
                return false;
            }

            _isCommitted = true;
            replacementGrant = _replacementGrant;
            return true;
        }
    }

    internal bool TryGetReplacementGrant(out WorldGuidGrant replacementGrant)
    {
        lock (_gate)
        {
            if (_isDisposed || _isCommitted)
            {
                replacementGrant = default;
                return false;
            }

            replacementGrant = _replacementGrant;
            return true;
        }
    }
}
