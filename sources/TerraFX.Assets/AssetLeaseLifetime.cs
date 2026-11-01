// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Assets;

internal sealed class AssetLeaseLifetime
{
    private AssetPackageBacking? _backing;
    private readonly long _decodedBytes;
    private int _referenceCount = 1;
    private AssetStore? _store;

    internal AssetLeaseLifetime(AssetStore store, AssetPackageBacking backing, long decodedBytes)
    {
        _store = store;
        _backing = backing;
        _decodedBytes = decodedBytes;
    }

    internal (AssetStore Store, AssetPackageBacking Backing) AcquirePackageBackingReference()
    {
        var store = Volatile.Read(ref _store);
        var backing = Volatile.Read(ref _backing);
        ObjectDisposedException.ThrowIf(store is null || backing is null, this);
        ObjectDisposedException.ThrowIf(!backing.TryAcquireReference(), this);
        return (store, backing);
    }

    internal void AddReference()
    {
        var store = Volatile.Read(ref _store);
        ObjectDisposedException.ThrowIf(store is null, this);
        store.ReserveRetainedLease();
        var referenceAcquired = false;

        try
        {
            while (true)
            {
                var referenceCount = Volatile.Read(ref _referenceCount);
                ObjectDisposedException.ThrowIf(referenceCount == 0, this);

                if (referenceCount == int.MaxValue)
                {
                    throw new InvalidOperationException("The asset lease has too many retained handles.");
                }

                if (Interlocked.CompareExchange(ref _referenceCount, referenceCount + 1, referenceCount) == referenceCount)
                {
                    referenceAcquired = true;
                    return;
                }
            }
        }
        finally
        {
            if (!referenceAcquired)
            {
                store.ReleaseLeaseReference(0, releaseDecodedBytes: false);
            }
        }
    }

    internal void ReleaseReference()
    {
        var store = Volatile.Read(ref _store)
            ?? throw new InvalidOperationException("The asset lease lifetime lost its owning store.");
        var referenceCount = Interlocked.Decrement(ref _referenceCount);

        if (referenceCount < 0)
        {
            throw new InvalidOperationException("The asset lease reference count became negative.");
        }

        var isFinalReference = referenceCount == 0;
        var backing = isFinalReference ? Interlocked.Exchange(ref _backing, null) : null;

        if (isFinalReference)
        {
            _ = Interlocked.Exchange(ref _store, null)
                ?? throw new InvalidOperationException("The final asset lease reference lost its owning store.");

            if (backing is null)
            {
                throw new InvalidOperationException("The final asset lease reference lost its package backing.");
            }
        }

        try
        {
            store.ReleaseLeaseReference(_decodedBytes, isFinalReference);
        }
        finally
        {
            backing?.ReleaseReference();
        }
    }
}
