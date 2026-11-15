// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using TerraFX.Assets;
using TerraFX.Entities;

namespace TerraFX.Hosting;

internal sealed class WorldPlaybackControl : IDisposable
{
    private readonly Lock _gate = new();
    private bool _isDisposed;
    private AssetPackageLease? _packageLease;
    private WorldRetainedHistoryControl? _retainedHistory;

    internal WorldPlaybackControl(AssetPackageLease packageLease)
    {
        ArgumentNullException.ThrowIfNull(packageLease);
        _packageLease = packageLease;
    }

    public void Dispose()
    {
        AssetPackageLease? packageLease;
        WorldRetainedHistoryControl? retainedHistory;

        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            packageLease = _packageLease;
            retainedHistory = _retainedHistory;
            _packageLease = null;
            _retainedHistory = null;
        }

        try
        {
            retainedHistory?.Dispose();
        }
        finally
        {
            packageLease?.Dispose();
        }
    }

    internal void AttachRetainedHistory(
        WorldRetainedHistoryControl retainedHistory,
        WorldDomainOperationCredit sourceCredit)
    {
        ArgumentNullException.ThrowIfNull(retainedHistory);
        ArgumentNullException.ThrowIfNull(sourceCredit);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (_retainedHistory is not null)
            {
                throw new InvalidOperationException("The World playback control already owns source history.");
            }

            retainedHistory.AttachPlaybackSourceCredit(sourceCredit);
            _retainedHistory = retainedHistory;
        }
    }
}
