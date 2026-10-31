// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Assets;

internal sealed class AssetLeaseLifetime
{
    private AssetPackageBacking? _backing;
    private readonly long _decodedBytes;
    private int _disposed;
    private AssetStore? _store;

    internal AssetLeaseLifetime(AssetStore store, AssetPackageBacking backing, long decodedBytes)
    {
        _store = store;
        _backing = backing;
        _decodedBytes = decodedBytes;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var backing = Interlocked.Exchange(ref _backing, null);
        var store = Interlocked.Exchange(ref _store, null);

        if (backing is not null)
        {
            try
            {
                store?.ReleaseLiveLease(_decodedBytes);
            }
            finally
            {
                backing.ReleaseReference();
            }
        }
    }
}
