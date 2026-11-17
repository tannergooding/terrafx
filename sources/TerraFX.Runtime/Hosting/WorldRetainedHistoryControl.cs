// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Threading;
using TerraFX.Entities;

namespace TerraFX.Hosting;

internal sealed class WorldRetainedHistoryControl : IDisposable, IWorldHistoryRetentionOwner
{
    private static readonly List<WorldRetainedHistoryControl> s_anchors = [];
    private static readonly Lock s_gate = new();

    private WorldPersistenceCheckpointContents? _checkpoint;
    private byte[]? _fileRecord;
    private readonly WorldHistoryInterest _historyInterest;
    private bool _isAttached;
    private bool _isDisposed;
    private WorldDomainOperationCredit? _playbackSourceCredit;
    private IDisposable? _sourceUse;

    internal WorldRetainedHistoryControl(WorldHistoryInterest historyInterest)
    {
        ArgumentNullException.ThrowIfNull(historyInterest);
        _historyInterest = historyInterest;

        lock (s_gate)
        {
            _ = s_anchors.EnsureCapacity(checked(s_anchors.Count + 1));
            s_anchors.Add(this);
        }
    }

    internal WorldPersistenceCheckpointContents Checkpoint
    {
        get
        {
            lock (s_gate)
            {
                return _checkpoint
                    ?? throw new InvalidOperationException("The retained World history has not received its validated checkpoint.");
            }
        }
    }

    public void Dispose()
    {
        IDisposable? sourceUse;
        WorldDomainOperationCredit? playbackSourceCredit;

        lock (s_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            sourceUse = _sourceUse;
            playbackSourceCredit = _playbackSourceCredit;
            _checkpoint = null;
            _fileRecord = null;
        }

        try
        {
            sourceUse?.Dispose();
        }
        finally
        {
            try
            {
                _historyInterest.Dispose();
            }
            finally
            {
                playbackSourceCredit?.Dispose();
            }
        }

        lock (s_gate)
        {
            if (!s_anchors.Remove(this))
            {
                throw new InvalidOperationException("The retained World history lost its quarantine anchor.");
            }

            _sourceUse = null;
            _playbackSourceCredit = null;
        }
    }

    internal void Attach(
        WorldPersistenceCheckpointContents checkpoint,
        byte[] fileRecord,
        IDisposable sourceUse)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(fileRecord);
        ArgumentNullException.ThrowIfNull(sourceUse);

        lock (s_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (_isAttached)
            {
                throw new InvalidOperationException("The retained World history already owns its source data.");
            }

            _checkpoint = checkpoint;
            _fileRecord = fileRecord;
            _sourceUse = sourceUse;
            _isAttached = true;
        }
    }

    internal void AttachPlaybackSourceCredit(WorldDomainOperationCredit sourceCredit)
    {
        ArgumentNullException.ThrowIfNull(sourceCredit);

        lock (s_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (!_isAttached || (_playbackSourceCredit is not null))
            {
                throw new InvalidOperationException("The retained World history cannot accept playback ownership.");
            }

            if (!ReferenceEquals(sourceCredit.Domain, _historyInterest.Domain))
            {
                throw new ArgumentException("The playback source credit belongs to a different World domain.", nameof(sourceCredit));
            }

            _playbackSourceCredit = sourceCredit;
        }
    }
}
