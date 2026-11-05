// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class WorldGuidReplacementPreparation : IDisposable
{
    private readonly Lock _gate = new();
    private readonly WorldGuidGrant _currentGrant;
    private bool _isCommitted;
    private bool _isDisposed;
    private readonly WorldGuidRegistry _registry;
    private readonly WorldGuidGrant _replacementGrant;

    internal WorldGuidReplacementPreparation(
        WorldGuidRegistry registry,
        WorldGuidGrant currentGrant,
        WorldGuidGrant replacementGrant)
    {
        _registry = registry;
        _currentGrant = currentGrant;
        _replacementGrant = replacementGrant;
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

            if (!_isCommitted && !_registry.TryReleaseFresh(_replacementGrant))
            {
                throw new InvalidOperationException("The prepared WorldGuid successor grant could not be released.");
            }
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
}
