// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class WorldDomainOperationCreditBundle : IDisposable
{
    private readonly WorldDomainOperationCredit _cleanup;
    private readonly WorldDomainOperationCredit _data;
    private readonly Lock _gate = new();
    private bool _isDisposed;
    private readonly WorldDomainOperationCredit _source;
    private readonly WorldDomainOperationCredit _target;

    internal WorldDomainOperationCreditBundle(
        WorldDomainOperationCredit source,
        WorldDomainOperationCredit target,
        WorldDomainOperationCredit data,
        WorldDomainOperationCredit cleanup)
    {
        _source = source;
        _target = target;
        _data = data;
        _cleanup = cleanup;
    }

    internal WorldDomainOperationCredit Cleanup => _cleanup;

    internal WorldDomainOperationCredit Data => _data;

    internal WorldDomainOperationCredit Source => _source;

    internal WorldDomainOperationCredit Target => _target;

    private static void DisposeRetainedUses(
        IDisposable? sourceUse,
        IDisposable? targetUse,
        IDisposable? dataUse,
        IDisposable? cleanupUse)
    {
        try
        {
            cleanupUse?.Dispose();
        }
        finally
        {
            try
            {
                dataUse?.Dispose();
            }
            finally
            {
                try
                {
                    targetUse?.Dispose();
                }
                finally
                {
                    sourceUse?.Dispose();
                }
            }
        }
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

            try
            {
                _source.Dispose();
            }
            finally
            {
                try
                {
                    _target.Dispose();
                }
                finally
                {
                    try
                    {
                        _data.Dispose();
                    }
                    finally
                    {
                        _cleanup.Dispose();
                    }
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
            IDisposable? sourceUse = null;
            IDisposable? targetUse = null;
            IDisposable? dataUse = null;
            IDisposable? cleanupUse = null;

            try
            {
                sourceUse = _source.AcquireRetainedUse(sourceWorkspaceBytes);
                targetUse = _target.AcquireRetainedUse(targetWorkspaceBytes);
                dataUse = _data.AcquireRetainedUse(dataWorkspaceBytes);
                cleanupUse = _cleanup.AcquireRetainedUse(cleanupWorkspaceBytes);
                var retainedUses = new RetainedUseLease(sourceUse, targetUse, dataUse, cleanupUse);
                sourceUse = null;
                targetUse = null;
                dataUse = null;
                cleanupUse = null;
                return retainedUses;
            }
            finally
            {
                DisposeRetainedUses(sourceUse, targetUse, dataUse, cleanupUse);
            }
        }
    }

    private sealed class RetainedUseLease : IDisposable
    {
        private readonly IDisposable _cleanupUse;
        private readonly IDisposable _dataUse;
        private int _isDisposed;
        private readonly IDisposable _sourceUse;
        private readonly IDisposable _targetUse;

        internal RetainedUseLease(
            IDisposable sourceUse,
            IDisposable targetUse,
            IDisposable dataUse,
            IDisposable cleanupUse)
        {
            _sourceUse = sourceUse;
            _targetUse = targetUse;
            _dataUse = dataUse;
            _cleanupUse = cleanupUse;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
            {
                return;
            }

            DisposeRetainedUses(_sourceUse, _targetUse, _dataUse, _cleanupUse);
        }
    }
}
