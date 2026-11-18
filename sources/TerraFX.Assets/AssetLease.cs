// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
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

    /// <summary>Gets the spatial profile that produced the leased asset.</summary>
    /// <exception cref="ObjectDisposedException">This lease has been disposed.</exception>
    public SpatialProfileId ProfileId
    {
        get
        {
            var state = Volatile.Read(ref _state);
            ObjectDisposedException.ThrowIf(state is null, this);
            return state.GetProfileId(this);
        }
    }

    /// <summary>Gets the spatial settings that produced the leased asset.</summary>
    /// <exception cref="ObjectDisposedException">This lease has been disposed.</exception>
    public SpatialSettings SpatialSettings
    {
        get
        {
            var state = Volatile.Read(ref _state);
            ObjectDisposedException.ThrowIf(state is null, this);
            return state.GetSpatialSettings(this);
        }
    }

    /// <summary>Gets the decoded asset value while this lease is active.</summary>
    /// <exception cref="ObjectDisposedException">This lease has been disposed.</exception>
    public T Value
    {
        get
        {
            var state = Volatile.Read(ref _state);
            ObjectDisposedException.ThrowIf(state is null, this);
            return state.GetValue(this);
        }
    }

    internal AssetPackageBackingToken BackingToken
    {
        get
        {
            var state = Volatile.Read(ref _state);
            ObjectDisposedException.ThrowIf(state is null, this);
            return state.GetBackingToken(this);
        }
    }

    /// <summary>Releases this handle's package reference and invalidates <see cref="Value"/>.</summary>
    public void Dispose()
    {
        var state = Volatile.Read(ref _state);

        if ((state is not null) && state.Release())
        {
            _ = Interlocked.CompareExchange(ref _state, null, state);
        }
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
        var (store, backing) = state.AcquirePackageBackingReference(this);
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
        return state.Retain(_assetId, this);
    }

    private sealed class LeaseState
    {
        private readonly object _gate = new();
        private readonly AssetLeaseLifetime _lifetime;
        private Exception? _releaseFailure;
        private int _releaseState;
        private readonly T _value;

        internal LeaseState(T value, AssetLeaseLifetime lifetime)
        {
            ArgumentNullException.ThrowIfNull(lifetime);
            _value = value;
            _lifetime = lifetime;
        }

        internal (AssetStore Store, AssetPackageBacking Backing) AcquirePackageBackingReference(object instance)
        {
            lock (_gate)
            {
                ThrowIfNotActive(instance);
                return _lifetime.AcquirePackageBackingReference();
            }
        }

        internal AssetPackageBackingToken GetBackingToken(object instance)
        {
            lock (_gate)
            {
                ThrowIfNotActive(instance);
                return _lifetime.BackingToken;
            }
        }

        internal SpatialProfileId GetProfileId(object instance)
        {
            lock (_gate)
            {
                ThrowIfNotActive(instance);
                return _lifetime.ProfileId;
            }
        }

        internal SpatialSettings GetSpatialSettings(object instance)
        {
            lock (_gate)
            {
                ThrowIfNotActive(instance);
                return _lifetime.SpatialSettings;
            }
        }

        internal T GetValue(object instance)
        {
            lock (_gate)
            {
                ThrowIfNotActive(instance);
                return _value;
            }
        }

        internal bool Release()
        {
            lock (_gate)
            {
                while (_releaseState is 1)
                {
                    _ = Monitor.Wait(_gate);
                }

                if (_releaseState is 2)
                {
                    return true;
                }

                if (_releaseState is 3 or 4)
                {
                    RethrowReleaseFailure();
                }

                _releaseState = 1;
            }

            try
            {
                _lifetime.ReleaseReference();
            }
            catch (Exception failure)
            {
                lock (_gate)
                {
                    _releaseFailure = failure;
                    _releaseState = 3;
                    Monitor.PulseAll(_gate);
                }

                throw;
            }

            lock (_gate)
            {
                _releaseState = 2;
                Monitor.PulseAll(_gate);
                return true;
            }
        }

        internal AssetLease<T> Retain(AssetId assetId, object instance)
        {
            lock (_gate)
            {
                ThrowIfNotActive(instance);
                _lifetime.AddReference();

                try
                {
                    return new AssetLease<T>(assetId, _value, _lifetime);
                }
                catch
                {
                    _lifetime.ReleaseReference();
                    throw;
                }
            }
        }

        private void RethrowReleaseFailure()
        {
            var failure = _releaseFailure
                ?? throw new InvalidOperationException("The asset lease has no recorded release failure.");
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private void ThrowIfNotActive(object instance)
            => ObjectDisposedException.ThrowIf(_releaseState != 0, instance);
    }
}
