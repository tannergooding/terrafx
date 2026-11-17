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
    private IDisposable? _sourceBudget;
    private IDisposable? _sourceOwnership;

    internal WorldPlaybackControl(
        AssetPackageLease packageLease,
        IDisposable? sourceOwnership = null,
        IDisposable? sourceBudget = null)
    {
        ArgumentNullException.ThrowIfNull(packageLease);

        if ((sourceOwnership is null) != (sourceBudget is null))
        {
            throw new ArgumentException("Retained source ownership and its candidate budget interest must transfer together.", nameof(sourceOwnership));
        }

        _packageLease = packageLease;
        _sourceOwnership = sourceOwnership;
        _sourceBudget = sourceBudget;
    }

    public void Dispose()
    {
        AssetPackageLease? packageLease;
        WorldRetainedHistoryControl? retainedHistory;
        IDisposable? sourceOwnership;
        IDisposable? sourceBudget;

        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            packageLease = _packageLease;
            retainedHistory = _retainedHistory;
            sourceOwnership = _sourceOwnership;
            sourceBudget = _sourceBudget;
            _packageLease = null;
            _retainedHistory = null;
            _sourceOwnership = null;
            _sourceBudget = null;
        }

        if (sourceOwnership is not null)
        {
            try
            {
                sourceOwnership.Dispose();
            }
            finally
            {
                sourceBudget?.Dispose();
            }

            return;
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
