// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class WorldResourceSource<TAuthor> : IWorldPreparedResourceSource
    where TAuthor : notnull
{
    private readonly WorldPreparedSourceAuthor<TAuthor> _author;
    private readonly Lock _gate = new();
    private bool _isDisposed;

    internal WorldResourceSource(
        IWorldResourceRegistration registration,
        IWorldRestoreSourceOwner owner,
        TAuthor author,
        IDisposable ownership)
    {
        ArgumentNullException.ThrowIfNull(registration);

        if (registration.SourceAuthorType != typeof(TAuthor))
        {
            throw new ArgumentException("The prepared resource author does not match its captured family.", nameof(registration));
        }

        _author = new WorldPreparedSourceAuthor<TAuthor>(registration, owner, author, ownership);
        ResourceId = registration.ResourceId;
        Schema = registration.Schema;
        PartitionCount = registration.PartitionCount;
    }

    private WorldResourceSource(WorldResourceSource<TAuthor> source)
    {
        _author = source._author;
        ResourceId = source.ResourceId;
        Schema = source.Schema;
        PartitionCount = source.PartitionCount;
    }

    public int PartitionCount { get; }

    public Guid ResourceId { get; }

    public ContentSchemaReference Schema { get; }

    internal TAuthor Author
    {
        get
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);
                return _author.Author;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (!_isDisposed)
            {
                _author.Release();
                _isDisposed = true;
            }
        }
    }

    internal bool IsBoundTo(IWorldResourceRegistration registration, IWorldRestoreSourceOwner owner)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(owner);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            return _author.IsBoundTo(registration, owner);
        }
    }

    internal bool IsFrom(IWorldRestoreSourceOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            return _author.IsFrom(owner);
        }
    }

    internal WorldResourceSource<TAuthor> Retain()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            _author.Retain();

            try
            {
                return new WorldResourceSource<TAuthor>(this);
            }
            catch
            {
                _author.Release();
                throw;
            }
        }
    }
}
