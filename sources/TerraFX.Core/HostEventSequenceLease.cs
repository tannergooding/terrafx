// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX;

/// <summary>Provides host-owned access to the process-wide event sequence issuer.</summary>
public sealed class HostEventSequenceLease
{
    private int _isRevoked;

    internal HostEventSequenceLease()
    {
    }

    /// <summary>Allocates the next positive host event sequence.</summary>
    /// <returns>The unique process-wide sequence.</returns>
    /// <exception cref="System.InvalidOperationException">The process-wide sequence domain is exhausted.</exception>
    /// <exception cref="System.ObjectDisposedException">The host revoked this lease.</exception>
    public long Allocate()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isRevoked) != 0, this);
        return HostEventSequenceAllocator.Allocate();
    }

    internal void Revoke()
        => _ = Interlocked.Exchange(ref _isRevoked, 1);
}
