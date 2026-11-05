// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;

namespace TerraFX.Assets;

/// <summary>Retains the exact immutable package selected by an AssetStore.</summary>
public sealed class AssetPackageLease : IDisposable
{
    private LeaseState? _state;

    internal AssetPackageLease(AssetStore store, AssetPackageBacking backing)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(backing);
        _state = new LeaseState(store, backing);
    }

    /// <summary>Gets the whole-package SHA-256 digest while this lease is active.</summary>
    /// <exception cref="ObjectDisposedException">This package lease has been disposed.</exception>
    public ImmutableArray<byte> PackageSha256
    {
        get
        {
            var state = Volatile.Read(ref _state);
            ObjectDisposedException.ThrowIf(state is null, this);
            return state.Backing.Options.ExpectedPackageSha256;
        }
    }

    internal AssetStoreRegistration Registration
    {
        get
        {
            var state = Volatile.Read(ref _state);
            ObjectDisposedException.ThrowIf(state is null, this);
            return state.Backing.Options.Registration;
        }
    }

    /// <summary>Releases this package backing reference.</summary>
    public void Dispose()
    {
        var state = Interlocked.Exchange(ref _state, null);

        if (state is null)
        {
            return;
        }

        try
        {
            state.Store.ReleaseLeaseReference(0, releaseDecodedBytes: false);
        }
        finally
        {
            state.Backing.ReleaseReference();
        }
    }

    internal bool ContainsAssetId(AssetId assetId)
    {
        if (!assetId.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(assetId));
        }

        var state = Volatile.Read(ref _state);
        ObjectDisposedException.ThrowIf(state is null, this);
        return state.Backing.TryGetEntry(assetId, out _);
    }

    private sealed class LeaseState(AssetStore store, AssetPackageBacking backing)
    {
        internal AssetPackageBacking Backing { get; } = backing;

        internal AssetStore Store { get; } = store;
    }
}
