// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;
using TerraFX.Platform;

namespace TerraFX.Hosting;

internal sealed class PlatformHostOwner
{
    private bool _isDisposed;
    private readonly uint _ownerId;
    private readonly int _ownerThreadId;
    private readonly PlatformHost _platform;

    private PlatformHostOwner(uint ownerId, PlatformHost platform)
    {
        _ownerId = ownerId;
        _platform = platform;
        _ownerThreadId = Environment.CurrentManagedThreadId;
    }

    internal uint OwnerId => _ownerId;

    internal PlatformHost Platform
    {
        get
        {
            EnsureOwnerThread();
            ThrowIfDisposed();
            return _platform;
        }
    }

    internal static PlatformHostOwner Create(
        EngineHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var ownerId = OwnerIdAllocator.Allocate();
        var platform = new PlatformHost(ownerId, options.Platform, options.ClipboardServicePolicy);
        return new PlatformHostOwner(ownerId, platform);
    }

    internal void DisposeFromOwner()
    {
        EnsureOwnerThread();

        if (_isDisposed)
        {
            return;
        }

        _platform.DisposeFromOwner();
        _isDisposed = true;
    }

    internal void Pump()
    {
        EnsureOwnerThread();
        ThrowIfDisposed();
        _platform.Pump();
    }

    private void EnsureOwnerThread()
    {
        if (_ownerThreadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("The PlatformHost owner is restricted to its issuing thread.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}
