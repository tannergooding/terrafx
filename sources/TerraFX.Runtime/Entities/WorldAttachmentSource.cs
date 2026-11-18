// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class WorldAttachmentSource<TAuthor> : IWorldPreparedAttachmentSource, IWorldPreparedAttachmentStorage
    where TAuthor : notnull
{
    private readonly WorldPreparedSourceAuthor<TAuthor> _author;
    private readonly Lock _gate = new();
    private bool _isDisposed;

    internal WorldAttachmentSource(
        IWorldAttachmentRegistration registration,
        IWorldRestoreSourceOwner owner,
        TAuthor author,
        IDisposable ownership)
    {
        ArgumentNullException.ThrowIfNull(registration);

        if ((registration.Scope != AttachmentScope.World) || (registration.SourceAuthorType != typeof(TAuthor)))
        {
            throw new ArgumentException("The prepared World attachment author does not match its captured family.", nameof(registration));
        }

        _author = new WorldPreparedSourceAuthor<TAuthor>(registration, owner, author, ownership);
        Schema = registration.Descriptor.Schema;
        Scope = registration.Scope;
    }

    private WorldAttachmentSource(WorldAttachmentSource<TAuthor> source)
    {
        _author = source._author;
        Schema = source.Schema;
        Scope = source.Scope;
    }

    public ContentSchemaReference Schema { get; }

    public AttachmentScope Scope { get; }

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

    internal bool IsFrom(IWorldRestoreSourceOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            return _author.IsFrom(owner);
        }
    }

    internal WorldAttachmentSource<TAuthor> Retain()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            _author.Retain();

            try
            {
                return new WorldAttachmentSource<TAuthor>(this);
            }
            catch
            {
                _author.Release();
                throw;
            }
        }
    }

    object IWorldPreparedAttachmentSource.GetAuthor(
        IWorldAttachmentRegistration registration,
        IWorldRestoreSourceOwner owner)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(owner);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (!_author.IsBoundTo(registration, owner))
            {
                throw new InvalidOperationException("The prepared attachment belongs to another source owner or captured family.");
            }

            return _author.Author;
        }
    }

    IDisposable IWorldPreparedAttachmentStorage.RetainStorage() => Retain();
}
