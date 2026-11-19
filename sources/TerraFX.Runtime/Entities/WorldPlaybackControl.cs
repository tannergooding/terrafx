// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class WorldPlaybackControl : IDisposable
{
    private readonly WorldAdmissionDomain _domain;
    private readonly Lock _gate = new();
    private bool _isDisposed;
    private bool _isInitialized;
    private WorldResourceOperationReservation? _sourceBudget;
    private WorldDomainOperationCredit? _sourceCredit;
    private WorldRestoreSourceControl.SourceUse? _sourceUse;

    internal WorldPlaybackControl(WorldAdmissionDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        _domain = domain;
    }

    public void Dispose()
    {
        WorldResourceOperationReservation? sourceBudget;
        WorldDomainOperationCredit? sourceCredit;
        WorldRestoreSourceControl.SourceUse? sourceUse;

        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            sourceUse = _sourceUse;
            sourceCredit = _sourceCredit;
            sourceBudget = _sourceBudget;
        }

        try
        {
            sourceUse?.Dispose();
        }
        finally
        {
            try
            {
                sourceBudget?.Dispose();
            }
            finally
            {
                sourceCredit?.Dispose();
            }
        }

        lock (_gate)
        {
            _sourceUse = null;
            _sourceCredit = null;
            _sourceBudget = null;
        }
    }

    internal void Initialize(
        WorldRestoreSourceControl.SourceUse sourceUse,
        WorldDomainOperationCredit sourceCredit,
        WorldResourceOperationReservation sourceBudget)
    {
        ArgumentNullException.ThrowIfNull(sourceUse);
        ArgumentNullException.ThrowIfNull(sourceCredit);
        ArgumentNullException.ThrowIfNull(sourceBudget);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (_isInitialized)
            {
                throw new InvalidOperationException("The World playback control is already initialized.");
            }

            if (!ReferenceEquals(sourceCredit.Domain, _domain))
            {
                throw new ArgumentException("The playback source credit belongs to a different World domain.", nameof(sourceCredit));
            }

            _sourceUse = sourceUse;
            _sourceCredit = sourceCredit;
            _sourceBudget = sourceBudget;
            _isInitialized = true;
        }
    }
}
