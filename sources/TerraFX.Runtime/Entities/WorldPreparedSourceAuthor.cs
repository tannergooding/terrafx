// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class WorldPreparedSourceAuthor<TAuthor>
    where TAuthor : notnull
{
    private static readonly List<WorldPreparedSourceAuthor<TAuthor>> s_anchors = [];
    private static readonly Lock s_gate = new();

    private TAuthor? _author;
    private readonly IWorldRestoreSourceOwner _owner;
    private readonly WorldPreparedSourceStorage.StorageUse _ownership;
    private readonly object _registration;
    private int _uses = 1;

    internal WorldPreparedSourceAuthor(
        object registration,
        IWorldRestoreSourceOwner owner,
        TAuthor author,
        IDisposable ownership)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(author);
        ArgumentNullException.ThrowIfNull(ownership);
        _registration = registration;
        _owner = owner;
        _author = author;
        _ownership = ownership as WorldPreparedSourceStorage.StorageUse
            ?? throw new ArgumentException("A prepared author requires its actual source storage credit.", nameof(ownership));

        lock (s_gate)
        {
            s_anchors.Add(this);
        }
    }

    internal TAuthor Author
    {
        get
        {
            lock (s_gate)
            {
                ObjectDisposedException.ThrowIf(_uses == 0, this);
                return _author ?? throw new InvalidOperationException("The prepared source lost its immutable author.");
            }
        }
    }

    internal bool IsBoundTo(object registration, IWorldRestoreSourceOwner owner)
        => ReferenceEquals(_registration, registration) && ReferenceEquals(_owner, owner);

    internal bool IsFrom(IWorldRestoreSourceOwner owner)
        => ReferenceEquals(_owner, owner);

    internal void Release()
    {
        lock (s_gate)
        {
            if (_uses <= 0)
            {
                throw new InvalidOperationException("The prepared author has no remaining ownership interest.");
            }

            if (_uses == 1)
            {
                _ownership.Dispose();
                _author = default;
                _ = s_anchors.Remove(this);
            }
            else
            {
                _ownership.ReleaseControl();
            }

            _uses--;
        }
    }

    internal void Retain()
    {
        lock (s_gate)
        {
            ObjectDisposedException.ThrowIf(_uses == 0, this);
            _ownership.ReserveControl();
            _uses = checked(_uses + 1);
        }
    }
}
