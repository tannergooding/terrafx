// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

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
            return state.GetPackageSha256(this);
        }
    }

    internal int MaximumDecodedAssetBytes
    {
        get
        {
            var state = Volatile.Read(ref _state);
            ObjectDisposedException.ThrowIf(state is null, this);
            return state.GetMaximumDecodedAssetBytes(this);
        }
    }

    internal AssetStoreRegistration Registration
    {
        get
        {
            var state = Volatile.Read(ref _state);
            ObjectDisposedException.ThrowIf(state is null, this);
            return state.GetRegistration(this);
        }
    }

    /// <summary>Releases this package backing reference.</summary>
    public void Dispose()
    {
        var state = Volatile.Read(ref _state);

        if (state is null)
        {
            return;
        }

        if (state.Release(out var sourceReturn))
        {
            _ = Interlocked.CompareExchange(ref _state, null, state);

            if (sourceReturn)
            {
                state.ReturnOriginalSource();
            }
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
        return state.ContainsAssetId(assetId, this);
    }

    internal AssetOwnedReadDescriptor DescribeOwnedRead<T>(AssetId assetId, object profileOrigin)
    {
        if (!assetId.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(assetId));
        }

        ArgumentNullException.ThrowIfNull(profileOrigin);
        var state = Volatile.Read(ref _state);
        ObjectDisposedException.ThrowIf(state is null, this);
        return state.DescribeOwnedRead<T>(assetId, profileOrigin, this);
    }

    internal Task<AssetLease<T>> LoadDescribedFromPackageAsync<T>(
        AssetOwnedReadDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        var state = Volatile.Read(ref _state);
        ObjectDisposedException.ThrowIf(state is null, this);

        if (!descriptor.AssetId.IsValid)
        {
            throw new ArgumentException("The read descriptor is uninitialized.", nameof(descriptor));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return AssetStore.CreatePreCanceledTask<AssetLease<T>>(cancellationToken);
        }

        var (store, backing) = state.AcquireBackingReference(this);
        return store.LoadDescribedFromPackageAsync<T>(descriptor, backing, cancellationToken);
    }

    internal Task<AssetLease<T>> LoadFromPackageAsync<T>(
        AssetId assetId,
        CancellationToken cancellationToken)
    {
        var state = Volatile.Read(ref _state);
        ObjectDisposedException.ThrowIf(state is null, this);

        if (!assetId.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(assetId));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return AssetStore.CreatePreCanceledTask<AssetLease<T>>(cancellationToken);
        }

        var (store, backing) = state.AcquireBackingReference(this);
        return store.LoadFromPackageAsync<T>(assetId, backing, cancellationToken);
    }

    internal Task<bool> MatchesAssetReferenceAsync(
        AssetReference reference,
        CancellationToken cancellationToken)
    {
        var state = Volatile.Read(ref _state);
        ObjectDisposedException.ThrowIf(state is null, this);
        return MatchesAssetReferenceCoreAsync(state, reference, requiredMinor: null, cancellationToken);
    }

    internal Task<bool> MatchesAssetReferenceAsync(
        AssetReference reference,
        int requiredMinor,
        CancellationToken cancellationToken)
    {
        var state = Volatile.Read(ref _state);
        ObjectDisposedException.ThrowIf(state is null, this);
        ArgumentOutOfRangeException.ThrowIfNegative(requiredMinor);
        return MatchesAssetReferenceCoreAsync(state, reference, requiredMinor, cancellationToken);
    }

    internal bool TryDescribeOwnedRead<T>(
        AssetId assetId,
        object profileOrigin,
        out AssetOwnedReadDescriptor descriptor)
    {
        if (!assetId.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(assetId));
        }

        ArgumentNullException.ThrowIfNull(profileOrigin);
        var state = Volatile.Read(ref _state);
        ObjectDisposedException.ThrowIf(state is null, this);
        return state.TryDescribeOwnedRead<T>(assetId, profileOrigin, this, out descriptor);
    }

    private async Task<bool> MatchesAssetReferenceCoreAsync(
        LeaseState state,
        AssetReference reference,
        int? requiredMinor,
        CancellationToken cancellationToken)
    {
        if (!reference.AssetId.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(reference));
        }

        ArgumentOutOfRangeException.ThrowIfEqual(reference.SchemaId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(reference.SchemaMajor);

        cancellationToken.ThrowIfCancellationRequested();
        var (store, backing) = state.AcquireBackingReference(this);

        try
        {
            return requiredMinor is int minor
                ? await store.MatchesAssetReferenceAsync(backing, reference, minor, cancellationToken).ConfigureAwait(false)
                : await store.MatchesAssetReferenceAsync(backing, reference, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            store.ReleaseOriginalBackingReference(backing);
        }
    }

    private sealed class LeaseState
    {
        private readonly AssetPackageBacking _backing;
        private readonly object _gate = new();
        private bool _backingReferenceReleased;
        private Exception? _releaseFailure;
        private int _releaseState;
        private readonly AssetStore _store;

        internal LeaseState(AssetStore store, AssetPackageBacking backing)
        {
            ArgumentNullException.ThrowIfNull(store);
            ArgumentNullException.ThrowIfNull(backing);
            _backing = backing;
            _store = store;
        }

        internal (AssetStore Store, AssetPackageBacking Backing) AcquireBackingReference(object instance)
        {
            lock (_gate)
            {
                ThrowIfNotActive(instance);
                ObjectDisposedException.ThrowIf(!_backing.TryAcquireReference(), instance);
                return (_store, _backing);
            }
        }

        internal bool ContainsAssetId(AssetId assetId, object instance)
        {
            lock (_gate)
            {
                ThrowIfNotActive(instance);
                return _backing.TryGetEntry(assetId, out _);
            }
        }

        internal AssetOwnedReadDescriptor DescribeOwnedRead<T>(
            AssetId assetId,
            object profileOrigin,
            object instance)
        {
            lock (_gate)
            {
                ThrowIfNotActive(instance);
                return _backing.DescribeOwnedRead<T>(_store, assetId, profileOrigin);
            }
        }

        internal int GetMaximumDecodedAssetBytes(object instance)
        {
            lock (_gate)
            {
                ThrowIfNotActive(instance);
                return _backing.MaximumDecodedAssetBytes;
            }
        }

        internal ImmutableArray<byte> GetPackageSha256(object instance)
        {
            lock (_gate)
            {
                ThrowIfNotActive(instance);
                return _backing.ExpectedPackageSha256;
            }
        }

        internal AssetStoreRegistration GetRegistration(object instance)
        {
            lock (_gate)
            {
                ThrowIfNotActive(instance);
                return _backing.Registration;
            }
        }

        internal bool Release(out bool sourceReturn)
        {
            sourceReturn = false;

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
                _backing.ReleaseReference(out sourceReturn);

                lock (_gate)
                {
                    _backingReferenceReleased = true;
                }

                _store.ReleaseLeaseReference(0, releaseDecodedBytes: false);
            }
            catch (Exception failure)
            {
                lock (_gate)
                {
                    _releaseFailure = failure;
                    _releaseState = _backingReferenceReleased ? 4 : 3;
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

        internal void ReturnOriginalSource()
            => _store.ReturnOriginalBackingSource(_backing);

        internal bool TryDescribeOwnedRead<T>(
            AssetId assetId,
            object profileOrigin,
            object instance,
            out AssetOwnedReadDescriptor descriptor)
        {
            lock (_gate)
            {
                ThrowIfNotActive(instance);

                if (!_backing.TryGetEntry(assetId, out var entry)
                    || !_backing.Registration.TryGetProfiledCodecReceipt<T>(profileOrigin, out var codecReceipt)
                    || (codecReceipt.RequiredPackageCodec is AssetPackagePayloadCodec requiredCodec
                        && (entry.Codec != (byte)requiredCodec)))
                {
                    descriptor = default;
                    return false;
                }

                descriptor = new AssetOwnedReadDescriptor(_store, _backing, entry, codecReceipt);
                return true;
            }
        }

        private void RethrowReleaseFailure()
        {
            var failure = _releaseFailure
                ?? throw new InvalidOperationException("The package lease has no recorded release failure.");
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private void ThrowIfNotActive(object instance)
            => ObjectDisposedException.ThrowIf(_releaseState != 0, instance);
    }
}
