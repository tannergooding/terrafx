// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

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
