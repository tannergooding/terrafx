// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class WorldPreparedSourceStorage : IDisposable
{
    // Fixed owner/cache/cleanup controls plus the bounded 4-KiB retained-use workspace.
    internal const int BaseByteReservation = 8192;
    private const int ControlCapacityBytes = 4096;

    private static readonly List<WorldPreparedSourceStorage> s_anchors = [];
    private static readonly Lock s_gate = new();

    private int _controlBytes;
    private bool _isDisposed;
    private readonly IDisposable _ownership;
    private int _uses = 1;

    internal WorldPreparedSourceStorage(IDisposable ownership, long ownedByteReservation)
    {
        ArgumentNullException.ThrowIfNull(ownership);
        ArgumentOutOfRangeException.ThrowIfLessThan(ownedByteReservation, BaseByteReservation);
        _ownership = ownership;
        OwnedByteReservation = ownedByteReservation;

        lock (s_gate)
        {
            s_anchors.Add(this);
        }
    }

    internal long OwnedByteReservation { get; }

    public void Dispose()
    {
        lock (s_gate)
        {
            if (!_isDisposed)
            {
                ReleaseUse();
                _isDisposed = true;
            }
        }
    }

    internal void ReleaseControl(int bytes)
    {
        lock (s_gate)
        {
            if ((bytes <= 0) || (bytes > _controlBytes))
            {
                throw new InvalidOperationException("The prepared source returned an unowned control-storage interest.");
            }

            _controlBytes -= bytes;
        }
    }

    internal void ReserveControl(int bytes)
    {
        lock (s_gate)
        {
            ObjectDisposedException.ThrowIf(_uses == 0, this);

            if ((bytes <= 0) || (bytes > ControlCapacityBytes - _controlBytes))
            {
                throw new InvalidOperationException("The retained source controls do not fit their existing load storage reservation.");
            }

            _controlBytes += bytes;
        }
    }

    internal IDisposable Retain(IDisposable sourceUse)
    {
        ArgumentNullException.ThrowIfNull(sourceUse);

        lock (s_gate)
        {
            ObjectDisposedException.ThrowIf(_uses == 0, this);

            // The first author interest includes its wrapper, shared author control and credit use.
            ReserveControl(512);
            StorageUse use;

            try
            {
                use = new StorageUse(this, sourceUse);
            }
            catch
            {
                ReleaseControl(512);
                throw;
            }

            _uses = checked(_uses + 1);
            return use;
        }
    }

    private void ReleaseUse()
    {
        if (_uses == 1)
        {
            // A failed release keeps both its charge and its strong quarantine anchor.
            _ownership.Dispose();
            _ = s_anchors.Remove(this);
        }

        _uses--;
    }

    internal sealed class StorageUse : IDisposable
    {
        private readonly Lock _disposeGate = new();
        private Exception? _failure;
        private IDisposable? _sourceUse;
        private WorldPreparedSourceStorage? _storage;

        internal StorageUse(WorldPreparedSourceStorage storage, IDisposable sourceUse)
        {
            _storage = storage;
            _sourceUse = sourceUse;
        }

        [SuppressMessage("Usage", "CA1031:Do not catch general exception types", Justification = "A cleanup failure must remain sticky and keep the author graph and its storage credits quarantined.")]
        public void Dispose()
        {
            lock (_disposeGate)
            {
                if (_failure is { } failure)
                {
                    throw failure;
                }

                if (_storage is not { } storage)
                {
                    return;
                }

                try
                {
                    // The source owner takes its gate before s_gate; never call it under s_gate.
                    _sourceUse?.Dispose();
                    _sourceUse = null;

                    lock (s_gate)
                    {
                        storage.ReleaseUse();
                        storage.ReleaseControl(512);
                        _storage = null;
                    }
                }
                catch (Exception exception)
                {
                    _failure = exception;
                    throw;
                }
            }
        }

        internal void ReleaseControl() => (_storage
            ?? throw new ObjectDisposedException(nameof(StorageUse))).ReleaseControl(256);

        internal void ReserveControl() => (_storage
            ?? throw new ObjectDisposedException(nameof(StorageUse))).ReserveControl(256);
    }
}
