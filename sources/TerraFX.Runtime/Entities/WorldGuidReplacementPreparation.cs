// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class WorldGuidReplacementPreparation : IDisposable
{
    private readonly Lock _gate = new();
    private WorldDomainOperationCreditBundle? _credits;
    private readonly WorldGuidGrant _currentGrant;
    private bool _isCommitted;
    private bool _isDisposed;
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
                    if (!_registry.TryReleaseFresh(_replacementGrant))
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
                if (!_registry.TryReleaseFresh(_replacementGrant))
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
        out WorldGuidGrant replacementGrant,
        out WorldDomainOperationCreditBundle? credits)
    {
        lock (_gate)
        {
            if (_isDisposed || _isCommitted || (_credits is null))
            {
                replacementGrant = default;
                credits = null;
                return false;
            }

            if (!_registry.TryCommitReplacement(_currentGrant, _replacementGrant))
            {
                if (!_registry.TryReleaseFresh(_replacementGrant))
                {
                    throw new InvalidOperationException("The rejected WorldGuid successor grant could not be released.");
                }

                _credits.Dispose();
                _credits = null;
                _isDisposed = true;
                replacementGrant = default;
                credits = null;
                return false;
            }

            _isCommitted = true;
            replacementGrant = _replacementGrant;
            credits = _credits;
            _credits = null;
            return true;
        }
    }
}
