// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TerraFX.Assets;

/// <summary>Retains one decoded asset and the immutable package backing that produced it.</summary>
/// <typeparam name="T">The registered immutable asset value type.</typeparam>
public sealed class AssetLease<T> : IDisposable
{
    private readonly AssetId _assetId;
    private LeaseState? _state;

    internal AssetLease(AssetId assetId, T value, AssetLeaseLifetime leaseLifetime)
    {
        if (!assetId.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(assetId));
        }

        ArgumentNullException.ThrowIfNull(leaseLifetime);
        _assetId = assetId;
        _state = new LeaseState(value, leaseLifetime);
    }

    /// <summary>Gets the identity of the leased asset.</summary>
    public AssetId AssetId => _assetId;

    /// <summary>Gets the decoded asset value while this lease is active.</summary>
    /// <exception cref="ObjectDisposedException">This lease has been disposed.</exception>
    public T Value
    {
        get
        {
            var state = Volatile.Read(ref _state);
            ObjectDisposedException.ThrowIf(state is null, this);
            return state.Value;
        }
    }

    /// <summary>Releases this handle's package reference and invalidates <see cref="Value"/>.</summary>
    public void Dispose()
    {
        var state = Interlocked.Exchange(ref _state, null);
        state?.Lifetime.ReleaseReference();
    }

    /// <summary>Loads a typed asset from the exact immutable package backing held by this lease.</summary>
    /// <typeparam name="TAsset">The registered immutable asset value type.</typeparam>
    /// <param name="assetId">The positive content-set asset identity.</param>
    /// <param name="cancellationToken">The token that cancels the admitted read/decode operation.</param>
    /// <returns>A lease that independently retains the source package backing.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="assetId"/> is invalid.</exception>
    /// <exception cref="InvalidDataException">The indexed envelope or typed record is malformed.</exception>
    /// <exception cref="InvalidOperationException">A live-lease or decode budget is exhausted.</exception>
    /// <exception cref="KeyNotFoundException"><paramref name="assetId"/> is absent from the source package.</exception>
    /// <exception cref="NotSupportedException">The CLR type or payload schema has no exact compiled codec.</exception>
    /// <exception cref="OperationCanceledException">The read/decode operation is canceled.</exception>
    /// <exception cref="ObjectDisposedException">This lease or its asset store has been disposed.</exception>
    public Task<AssetLease<TAsset>> LoadFromPackageAsync<TAsset>(
        AssetId assetId,
        CancellationToken cancellationToken = default)
    {
        var state = Volatile.Read(ref _state);
        ObjectDisposedException.ThrowIf(state is null, this);
        var (store, backing) = state.Lifetime.AcquirePackageBackingReference();
        return store.LoadFromPackageAsync<TAsset>(assetId, backing, cancellationToken);
    }

    /// <summary>Creates an independently disposable lease over the same decoded value and package backing.</summary>
    /// <returns>A new lease that shares this lease's decoded value and package backing.</returns>
    /// <exception cref="InvalidOperationException">The asset store's live lease capacity is exhausted.</exception>
    /// <exception cref="ObjectDisposedException">This lease or its asset store has been disposed.</exception>
    public AssetLease<T> Retain()
    {
        var state = Volatile.Read(ref _state);
        ObjectDisposedException.ThrowIf(state is null, this);
        state.Lifetime.AddReference();

        try
        {
            return new AssetLease<T>(_assetId, state.Value, state.Lifetime);
        }
        catch
        {
            state.Lifetime.ReleaseReference();
            throw;
        }
    }

    private sealed class LeaseState(T value, AssetLeaseLifetime lifetime)
    {
        internal AssetLeaseLifetime Lifetime { get; } = lifetime;

        internal T Value { get; } = value;
    }
}
