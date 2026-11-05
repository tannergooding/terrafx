// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class WorldDomainOperationCredit : IDisposable
{
    private readonly long _bytes;
    private readonly WorldAdmissionDomain _domain;
    private readonly Lock _gate = new();
    private int _activeUseCount;
    private long _activeWorkspaceBytes;
    private bool _isChargeReleased;
    private bool _isDisposed;

    internal WorldDomainOperationCredit(WorldAdmissionDomain domain, long bytes)
    {
        _domain = domain;
        _bytes = bytes;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _isDisposed = true;

            if (_activeUseCount == 0)
            {
                ReleaseCharge();
            }
        }
    }

    internal IDisposable AcquireRetainedUse(long workspaceBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(workspaceBytes);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (workspaceBytes > (_bytes - _activeWorkspaceBytes))
            {
                throw new ArgumentException("The operation credit does not cover the retained workspace.", nameof(workspaceBytes));
            }

            var lease = new UseLease(this, workspaceBytes);
            _activeUseCount = checked(_activeUseCount + 1);
            _activeWorkspaceBytes = checked(_activeWorkspaceBytes + workspaceBytes);
            return lease;
        }
    }

    private void ReleaseCharge()
    {
        if (!_isChargeReleased)
        {
            _domain.ReleaseOperationCredit(_bytes);
            _isChargeReleased = true;
        }
    }

    private void ReleaseUse(long workspaceBytes)
    {
        lock (_gate)
        {
            if ((_activeUseCount <= 0)
                || (workspaceBytes < 0)
                || (workspaceBytes > _activeWorkspaceBytes))
            {
                throw new InvalidOperationException("The operation credit has no matching retained use.");
            }

            _activeUseCount--;
            _activeWorkspaceBytes -= workspaceBytes;

            if (_isDisposed && (_activeUseCount == 0))
            {
                ReleaseCharge();
            }
        }
    }

    private sealed class UseLease : IDisposable
    {
        private readonly WorldDomainOperationCredit _credit;
        private int _isReleased;
        private readonly long _workspaceBytes;

        internal UseLease(WorldDomainOperationCredit credit, long workspaceBytes)
        {
            _credit = credit;
            _workspaceBytes = workspaceBytes;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isReleased, 1) == 0)
            {
                _credit.ReleaseUse(_workspaceBytes);
            }
        }
    }
}
